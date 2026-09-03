using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the trim is charged for a correction, in metres a second per kilometre of aim.
///
/// <para><b>The exchange rate is the missing half of the divergence.</b> Flown traces show a
/// post-boost pass converging to hundredths of a metre a second and the next solve demanding ten
/// times more — read as the loop diverging. It is not: at 12,902 km an aim move costs 0.53 m/s per
/// kilometre, so a 12.63 m/s demand is 24 km of aim movement and a 43.59 m/s demand is 82 km. The
/// trim is faithfully buying an aim correction far larger than the miss it corrects.</para>
///
/// <para>The second measurement is what <see cref="AimCorrection.MaxMetres"/> is worth in
/// propellant. 300 km of permitted bias against a 60 m/s budget is an aim the trim can never fly at
/// any range this mod shoots at — which is what makes the demand exceed the ceiling every pass
/// until the budget is gone.</para>
/// </summary>
public class ArrivalDebtTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;
    private const double EarthSpin = 7.2921159e-5;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    private static double DensityAt(double3 p)
        => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / ScaleHeight);

    private static double3 Downrange(double metres)
        => new(R * Math.Cos(metres / R), R * Math.Sin(metres / R), 0);

    /// <summary>The bus <see cref="CutoffResidualTests"/> flies, with the game's actuator.</summary>
    private static IcbmFlightRig InOrbit()
        => new()
        {
            Body = Earth,
            PositionCci = new double3(R + 300_000.0, 0, 0),
            VelocityCci = new double3(0, Math.Sqrt(Mu / (R + 300_000.0)), 0),
            Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
            CommandLatencyFrames = 1,
            ThrottleRatePerSecond = 2.0,
            MinThrottle = 0.12,
            StepJitter = 0.5,
        };

    private readonly record struct Shot(IcbmFlightRig.Flight Flight, IcbmProgram Program, double3 AimAtArrival, double FlightSeconds);

    /// <summary>Fly to cutoff, then fly the warhead down through air to find when it really lands.</summary>
    private static Shot Fly(double shotMetres)
    {
        IcbmFlightRig rig = InOrbit();
        IcbmProgram program = new(new IcbmConfig { Armed = true, ArrivalPreference = FixtureGeometry.ArrivalPreference });
        double3 aim = Downrange(shotMetres);
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 6_000.0);

        Assert.True(flight.Reached);

        bool flew = ImpactPredictor.TryPredict(
            Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, 1.0,
            ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact wet, null, null,
            new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21));

        Assert.True(flew);

        return new Shot(flight, program,
                        Earth.CarryCci(aim, flight.CutoffSeconds + wet.Seconds), wet.Seconds);
    }

    /// <summary>The velocity a bus must find to move its impact by <paramref name="km"/>.</summary>
    private static double CostOfMovingTheAim(in Shot shot, double km, double arrivalSlip = 0.0)
    {
        double3 moved = Earth.CarryCci(Downrange(GroundMetres(shot) + km * 1000.0),
                                       shot.Flight.CutoffSeconds + shot.FlightSeconds);

        Assert.True(BallisticArc.TrySolve(Earth, shot.Flight.CutoffPositionCci, shot.AimAtArrival,
                                          shot.FlightSeconds, out BallisticArc.Solution held, false));
        Assert.True(BallisticArc.TrySolve(Earth, shot.Flight.CutoffPositionCci, moved,
                                          shot.FlightSeconds + arrivalSlip,
                                          out BallisticArc.Solution asked, false));

        return Vec.Len(asked.RequiredVelocityCci - held.RequiredVelocityCci);
    }

    private static double GroundMetres(in Shot shot)
        => R * Vec.AngleBetween(Downrange(0), Earth.UncarryCci(shot.AimAtArrival,
                                shot.Flight.CutoffSeconds + shot.FlightSeconds));

    /// <summary>
    /// The demands in the flown traces are aim movements, not a diverging solve.
    /// </summary>
    [Theory]
    [InlineData(3_459_000.0, 2.48)]
    [InlineData(8_500_000.0, 1.03)]
    [InlineData(12_902_000.0, 0.53)]
    public void AnAimMoveCostsMetresPerSecondPerKilometre(double shotMetres, double expected)
    {
        Shot shot = Fly(shotMetres);

        double perKm = CostOfMovingTheAim(shot, 1.0);
        Out.WriteLine($"{shotMetres / 1000:F0} km: {perKm:F2} m/s per km of aim");

        Assert.Equal(expected, perKm, 1);

        // Linear over the range the correction actually walks, which is what lets one number price
        // a demand read off a log.
        Assert.Equal(perKm * 20.0, CostOfMovingTheAim(shot, 20.0), 0);
    }

    /// <summary>
    /// The permitted bias is an aim the trim cannot fly, at every range this mod shoots at.
    ///
    /// <para>That is the shape of the flown failure: the demand is the distance to an unaffordable
    /// aim, so it exceeds whatever is left of the ceiling on every pass until the budget is spent.
    /// A bound in metres cannot know this; only one derived from the exchange rate can.</para>
    /// </summary>
    [Theory]
    [InlineData(3_459_000.0)]
    [InlineData(8_500_000.0)]
    [InlineData(12_902_000.0)]
    public void TheAimMayBeWalkedFurtherThanTheTrimCanPayFor(double shotMetres)
    {
        Shot shot = Fly(shotMetres);

        double perKm = CostOfMovingTheAim(shot, 1.0);
        double affordableKm = PostBoostAim.MaxTrimMetresPerSecond / perKm;

        Out.WriteLine($"{shotMetres / 1000:F0} km: the budget buys {affordableKm:F0} km of aim, "
                      + $"and {AimCorrection.MaxMetres / 1000:F0} km is permitted");

        Assert.True(affordableKm < AimCorrection.MaxMetres / 1000.0);
    }

    /// <summary>
    /// Pinning the arrival is free at the ranges the eight-rocket instrument flies.
    ///
    /// <para><see cref="AimCorrection.Freeze"/> and the latch in <c>IcbmProgram</c> both hold that
    /// a committed arrival makes the same aim change dearer. It does at 3,459 km, where a 20 km move
    /// is 4.5x the free cost — and not at all at 8,500 or 12,902, where the pinned arrival is
    /// already the cheapest one. So latching is not what makes a long correction expensive.</para>
    /// </summary>
    [Fact]
    public void PinningTheArrivalCostsNothingAtLongRange()
    {
        Shot shot = Fly(12_902_000.0);

        double pinned = CostOfMovingTheAim(shot, 20.0);
        double free = double.PositiveInfinity;

        for (double slip = -shot.FlightSeconds * 0.3; slip <= shot.FlightSeconds * 0.3;
             slip += shot.FlightSeconds * 0.005)
        {
            free = Math.Min(free, CostOfMovingTheAim(shot, 20.0, slip));
        }

        Out.WriteLine($"20 km of aim: {pinned:F2} m/s pinned, {free:F2} m/s with the arrival free");

        Assert.Equal(pinned, free, 1);
    }
}
