using System.Text;
using Brutal.KtxApi;
using Brutal.KtxApi.Abstractions;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A body's cloud layers as <see cref="CloudCover"/> reads them: each layer's shell from its cloud
/// types, its coverage map read off disk on a worker thread the first time it is asked for, and the
/// slow drift KSA turns it by, applied exactly as <c>CloudLayerRenderData.UpdateCommonFrameData</c>
/// does. Until a map is in, or where one cannot be read, the layer stops nothing.
/// </summary>
internal static class CloudLayers
{
    // One level below the top: 2048 a face on Earth, about 5 km a texel, 12 MB held compressed.
    private const uint MipBelowTop = 1;

    private sealed class Layer(CloudLayerReference reference, double bottomRadius, double topRadius)
    {
        public readonly CloudLayerReference Reference = reference;
        public readonly double BottomRadius = bottomRadius;
        public readonly double TopRadius = topRadius;
        public volatile CoverageCube? Coverage;
    }

    private static readonly Dictionary<Celestial, Layer[]> _bodies = new(ReferenceEqualityComparer.Instance);
    private static CloudLayer[] _read = [];

    public static bool TryEntry(Celestial body, double3 originEcl, double3 directionEcl, double reach,
                                out double distance)
    {
        distance = double.PositiveInfinity;

        if (body is not AtmosphericBody atmospheric || !atmospheric.RenderClouds || !GameSettings.ShowClouds())
        {
            return false;
        }

        Layer[] layers = LayersOf(atmospheric);
        if (layers.Length == 0) return false;

        doubleQuat cce2Ccf = body.GetCce2Ccf();
        double3 origin = (originEcl - body.GetPositionEcl()).Transform(cce2Ccf);
        double3 direction = directionEcl.Transform(cce2Ccf);
        double elapsed = Universe.GetElapsedSeconds();

        if (_read.Length < layers.Length) _read = new CloudLayer[layers.Length];
        CloudLayer[] read = _read;
        int count = 0;

        foreach (Layer layer in layers)
        {
            if (layer.Coverage is not { } coverage) continue;

            double3 turn = (double3)layer.Reference.RotationSpeed / layer.BottomRadius * elapsed;
            read[count++] = new CloudLayer(layer.BottomRadius, layer.TopRadius,
                                           doubleQuat.CreateFromYawPitchRoll(turn.X, turn.Y, turn.Z), coverage);
        }

        return count > 0 && CloudCover.TryEntry(origin, direction, reach, read.AsSpan(0, count), out distance);
    }

    /// <summary>
    /// Each loaded layer's coverage round a direction in the body's own frame, as rows of tenths from
    /// north to south and west to east, for checking the map is read the way the clouds are drawn.
    /// </summary>
    public static List<string[]> CoverageAround(Celestial body, double3 upCcf, double halfMetres, int cells)
    {
        List<string[]> grids = [];
        if (body is not AtmosphericBody atmospheric || cells < 1) return grids;

        double3 up = Vec.Unit(upCcf);
        double3 east = Vec.Unit(Vec.Cross(new double3(0, 0, 1), up));
        double3 north = Vec.Cross(up, east);
        double elapsed = Universe.GetElapsedSeconds();

        foreach (Layer layer in LayersOf(atmospheric))
        {
            if (layer.Coverage is not { } coverage) continue;

            double3 turn = (double3)layer.Reference.RotationSpeed / layer.BottomRadius * elapsed;
            doubleQuat toCloud = doubleQuat.CreateFromYawPitchRoll(turn.X, turn.Y, turn.Z);
            string[] rows = new string[cells];

            for (int r = 0; r < cells; r++)
            {
                char[] row = new char[cells];
                double n = halfMetres * (1.0 - (2.0 * r / Math.Max(cells - 1, 1)));
                for (int c = 0; c < cells; c++)
                {
                    double e = halfMetres * ((2.0 * c / Math.Max(cells - 1, 1)) - 1.0);
                    double3 at = (up * layer.BottomRadius) + (east * e) + (north * n);
                    int tenths = (int)Math.Clamp(Math.Floor(coverage.Sample(toCloud * at) * 10.0), 0, 9);
                    row[c] = (char)('0' + tenths);
                }

                rows[r] = new string(row);
            }

            grids.Add(rows);
        }

        return grids;
    }

    private static Layer[] LayersOf(AtmosphericBody body)
    {
        if (_bodies.TryGetValue(body, out Layer[]? known)) return known;

        List<Layer> layers = [];
        BodyTraits traits = BodyCatalogue.For(body.Id);
        foreach (CloudLayerReference reference in body.BodyTemplate.CloudsReference?.Layers ?? [])
        {
            if (traits.IsThinCloud(reference.Id))
            {
                Log.Info($"clouds: {body.Id} {reference.Id} is thin cloud; a beam passes through it");
                continue;
            }

            double bottom = body.MeanRadius + (double)reference.BottomAltitude;
            double top = body.MeanRadius + (double)reference.TopAltitude;
            if (!double.IsFinite(bottom) || !(top > bottom)) continue;

            Layer layer = new(reference, bottom, top);
            layers.Add(layer);

            string? path = reference.VolumetricCloud.Texture?.Get()?.ModPath;
            string name = $"{body.Id} {reference.Id}";
            if (string.IsNullOrEmpty(path))
            {
                Log.Info($"clouds: {name} names no coverage map; it will not stop a beam");
                continue;
            }

            Task.Run(() => Load(layer, Path.GetFullPath(path), name));
        }

        Layer[] all = [.. layers];
        _bodies[body] = all;
        return all;
    }

    private static void Load(Layer layer, string path, string name)
    {
        try
        {
            KtxErrorCode code = KtxTexture2.CreateFromNamedFile(Encoding.UTF8.GetBytes(path + "\0"),
                                                                KtxTextureCreateFlagBits.LoadImageDataBit,
                                                                out KtxTexture2Handle texture);
            if (code != KtxErrorCode.Success)
            {
                Log.Warn($"clouds: {name}'s coverage map {path} would not load ({code}); it will not stop a beam");
                return;
            }

            try
            {
                VkFormat format = texture.GetVkFormat();
                if (format != VkFormat.Bc4UnormBlock || texture.NumFaces != 6)
                {
                    Log.Info($"clouds: {name}'s coverage map is {format} with {texture.NumFaces} face(s), "
                             + "not a BC4 cubemap; it will not stop a beam");
                    return;
                }

                uint level = Math.Min(MipBelowTop, texture.NumLevels - 1);
                int size = (int)(texture.BaseWidth >> (int)level);
                byte[][] faces = new byte[6][];

                for (uint face = 0; face < 6; face++)
                {
                    code = texture.GetImageData(level, 0, face, out Span<byte> data);
                    if (code != KtxErrorCode.Success)
                    {
                        Log.Warn($"clouds: {name}'s coverage face {face} would not read ({code})");
                        return;
                    }

                    faces[face] = data.ToArray();
                }

                layer.Coverage = CoverageCube.FromBc4(faces, size);
                Log.Info(layer.Coverage is null
                             ? $"clouds: {name}'s coverage map did not decode; it will not stop a beam"
                             : $"clouds: {name}'s coverage map is in, {size} a face; it stops beams");
            }
            finally
            {
                texture.Destroy();
            }
        }
        catch (Exception e)
        {
            Log.Warn($"clouds: {name}'s coverage map failed to load: {e.Message}; it will not stop a beam");
        }
    }
}
