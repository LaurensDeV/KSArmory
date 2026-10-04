using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="IcbmConfig.AscentReserveSeconds"/> must leave a long shot exactly as it was: the same
/// cutoff, bit for bit, flown on the same build with the setting off and on. Never against stored
/// numbers, which would pin the flight rather than the setting.
///
/// <para>Only the pitch programme reads it, so only a pad launch can tell; a shot picked up in orbit
/// never enters that phase. It acts on any shot with less than the reserve's burn left at some point
/// in the pitch programme, which on these stacks is everything under ~1,500 km and nothing from
/// 2,000 km out (19-65 s to spare); between them a shot must still land, which is what keeps the
/// regime from being a cliff. The 418 km row is the control: it must differ, or the harness is
/// comparing a flight with itself.</para>
/// </summary>
public class AscentReserveGoldenTests
{
    private const double Reserve = 15.0;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(3.986004418e14, R, new double3(0, 0, 1), 7.2921159e-5);

    // IcbmFlightTests' pad rig: two liquid stages, no structural limit, no acceleration cap.
    private static IcbmFlightRig Uncapped()
    {
        double3 pad = ShortRangeAscentTests.OnTheGround(0.0);
        return new IcbmFlightRig
        {
            Body = Earth,
            PositionCci = pad,
            VelocityCci = Earth.GroundVelocityCci(pad),
            Stages =
            [
                new() { DryMassKg = 4_000, PropellantKg = 46_000, ThrustNewtons = 1_400_000, ExhaustVelocity = 2_600 },
                new() { DryMassKg = 1_200, PropellantKg = 12_000, ThrustNewtons = 260_000, ExhaustVelocity = 3_000 },
            ],
        };
    }

    private static double3 LatLon(double latDeg, double lonEastOfPadDeg)
    {
        double lat = double.DegreesToRadians(latDeg), lon = double.DegreesToRadians(lonEastOfPadDeg);
        return new double3(R * Math.Cos(lat) * Math.Cos(lon), R * Math.Cos(lat) * Math.Sin(lon), R * Math.Sin(lat));
    }

    public static TheoryData<string> LongShots => new()
    {
        "uncapped 2500", "uncapped 5000",
        "pad 2500", "pad 6269",
        "game 1600", "game 2000", "game 2500", "game flown 09-07",
    };

    private static (IcbmFlightRig Rig, IcbmConfig Config, double3 Aim) Fixture(string name, double reserve,
                                                                               bool anyRange = false,
                                                                               double slowLine = 0.0)
    {
        string[] p = name.Split(' ');
        IcbmConfig config = new()
        {
            Armed = true, AscentReserveSeconds = reserve, FlyAnyRange = anyRange, ShortShotSlowsLineSeconds = slowLine,
            ShortShotFinishesInTheAir = slowLine > 0.0, ShortShotSolvesWithDrag = slowLine > 0.0,
        };

        switch (p[0])
        {
            case "uncapped":
                return (Uncapped(), config, ShortRangeAscentTests.OnTheGround(double.Parse(p[1]) * 1000.0));
            case "pad":
                return (ShortRangeAscentTests.PadRig(), config, ShortRangeAscentTests.OnTheGround(double.Parse(p[1]) * 1000.0));
            default:
                // The scenario's settings, which are what the game's stack is flown on.
                config.MaxAccelerationGee = 8.0f;
                double3 aim = p[1] == "flown" ? LatLon(-26.485, -68.148 + 80.604)
                                              : GameStackShortRangeTests.South(double.Parse(p[1]) * 1000.0);
                return (GameStackShortRangeTests.GameStack(true), config, aim);
        }
    }

    private static IcbmFlightRig.Flight Fly(string name, double reserve, bool anyRange = false, double slowLine = 0.0)
    {
        (IcbmFlightRig rig, IcbmConfig config, double3 aim) = Fixture(name, reserve, anyRange, slowLine);
        // Both arms carry the warhead, so a drag solve that engaged where it should not would show.
        rig.Warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);
        return rig.Fly(new IcbmProgram(config), aim, 0.02, 6_000.0);
    }

    [Theory]
    [MemberData(nameof(LongShots))]
    public void ALongShotIsFlownExactlyAsItWasWithTheReserveOn(string fixture)
    {
        IcbmFlightRig.Flight off = Fly(fixture, 0.0);
        IcbmFlightRig.Flight on = Fly(fixture, Reserve);

        Assert.True(off.Reached, $"{fixture} never cut off with the reserve off: {off.Hold}");
        Assert.Equal(off.CutoffSeconds, on.CutoffSeconds);
        Assert.Equal(off.CutoffPositionCci, on.CutoffPositionCci);
        Assert.Equal(off.CutoffVelocityCci, on.CutoffVelocityCci);
    }

    /// <summary>
    /// <see cref="IcbmConfig.FlyAnyRange"/> leaves them alone too: what a solid stage cannot avoid
    /// adding is less than these shots need, and the floor its throttle sets is far under what they
    /// still have to gain.
    /// </summary>
    [Theory]
    [MemberData(nameof(LongShots))]
    public void ALongShotIsFlownExactlyAsItWasAtAnyRange(string fixture)
    {
        IcbmFlightRig.Flight off = Fly(fixture, 0.0);
        IcbmFlightRig.Flight on = Fly(fixture, 0.0, anyRange: true);

        Assert.Equal(off.CutoffSeconds, on.CutoffSeconds);
        Assert.Equal(off.CutoffPositionCci, on.CutoffPositionCci);
        Assert.Equal(off.CutoffVelocityCci, on.CutoffVelocityCci);
    }

    /// <summary>
    /// <see cref="IcbmConfig.ShortShotSlowsLineSeconds"/>, <see cref="IcbmConfig.ShortShotFinishesInTheAir"/>
    /// and <see cref="IcbmConfig.ShortShotSolvesWithDrag"/> act only on a short shot, so a long one is flown
    /// to the bit. Where they do act is <see cref="FloorHoldTests"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(LongShots))]
    public void ALongShotIsFlownExactlyAsItWasWithTheShortShotSettingsOn(string fixture)
    {
        IcbmFlightRig.Flight off = Fly(fixture, 0.0, anyRange: true);
        IcbmFlightRig.Flight on = Fly(fixture, 0.0, anyRange: true, slowLine: 0.5);

        Assert.Equal(off.CutoffSeconds, on.CutoffSeconds);
        Assert.Equal(off.CutoffPositionCci, on.CutoffPositionCci);
        Assert.Equal(off.CutoffVelocityCci, on.CutoffVelocityCci);
    }

    public static TheoryData<string> WhereItActs => new()
    {
        "pad 1000", "pad 1200", "pad 1600", "game 1000", "game 1200", "game 1600",
    };

    private static double MissMetres(string name, double reserve)
    {
        (IcbmFlightRig rig, IcbmConfig config, double3 aim) = Fixture(name, reserve);
        IcbmFlightRig.Flight flight = rig.Fly(new IcbmProgram(config), aim, 0.02, 6_000.0);
        if (!flight.Reached) return double.PositiveInfinity;

        Assert.True(ImpactPredictor.TryPredict(rig.Body, flight.CutoffPositionCci, flight.CutoffVelocityCci,
                                               1.0, ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit),
                    $"{name} never came down");
        return R * Vec.AngleBetween(hit.GroundFixedPointCci, rig.Body.CarryCci(aim, flight.CutoffSeconds));
    }

    [Theory]
    [MemberData(nameof(WhereItActs))]
    public void WhereTheReserveActsAShotStillLands(string fixture)
    {
        double off = MissMetres(fixture, 0.0);
        double on = MissMetres(fixture, Reserve);

        Assert.True(on <= Math.Max(off, 0.0) + 500.0, $"{fixture}: {off:F0} m off, {on:F0} m on");
    }

    [Fact]
    public void TheHarnessCanSeeTheReserveWhereItActs()
    {
        IcbmFlightRig.Flight off = Fly("game 418", 0.0);
        IcbmFlightRig.Flight on = Fly("game 418", Reserve);

        Assert.NotEqual(off.CutoffVelocityCci, on.CutoffVelocityCci);
    }
}
