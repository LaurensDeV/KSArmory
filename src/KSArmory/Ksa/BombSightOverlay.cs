using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The pipper: where a store released now would land, and the arc it would take there, flown by
/// <see cref="BombSight"/> rather than solved.
/// <para>Re-solved every <see cref="SolveIntervalSeconds"/> of wall clock and held between solves as
/// offsets from the platform, which carry it with the aircraft.</para>
/// </summary>
internal sealed class BombSightOverlay
{
    // The integration step. At terminal velocity a bomb crosses 55 m in a fifth of a second, so a
    // coarse step quantises the impact point to that and the ring hops between two places.
    private const double IntegrationStep = 0.05;

    private const int ArcRibs = 48;

    // The pipper's orange, its alpha the brightness it keeps over dark ground.
    private static readonly float4 PaintedRing = new(1.0f, 0.45f, 0.10f, 0.05f);

    // Offsets from the platform sample it was solved against, never ecliptic positions: DrawAnchor
    // cancels one frame of the planet's motion, and a solve held 0.2 s would drift kilometres.
    private readonly List<double3> _path = [];
    private readonly List<double3> _next = [];
    private bool _solved;

    private const double TraceSeconds = 2.0;
    private double _sinceTrace;

    // Its own, because it caches across a trajectory and two sights solving at once must not share
    // one another's last sample.
    private readonly CoarseGroundTest _ground = new(GroundTest.Shared);

    // Platform-relative too: in steady flight where a bomb released now lands moves with the aircraft,
    // and a ground anchor would jump its travel on every solve, 40 m at 200 m/s.
    private double3 _impactOffset;

    /// <summary>Forgets the solution, so a system that stops carrying a bomb stops drawing one.</summary>
    public void Clear()
    {
        _solved = false;
        _path.Clear();
    }

    // Wall clock: a solve is up to BombSight.MaxSteps steps per armed system, a computation budget
    // rather than physics, and under timewarp a simulated interval passes every frame.
    private const double SolveIntervalSeconds = 0.2;

    // How long to wait before asking again once the store has been found unsightable. Long, but
    // not never: an aircraft that climbs out of range comes back down.
    private const double UnreachableIntervalSeconds = 2.0;

    private readonly System.Diagnostics.Stopwatch _sinceSolve = System.Diagnostics.Stopwatch.StartNew();
    private bool _unreachable;

    public void Update(WeaponSystem system, double dtSim)
    {
        if (system.Platform is not { } platform || system.Launcher is null) return;

        // Held between solves, so what was drawn last time keeps being drawn.
        double wanted = _unreachable ? UnreachableIntervalSeconds : SolveIntervalSeconds;

        if (_sinceSolve.Elapsed.TotalSeconds < wanted) return;

        _sinceSolve.Restart();

        // A powered round goes where it is steered. A tail kit still gets a pipper: it says where the
        // store lands if nothing is designated.
        if (system.Munition.Powered) { Clear(); return; }

        if (!TryReleaseState(system, platform, out double3 releaseEcl, out double3 releaseVelocity))
        {
            Clear();
            return;
        }

        // Into a scratch list, so a failed solve leaves the last good one on screen.
        bool ok = Fly(system, platform, releaseEcl, releaseVelocity, _next, out double3 impact);

        // No landing inside the horizon, MaxSteps x IntegrationStep (about 102 s), so back off: a
        // reentry vehicle released minutes above its target never has a pipper.
        if (!ok)
        {
            _unreachable = true;
            Clear();
            return;
        }

        _unreachable = false;

        // Already in the ground's frame, so these are plain offsets from the craft.
        _path.Clear();
        for (int i = 0; i < _next.Count; i++) _path.Add(_next[i] - system.PlatformEcl);

        double flightSeconds = Math.Max(0, _next.Count - 1) * IntegrationStep;
        _impactOffset = impact - system.PlatformEcl;
        _solved = true;

        _sinceTrace += Math.Abs(dtSim);
        if (_sinceTrace >= TraceSeconds)
        {
            _sinceTrace = 0.0;
            Log.Debug(() =>
                $"bomb sight: {flightSeconds:F1} s of fall, "
                + $"release speed over the ground {Vec.Len(releaseVelocity):F0} m/s, "
                + $"impact {Vec.Len(_impactOffset):F0} m from the craft");
        }
    }

    /// <summary>
    /// Where a store released this instant would land, solved now rather than read off the ring,
    /// which can be <see cref="SolveIntervalSeconds"/> old.
    /// </summary>
    public bool TryPredictNow(WeaponSystem system, out double3 impactEcl)
    {
        impactEcl = Vec.Zero;
        if (system.Platform is not { } platform || system.Munition.Powered) return false;

        return TryReleaseState(system, platform, out double3 releaseEcl, out double3 velocity)
               && Fly(system, platform, releaseEcl, velocity, _next, out impactEcl);
    }

    /// <summary>
    /// The same flight from a release state handed in rather than read off the rack. Given the state
    /// a round actually left with, the difference from <see cref="TryPredictNow"/> is exactly what
    /// the sight assumes about the release.
    /// </summary>
    public bool TryPredictFrom(WeaponSystem system, double3 releaseEcl, double3 velocityOverGround,
                               out double3 impactEcl)
    {
        impactEcl = Vec.Zero;
        return system.Platform is { } platform
               && Fly(system, platform, releaseEcl, velocityOverGround, _next, out impactEcl);
    }

    private static bool TryReleaseState(WeaponSystem system, Vehicle platform,
                                        out double3 releaseEcl, out double3 velocityOverGround)
    {
        velocityOverGround = Vec.Zero;
        if (!system.TryNextReleaseEcl(out releaseEcl, out double3 velocityEcl)) return false;

        // Flown in the ground's frame: GroundTest resolves each predicted point against the planet as
        // sampled now, and in the ecliptic a step carries 29.8 km/s of orbit, 1.5 km, underground.
        velocityOverGround = velocityEcl - KsaWorld.GroundVelocityAt(platform, system.PlatformEcl);
        return true;
    }

    private bool Fly(WeaponSystem system, Vehicle platform, double3 releaseEcl,
                     double3 velocityOverGround, List<double3> path, out double3 impactEcl)
        => BombSight.TryPredict(releaseEcl, velocityOverGround,
                                KsaWorld.GroundVelocityAt(platform, system.PlatformEcl),
                                KsaWorld.GroundAccelerationAt(platform, system.PlatformEcl),
                                KsaWorld.BodyVelocityAt(platform),
                                at => KsaWorld.GroundVelocityAt(platform, at),
                                system.Munition,
                                at => KsaWorld.GravityAt(platform, at),
                                at => KsaWorld.MediumDensityRatioAt(platform, at),
                                Ground(), BombSight.StepFor(system.Munition, IntegrationStep), path,
                                out impactEcl,
                                inheritedOverGround: KsaWorld.VelocityEcl(platform)
                                                     - KsaWorld.GroundVelocityAt(platform, system.PlatformEcl));

    // Through the anchor BeginDraw set, on the list under the panel; not depth-tested, which the arc
    // in the air above its landing rarely shows.
    private void DrawArcOnScreen(double3 here, int stride)
    {
        _screen.Clear();

        for (int i = 0; i < _path.Count; i += stride) AddScreenPoint(here + _path[i]);
        AddScreenPoint(here + _path[^1]);

        ImDrawListPtr draw = ImGui.GetBackgroundDrawList();
        for (int pass = 0; pass < 2; pass++)
        {
            uint colour = pass == 0 ? ArcEdge : ArcInk;
            float width = pass == 0 ? 4f : 2f;

            for (int i = 1; i < _screen.Count; i++)
            {
                if (_screen[i - 1] is { } a && _screen[i] is { } b) draw.AddLine(a, b, colour, width);
            }
        }
    }

    // A point behind the camera breaks the line there rather than folding it across the screen.
    private void AddScreenPoint(double3 pointEcl)
        => _screen.Add(KsaWorld.TryAnchoredEgo(pointEcl, out double3 ego) && KsaWorld.TryProjectEgo(ego, out float2 at)
                           ? at
                           : null);

    private readonly List<float2?> _screen = [];

    private static readonly uint ArcInk = ImGui.ColorConvertFloat4ToU32(new float4(1.0f, 0.75f, 0.15f, 1f));
    private static readonly uint ArcEdge = ImGui.ColorConvertFloat4ToU32(new float4(0f, 0f, 0f, 0.55f));

    // Reset per solve: the cache exists to skip lookups down one trajectory, not to remember the
    // last one, and a sample kept from the previous frame's fall would be trusted from the wrong
    // place.
    private IGroundTest Ground()
    {
        _ground.Reset();
        return _ground;
    }

    public void Draw(WeaponSystem system)
    {
        if (!_solved || _path.Count < 2) return;
        if (system.Platform is not { } platform) return;
        if (!KsaWorld.BeginDraw(platform, system.PlatformEcl)) return;

        // This update's platform sample, the one BeginDraw anchored to.
        double3 here = system.PlatformEcl;

        int stride = Math.Max(1, _path.Count / ArcRibs);

        DrawArcOnScreen(here, stride);

        double3 impactEcl = here + _impactOffset;

        // The store's own lethal radius, so what the ring circles is what the bomb reaches.
        double radius = Warhead.LethalRadius(system.Munition.ChargeKg);

        GroundRings.Add(impactEcl, radius, radius * 0.15, PaintedRing);
    }
}
