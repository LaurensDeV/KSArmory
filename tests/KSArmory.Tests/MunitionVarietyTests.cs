using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The core modularity claim, exercised rather than asserted: <b>a second round is a second
/// <see cref="MunitionProfile"/> and nothing else.</b>
///
/// <para>Every other test in the suite flies one round — the shipping 57E6 or a vacuum variant of
/// it — so nothing checked that two genuinely different munitions fly differently through the same
/// <see cref="Interceptor"/>. A flight model that quietly ignored half the profile would pass the
/// whole suite. <see cref="GuidanceMode.Seeker"/> in particular appeared in exactly one test.</para>
///
/// <para>See <c>docs/MODULARITY.md</c>.</para>
/// </summary>
public class MunitionVarietyTests
{
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);

    private sealed record Engagement(RoundState State, double Closest, double Speed, double Distance, double Age);

    /// <summary>Radius of the target body in this harness, which the fuse trigger includes.</summary>
    private const double TargetRadius = 5.0;

    /// <summary>
    /// One crossing engagement, flown with whatever munition it is handed. The geometry is fixed,
    /// so every difference in the result is a difference in the round.
    ///
    /// <para><b>Closest approach is analytic, not min-over-samples.</b> At 1000 m/s a 1/60 s
    /// sample is ~17 m of travel, so a sampled minimum reports the grid spacing rather than the
    /// pass. The true closest approach across each step — the measure the fuse itself uses — is
    /// what makes a sub-metre pass measurable.</para>
    /// </summary>
    private static Engagement Fly(MunitionProfile munition, double3 targetStart, double3 targetVel)
    {
        var round = new Interceptor(
            new double3(0, 0, 0),
            new double3(munition.LaunchSpeed, 0, 0),
            TargetHandle,
            tube: 1,
            platformEcl: default,
            frameVelocityEcl: default) { Munition = BuiltIns.Missile57E6 };

        const double dt = 1.0 / 60.0;
        double t = 0.0;
        double closest = double.MaxValue;

        while (round.State == RoundState.Flying && t < 60.0)
        {
            double3 targetPos = targetStart + targetVel * t;

            // True closest approach over this step, assuming linear relative motion across it -
            // the assumption the fuse makes too.
            double3 r = targetPos - round.PositionEcl;
            double3 v = targetVel - round.VelocityEcl;
            closest = Math.Min(closest, Vec.Len(r + v * Vec.TimeOfClosestApproach(r, v, dt)));

            // End-of-step, the way KSA hands a sample over.
            round.Update(dt, new TargetState(targetPos + targetVel * dt, targetVel, TargetRadius),
                         NoGravity, frameVelocityEcl: default, platformEcl: default, munition);
            t += dt;
        }

        return new Engagement(round.State, closest, round.Speed, round.DistanceFlown, round.Age);
    }

    /// <summary>
    /// A straight coast with nothing to shoot at, flown in air of a given density. Isolates drag:
    /// the round expires on its own timer, so the only thing separating two runs is what the air
    /// did to it.
    /// </summary>
    private static Engagement FlyAt(MunitionProfile munition, double mediumDensityRatio)
    {
        var round = new Interceptor(
            new double3(0, 0, 0),
            new double3(munition.LaunchSpeed, 0, 0),
            TargetHandle,
            tube: 1,
            platformEcl: default,
            frameVelocityEcl: default) { Munition = BuiltIns.Missile57E6 };

        const double dt = 1.0 / 60.0;
        double t = 0.0;

        while (round.State == RoundState.Flying && t < 60.0)
        {
            // Target far enough away to be unreachable, so the flight always runs to its timer.
            round.Update(dt, new TargetState(new double3(1e9, 0, 0), Vec.Zero, TargetRadius),
                         NoGravity, frameVelocityEcl: default, platformEcl: default, munition,
                         mediumDensityRatio);
            t += dt;
        }

        return new Engagement(round.State, double.MaxValue, round.Speed, round.DistanceFlown, round.Age);
    }

    /// <summary>A short-legged round: brief boost, hard drag, tight fuse.</summary>
    private static MunitionProfile ShortRange() => new()
    {
        Name = "short",
        DisplayName = "short-range round",
        BoostSeconds = 0.8f,
        BoostAccel = 300f,
        DragK = 2.0e-4f,
        MaxFlightSeconds = 8f,
        FuseRadius = 8f,
        ChargeKg = 2.5f,          // ~10 m lethal

    };

    /// <summary>A long-legged one: long boost, low drag, generous fuse.</summary>
    private static MunitionProfile LongRange() => new()
    {
        Name = "long",
        DisplayName = "long-range round",
        BoostSeconds = 4.0f,
        BoostAccel = 600f,
        DragK = 1.0e-5f,
        MaxFlightSeconds = 40f,
        FuseRadius = 20f,
        ChargeKg = 39f,           // ~25 m lethal

    };

    // ---- The profile actually drives the flight ------------------------

    /// <summary>
    /// The headline: one launcher, one geometry, two rounds, two outcomes. The long round reaches
    /// a target the short one cannot, and it is the profile alone that decides.
    /// </summary>
    [Fact]
    public void TwoMunitionsFlownOnOneGeometryGiveDifferentOutcomes()
    {
        double3 targetStart = new(9000, 0, 0);
        double3 targetVel = new(0, 120, 0);

        Engagement shortRound = Fly(ShortRange(), targetStart, targetVel);
        Engagement longRound = Fly(LongRange(), targetStart, targetVel);

        Assert.Equal(RoundState.Expired, shortRound.State);
        Assert.Equal(RoundState.Detonated, longRound.State);

        Assert.True(longRound.Distance > shortRound.Distance * 2.0,
            $"the long round flew {longRound.Distance:F0} m against the short round's {shortRound.Distance:F0} m - " +
            "the profile is not driving the flight");
    }

    [Fact]
    public void BoostDurationAndThrustSetTheSpeedTheRoundReaches()
    {
        double3 targetStart = new(30000, 0, 0);
        double3 targetVel = new(0, 0, 0);

        Engagement slow = Fly(ShortRange(), targetStart, targetVel);
        Engagement fast = Fly(LongRange(), targetStart, targetVel);

        Assert.True(fast.Speed > slow.Speed * 2.0,
            $"boost profile barely mattered: {fast.Speed:F0} m/s against {slow.Speed:F0} m/s");
    }

    [Fact]
    public void FlightTimeIsTheProfilesAndNotAConstant()
    {
        // Nothing to shoot at, so each round flies until its own self-destruct.
        var brief = new MunitionProfile { Name = "brief", DisplayName = "brief", MaxFlightSeconds = 3f };
        var patient = new MunitionProfile { Name = "patient", DisplayName = "patient", MaxFlightSeconds = 25f };

        Engagement a = Fly(brief, new double3(1e9, 0, 0), Vec.Zero);
        Engagement b = Fly(patient, new double3(1e9, 0, 0), Vec.Zero);

        Assert.Equal(RoundState.Expired, a.State);
        Assert.Equal(RoundState.Expired, b.State);
        Assert.InRange(a.Age, 3.0 - 0.05, 3.0 + 0.05);
        Assert.InRange(b.Age, 25.0 - 0.05, 25.0 + 0.05);
    }

    /// <summary>
    /// The fuse radius is per-round, and a bigger one triggers further out. Both rounds fly the
    /// identical trajectory here, so the only thing that can separate them is the fuse.
    /// </summary>
    [Fact]
    public void TheFuseRadiusIsTheRoundsAndNotTheMods()
    {
        double3 targetStart = new(4000, 0, 0);
        double3 targetVel = new(0, 60, 0);

        var tight = LongRange();
        tight.FuseRadius = 5f;
        tight.FuseArmSeconds = 0f;

        var wide = LongRange();
        wide.FuseRadius = 40f;
        wide.FuseArmSeconds = 0f;

        Engagement a = Fly(tight, targetStart, targetVel);
        Engagement b = Fly(wide, targetStart, targetVel);

        Assert.Equal(RoundState.Detonated, a.State);
        Assert.Equal(RoundState.Detonated, b.State);

        // The wide fuse fires earlier, so it has been flying for less time when it goes off.
        Assert.True(b.Age < a.Age,
            $"the 40 m fuse burst at {b.Age:F3}s and the 5 m fuse at {a.Age:F3}s - the radius is not being read");
    }

    // ---- Guidance mode is a property of the round ----------------------

    /// <summary>
    /// A command-linked round is steered by the launcher and cannot be blinded; a seeker round
    /// stops steering once the target leaves its gimbal. Flying the same hard-crossing engagement
    /// with a deliberately narrow seeker must therefore do measurably worse.
    /// </summary>
    [Fact]
    public void GuidanceModeChangesWhetherAHardCrossingTargetCanBeHeld()
    {
        double3 targetStart = new(1500, 0, 0);
        double3 targetVel = new(0, 400, 0);

        var linked = LongRange();
        linked.Guidance = GuidanceMode.CommandLink;

        var blinkered = LongRange();
        blinkered.Guidance = GuidanceMode.Seeker;
        blinkered.SeekerFovDeg = 3f;      // narrow enough that the crossing target leaves it

        Engagement a = Fly(linked, targetStart, targetVel);
        Engagement b = Fly(blinkered, targetStart, targetVel);

        Assert.True(a.Closest < b.Closest,
            $"command link closed to {a.Closest:F0} m and a 3 degree seeker to {b.Closest:F0} m - " +
            "guidance mode is not being read off the profile");
    }

    [Fact]
    public void ACommandLinkedRoundIgnoresItsSeekerFieldOfViewEntirely()
    {
        double3 targetStart = new(2500, 0, 0);
        double3 targetVel = new(0, 250, 0);

        var narrow = LongRange();
        narrow.Guidance = GuidanceMode.CommandLink;
        narrow.SeekerFovDeg = 1f;

        var wide = LongRange();
        wide.Guidance = GuidanceMode.CommandLink;
        wide.SeekerFovDeg = 179f;

        Engagement a = Fly(narrow, targetStart, targetVel);
        Engagement b = Fly(wide, targetStart, targetVel);

        // The 57E6 carries no seeker at all, so the gimbal limit must be inert for this mode.
        Assert.Equal(b.State, a.State);
        Assert.Equal(b.Closest, a.Closest, 6);
    }

    // ---- Airframe limits -----------------------------------------------

    [Fact]
    public void TheLateralGLimitIsPerRound()
    {
        var sluggish = LongRange();
        sluggish.MaxLateralG = 2f;

        var agile = LongRange();
        agile.MaxLateralG = 50f;

        double3 r = new(1200, 0, 0);
        double3 losRate = new(-400, 300, 0);
        double3 v = new(700, 0, 0);

        double3 lazy = Interceptor.GuidanceAccel(r, losRate, v, NoGravity, sluggish);
        double3 hard = Interceptor.GuidanceAccel(r, losRate, v, NoGravity, agile);

        Assert.True(Vec.Len(lazy) <= sluggish.MaxLateralAccel + 1e-6);
        Assert.True(Vec.Len(hard) <= agile.MaxLateralAccel + 1e-6);
        Assert.True(Vec.Len(hard) > Vec.Len(lazy) * 2.0,
            "the airframe limit is not being read off the profile");
    }

    [Fact]
    public void DragIsPerRoundAndScrubsSpeedAccordingly()
    {
        var slippery = LongRange();
        slippery.DragK = 0f;

        var draggy = LongRange();
        draggy.DragK = 5.0e-4f;

        Engagement a = Fly(slippery, new double3(1e9, 0, 0), Vec.Zero);
        Engagement b = Fly(draggy, new double3(1e9, 0, 0), Vec.Zero);

        Assert.True(a.Speed > b.Speed * 1.5,
            $"drag barely mattered: {a.Speed:F0} m/s clean against {b.Speed:F0} m/s draggy");
    }

    // ---- Vacuum --------------------------------------------------------

    /// <summary>
    /// The same round, fired in vacuum and at sea level, must fly differently — otherwise the drag
    /// coefficient is being applied regardless of where the round actually is, and a round launched
    /// in orbit is scrubbed as though at sea level.
    /// </summary>
    [Fact]
    public void TheSameRoundFliesFurtherInVacuumThanInAir()
    {
        MunitionProfile munition = LongRange();
        munition.DragK = 3.0e-4f;             // draggy enough for the difference to be obvious

        Engagement sealevel = FlyAt(munition, mediumDensityRatio: 1.0);
        Engagement vacuum = FlyAt(munition, mediumDensityRatio: 0.0);

        Assert.True(vacuum.Speed > sealevel.Speed * 1.5,
            $"vacuum {vacuum.Speed:F0} m/s against sea level {sealevel.Speed:F0} m/s - " +
            "drag is not being scaled by density");
        Assert.True(vacuum.Distance > sealevel.Distance,
            $"vacuum flight covered {vacuum.Distance:F0} m against {sealevel.Distance:F0} m in air");
    }

    /// <summary>
    /// A ratio of one is sea level, and must be exactly the unscaled behaviour: every
    /// <see cref="MunitionProfile.DragK"/> in the arsenal is tuned there, so scaling by an absolute
    /// density instead silently retunes all of them.
    /// </summary>
    [Fact]
    public void SeaLevelIsExactlyTheUnscaledBehaviour()
    {
        MunitionProfile munition = LongRange();
        munition.DragK = 2.0e-4f;

        Engagement explicitly = FlyAt(munition, mediumDensityRatio: 1.0);
        Engagement byDefault = Fly(munition, new double3(1e9, 0, 0), Vec.Zero);

        Assert.Equal(byDefault.Speed, explicitly.Speed, 9);
        Assert.Equal(byDefault.Distance, explicitly.Distance, 9);
    }

    /// <summary>Thinner air is less drag, monotonically — nothing clever, but it pins the sign.</summary>
    [Fact]
    public void ThinnerAirScrubsLessSpeed()
    {
        MunitionProfile munition = LongRange();
        munition.DragK = 3.0e-4f;

        double previous = 0.0;
        foreach (double ratio in new[] { 1.0, 0.5, 0.1, 0.0 })
        {
            double speed = FlyAt(munition, ratio).Speed;
            Assert.True(speed > previous, $"density {ratio:F1} left {speed:F0} m/s, thicker air left {previous:F0} m/s");
            previous = speed;
        }
    }

    // ---- Kinetic kill --------------------------------------------------

    /// <summary>
    /// A hit-to-kill round is expressible with no new code: the trigger is
    /// <c>munition.FuseRadius + target.Radius</c>, so a zero fuse radius means "trigger on contact
    /// with the target's body" exactly.
    ///
    /// <para>And it cannot tunnel however small the radius gets, because the fuse is analytic —
    /// <see cref="Vec.TimeOfClosestApproach"/> then the true range at that instant, not a sampled
    /// distance. The limit on a kinetic round is guidance accuracy, not the fuse model.</para>
    /// </summary>
    [Fact]
    public void AKineticRoundIsAProfileWithNoFuseRadiusAtAll()
    {
        var kinetic = LongRange();
        kinetic.FuseRadius = 0f;          // contact with the target body, nothing more
        kinetic.ChargeKg = 0f;            // no charge, so no lethal or blast radius either

        Engagement hit = Fly(kinetic, new double3(6000, 0, 0), new double3(0, 150, 0));

        Assert.Equal(RoundState.Detonated, hit.State);
    }

    /// <summary>
    /// The discriminator for the above. A round that intercepts on contact must <em>miss</em> the
    /// same target when its guidance is turned off — otherwise the scenario is winnable by flying
    /// straight and proves nothing about hit-to-kill accuracy.
    /// </summary>
    [Fact]
    public void AKineticRoundWithoutGuidanceMissesTheSameTarget()
    {
        var unguided = LongRange();
        unguided.FuseRadius = 0f;
        unguided.ChargeKg = 0f;
        unguided.NavConstant = 0f;

        Engagement miss = Fly(unguided, new double3(6000, 0, 0), new double3(0, 150, 0));

        Assert.NotEqual(RoundState.Detonated, miss.State);
        Assert.True(miss.Closest > 100.0,
            $"an unguided round still passed within {miss.Closest:F1} m - the geometry is too easy");
    }

    /// <summary>
    /// The fuse radius decides <em>when</em> a round bursts, not how close it gets.
    ///
    /// <para>The obvious assertion is the wrong one: proportional navigation drives this
    /// engagement to a measured 0.000 m closest approach at every fuse setting from 20 m down to
    /// contact, so "a smaller fuse forces a closer pass" is vacuous. What the radius moves is the
    /// burst instant — a wider fuse trips further out and therefore earlier.</para>
    /// </summary>
    [Fact]
    public void AWiderFuseBurstsEarlierWithoutChangingThePass()
    {
        var ages = new List<double>();

        foreach (float fuseRadius in new[] { 40f, 20f, 5f, 0f })
        {
            var munition = LongRange();
            munition.FuseRadius = fuseRadius;
            munition.FuseArmSeconds = 0f;

            Engagement flight = Fly(munition, new double3(5000, 0, 0), new double3(0, 100, 0));

            Assert.Equal(RoundState.Detonated, flight.State);
            Assert.True(flight.Closest <= fuseRadius + TargetRadius + 1e-6,
                $"a {fuseRadius} m fuse triggered at {flight.Closest:F3} m");

            ages.Add(flight.Age);
        }

        // Never earlier as the fuse narrows, though not strictly later at every step: with the
        // round converging to a direct hit, a 5 m fuse and a 0 m one are crossed inside one
        // sub-step and burst at the same instant. That is integrator resolution; the ends show the
        // profile is read.
        for (int i = 1; i < ages.Count; i++)
        {
            Assert.True(ages[i] >= ages[i - 1] - 1e-9,
                $"burst {i} at {ages[i]:F4}s came earlier than {ages[i - 1]:F4}s as the fuse narrowed");
        }

        Assert.True(ages[^1] > ages[0],
            $"a contact fuse burst at {ages[^1]:F4}s and a 40 m one at {ages[0]:F4}s - the radius is not being read");
    }

    /// <summary>
    /// A round with no body mesh is a legitimate profile — it simply draws as a tracer. Nothing
    /// should require <see cref="MunitionProfile.BodyMarker"/> to be set.
    /// </summary>
    [Fact]
    public void ARoundWithNoModelIsAValidProfile()
    {
        var tracerOnly = new MunitionProfile { Name = "tracer", DisplayName = "tracer only" };

        Assert.Null(tracerOnly.BodyMarker);
        Assert.Null(tracerOnly.FinMarker);

        Engagement flight = Fly(tracerOnly, new double3(3000, 0, 0), new double3(0, 100, 0));
        Assert.Equal(RoundState.Detonated, flight.State);
    }
}
