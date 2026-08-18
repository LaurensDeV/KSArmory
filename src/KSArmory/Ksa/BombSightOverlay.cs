using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The pipper: where a store released now would land, and the arc it would take there.
///
/// <para>Flown rather than solved — see <see cref="BombSight"/> — so the ring is wherever the bomb
/// will actually go, drag and terrain and all. The alternative is a tidier prediction than the
/// round obeys, which is a sight that lies at exactly the moment it matters.</para>
///
/// <para>Solved every frame. It is a few hundred integration steps, but the terrain lookup that
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

    public void Update(WeaponSystem battery, double dtSim)
    {
        if (battery.Platform is not { } platform || battery.Launcher is null) return;

        // Only a store that is released. A round that flies under its own power goes where it is
        // steered, so a ballistic pipper would answer the wrong question.
        //
        // A guided tail kit still gets one: it is released and then falls, and the pipper says
        // where it lands if nothing is designated -- which is the release cue either way.
        if (battery.Munition.Powered) { Clear(); return; }

        if (!LauncherPart.TryGetTubeMuzzleEcl(platform, battery.Launcher, battery.PodsPart,
                                              battery.Profile, 0, battery.PlatformEcl,
                                              out double3 releaseEcl))
        {
            Clear();
            return;
        }

        double3 tubeEcl = LauncherPart.TryGetTubeAxisEcl(platform, battery.Launcher,
                                                         battery.PodsPart, battery.Profile, 0,
                                                         out double3 axis)
                              ? axis
                              : battery.Boresight;

        double3 craftVel = KsaWorld.VelocityEcl(platform);
        double3 groundVel = KsaWorld.GroundVelocityAt(platform, battery.PlatformEcl);

        // Into a scratch list, so a failed solve leaves the last good one on screen. A sight that
        // blanks for a frame reads as broken, and the answer it had a quarter of a second ago is
        // still very nearly right.
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
        bool ok = BombSight.TryPredict(
            releaseEcl,
            craftVel - groundVel + tubeEcl * battery.Munition.LaunchSpeed,
            Vec.Zero,
            battery.Munition,
            at => KsaWorld.GravityAt(platform, at),
            at => KsaWorld.MediumDensityRatioAt(platform, at),
            Ground(),
            IntegrationStep,
            _next,
            out double3 impact);

        if (!ok) return;

        // Already in the ground's frame, so these are plain offsets from the craft.
        _path.Clear();
        for (int i = 0; i < _next.Count; i++) _path.Add(_next[i] - battery.PlatformEcl);

        double flightSeconds = Math.Max(0, _next.Count - 1) * IntegrationStep;
        _impactOffset = impact - battery.PlatformEcl;
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
                + $"speed over the ground {Vec.Len(craftVel - groundVel):F0} m/s, "
                + $"impact {Vec.Len(_impactOffset):F0} m from the craft");
        }
    }

    // Reset per solve: the cache exists to skip lookups down one trajectory, not to remember the
    // last one, and a sample kept from the previous frame's fall would be trusted from the wrong
    // place.
    private IGroundTest Ground()
    {
        _ground.Reset();
        return _ground;
    }

    public void Draw(WeaponSystem battery)
    {
        if (!_solved || _path.Count < 2) return;
        if (battery.Platform is not { } platform) return;
        if (!KsaWorld.BeginDraw(platform, battery.PlatformEcl)) return;

        // Every rib would be a line per 0.2 s of fall, which is hundreds on a high drop and
        // unreadable. Thinning keeps the arc the same shape and a fraction of the cost.
        // Put back against *this* update's platform sample, which is the one BeginDraw anchored
        // to. Measured and drawn against the same sample, the difference is the offset exactly and
        // carries none of the motion between solves.
        double3 here = battery.PlatformEcl;

        int stride = Math.Max(1, _path.Count / ArcRibs);

        for (int i = stride; i < _path.Count; i += stride)
        {
            KsaWorld.DrawLineEcl(here + _path[i - stride], here + _path[i], ArcColour);
        }

        KsaWorld.DrawLineEcl(here + _path[^Math.Min(_path.Count, stride + 1)],
                             here + _path[^1], ArcColour);

        // Draped on the terrain, so the ring reads as a place on the ground rather than a disc
        // floating over it.
        // Radial at the impact, which is what a ring lying on the ground is flat against. Taken
        // off gravity because that is the one direction the mod already resolves everywhere.
        double3 impactEcl = here + _impactOffset;

        double3 up = Vec.Unit(KsaWorld.GravityAt(platform, impactEcl) * -1.0);
        if (Vec.Len2(up) < 0.5) return;

        // The store's own lethal radius, so what the ring circles is what the bomb reaches.
        double radius = Warhead.LethalRadius(battery.Munition.ChargeKg);

        KsaWorld.DrawCircleEcl(impactEcl, up, radius, RingColour);
        KsaWorld.DrawCircleEcl(impactEcl, up, radius * 0.15, RingColour, segments: 16);
    }
}
