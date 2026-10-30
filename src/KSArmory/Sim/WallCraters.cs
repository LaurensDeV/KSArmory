using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where hits that did not get through a tank's wall have thinned it, kept per skin in the skin's own
/// frame as the holes are, so a spot hit again needs less to go through.
/// </summary>
internal sealed class WallCraters
{
    private readonly record struct Crater(double3 At, double DiameterMetres, double DepthMetres);

    /// <summary>Most craters one skin keeps; past it the shallowest goes.</summary>
    public const int MostPerSkin = 32;

    private readonly ConditionalWeakTable<object, List<Crater>> _bySkin = new();

    public static WallCraters World { get; } = new();

    public void Clear() => _bySkin.Clear();

    /// <summary>What is left of a wall this thick under a hit of this diameter at this point.</summary>
    public double ThicknessLeft(object skin, double3 at, double diameterMetres, double thicknessMetres)
    {
        if (!_bySkin.TryGetValue(skin, out List<Crater>? craters)) return thicknessMetres;

        double deepest = 0.0;
        foreach (Crater crater in craters)
        {
            if (Overlaps(crater, at, diameterMetres)) deepest = Math.Max(deepest, crater.DepthMetres);
        }

        return Math.Max(thicknessMetres - deepest, 0.0);
    }

    /// <summary>Deepens the crater this hit lands in by <paramref name="depthMetres"/>, or starts one.</summary>
    public void Dent(object skin, double3 at, double diameterMetres, double depthMetres)
    {
        if (!(depthMetres > 0.0) || !(diameterMetres > 0.0)) return;

        List<Crater> craters = _bySkin.GetValue(skin, static _ => []);
        for (int i = 0; i < craters.Count; i++)
        {
            if (!Overlaps(craters[i], at, diameterMetres)) continue;

            craters[i] = craters[i] with
            {
                DiameterMetres = Math.Max(craters[i].DiameterMetres, diameterMetres),
                DepthMetres = craters[i].DepthMetres + depthMetres,
            };
            return;
        }

        if (craters.Count >= MostPerSkin)
        {
            int shallowest = 0;
            for (int i = 1; i < craters.Count; i++)
            {
                if (craters[i].DepthMetres < craters[shallowest].DepthMetres) shallowest = i;
            }

            craters.RemoveAt(shallowest);
        }

        craters.Add(new Crater(at, diameterMetres, depthMetres));
    }

    private static bool Overlaps(Crater crater, double3 at, double diameterMetres) =>
        Vec.Len(crater.At - at) <= 0.5 * (crater.DiameterMetres + diameterMetres);
}
