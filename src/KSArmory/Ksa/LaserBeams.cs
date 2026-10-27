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

    private readonly record struct Firing(Vehicle Craft, double3 From, double3 To, double AirAtStart, double AirAtEnd,
                                          bool Burning, bool OpenEnded, bool InCloud, double3 Up);

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

        double3 up = KsaWorld.ParentBody(craft) is { } body ? Vec.Unit(head.BeamToEcl - body.GetPositionEcl()) : Vec.Zero;

        _beams.Add(new Firing(craft, head.BeamFromEcl - head.PlatformEcl, head.BeamToEcl - head.PlatformEcl,
                              Air(KsaWorld.AirDensityRatioAt(craft, head.BeamFromEcl)),
                              Air(KsaWorld.AirDensityRatioAt(craft, head.BeamToEcl)),
                              head.Burning is not null, head.Spot is null && !head.InCloud, head.InCloud,
                              Vec.IsFinite(up) ? up : Vec.Zero));
    }

    private static double Air(double ratio) => double.IsFinite(ratio) ? Math.Max(ratio, 0.0) : 0.0;

    public static void MarkPainted() => _sincePainted.Restart();

    /// <summary>Ends relative to the camera drawing this frame; <paramref name="up"/> is zero where there is no body to say.</summary>
    public static bool TryAt(int index, out double3 fromEgo, out double3 toEgo, out double airAtStart,
                             out double airAtEnd, out bool burning, out bool openEnded, out bool inCloud,
                             out double3 up)
    {
        fromEgo = toEgo = up = Vec.Zero;
        airAtStart = airAtEnd = 0.0;
        burning = openEnded = inCloud = false;

        Firing beam = _beams[index];
        if (!KsaWorld.TryVehicleEgo(beam.Craft, out double3 craftEgo)) return false;

        fromEgo = craftEgo + beam.From;
        toEgo = craftEgo + beam.To;
        airAtStart = beam.AirAtStart;
        airAtEnd = beam.AirAtEnd;
        burning = beam.Burning;
        openEnded = beam.OpenEnded;
        inCloud = beam.InCloud;
        up = beam.Up;
        return Vec.IsFinite(fromEgo) && Vec.IsFinite(toEgo);
    }
}
