using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What a store already in the air can still do about where it lands: where it is coming down, and
/// the region on the ground it can still be walked into.
///
/// <para><b>One answer, three surfaces.</b> The ring on the ground, the line on the panel and the
/// clause logged when a designation is handed to a falling store all read this — the same rule
/// <see cref="ReachDisplay"/> obeys for the bus, and for the same reason: a player who is told one
/// thing by the ring and another by the log stops believing both.</para>
///
/// <para>Not to be confused with <see cref="RoundReach"/>, which is the reaper deciding whether a
/// round the ground will never stop should be taken out of the world. This is about steering.</para>
///
/// <para><b>What it costs, and when.</b> Three <see cref="BombSight"/> flights per solve, which is
/// three times the pipper — and the pipper was measured at 5.41 ms of a 7.17 ms draw over eight
/// rockets. So it is solved only while a store that steers its own fall is actually in the air,
/// which is one bomb for half a minute rather than a rocket's whole ascent, and at half the
/// pipper's rate. <see cref="Update"/> is where that is enforced.</para>
/// </summary>
internal sealed class StoreReach
{
    // Coarser than the pipper's 0.05, and it costs nothing measurable. The round sub-steps at 5 ms
    // whatever this is, so the outer step sets how often the ground is sampled rather than how the
    // fall is integrated: flown at 0.05, 0.10, 0.20 and 0.40 the radius moves under a metre on
    // answers of 634, 1610 and 2898 m, while the terrain lookups fall 8x. The lookups are the half
    // that costs in game -- 2395 of them per solve at 0.05 -- and a trivial ground test is what
    // hides that headlessly.
    private const double IntegrationStep = 0.20;

    // Half the pipper's rate. This is three flights rather than one, and unlike the pipper it is
    // not tracking a moving release point -- the landing of a store already gone is a place on the
    // ground, and it barely moves between solves.
    private const double SolveIntervalSeconds = 0.4;

    // How long to wait before asking again once a store has been found unsolvable. A store above
    // the atmosphere on a long coast has no landing inside the sight's horizon and will not have
    // one for a while, and re-flying three trajectories at the solve rate to be told so again is
    // the whole cost of this feature spent on nothing.
    private const double UnsolvableIntervalSeconds = 2.0;

    private static readonly float4 ImpactColour = new(1.0f, 0.45f, 0.10f, 1f);
    private static readonly float4 ReachColour = new(0.45f, 0.85f, 1.0f, 0.75f);

    // Its own, because it caches down one trajectory and the three flights here must not be shown
    // one another's last sample -- they deliberately end up in different places.
    private readonly CoarseGroundTest _ground = new(GroundTest.Shared);
    private readonly List<double3> _path = [];

    private readonly System.Diagnostics.Stopwatch _sinceSolve = System.Diagnostics.Stopwatch.StartNew();
    private bool _unsolvable;

    // Which of the three flights this tick runs, and what the other two last found. Round-robin
    // rather than all at once: they are independent, so one per tick is a third of the lump on
    // any one frame and the whole answer is still refreshed every three ticks.
    private int _stage;
    private TailKitReach _building;
    private double _along;
    private double _across;

    // The landing as a place on the ground rather than as an ecliptic point or an offset from the
    // craft, and it has to be: it is a place on the ground. Held absolutely it is left behind by
    // ~29.8 km/s between solves, and held against the platform it is dragged along by an aircraft
    // doing 250 m/s -- 100 m over one solve interval, on the one mark the player is judging the
    // shot by. TargetLock anchors a designation the same way.
    private object? _body;
    private double3 _anchor;

    // The two rings as offsets from the landing, draped once per solve rather than per frame.
    //
    // Draping asks the terrain where every segment sits, and both rings together are 96 lookups.
    // Paid every frame that was 1.52 ms of a 16.7 ms budget -- more than the three flown
    // trajectories behind it, for a shape that does not move: a ring on the ground is a place on
    // the ground, and the ground is where it was. Offsets rather than positions for the reason
    // CollectDrapedCircleEcl gives: an absolute point carries the planet's motion and is left
    // behind within one frame.
    private readonly List<double3> _impactRing = [];
    private readonly List<double3> _reachRing = [];

    // Fewer on the inner one: it is the store's own lethal radius, a few hundred metres, and a
    // circle that small is smooth long before the outer one is.
    private const int ImpactSegments = 32;
    private const int ReachSegments = 64;

    /// <summary>What the last solve found. <c>Unreadable</c> until one has landed.</summary>
    public TailKitReach Latest { get; private set; }

    /// <summary>Forgets it, so a system with nothing falling draws nothing.</summary>
    public void Clear()
    {
        Latest = default;
        _body = null;
        _impactRing.Clear();
        _reachRing.Clear();

        // The part-built answer goes with it, or the next store inherits this one's axes.
        _building = default;
        _along = 0.0;
        _across = 0.0;
        _stage = 0;
    }

    /// <summary>
    /// Re-solves for the first store in the air that steers its own fall, at most every
    /// <see cref="SolveIntervalSeconds"/>.
    ///
    /// <para>The first rather than all of them. The shipped rack holds one bomb, so this is the
    /// whole answer today; a rack that held several would want a region each, and the thing to fix
    /// then is this loop rather than anything below it.</para>
    /// </summary>
    public void Update(WeaponSystem battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        if (FallingStore(battery) is not { } store) { Clear(); return; }

        if (_sinceSolve.Elapsed.TotalSeconds < (_unsolvable ? UnsolvableIntervalSeconds
                                                            : SolveIntervalSeconds))
        {
            return;
        }

        _sinceSolve.Restart();

        // With nothing on screen yet, fly all three at once rather than making the player wait
        // three ticks for a ring to appear. The staging exists to keep a recurring lump off the
        // frame, not to withhold the first answer.
        TailKitReach reach = Solve(battery, store, _ground, _path, Latest.Known ? 1 : 3);
        _unsolvable = reach.Hold == TailKitHold.NoLanding;

        // Every failure below leaves the last good answer standing, exactly as the pipper does.
        // None of them clears: a reset costs three ticks to rebuild, so a transient miss would take
        // the rings away for more than a second rather than for a frame.
        if (!reach.Known) return;
        if (!KsaWorld.TryAnchorToGround(reach.ImpactEcl, out object? body, out double3 anchor)) return;
        if (battery.Platform is not { } craft) return;

        double3 up = Vec.Unit(-KsaWorld.GravityAt(craft, reach.ImpactEcl));
        if (Vec.Len2(up) < 0.5) return;

        // Published together, and it has to be. The anchor is what the rings are drawn around and
        // the offsets are measured from the landing it came from, so writing one without the other
        // draws this solve's ring around the last solve's landing.
        Latest = reach;
        _body = body;
        _anchor = anchor;

        KsaWorld.CollectDrapedCircleEcl(reach.ImpactEcl, up, Warhead.LethalRadius(battery.Munition.ChargeKg),
                                        _impactRing, ImpactSegments);
        KsaWorld.CollectDrapedCircleEcl(reach.ImpactEcl, up, reach.RadiusMetres,
                                        _reachRing, ReachSegments);
    }

    /// <summary>The store this is about, or null.</summary>
    public static IProjectile? FallingStore(WeaponSystem battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        foreach (IProjectile round in battery.Rounds)
        {
            if (round.State == RoundState.Flying && round.Munition.SteersItsFall) return round;
        }

        return null;
    }

    /// <summary>
    /// The region for one store, flown now rather than read off a solve that can be
    /// <see cref="SolveIntervalSeconds"/> old.
    ///
    /// <para>What <c>WeaponSystem.Designate</c> asks, because a click deserves an answer about the
    /// world as it is at the click — and all three flights at once, where <see cref="Update"/>
    /// spreads them. A 7 ms lump on the frame somebody pressed a button is nothing; the same lump
    /// arriving unbidden every <see cref="SolveIntervalSeconds"/> is what that split avoids.</para>
    /// </summary>
    public static TailKitReach SolveNow(WeaponSystem battery, IProjectile round)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(round);

        if (battery.Platform is not { } platform) return default;

        double3 at = round.PositionEcl;
        double3 overGround = round.VelocityEcl - KsaWorld.GroundVelocityAt(platform, at);

        return TailKitReach.Fly(at, overGround,
                                KsaWorld.GroundVelocityAt(platform, at),
                                KsaWorld.GroundAccelerationAt(platform, at),
                                KsaWorld.BodyVelocityAt(platform),
                                p => KsaWorld.GroundVelocityAt(platform, p),
                                round.Munition,
                                p => KsaWorld.GravityAt(platform, p),
                                p => KsaWorld.MediumDensityRatioAt(platform, p),
                                new CoarseGroundTest(GroundTest.Shared), IntegrationStep, []);
    }

    private TailKitReach Solve(WeaponSystem battery, IProjectile round,
                               CoarseGroundTest ground, List<double3> path, int passes)
    {
        if (battery.Platform is not { } platform) return default;

        // Every frame term is read at the store rather than at the rack. A bomb released from
        // altitude is kilometres from its launcher, through thinner air and over ground turning at
        // a different rate, and the whole answer is a fall time squared.
        double3 at = round.PositionEcl;
        double3 overGround = round.VelocityEcl - KsaWorld.GroundVelocityAt(platform, at);

        ground.Reset();

        for (int i = 0; i < passes; i++)
        {
            _building = TailKitReach.FlyStage(_stage, _building, at, overGround,
                                              KsaWorld.GroundVelocityAt(platform, at),
                                              KsaWorld.GroundAccelerationAt(platform, at),
                                              KsaWorld.BodyVelocityAt(platform),
                                              p => KsaWorld.GroundVelocityAt(platform, p),
                                              round.Munition,
                                              p => KsaWorld.GravityAt(platform, p),
                                              p => KsaWorld.MediumDensityRatioAt(platform, p),
                                              ground, IntegrationStep, path,
                                              ref _along, ref _across);

            _stage = (_stage + 1) % 3;
        }

        return _building;
    }

    /// <summary>
    /// The landing, and the circle the kit can still walk it inside.
    ///
    /// <para>Drawn even when a designation is already inside the region, because what the ring says
    /// is how much is <em>left</em> — it shrinks as the square of the time to go, and watching it
    /// close is the only thing that tells an operator whether there is still time to change their
    /// mind.</para>
    /// </summary>
    public void Draw(WeaponSystem battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        if (!Latest.Known || battery.Platform is not { } platform) return;
        if (!KsaWorld.TryGroundAnchorEcl(_body, _anchor, out double3 impactEcl, out _)) return;
        if (!KsaWorld.BeginDraw(platform, battery.PlatformEcl)) return;

        // What the store reaches, so the inner ring means the same thing the pipper's does; and
        // around it the region it can still be walked into, in its own colour, because one says
        // where it is going and the other how much choice is left.
        Ring(_impactRing, impactEcl, ImpactColour);
        Ring(_reachRing, impactEcl, ReachColour);
    }

    // Put back against this frame's landing, which is the sample the offsets were measured from.
    private static void Ring(List<double3> offsets, double3 centreEcl, float4 colour)
    {
        for (int i = 1; i < offsets.Count; i++)
        {
            KsaWorld.DrawLineEcl(centreEcl + offsets[i - 1], centreEcl + offsets[i], colour);
        }
    }
}
