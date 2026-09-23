using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// How far a store already falling can still move its landing — the number the ring, the panel and
/// the retarget all read, so that designating after release is a shot rather than a click that
/// does nothing.
///
/// <para>The load-bearing one is <see cref="TheReachIsAFloorUnderWhatTheKitFlies"/>. A region drawn
/// larger than the kit can fly is a ring promising a shot it cannot take, which is the one way this
/// instrument is worse than not having it.</para>
///
/// <para>It is also the test that killed the closed form. <c>½·a·t²</c> is not a bound at any
/// constant fraction: lateral push does not accumulate against drag, it settles at the drift where
/// fin authority and lateral drag balance, so displacement stops growing as <c>t²</c>. Flown here,
/// the share of <c>a·t²</c> the shipped kit delivers runs 0.46 at a 21 s fall and 0.18 at 95 s, so
/// a constant that looks right at two kilometres promises three times the truth from twenty.</para>
/// </summary>
public class TailKitReachTests(ITestOutputHelper Out)
{
    private const double Dt = 1.0 / 60.0;
    private const double Step = 0.05;
    private const double PlanetRadius = 6_371_000.0;
    private const double Gravity = 9.80665;
    private static readonly double3 Centre = double3.Zero;

    private sealed class Ball(double3 centre) : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = centre;
            surfaceRadius = PlanetRadius;
            return true;
        }
    }

    private static MunitionProfile Bomb(GuidanceMode guidance = GuidanceMode.Inertial) => new()
    {
        Name = "TESTKIT",
        DisplayName = "test tail kit",
        Guidance = guidance,
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 600f,
        DragK = 1.25e-4f,
        FuseRadius = 0f,
        ChargeKg = 250f,
        HitsTerrain = true,
        NavConstant = 3f,
        MaxLateralG = 0.4f,
        GravityCompensation = 0f,
    };

    private static double3 GravityAt(double3 positionEcl) => GravityAbout(Centre, positionEcl);

    private static double3 GravityAbout(double3 centre, double3 positionEcl)
        => Vec.Unit(centre - positionEcl) * Gravity;

    // A planet that neither turns nor travels, so what is measured here is the kit and nothing
    // else. BombSightSpinTests is where the carried frame is held to account.
    private static TailKitReach Reach(MunitionProfile munition, double3 at, double3 velocity)
        => ReachAbout(Centre, munition, at, velocity);

    private static TailKitReach ReachAbout(double3 centre, MunitionProfile munition, double3 at,
                                           double3 velocity)
        => TailKitReach.Fly(at, velocity, Vec.Zero, Vec.Zero, Vec.Zero, _ => Vec.Zero, munition,
                            p => GravityAbout(centre, p), _ => 1.0, new Ball(centre), Step, []);

    // Flies the real round to the ground, steered at `aim` or ballistic when it is zero.
    private static double3 Land(MunitionProfile munition, double3 start, double3 velocity, double3 aim)
    {
        Slug bomb = new(start, velocity, null, 1, start, Vec.Zero)
        {
            Munition = munition,
            Ground = new Ball(Centre),
        };

        TargetState? target = aim.Equals(Vec.Zero) ? null : new TargetState(aim, Vec.Zero, 0.0);

        for (int i = 0; i < 60_000 && bomb.State == RoundState.Flying; i++)
        {
            bomb.Update(Dt, target, GravityAt(bomb.PositionEcl), Vec.Zero, start, munition);
        }

        return bomb.PositionEcl;
    }

    public static TheoryData<double, double> Releases => new()
    {
        { 2000.0, 0.0 },
        { 2000.0, 250.0 },
        { 5000.0, 0.0 },
        { 5000.0, 250.0 },
        { 9000.0, 0.0 },
        { 9000.0, 250.0 },
        { 15000.0, 300.0 },
    };

    // The largest offset along `axis` the kit still settles within 25 m of, walked in hundred-metre
    // steps. This is the question the ring is claiming to answer, and it is stricter than the one
    // the probe inside Fly asks -- see TailKitReach.SettlingMargin.
    private static double Settles(MunitionProfile kit, double3 start, double3 velocity,
                                  double3 ballistic, double3 axis)
    {
        double flown = 0.0;
        for (double d = 100.0; d <= 30_000.0; d += 100.0)
        {
            double3 aim = Vec.Unit(ballistic + (axis * d)) * PlanetRadius;
            if (Vec.Len(Land(kit, start, velocity, aim) - aim) > 25.0) break;
            flown = d;
        }

        return flown;
    }

    // Along the ground track and across it, which are the two the probe flies.
    private static (double Along, double Cross) SettlesBothWays(
        MunitionProfile kit, double3 start, double3 velocity, out double3 ballistic)
    {
        ballistic = Land(kit, start, velocity, Vec.Zero);

        double3 up = Vec.Unit(ballistic);
        double3 track = Vec.Unit(Vec.RejectFrom(velocity, up));
        if (Vec.Len2(track) < 0.5) track = Vec.AnyPerpendicular(up);

        return (Settles(kit, start, velocity, ballistic, track),
                Settles(kit, start, velocity, ballistic, Vec.Unit(Vec.Cross(up, track))));
    }

    /// <summary>
    /// The region never promises a shot the kit cannot take: its radius is inside what the store
    /// settles on in <em>both</em> the directions the probe flies, at every release geometry —
    /// including the long falls where a <c>t²</c> law goes optimistic.
    ///
    /// <para>Against the narrower of the two rather than against one direction, because that is
    /// what a circle claims: it is inscribed in a footprint that is really an ellipse, and which
    /// axis is the short one swaps with release speed — along-track beats cross-track from 2 km at
    /// 250 m/s and loses from 9 km.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Releases))]
    public void TheReachIsAFloorUnderWhatTheKitFlies(double height, double releaseSpeed)
    {
        MunitionProfile kit = Bomb();
        double3 start = new(PlanetRadius + height, 0, 0);
        double3 velocity = new(0, 0, releaseSpeed);

        TailKitReach reach = Reach(kit, start, velocity);
        Assert.True(reach.Known, $"a {height:F0} m drop should have a region, held as {reach.Hold}");

        (double along, double cross) = SettlesBothWays(kit, start, velocity, out double3 ballistic);
        double narrower = Math.Min(along, cross);

        double3 up = Vec.Unit(ballistic);
        double3 track = Vec.Unit(Vec.RejectFrom(velocity, up));
        if (Vec.Len2(track) < 0.5) track = Vec.AnyPerpendicular(up);
        double3 atEdge = Vec.Unit(ballistic + (Vec.Unit(Vec.Cross(up, track)) * reach.RadiusMetres))
                         * PlanetRadius;
        double missAtEdge = Vec.Len(Land(kit, start, velocity, atEdge) - atEdge);

        double at2 = kit.MaxLateralAccel * reach.SecondsToGo * reach.SecondsToGo;
        Out.WriteLine($"{height:F0} m at {releaseSpeed:F0} m/s: {reach.SecondsToGo:F1} s of fall, "
                      + $"claims {reach.RadiusMetres:F0} m, settles along {along:F0} m / across "
                      + $"{cross:F0} m, share of a.t^2 {narrower / at2:F3}, "
                      + $"miss at the claimed edge {missAtEdge:F0} m");

        Assert.True(missAtEdge <= 25.0,
                    $"a designation at the claimed {reach.RadiusMetres:F0} m should be arrived at, "
                    + $"missed by {missAtEdge:F1} m");
        Assert.True(reach.RadiusMetres <= narrower,
                    $"the region must be a floor: claims {reach.RadiusMetres:F0} m against "
                    + $"{narrower:F0} m settled along the narrower axis");
    }

    /// <summary>
    /// And not so far under it that it misleads the other way. A ring drawn at a fraction of what
    /// the kit can do sends an operator away from a shot that would have worked, which is the same
    /// failure with the sign flipped.
    /// </summary>
    [Theory]
    [MemberData(nameof(Releases))]
    public void TheReachIsNotSoConservativeThatItMisleads(double height, double releaseSpeed)
    {
        MunitionProfile kit = Bomb();
        double3 start = new(PlanetRadius + height, 0, 0);
        double3 velocity = new(0, 0, releaseSpeed);

        double claimed = Reach(kit, start, velocity).RadiusMetres;
        (double along, double cross) = SettlesBothWays(kit, start, velocity, out _);
        double narrower = Math.Min(along, cross);

        Assert.True(claimed >= 0.6 * narrower,
                    $"the region should be most of the inscribed circle: claims {claimed:F0} m "
                    + $"against {narrower:F0} m");
    }

    /// <summary>
    /// A release too high for the sight's horizon reports no region rather than a wrong one.
    ///
    /// <para>The probe is flown, so it is bounded by <see cref="BombSight.MaxSteps"/> exactly as
    /// the pipper is — and a steered flight is longer than the ballistic one it is measured
    /// against, so the probes run out of horizon before the drop does. From 20 km at 300 m/s the
    /// fall is 95 s of a 102 s horizon and there is no room for the excursion, which is the real
    /// ceiling on this instrument and is the same one that leaves a reentry vehicle with no pipper.
    /// </para>
    ///
    /// <para>The failure that matters is the one this shape avoids: reading a probe that never
    /// landed as a region of zero, which reported a store with a perfectly good landing as having
    /// nothing to move it with.</para>
    /// </summary>
    [Fact]
    public void AReleaseAboveTheSightsHorizonReportsNoRegionRatherThanAWrongOne()
    {
        TailKitReach reach = Reach(Bomb(), new double3(PlanetRadius + 20_000.0, 0, 0),
                                   new double3(0, 0, 300.0));

        Assert.Equal(TailKitHold.Unreadable, reach.Hold);
        Assert.False(reach.Known);
        Assert.Equal(0.0, reach.RadiusMetres);
    }

    /// <summary>
    /// The region does not depend on where the body happens to sit in the ecliptic.
    ///
    /// <para>The probe is aimed at a point on the surface, and that surface has to be built about
    /// the <em>body's</em> centre, which the ground test is asked for. Built about the ecliptic
    /// origin it is a sphere centred on the Sun, so the probe is aimed ~1.5e11 m away and the
    /// region it measures is nonsense — and every other test here would still pass, because a rig
    /// has no reason to put its planet anywhere but the origin.</para>
    /// </summary>
    [Fact]
    public void TheRegionDoesNotDependOnWhereTheBodySitsInTheEcliptic()
    {
        MunitionProfile kit = Bomb();
        double3 far = new(1.496e11, 0, 0);

        // Over the body's +Z, square to the line out to the origin. Straight out along +X instead
        // and the two centres give the same local up, so the wrong sphere is locally the right one
        // and the bug hides completely -- which is what the first version of this test did.
        double atOrigin = ReachAbout(Vec.Zero, kit, new double3(0, 0, PlanetRadius + 5000.0),
                                     new double3(250.0, 0, 0)).RadiusMetres;

        TailKitReach moved = ReachAbout(far, kit, far + new double3(0, 0, PlanetRadius + 5000.0),
                                        new double3(250.0, 0, 0));

        Out.WriteLine($"at the origin {atOrigin:F1} m, an au out {moved.RadiusMetres:F1} m");

        Assert.True(moved.Known, $"a body away from the origin should still answer, held as {moved.Hold}");
        Assert.True(Math.Abs(moved.RadiusMetres - atOrigin) < 1.0,
                    $"the region should not move with the body: {atOrigin:F1} m at the origin "
                    + $"against {moved.RadiusMetres:F1} m an au out");
    }

    /// <summary>It closes as the ground comes up, which is the whole reason it is worth drawing.</summary>
    [Fact]
    public void TheRegionClosesAsTheStoreFalls()
    {
        MunitionProfile kit = Bomb();

        double high = Reach(kit, new double3(PlanetRadius + 8000.0, 0, 0), Vec.Zero).RadiusMetres;
        double low = Reach(kit, new double3(PlanetRadius + 1000.0, 0, 0), Vec.Zero).RadiusMetres;

        Out.WriteLine($"8 km: {high:F0} m, 1 km: {low:F0} m");
        Assert.True(low < high / 4.0,
                    $"authority goes roughly as the square of the time left: {high:F0} m at 8 km "
                    + $"against {low:F0} m at 1 km");
    }

    /// <summary>A store with no kit has no region — and is not given a small one.</summary>
    [Fact]
    public void AnUnguidedStoreHasNoRegionRatherThanASmallOne()
    {
        TailKitReach reach = Reach(Bomb(GuidanceMode.None), new double3(PlanetRadius + 5000.0, 0, 0),
                                   Vec.Zero);

        Assert.Equal(TailKitHold.Unguided, reach.Hold);
        Assert.False(reach.Known);
        Assert.Equal(0.0, reach.RadiusMetres);
    }

    /// <summary>
    /// A store that has no landing inside the sight's horizon reports that rather than a region.
    /// Released in orbit a bomb keeps the craft's velocity and flies alongside it, which is the
    /// case <c>MaxFlightSeconds</c> was never the answer to either.
    /// </summary>
    [Fact]
    public void AStoreWithNoLandingSaysSoRatherThanGuessing()
    {
        double3 at = new(PlanetRadius + 200_000.0, 0, 0);
        TailKitReach reach = Reach(Bomb(), at, new double3(0, 0, 7_800.0));

        Assert.Equal(TailKitHold.NoLanding, reach.Hold);
        Assert.False(reach.Known);
    }

    /// <summary>
    /// A reach nobody has solved covers nothing.
    ///
    /// <para>The enum's ordering is what makes this true, and it is not decoration: a zero-valued
    /// <c>Known</c> would report a store with no authority at all as covering a designation sitting
    /// on top of its landing, which reads exactly like a working instrument.</para>
    /// </summary>
    [Fact]
    public void ADefaultReachClaimsNothing()
    {
        TailKitReach none = default;

        Assert.Equal(TailKitHold.Unreadable, none.Hold);
        Assert.False(none.Known);
        Assert.False(none.Covers(Vec.Zero));
        Assert.True(double.IsNaN(none.ShortfallFrom(Vec.Zero)));
    }

    /// <summary>The arithmetic the ring, the panel and the log all read.</summary>
    [Fact]
    public void CoversAndShortfallAreMeasuredFromTheUnsteeredLanding()
    {
        TailKitReach reach = new(TailKitHold.Known, 20.0, new double3(100.0, 0, 0), 500.0);

        Assert.True(reach.Covers(new double3(100.0, 400.0, 0)));
        Assert.Equal(0.0, reach.ShortfallFrom(new double3(100.0, 400.0, 0)));

        Assert.False(reach.Covers(new double3(100.0, 900.0, 0)));
        Assert.Equal(400.0, reach.ShortfallFrom(new double3(100.0, 900.0, 0)), 6);
    }
}
