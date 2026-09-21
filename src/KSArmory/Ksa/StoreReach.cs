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
    // The integration step, and BombSightOverlay's for the same reason: at terminal velocity a
    // bomb crosses 55 m in a fifth of a second, so a coarser step quantises the answer to that.
    private const double IntegrationStep = 0.05;

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

    // The landing as a place on the ground rather than as an ecliptic point or an offset from the
    // craft, and it has to be: it is a place on the ground. Held absolutely it is left behind by
    // ~29.8 km/s between solves, and held against the platform it is dragged along by an aircraft
    // doing 250 m/s -- 100 m over one solve interval, on the one mark the player is judging the
    // shot by. TargetLock anchors a designation the same way.
    private object? _body;
    private double3 _anchor;

    /// <summary>What the last solve found. <c>Unreadable</c> until one has landed.</summary>
    public TailKitReach Latest { get; private set; }

    /// <summary>Forgets it, so a system with nothing falling draws nothing.</summary>
    public void Clear()
    {
        Latest = default;
        _body = null;
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

        TailKitReach reach = Solve(battery, store, _ground, _path);
        _unsolvable = !reach.Known;

        // A failed solve leaves the last good answer standing, exactly as the pipper does. A ring
        // that blanks for a frame reads as broken, and the answer from half a second ago is still
        // very nearly right.
        if (!reach.Known) return;

        Latest = reach;
        if (!KsaWorld.TryAnchorToGround(reach.ImpactEcl, out _body, out _anchor)) Clear();
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
    /// world as it is at the click. Three trajectories on a keypress is nothing; three per frame is
    /// what <see cref="Update"/> exists to avoid.</para>
    /// </summary>
    public static TailKitReach SolveNow(WeaponSystem battery, IProjectile round)
        => Solve(battery, round, new CoarseGroundTest(GroundTest.Shared), []);

    private static TailKitReach Solve(WeaponSystem battery, IProjectile round,
                                      CoarseGroundTest ground, List<double3> path)
    {
        if (battery.Platform is not { } platform) return default;

        // Every frame term is read at the store rather than at the rack. A bomb released from
        // altitude is kilometres from its launcher, through thinner air and over ground turning at
        // a different rate, and the whole answer is a fall time squared.
        double3 at = round.PositionEcl;
        double3 overGround = round.VelocityEcl - KsaWorld.GroundVelocityAt(platform, at);

        ground.Reset();

        return TailKitReach.Fly(at, overGround,
                                KsaWorld.GroundVelocityAt(platform, at),
                                KsaWorld.GroundAccelerationAt(platform, at),
                                KsaWorld.BodyVelocityAt(platform),
                                p => KsaWorld.GroundVelocityAt(platform, p),
                                round.Munition,
                                p => KsaWorld.GravityAt(platform, p),
                                p => KsaWorld.MediumDensityRatioAt(platform, p),
                                ground, IntegrationStep, path);
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

        double3 up = Vec.Unit(-KsaWorld.GravityAt(platform, impactEcl));
        if (Vec.Len2(up) < 0.5) return;

        // What the store reaches, so the inner ring means the same thing the pipper's does.
        KsaWorld.DrawCircleEcl(impactEcl, up, Warhead.LethalRadius(battery.Munition.ChargeKg),
                               ImpactColour);

        // And the region it can still be walked into. Its own colour rather than the impact's:
        // one says where it is going and the other says how much choice is left, and two meanings
        // on one mark is worse than either.
        KsaWorld.DrawCircleEcl(impactEcl, up, Latest.RadiusMetres, ReachColour);
    }
}
