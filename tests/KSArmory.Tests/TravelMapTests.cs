using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The travel map a mount's own craft leaves it, the sweep that builds it, and the turret reading
/// it. What the sweep tests against is the engine's geometry, so here a predicate stands in for it.
/// </summary>
public class TravelMapTests
{
    private static readonly double MinE = double.DegreesToRadians(-10), MaxE = double.DegreesToRadians(50);

    private static TravelMap Sweep(Func<double, double, bool> blocked, out int tested, int bearings = 72, int rows = 13)
    {
        var sweep = new TravelSweep(bearings, rows);
        var probe = new TravelMap(new bool[bearings, rows], MinE, MaxE, 0.0);
        while (sweep.TryNext(out int i, out int j))
        {
            sweep.Record(i, j, !blocked(i * probe.BearingStepRad, probe.ElevationOfRow(j)));
        }
        tested = sweep.Tested;
        return sweep.Result(MinE, MaxE, 0.0);
    }

    private static double Deg(double rad) => double.RadiansToDegrees(rad);

    [Fact]
    public void NothingInTheWayIsAWholeTurnAndTheWholeTravel()
    {
        TravelMap map = Sweep((_, _) => false, out _);

        Assert.Null(map.Arc);
        (double floor, double ceiling) = map.BandAt(double.DegreesToRadians(137));
        Assert.Equal(-10.0, Deg(floor), 9);
        Assert.Equal(50.0, Deg(ceiling), 9);
    }

    [Fact]
    public void AHostBehindTheMountLimitsTheTraverseToTheArcRoundForward()
    {
        // Blocked from 115 deg round to -115: the fuselage behind a chin turret.
        TravelMap map = Sweep((b, _) => Math.Abs(Turret.WrapPi(b)) > double.DegreesToRadians(112), out _);

        Assert.NotNull(map.Arc);
        Assert.Equal(-110.0, Deg(map.Arc!.Value.Lo), 6);
        Assert.Equal(110.0, Deg(map.Arc!.Value.Hi), 6);
    }

    [Fact]
    public void AnObstacleOffToOneSideGivesAnArcThatWrapsBehind()
    {
        // Something at 90 deg only: the gun can go the long way round to 100 deg through 180.
        TravelMap map = Sweep((b, _) => Math.Abs(Deg(b) - 90.0) < 8.0, out _);

        Assert.NotNull(map.Arc);
        Assert.Equal(80.0, Deg(map.Arc!.Value.Hi), 6);
        Assert.Equal(-260.0, Deg(map.Arc!.Value.Lo), 6);
    }

    [Fact]
    public void ABlockedDepressionNarrowsTheBandOnlyWhereItIs()
    {
        // Pointing forward the line of fire meets the host below 20 deg of depression.
        TravelMap map = Sweep((b, e) => Math.Abs(Turret.WrapPi(b)) < double.DegreesToRadians(30)
                                        && e > double.DegreesToRadians(22), out _);

        Assert.Equal(20.0, Deg(map.BandAt(0.0).Ceiling), 6);
        Assert.Equal(50.0, Deg(map.BandAt(double.DegreesToRadians(90)).Ceiling), 6);
    }

    [Fact]
    public void TheCoarsePassSavesTestsAndFindsTheSameAnswerForAWideObstacle()
    {
        Func<double, double, bool> blocked = (b, e) => Math.Abs(Turret.WrapPi(b)) > double.DegreesToRadians(112)
                                                       || (Math.Abs(Turret.WrapPi(b)) < 0.5 && e > 0.4);
        TravelMap swept = Sweep(blocked, out int tested);

        var all = new bool[72, 13];
        for (int i = 0; i < 72; i++)
            for (int j = 0; j < 13; j++)
                all[i, j] = !blocked(i * swept.BearingStepRad, swept.ElevationOfRow(j));
        var brute = new TravelMap(all, MinE, MaxE, 0.0);

        for (int i = 0; i < 72; i++)
            for (int j = 0; j < 13; j++)
                Assert.Equal(brute.IsClear(i, j), swept.IsClear(i, j));
        Assert.True(tested < 72 * 13 / 2, $"{tested} tests of {72 * 13}");
    }

    [Fact]
    public void TheTurretStaysInsideTheArcTheMapGivesIt()
    {
        TravelMap map = Sweep((b, _) => Math.Abs(Turret.WrapPi(b)) > double.DegreesToRadians(112), out _);
        var turret = new Turret { Map = map, MinElevationRad = MinE, MaxElevationRad = MaxE, ForwardArcRad = 0.0 };

        turret.Point(Math.PI);
        turret.Update(10.0, 1.0, 1.0);

        Assert.InRange(Deg(Math.Abs(turret.BearingRad)), 109.999, 110.001);
    }

    [Fact]
    public void TheTurretTurnsTheLongWayRoundAnObstacleBesideIt()
    {
        // From 60 to 120 the short way passes through 90, where the obstacle is.
        TravelMap map = Sweep((b, _) => Math.Abs(Deg(b) - 90.0) < 8.0, out _);
        var turret = new Turret { Map = map, MinElevationRad = MinE, MaxElevationRad = MaxE, ForwardArcRad = 0.0 };
        turret.Point(double.DegreesToRadians(60));
        turret.Update(10.0, 10.0, 1.0);

        turret.Point(double.DegreesToRadians(120));
        turret.Update(0.1, 1.0, 1.0);

        Assert.True(turret.BearingRad < double.DegreesToRadians(60), $"{Deg(turret.BearingRad):F1} deg");
    }

    [Fact]
    public void TheProbesRunFromTheTrunnionOutAlongTheBore()
    {
        LauncherProfile gun = Arsenal.M197;
        (double3 Start, double3 End)[] probes = TravelSweep.Probes(gun, 0.0, 0.0);

        Assert.True(Vec.Len(probes[0].Start - (gun.TurretPivot + gun.GunPivotFromTurret)) < 1e-9);
        Assert.Equal(2.63549, probes[0].End.Y - probes[0].Start.Y, 5);
        double3 fire = probes[1].End - probes[1].Start;
        Assert.Equal(TravelSweep.LineOfFireMetres, fire.Y, 6);
        Assert.True(Math.Abs(fire.X) + Math.Abs(fire.Z) < 1e-9);
    }
}
