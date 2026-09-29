using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The pipper: where a store released now would land, and the arc it would take there.
///
/// <para>Flown rather than solved — see <see cref="BombSight"/> — so the ring is wherever the bomb
/// will actually go, drag and terrain and all. The alternative is a tidier prediction than the
/// round obeys, which is a sight that lies at exactly the moment it matters.</para>
///
/// <para>Solved every frame. It is BombSight.MaxSteps integration steps, each sub-stepped by the round, but the terrain lookup that
/// would make that expensive only happens near the ground — see <see cref="CoarseGroundTest"/> —
/// and solving continuously is what makes the pipper move like a sight rather than step like a
/// clock. Anything cached between frames has to be carried forward correctly, and every way of
/// getting that wrong puts the ring somewhere it is not.</para>
/// </summary>
internal sealed class BombSightOverlay
{
    // The integration step. At terminal velocity a bomb crosses 55 m in a fifth of a second, so a
    // coarse step quantises the impact point to that and the ring hops between two places.
    private const double IntegrationStep = 0.05;

    private const int ArcRibs = 48;

    private static readonly float4 ArcColour = new(1.0f, 0.75f, 0.15f, 1f);
    private static readonly float4 RingColour = new(1.0f, 0.45f, 0.10f, 1f);

    // The same orange painted on the ground, its alpha the brightness it keeps over dark ground.
    private static readonly float4 PaintedRing = new(1.0f, 0.45f, 0.10f, 0.05f);

    // The arc, as offsets from the platform sample it was solved against -- never as ecliptic
    // positions.
    //
    // DrawAnchor cancels exactly one frame of the planet's motion, against the platform sample of
    // the update that measured the geometry. This is measured once and drawn for a quarter of a
    // second, so held absolutely it accumulates 29.8 km/s of ecliptic motion between solves: up to
    // 7.5 km of drift that resets on every refresh, which reads as the sight flashing across the
    // screen. Interceptor's smoke trail is stored this way for the same reason.
    //
    // Relative to the *platform* rather than the ground, so the top of the arc stays on the
    // aircraft that would drop the bomb.
    private readonly List<double3> _path = [];
    private readonly List<double3> _next = [];
    private bool _solved;

    private const double TraceSeconds = 2.0;
    private double _sinceTrace;

    // Its own, because it caches across a trajectory and two sights solving at once must not share
    // one another's last sample.
    private readonly CoarseGroundTest _ground = new(GroundTest.Shared);

    // The impact, stored the same way and for a second reason beyond the drift.
    //
    // This is where a bomb released *now* would land, not where a released one is going. In steady
    // flight that answer translates with the aircraft exactly -- same velocity, same fall, so the
    // impact moves with the release point -- which means holding it platform-relative tracks it
    // between solves for free. Anchoring it to the ground instead freezes it and makes it jump the
    // aircraft's travel on every refresh: 50 m at 200 m/s, four times a second.
    private double3 _impactOffset;

    /// <summary>Forgets the solution, so a system that stops carrying a bomb stops drawing one.</summary>
    public void Clear()
    {
        _solved = false;
        _path.Clear();
    }

    // How often the sight is re-solved, in REAL seconds.
    //
    // One solve flies a whole BombSight.MaxSteps trajectory with a ground lookup per step, and
    // there is one sight per weapons system -- so an unrated solve is 2048 steps times however
    // many craft are armed, every frame, for a readout.
    //
    // Wall clock, not simulated: a solve is a computation budget rather than physics, so under
    // timewarp a simulated interval passes every frame and a rate limit built on one stops
    // limiting. Same trap IcbmState.PlayerStepSeconds documents.
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

        // Only a store that is released. A round that flies under its own power goes where it is
        // steered, so a ballistic pipper would answer the wrong question.
        //
        // A guided tail kit still gets one: it is released and then falls, and the pipper says
        // where it lands if nothing is designated -- which is the release cue either way.
        if (system.Munition.Powered) { Clear(); return; }

        if (!TryReleaseState(system, platform, out double3 releaseEcl, out double3 releaseVelocity))
        {
            Clear();
            return;
        }

        // Into a scratch list, so a failed solve leaves the last good one on screen. A sight that
        // blanks for a frame reads as broken, and the answer it had a quarter of a second ago is
        // still very nearly right.
        bool ok = Fly(system, platform, releaseEcl, releaseVelocity, _next, out double3 impact);

        // A store that cannot reach the ground inside BombSight.MaxSteps has no pipper, and it will
        // still have none a frame later -- so back off rather than re-flying 2048 steps at the
        // solve rate for a shot that can never answer.
        //
        // The ballistic case is why this is needed. The gate above asks whether the round is
        // *powered*, and a Mk 21 reentry vehicle is not, so it passes -- but the sight's horizon is
        // MaxSteps x IntegrationStep, about 102 s, and a warhead released minutes above its target
        // falls for far longer. Such a store can never have a pipper.
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

        // What the correction is worth, against how far the answer ended up from the craft. If the
        // ring is ever wrong again these two numbers say whether the frame or the flight is to
        // blame.
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

        // Flown in the ground's frame, not the ecliptic's, and that is the whole trick.
        //
        // Ecliptic velocities here are ~29.8 km/s of Earth's orbit, so a round integrated in them
        // moves 1.5 km per step -- while GroundTest resolves each predicted position against the
        // planet where it is *now*, which has not moved. One step in, the round reads as being a
        // kilometre and a half underground and the trajectory ends immediately, leaving the pipper
        // at the release point plus one step of orbital motion: kilometres out, fixed to the
        // ecliptic, and indifferent to which way the craft is pointing.
        //
        // Taking the release velocity relative to the ground removes the carrier. What is left is
        // the motion the ground sees, the predicted positions stay next to the planet the terrain
        // is sampled from, and the path comes out already in the frame it has to be drawn in.
        // The airspeed is unchanged: the frame's velocity is subtracted here instead of inside.
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
                                out impactEcl);

    // The arc as an anti-aliased line two pixels wide with a dark edge, to match the rings painted
    // under it: the same points the gizmo line takes, through the anchor BeginDraw set, projected
    // and drawn on the list under the panel. Not hidden by terrain or the craft, which the gizmo
    // line was; the arc is in the air above the ground it lands on, so that rarely shows.
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
        => _screen.Add(KsaWorld.TryEclToEgo(pointEcl, out double3 ego) && KsaWorld.TryProjectEgo(ego, out float2 at)
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

        // Every rib would be a line per 0.2 s of fall, which is hundreds on a high drop and
        // unreadable. Thinning keeps the arc the same shape and a fraction of the cost.
        // Put back against *this* update's platform sample, which is the one BeginDraw anchored
        // to. Measured and drawn against the same sample, the difference is the offset exactly and
        // carries none of the motion between solves.
        double3 here = system.PlatformEcl;

        int stride = Math.Max(1, _path.Count / ArcRibs);

        if (GroundRings.Painting)
        {
            DrawArcOnScreen(here, stride);
        }
        else
        {
            for (int i = stride; i < _path.Count; i += stride)
            {
                KsaWorld.DrawLineEcl(here + _path[i - stride], here + _path[i], ArcColour);
            }

            KsaWorld.DrawLineEcl(here + _path[^Math.Min(_path.Count, stride + 1)],
                                 here + _path[^1], ArcColour);
        }

        // Draped on the terrain, so the ring reads as a place on the ground rather than a disc
        // floating over it.
        // Radial at the impact, which is what a ring lying on the ground is flat against. Taken
        // off gravity because that is the one direction the mod already resolves everywhere.
        double3 impactEcl = here + _impactOffset;

        double3 up = Vec.Unit(KsaWorld.GravityAt(platform, impactEcl) * -1.0);
        if (Vec.Len2(up) < 0.5) return;

        // The store's own lethal radius, so what the ring circles is what the bomb reaches.
        double radius = Warhead.LethalRadius(system.Munition.ChargeKg);

        if (GroundRings.Painting && GroundRings.Add(impactEcl, radius, radius * 0.15, PaintedRing)) return;

        KsaWorld.DrawCircleEcl(impactEcl, up, radius, RingColour);
        KsaWorld.DrawCircleEcl(impactEcl, up, radius * 0.15, RingColour, segments: 16);
    }
}
