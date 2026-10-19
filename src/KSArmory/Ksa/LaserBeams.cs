using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The high-energy beams firing this frame, for <see cref="CloudPass"/> to add into the scene through
/// the tracer shader in their own colour.
///
/// <para>Each beam is held as its two ends' offsets from the craft firing it, measured against one
/// platform sample in the step, and anchored where the engine draws that craft when the pass
/// records — the pairing <see cref="Visuals"/> uses, so the beam leaves the turret it is drawn on.</para>
/// </summary>
internal static class LaserBeams
{
    /// <summary>The share of the slot's segments a view keeps for beams, at the end of its run.</summary>
    public const int MostSegments = 256;

    // One beam is cut into this many pieces, so its width can follow the perspective along it.
    private const int Pieces = 48;

    // The drawn radius at the window and the least it narrows to. The COIL's 1.315 um is invisible;
    // this is drawn for the operator, as a rod rather than the telescope's full 1.5 m.
    private const double ExitRadiusMetres = 0.20;
    private const double LeastRadiusMetres = 0.08;

    // A little over a tracer's 24, so it blooms over a daylit sky without whiting out.
    private const double Radiance = 32.0;

    // Past this a beam passing the eye fills the picture, and its halo five times over.
    private const double WidestHalfPixels = 24.0;

    private const double NearestW = 0.05;
    private const double StaleSeconds = 0.5;

    private readonly record struct Firing(Vehicle Craft, double3 From, double3 To);

    private static readonly List<Firing> _beams = [];
    private static readonly System.Diagnostics.Stopwatch _sinceCollected = new();
    private static readonly System.Diagnostics.Stopwatch _sincePainted = new();

    /// <summary>Whether the pass drew beams recently enough that the gizmo line need not.</summary>
    public static bool Painting => _sincePainted.IsRunning && _sincePainted.Elapsed.TotalSeconds < StaleSeconds;

    public static void BeginFrame()
    {
        _beams.Clear();
        _sinceCollected.Restart();
    }

    public static void Collect(OpticalHead head)
    {
        if (!head.BeamOn || head.Platform is not { } craft || !KsaWorld.IsAlive(craft)) return;
        _beams.Add(new Firing(craft, head.BeamFromEcl - head.PlatformEcl, head.BeamToEcl - head.PlatformEcl));
    }

    public static void MarkPainted() => _sincePainted.Restart();

    /// <summary>
    /// Projects every beam into <paramref name="into"/> as a run of segments for a view of
    /// <paramref name="width"/> by <paramref name="height"/>, and the pixels they can reach.
    /// </summary>
    public static int Build(Camera camera, int width, int height, Span<ShellTracers.Segment> into,
                            out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = int.MaxValue;
        maxX = maxY = int.MinValue;
        int n = 0;

        if (_beams.Count == 0 || _sinceCollected.Elapsed.TotalSeconds > StaleSeconds) return 0;

        foreach (Firing beam in _beams)
        {
            if (!KsaWorld.TryVehicleEgo(beam.Craft, out double3 craftEgo)) continue;

            double3 from = craftEgo + beam.From;
            double3 to = craftEgo + beam.To;

            for (int i = 0; i < Pieces && n < into.Length; i++)
            {
                double s0 = (double)i / Pieces, s1 = (double)(i + 1) / Pieces;
                double3 a = from + ((to - from) * s0);
                double3 b = from + ((to - from) * s1);

                double4 h = camera.EgoToClipDouble(a);
                double4 t = camera.EgoToClipDouble(b);

                // Width is measured at whichever end is in front of the eye, before clipping moves it.
                double3 at = h.W > NearestW ? a : b;
                double4 centre = h.W > NearestW ? h : t;
                if (!TracerLook.TryClipToFront(ref h, ref t, NearestW)) continue;

                double radius = ExitRadiusMetres + ((LeastRadiusMetres - ExitRadiusMetres) * s0);

                // Square to the beam and to the line of sight, so the width read off it is the one seen.
                double3 across = Vec.Cross(to - from, at);
                across = Vec.Len2(across) > 1e-12 ? Vec.Unit(across) : Vec.Unit(Vec.AnyPerpendicular(at));
                double4 side = camera.EgoToClipDouble(at + (across * radius));
                if (!(side.W > NearestW)) continue;

                (double hx, double hy) = Pixel(h, width, height);
                (double tx, double ty) = Pixel(t, width, height);
                (double cx, double cy) = Pixel(centre, width, height);
                (double sx, double sy) = Pixel(side, width, height);
                double half = Math.Clamp(Math.Sqrt(((sx - cx) * (sx - cx)) + ((sy - cy) * (sy - cy))), 0.75,
                                         WidestHalfPixels);
                if (!double.IsFinite(hx + hy + tx + ty + half)) continue;

                into[n++] = new ShellTracers.Segment
                {
                    Head = new float4((float)hx, (float)hy, (float)(h.Z / h.W), (float)Radiance),
                    Tail = new float4((float)tx, (float)ty, (float)(t.Z / t.W), (float)half),
                };

                // The shader's halo reaches five half-widths.
                int reach = (int)Math.Ceiling(half * 5.0) + 2;
                minX = Math.Min(minX, (int)Math.Floor(Math.Min(hx, tx)) - reach);
                minY = Math.Min(minY, (int)Math.Floor(Math.Min(hy, ty)) - reach);
                maxX = Math.Max(maxX, (int)Math.Ceiling(Math.Max(hx, tx)) + reach);
                maxY = Math.Max(maxY, (int)Math.Ceiling(Math.Max(hy, ty)) + reach);
            }
        }

        return n;
    }

    private static (double X, double Y) Pixel(double4 clip, int width, int height)
        => (((clip.X / clip.W * 0.5) + 0.5) * width, ((clip.Y / clip.W * 0.5) + 0.5) * height);
}
