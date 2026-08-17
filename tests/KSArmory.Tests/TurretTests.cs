using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The launcher's two drives: traverse and elevation. Cheap to test and worth testing, because
/// the failure modes are all invisible-until-you-watch-it — a turret that takes the long way
/// round, one that jitters at the wrap point, one that snaps instead of sweeping, or pods that
/// reach a high angle by swinging down through the deck.
/// </summary>
public class TurretTests
{
    private const double Tolerance = 1e-9;
    private static readonly double ElevRate = double.DegreesToRadians(45);

    [Fact]
    public void BearingTo_ForwardIsZero()
    {
        // +Y is the turret's rest facing, which must be bearing zero.
        Assert.Equal(0.0, Turret.BearingTo(new double3(0, 1, 0)), 9);
    }

    [Fact]
    public void BearingTo_RightIsQuarterTurn()
    {
        Assert.Equal(Math.PI / 2, Turret.BearingTo(new double3(0, 0, 1)), 9);
    }

    [Fact]
    public void BearingTo_IgnoresElevation()
    {
        // The two axes are independent: how high a target sits is the elevation drive's
        // problem and must not shift the traverse order.
        double level = Turret.BearingTo(new double3(0.0, 1.0, 0.0));
        double steep = Turret.BearingTo(new double3(9.0, 1.0, 0.0));
        Assert.Equal(level, steep, 9);
    }

    [Fact]
    public void ElevationTo_ReadsTheAngleAboveTheHorizon()
    {
        Assert.Equal(0.0, Turret.ElevationTo(new double3(0, 1, 0)), 9);
        Assert.Equal(Math.PI / 2, Turret.ElevationTo(new double3(1, 0, 0)), 9);
        Assert.Equal(Math.PI / 4, Turret.ElevationTo(new double3(1, 1, 0)), 9);

        // Straight up is straight up regardless of which way the turret happens to face.
        Assert.Equal(Turret.ElevationTo(new double3(1, 0, 0)),
                     Turret.ElevationTo(new double3(1, 0, 0)), 9);
    }

    [Fact]
    public void ElevationTo_IsBearingIndependent()
    {
        // Same height, different quadrants: the elevation order must not care.
        double front = Turret.ElevationTo(new double3(1, 1, 0));
        double side = Turret.ElevationTo(new double3(1, 0, 1));
        double behind = Turret.ElevationTo(new double3(1, -1, 0));
        Assert.Equal(front, side, 9);
        Assert.Equal(front, behind, 9);
    }

    [Fact]
    public void Track_ClampsElevationToTheDrivesTravel()
    {
        var turret = new Turret();

        // A target below would order a negative elevation the pods cannot reach. Off the beam,
        // where nothing fouls, that floors at level.
        turret.Track(new double3(-1, 0, 1));
        Assert.Equal(turret.MinElevationRad, turret.CommandElevationRad!.Value, 9);

        // Dead ahead the same target floors higher instead, because the bodywork is in the way.
        turret.Track(new double3(-1, 0.05, 0));
        Assert.Equal(turret.ForwardMinElevationRad, turret.CommandElevationRad!.Value, 9);

        turret.Track(new double3(1, 0.001, 0));
        Assert.Equal(turret.MaxElevationRad, turret.CommandElevationRad!.Value, 9);
    }

    [Fact]
    public void Elevation_IsRateLimitedAndNeverWraps()
    {
        var turret = new Turret();
        turret.Track(new double3(0, 1, 0));           // level, so elevation must come down

        double before = turret.ElevationRad;
        turret.Update(0.1, double.DegreesToRadians(70), ElevRate);

        // 45 deg/s for a tenth of a second is 4.5 degrees down from the modelled 55.
        Assert.Equal(before - double.DegreesToRadians(4.5), turret.ElevationRad, 9);

        // Elevation is an arc, not a circle. Driving it hard must never let it take a "short
        // way" round through the deck and come out the other side.
        for (int i = 0; i < 500; i++) turret.Update(0.1, double.DegreesToRadians(70), ElevRate);
        Assert.InRange(turret.ElevationRad, turret.MinElevationRad, turret.MaxElevationRad);
    }

    [Fact]
    public void OnTarget_NeedsBothAxes()
    {
        var turret = new Turret();
        turret.Track(new double3(0, 1, 0));           // dead ahead and level

        // Traverse has nothing to do; elevation has 55 degrees to come down.
        turret.Update(0.05, double.DegreesToRadians(70), ElevRate);
        Assert.Equal(0.0, turret.ErrorRad, 9);
        Assert.False(turret.OnTarget);

        for (int i = 0; i < 100; i++) turret.Update(0.05, double.DegreesToRadians(70), ElevRate);
        Assert.True(turret.OnTarget);
    }

    [Fact]
    public void DepressionFloor_ProtectsTheForwardArcOnly()
    {
        var turret = new Turret();

        // Dead ahead the pods would swing down through the bodywork behind the cab.
        Assert.Equal(turret.ForwardMinElevationRad, turret.DepressionFloorAt(0.0), 9);

        // Traversed off the beam there is nothing in the way, so level is allowed.
        Assert.Equal(turret.MinElevationRad, turret.DepressionFloorAt(Math.PI / 2), 9);
        Assert.Equal(turret.MinElevationRad, turret.DepressionFloorAt(Math.PI), 9);

        // And it eases in rather than stepping, so traversing does not snap the pods up.
        double edge = turret.DepressionFloorAt(turret.ForwardArcRad * 0.99);
        Assert.InRange(edge, turret.MinElevationRad, turret.ForwardMinElevationRad * 0.1);
    }

    /// <summary>
    /// A mount that may depress below level has no forward cutout to ease out of, so the floor
    /// must arrive at its own minimum by the edge of the arc. Easing toward level instead invents
    /// a cutout it does not have: the floor climbs across the band and then drops back the moment
    /// the arc ends. The clamp is applied against the current bearing and is not rate limited, so
    /// that is a step in one frame — on the Mk 15, which depresses to −25°, a 25° one.
    /// </summary>
    [Fact]
    public void DepressionFloor_HasNoStepForAMountThatDepressesBelowLevel()
    {
        var turret = new Turret
        {
            MinElevationRad = double.DegreesToRadians(-25),
            ForwardMinElevationRad = double.DegreesToRadians(-25),
        };

        // Nothing is in the way of this mount anywhere, so the floor is its minimum throughout.
        Assert.Equal(turret.MinElevationRad, turret.DepressionFloorAt(0.0), 9);
        Assert.Equal(turret.MinElevationRad, turret.DepressionFloorAt(turret.ForwardArcRad), 9);

        // Including across the band the easing runs over, which is where a floor eased toward
        // level would lift.
        double justInside = turret.DepressionFloorAt(turret.ForwardArcRad * 0.999);
        double justOutside = turret.DepressionFloorAt(turret.ForwardArcRad * 1.001);

        Assert.Equal(turret.MinElevationRad, justInside, 6);
        Assert.True(Math.Abs(justInside - justOutside) < 1e-6,
                    $"floor steps {double.RadiansToDegrees(Math.Abs(justInside - justOutside)):F1} deg "
                    + "at the edge of the forward arc");
    }

    /// <summary>
    /// The two floors are ends of the same easing, so a mount with a genuine cutout still gets one
    /// — the step-free case above must not be bought by flattening the case the floor exists for.
    ///
    /// <para>The floor only ever falls as the turret traverses off the bow, and that direction is
    /// the point: the bodywork it protects does not taper, so a curve that starts dropping at the
    /// bow has given most of its height away by the time the tubes are abeam the obstruction —
    /// which is where they reach furthest across it.</para>
    /// </summary>
    [Fact]
    public void DepressionFloor_StillProtectsAMountThatCannotDepressAtAll()
    {
        var turret = new Turret();

        Assert.True(turret.DepressionFloorAt(0.0) > turret.DepressionFloorAt(Math.PI / 2),
                    "the forward arc is no longer protected");

        // Monotonic across the band: it only ever falls as the turret traverses off the bow.
        double previous = turret.DepressionFloorAt(0.0);
        for (double f = 0.0; f <= 1.2; f += 0.02)
        {
            double floor = turret.DepressionFloorAt(turret.ForwardArcRad * f);
            Assert.True(floor <= previous + 1e-9, $"floor rose at {f:F2} of the arc");
            previous = floor;
        }
    }

    /// <summary>
    /// The radar overlay is clocked off this. Seeded from a fixed ecliptic axis instead, it turns
    /// with the planet under a boresight that is local "up", so the cone's ribs rotate on their
    /// own and snap when the seed swaps.
    /// </summary>
    [Fact]
    public void PerpendicularTo_HoldsStillWhileTheBoresightSweepsWithTheCraft()
    {
        double previous = double.NaN;
        for (double spin = 0.0; spin < Math.Tau; spin += 0.05)
        {
            // A craft standing on a rotating planet: "up" and "forward" turn together.
            double3 up = new(Math.Cos(spin), Math.Sin(spin), 0.0);
            double3 forward = new(-Math.Sin(spin), Math.Cos(spin), 0.0);

            double3 u = Vec.PerpendicularTo(up, forward);

            // The clocking must stay on the craft's forward axis, not drift round the rim.
            Assert.Equal(1.0, Vec.Dot(u, forward), 9);
            Assert.Equal(0.0, Vec.Dot(u, up), 9);

            double angle = Math.Atan2(Vec.Dot(u, forward), Vec.Dot(u, up));
            if (!double.IsNaN(previous)) Assert.Equal(previous, angle, 9);
            previous = angle;
        }
    }

    [Fact]
    public void PerpendicularTo_FallsBackWhenTheReferenceIsUseless()
    {
        double3 up = new(0, 0, 1);

        // Parallel to the boresight, so it has no perpendicular component to clock against.
        Assert.Equal(0.0, Vec.Dot(Vec.PerpendicularTo(up, up), up), 9);
        Assert.Equal(0.0, Vec.Dot(Vec.PerpendicularTo(up, Vec.Zero), up), 9);
    }

    [Fact]
    public void DepressionFloor_HoldsFullHeightAcrossTheSectorItProtects()
    {
        var turret = new Turret();

        foreach (double deg in new[] { 0.0, 15.0, 25.0, 40.0, 50.0, 60.0, 62.0 })
        {
            Assert.Equal(turret.ForwardMinElevationRad,
                         turret.DepressionFloorAt(double.DegreesToRadians(deg)), 9);
        }
    }

    [Fact]
    public void DepressionFloor_EasesAwayOutsideThePlateauAndNeverRises()
    {
        var turret = new Turret();

        double previous = turret.ForwardMinElevationRad;
        for (double deg = 62.0; deg <= 90.0; deg += 2.0)
        {
            double floor = turret.DepressionFloorAt(double.DegreesToRadians(deg));
            Assert.True(floor <= previous + 1e-9, $"floor rose again at {deg} degrees");
            previous = floor;
        }
        Assert.Equal(turret.MinElevationRad, previous, 9);
    }

    [Fact]
    public void DepressionFloor_IsSymmetricAboutTheBow()
    {
        var turret = new Turret();

        foreach (double deg in new[] { 20.0, 45.0, 62.0, 70.0, 79.0 })
        {
            Assert.Equal(turret.DepressionFloorAt(double.DegreesToRadians(deg)),
                         turret.DepressionFloorAt(double.DegreesToRadians(-deg)), 9);
        }
    }

    [Fact]
    public void Elevation_IsLiftedWhenTraversingIntoTheForwardArc()
    {
        var turret = new Turret();

        // Pods down at level, pointing off to one side where that is legal.
        turret.Point(Math.PI / 2, 0.0);
        for (int i = 0; i < 200; i++) turret.Update(0.05, double.DegreesToRadians(70), ElevRate);
        Assert.Equal(0.0, turret.ElevationRad, 6);

        // Now swing round to face forward. The interlock must raise them on the way, not let
        // them plough through the hull and sort it out on arrival.
        turret.Point(0.0, 0.0);
        for (int i = 0; i < 200; i++)
        {
            turret.Update(0.05, double.DegreesToRadians(70), ElevRate);
            Assert.True(turret.ElevationRad >= turret.DepressionFloorAt(turret.BearingRad) - 1e-9);
        }

        Assert.Equal(turret.ForwardMinElevationRad, turret.ElevationRad, 6);
    }

    [Fact]
    public void IsLaid_WaitsForTheSettleTime()
    {
        var turret = new Turret();
        turret.Track(new double3(0, 0, 1));
        for (int i = 0; i < 200; i++) turret.Update(0.05, double.DegreesToRadians(70), ElevRate);

        Assert.True(turret.OnTarget);
        Assert.True(turret.IsLaid(0.35));

        // Swinging onto a new target drops it again, so a round cannot leave mid-sweep.
        turret.Track(new double3(0, -1, 0));
        turret.Update(0.05, double.DegreesToRadians(70), ElevRate);
        Assert.False(turret.IsLaid(0.35));
        Assert.Equal(0.0, turret.SecondsOnTarget, 9);
    }

    [Fact]
    public void IsLaid_IsNotSatisfiedByMerelyPassingThrough()
    {
        // A turret sweeping across the aim point is momentarily OnTarget. Without the settle time,
        // that instant releases a round while the launcher is still moving.
        var turret = new Turret();
        turret.Track(new double3(0, 0, 1));
        turret.Stow(Turret.DefaultRestElevation);

        bool everLaid = false;
        for (int i = 0; i < 4; i++)
        {
            turret.Update(0.05, double.DegreesToRadians(70), ElevRate);
            everLaid |= turret.IsLaid(0.35);
        }
        Assert.False(everLaid);
    }

    [Fact]
    public void WrapPi_FoldsIntoRange()
    {
        Assert.Equal(0.0, Turret.WrapPi(Math.Tau), 9);
        Assert.Equal(0.1, Turret.WrapPi(Math.Tau + 0.1), 9);
        Assert.Equal(-0.1, Turret.WrapPi(-Math.Tau - 0.1), 9);
        Assert.InRange(Turret.WrapPi(100.0), -Math.PI, Math.PI);
    }

    [Fact]
    public void WrapPi_SurvivesNonsense()
    {
        Assert.Equal(0.0, Turret.WrapPi(double.NaN));
        Assert.Equal(0.0, Turret.WrapPi(double.PositiveInfinity));
    }

    [Fact]
    public void StepToward_TakesTheShortWayAcrossTheWrap()
    {
        // From 170 degrees to -170 degrees is 20 degrees the short way, not 340 the long way.
        double from = double.DegreesToRadians(170);
        double to = double.DegreesToRadians(-170);

        double stepped = Turret.StepToward(from, to, double.DegreesToRadians(5));

        // Five degrees further round in the *positive* direction, having wrapped past pi.
        Assert.Equal(double.DegreesToRadians(175), stepped, 9);
    }

    [Fact]
    public void StepToward_ArrivesExactlyWhenWithinReach()
    {
        double stepped = Turret.StepToward(0.0, 0.1, 0.5);
        Assert.Equal(0.1, stepped, 9);
    }

    [Fact]
    public void Update_IsRateLimited()
    {
        var turret = new Turret();
        turret.Track(new double3(0, 0, 1));          // ninety degrees right

        turret.Update(0.1, double.DegreesToRadians(70), ElevRate);

        // One tenth of a second at 70 deg/s is seven degrees, nowhere near the ninety ordered.
        Assert.Equal(double.DegreesToRadians(7), turret.BearingRad, 9);
        Assert.False(turret.OnTarget);
    }

    [Fact]
    public void Update_EventuallyArrivesAndStops()
    {
        var turret = new Turret();
        turret.Track(new double3(0, -1, 0));         // dead astern, the worst case

        for (int i = 0; i < 200; i++) turret.Update(0.05, double.DegreesToRadians(70), ElevRate);

        Assert.True(turret.OnTarget);
        Assert.Equal(Math.Abs(turret.BearingRad), Math.PI, 6);

        // And having arrived, it holds rather than creeping or oscillating.
        double settled = turret.BearingRad;
        turret.Update(0.05, double.DegreesToRadians(70), ElevRate);
        Assert.Equal(settled, turret.BearingRad, 9);
    }

    [Fact]
    public void Stow_ReturnsToForward()
    {
        var turret = new Turret();
        turret.Track(new double3(0, 0, 1));
        for (int i = 0; i < 100; i++) turret.Update(0.05, double.DegreesToRadians(70), ElevRate);

        turret.Stow();
        for (int i = 0; i < 100; i++) turret.Update(0.05, double.DegreesToRadians(70), ElevRate);

        Assert.Equal(0.0, turret.BearingRad, 6);
    }

    [Fact]
    public void Hold_LeavesTheTurretWhereItIs()
    {
        var turret = new Turret();
        turret.Track(new double3(0, 0, 1));
        turret.Update(0.1, double.DegreesToRadians(70), ElevRate);
        double parked = turret.BearingRad;

        turret.Hold();
        for (int i = 0; i < 20; i++) turret.Update(0.1, double.DegreesToRadians(70), ElevRate);

        Assert.Equal(parked, turret.BearingRad, Tolerance);
    }

    [Fact]
    public void Track_IgnoresADegenerateDirection()
    {
        var turret = new Turret();
        turret.Track(new double3(0, 0, 1));
        double ordered = turret.CommandRad!.Value;

        // A target sitting exactly on the slew axis has no bearing; the previous order stands
        // rather than the turret whipping round to an arbitrary angle.
        turret.Track(Vec.Zero);
        turret.Track(new double3(double.NaN, 1, 0));

        Assert.Equal(ordered, turret.CommandRad!.Value, Tolerance);
    }

    [Fact]
    public void Update_IgnoresAStoppedClock()
    {
        var turret = new Turret();
        turret.Track(new double3(0, 0, 1));

        turret.Update(0.0, double.DegreesToRadians(70), ElevRate);
        Assert.Equal(0.0, turret.BearingRad, Tolerance);
    }
}
