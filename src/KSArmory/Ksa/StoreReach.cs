using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What a store already in the air can still do about where it lands: where it is coming down, and
/// the region on the ground it can still be walked into.
///
/// <para><b>One answer, three surfaces</b>: the ring on the ground, the line on the panel and the
/// clause logged when a designation reaches a falling store all read this, as with
/// <see cref="ReachDisplay"/>. Not <see cref="RoundReach"/>, which is the reaper.</para>
///
/// <para>Three <see cref="BombSight"/> flights per solve, three times the pipper, which measured
/// 5.41 ms of a 7.17 ms draw over eight rockets. So <see cref="Update"/> solves only while a store
/// that steers its own fall is in the air, at half the pipper's rate.</para>
/// </summary>
internal sealed class StoreReach
{
    // The round sub-steps at 5 ms whatever this is, so it sets only how often the ground is sampled:
    // 0.05 to 0.40 moves the radius under a metre on answers of 634, 1610 and 2898 m while the
    // terrain lookups (2395 per solve at 0.05) fall 8x. CLAUDE.md, "And the expensive half of a sight".
    private const double IntegrationStep = 0.20;

    // Half the pipper's rate: the landing of a store already gone barely moves between solves.
    private const double SolveIntervalSeconds = 0.4;

    // How long to wait before asking again once a store has been found unsolvable, as one on a long
    // coast with no landing inside the sight's horizon.
    private const double UnsolvableIntervalSeconds = 2.0;

    // The two painted on the ground, their alpha the brightness each keeps over dark ground.
    // White and dashed rather than the pipper's solid orange: the pipper is where the next store
    // lands, and this is one already falling.
    private static readonly float4 PaintedImpact = new(1.0f, 1.0f, 1.0f, 0.05f);
    private static readonly float4 PaintedReach = new(0.45f, 0.85f, 1.0f, 0.05f);

    // Its own, because it caches down one trajectory and the three flights here must not be shown
    // one another's last sample -- they deliberately end up in different places.
    private readonly CoarseGroundTest _ground = new(GroundTest.Shared);
    private readonly List<double3> _path = [];

    private readonly System.Diagnostics.Stopwatch _sinceSolve = System.Diagnostics.Stopwatch.StartNew();
    private bool _unsolvable;

    // Which of the three flights this tick runs, and what the other two last found: one per tick
    // is a third of the cost. Only a whole cycle is published: see Update.
    private int _stage;
    private TailKitReach _building;
    private object? _landingBody;
    private double3 _landingAnchor;
    private double _along;
    private double _across;

    // The landing as a place on the ground: as an ecliptic point it is left behind at ~29.8 km/s
    // between solves, and against the platform it is dragged along with the aircraft.
    private object? _body;
    private double3 _anchor;

    /// <summary>What the last solve found. <c>Unreadable</c> until one has landed.</summary>
    public TailKitReach Latest { get; private set; }

    /// <summary>Forgets it, so a system with nothing falling draws nothing.</summary>
    public void Clear()
    {
        Latest = default;
        _body = null;

        // The part-built answer goes with it, or the next store inherits this one's axes.
        _building = default;
        _landingBody = null;
        _along = 0.0;
        _across = 0.0;
        _stage = 0;
    }

    /// <summary>
    /// Re-solves for the store the marks still steer (<see cref="WeaponSystem.Steerable"/>), at most
    /// every <see cref="SolveIntervalSeconds"/>. Any other store keeps its aim, so its reach is
    /// nothing anyone can act on.
    /// </summary>
    public void Update(WeaponSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (system.Steerable is not { } store) { Clear(); return; }

        if (_sinceSolve.Elapsed.TotalSeconds < (_unsolvable ? UnsolvableIntervalSeconds
                                                            : SolveIntervalSeconds))
        {
            return;
        }

        _sinceSolve.Restart();

        // With nothing on screen yet, fly all three at once rather than wait three ticks.
        TailKitReach reach = Solve(system, store, _ground, _path, Latest.Known ? 1 : 3);
        _unsolvable = reach.Hold == TailKitHold.NoLanding;

        // Nothing mid-cycle: a partial answer pairs a new centre with the last cycle's radius, and a
        // designation near the edge pops in and out of reach.
        if (_stage != 0) return;

        // Every failure below leaves the last good answer standing, as the pipper does: a reset takes
        // three ticks to rebuild.
        if (!reach.Known) return;
        if (!KsaWorld.TryAnchorToGround(reach.ImpactEcl, out object? body, out double3 anchor)) return;
        if (system.Platform is null) return;

        // How far the landing moved since the last solve, which is what a ring hopping about is made of.
        if (Latest.Known && KsaWorld.TryGroundAnchorEcl(_body, _anchor, out double3 was, out _))
        {
            double moved = Vec.Len(reach.ImpactEcl - was);
            Log.Debug(() => $"store reach: landing moved {moved:F1} m since the last solve, "
                            + $"{Distance.Say(reach.RadiusMetres)} of reach, {reach.SecondsToGo:F0} s to go");
        }

        // Published together: the offsets are measured from the landing the anchor came from.
        Latest = reach;
        _body = body;
        _anchor = anchor;
    }

    /// <summary>The first store in the air that steers its own fall, whatever it is aimed at.</summary>
    public static IProjectile? FallingStore(WeaponSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        foreach (IProjectile round in system.Rounds)
        {
            if (round.State == RoundState.Flying && round.Munition.SteersItsFall) return round;
        }

        return null;
    }

    /// <summary>
    /// The region for one store, flown now rather than read off a solve that can be
    /// <see cref="SolveIntervalSeconds"/> old.
    ///
    /// <para>What <c>WeaponSystem.Designate</c> asks: all three flights at once, where
    /// <see cref="Update"/> spreads them.</para>
    /// </summary>
    public static TailKitReach SolveNow(WeaponSystem system, IProjectile round)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(round);

        if (system.Platform is not { } platform) return default;

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
                                new CoarseGroundTest(GroundTest.Shared),
                                BombSight.StepFor(round.Munition, IntegrationStep), [], round.Age);
    }

    private TailKitReach Solve(WeaponSystem system, IProjectile round,
                               CoarseGroundTest ground, List<double3> path, int passes)
    {
        if (system.Platform is not { } platform) return default;

        // Every frame term is read at the store rather than at the rack, which can be kilometres away.
        double3 at = round.PositionEcl;
        double3 overGround = round.VelocityEcl - KsaWorld.GroundVelocityAt(platform, at);

        ground.Reset();

        for (int i = 0; i < passes; i++)
        {
            // The landing is a tick or two old, 12 km of the planet's travel per tick as an ecliptic
            // point: put back on this instant's ground first.
            if (_stage != 0 && _building.SecondsToGo > 0.0)
            {
                if (!KsaWorld.TryGroundAnchorEcl(_landingBody, _landingAnchor, out double3 landing, out _))
                {
                    _building = default;
                    _stage = 0;
                }
                else
                {
                    _building = _building with { ImpactEcl = landing };
                }
            }

            _building = TailKitReach.FlyStage(_stage, _building, at, overGround,
                                              KsaWorld.GroundVelocityAt(platform, at),
                                              KsaWorld.GroundAccelerationAt(platform, at),
                                              KsaWorld.BodyVelocityAt(platform),
                                              p => KsaWorld.GroundVelocityAt(platform, p),
                                              round.Munition,
                                              p => KsaWorld.GravityAt(platform, p),
                                              p => KsaWorld.MediumDensityRatioAt(platform, p),
                                              ground, BombSight.StepFor(round.Munition, IntegrationStep), path,
                                              ref _along, ref _across, round.Age);

            if (_stage == 0)
            {
                _landingBody = _building.SecondsToGo > 0.0
                               && KsaWorld.TryAnchorToGround(_building.ImpactEcl, out object? body,
                                                             out _landingAnchor)
                                   ? body
                                   : null;
            }

            _stage = (_stage + 1) % 3;
        }

        return _building;
    }

    /// <summary>
    /// The landing, and the circle the kit can still walk it inside.
    ///
    /// <para>Drawn even when a designation is already inside the region: it shows how much reach is
    /// left, and shrinks as the time to go runs down.</para>
    /// </summary>
    public void Draw(WeaponSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (system.Platform is null) return;

        double3 impactEcl = default;
        bool landing = Latest.Known && KsaWorld.TryGroundAnchorEcl(_body, _anchor, out impactEcl, out _);

        PaintFalling(system, landing ? impactEcl : null);
    }

    // Every store of this launcher still falling, where it will land: onto its mark where it has
    // one, and where it comes down untouched where it has none, which only the steerable store's
    // solve knows. Round the steerable one, the reach its fins have left.
    private void PaintFalling(WeaponSystem system, double3? untouched)
    {
        double lethal = Warhead.LethalRadius(system.Munition.ChargeKg);
        IProjectile? steerable = system.Steerable;

        foreach (IProjectile round in system.Rounds)
        {
            if (round.State != RoundState.Flying || !round.Munition.SteersItsFall) continue;

            double3? at = TryAimEcl(round.Aimpoint, out double3 aim) ? aim
                        : ReferenceEquals(round, steerable) ? untouched
                        : null;

            if (at is { } landsAt) GroundRings.Add(landsAt, lethal, 0.0, PaintedImpact, GroundRings.Dashed);
        }

        if (steerable is not null && untouched is { } centre)
        {
            GroundRings.Add(centre, Latest.RadiusMetres, 0.0, PaintedReach);
        }
    }

    private static bool TryAimEcl(Aimpoint aim, out double3 positionEcl)
    {
        positionEcl = default;

        switch (aim.Kind)
        {
            case AimpointKind.Ground:
                return KsaWorld.TryGroundAnchorEcl(aim.Handle, aim.Anchor, out positionEcl, out _);
            case AimpointKind.Vehicle or AimpointKind.Part when aim.Handle is KSA.Vehicle craft && KsaWorld.IsAlive(craft):
                positionEcl = KsaWorld.PositionEcl(craft);
                return true;
            case AimpointKind.Point:
                positionEcl = aim.PositionEcl;
                return true;
            default:
                return false;
        }
    }
}
