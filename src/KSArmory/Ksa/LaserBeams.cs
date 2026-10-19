using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The high-energy beams firing this frame, for <see cref="CloudPass"/> to add into the scene as
/// <c>Shaders/KSArmoryBeam.comp</c>.
///
/// <para>Each beam is held as its two ends' offsets from the craft firing it, measured against one
/// platform sample in the step, and anchored where the engine draws that craft when the pass
/// records — the pairing <see cref="Visuals"/> uses, so the beam leaves the turret it is drawn on.</para>
/// </summary>
internal static class LaserBeams
{
    private const double StaleSeconds = 0.5;

    private readonly record struct Firing(Vehicle Craft, double3 From, double3 To, double AirDensity, bool Burning);

    private static readonly List<Firing> _beams = [];
    private static readonly System.Diagnostics.Stopwatch _sinceCollected = new();
    private static readonly System.Diagnostics.Stopwatch _sincePainted = new();

    /// <summary>Whether the pass drew beams recently enough that the gizmo line need not.</summary>
    public static bool Painting => _sincePainted.IsRunning && _sincePainted.Elapsed.TotalSeconds < StaleSeconds;

    public static int Count => _sinceCollected.Elapsed.TotalSeconds > StaleSeconds ? 0 : _beams.Count;

    public static void BeginFrame()
    {
        _beams.Clear();
        _sinceCollected.Restart();
    }

    public static void Collect(OpticalHead head)
    {
        if (!head.BeamOn || head.Platform is not { } craft || !KsaWorld.IsAlive(craft)) return;

        double density = 0.5 * (KsaWorld.AirDensityRatioAt(craft, head.BeamFromEcl)
                                + KsaWorld.AirDensityRatioAt(craft, head.BeamToEcl));
        _beams.Add(new Firing(craft, head.BeamFromEcl - head.PlatformEcl, head.BeamToEcl - head.PlatformEcl,
                              double.IsFinite(density) ? Math.Max(density, 0.0) : 0.0, head.Burning is not null));
    }

    public static void MarkPainted() => _sincePainted.Restart();

    /// <summary>
    /// One beam's ends, relative to the camera drawing this frame, the mean air density ratio along it,
    /// and whether it is burning what it lands on.
    /// </summary>
    public static bool TryAt(int index, out double3 fromEgo, out double3 toEgo, out double airDensity,
                             out bool burning)
    {
        fromEgo = toEgo = Vec.Zero;
        airDensity = 0.0;
        burning = false;

        Firing beam = _beams[index];
        if (!KsaWorld.TryVehicleEgo(beam.Craft, out double3 craftEgo)) return false;

        fromEgo = craftEgo + beam.From;
        toEgo = craftEgo + beam.To;
        airDensity = beam.AirDensity;
        burning = beam.Burning;
        return Vec.IsFinite(fromEgo) && Vec.IsFinite(toEgo);
    }
}
