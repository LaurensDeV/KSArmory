using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A walk's hop priced at what <see cref="BusTrim"/> pays for it, which is the divert's parts on the bus's three
/// axes summed, because it fires one at a time. Flown at 4 km, hops priced at their length cost 1.7x that and
/// every walk ran dry at stop 5 of 6. <c>docs/MIRV-TARGETS.md</c>, "six stops to the ground".
/// </summary>
public class HopTrimPriceTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    // The flown release at 6,179 km that DivertFootprintTests prices.
    private static readonly double3 FlownPositionCci = new(3_835_254.4, -5_998_331.6, -1_302_454.7);
    private static readonly double3 FlownVelocityCci = new(279.7753, 2844.5295, -3967.0558);

    private static double DensityAt(double3 p)
    {
        double altitude = Math.Max(0.0, Vec.Len(p) - R);
        return altitude >= 167_410.0 ? 0.0 : Math.Exp(-altitude / 8_000.0);
    }

    private static ReleaseFocus.FlownSensitivity Columns()
    {
        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21), 2.0, R, true);
        return ReleaseFocus.FlownSensitivity.TryFly(Earth, FlownPositionCci, FlownVelocityCci, air)
               ?? throw new InvalidOperationException("the columns did not come down");
    }

    // A bus holding a line that no hop along the ground lies on, as the flown bus's does: its nose 113 deg from
    // the track and a hop splitting over all three axes.
    private static (double3 Nose, double3 Right, double3 Down) TiltedAxes()
    {
        double3 nose = Vec.Unit(Vec.Unit(FlownVelocityCci) + (Vec.Unit(FlownPositionCci) * 0.6));
        double3 right = Vec.Unit(Vec.Cross(FlownPositionCci, nose));
        return (nose, right, Vec.Cross(nose, right));
    }

    private static DivertFootprint Priced(ReleaseFocus.FlownSensitivity flown, double3 nose, double3 right,
                                          double3 down)
    {
        Assert.True(DivertFootprint.TryFrom(Earth, flown, DivertFootprint.ArrivalClock.Pinned, true,
                                            out DivertFootprint footprint));
        Assert.True(DivertFootprint.TryAxisPrice(Earth, flown, nose, right, down, out DivertFootprint.AxisPrice axes));
        return footprint with { Axes = axes };
    }

    // What BusTrim spends taking a bus that is off its solution by dv back onto it, jets on all six directions.
    // Under the 10 m/s a pass will fly, which it refuses whole.
    private static double TrimSpends(double3 dvCci, double3 nose, double3 right, double3 down)
    {
        TrimBus bus = new()
        {
            PositionCci = FlownPositionCci, VelocityCci = FlownVelocityCci - dvCci,
            NoseCci = nose, RightCci = right, DownCci = down,
            AxialAcceleration = 0.55, LateralAcceleration = 0.55,
        };

        BusTrim trim = new();
        trim.Begin();

        const double step = 0.013;
        double spent = 0.0;

        for (double t = 0.0; t < 600.0; t += step)
        {
            TrimCommand c = trim.Update(step, new TrimSituation(Earth, bus.PositionCci, bus.VelocityCci,
                                                                FlownPositionCci, FlownVelocityCci, t,
                                                                nose, right, down));
            if (c.Done) break;

            double3 before = bus.VelocityCci + (Earth.GravityCci(bus.PositionCci) * step);
            bus.Step(Earth, c.Fire, step, c.Pulse);
            spent += Vec.Len(bus.VelocityCci - before);
        }

        return spent;
    }

    [Theory]
    [InlineData(2_500.0, 0.0)]
    [InlineData(0.0, 2_500.0)]
    [InlineData(1_768.0, 1_768.0)]
    public void AHopIsPricedAtWhatTheTrimSpendsOnIt(double alongMetres, double crossMetres)
    {
        (double3 nose, double3 right, double3 down) = TiltedAxes();
        DivertFootprint footprint = Priced(Columns(), nose, right, down);

        double3 onAxes = (footprint.Axes.PerMetreAlong * alongMetres) + (footprint.Axes.PerMetreAcross * crossMetres);
        double3 dvCci = (nose * onAxes.X) + (right * onAxes.Y) + (down * onAxes.Z);

        double spent = TrimSpends(dvCci, nose, right, down);
        double priced = footprint.TrimCostMetresPerSecond(alongMetres, crossMetres);
        double atItsLength = footprint.CostMetresPerSecond(alongMetres, crossMetres);

        Out.WriteLine($"{alongMetres:F0} m along, {crossMetres:F0} across: the trim spends {spent:F2} m/s, priced "
                      + $"{priced:F2}, at its length {atItsLength:F2}");

        Assert.True(Math.Abs(priced - spent) <= 0.05 * spent,
                    $"priced at {priced:F2} m/s against {spent:F2} the trim spent");
    }

    [Fact]
    public void TheAxisPriceIsTheSameDivertTheReachDescribes()
    {
        (double3 nose, double3 right, double3 down) = TiltedAxes();
        DivertFootprint footprint = Priced(Columns(), nose, right, down);

        foreach ((double along, double cross) in new[] { (1.0, 0.0), (0.0, 1.0), (0.6, 0.8) })
        {
            double3 onAxes = (footprint.Axes.PerMetreAlong * along) + (footprint.Axes.PerMetreAcross * cross);
            double length = footprint.CostMetresPerSecond(along, cross);

            Assert.True(Math.Abs(Vec.Len(onAxes) - length) <= 0.03 * length,
                        $"{Vec.Len(onAxes):E3} m/s a metre against the reach's {length:E3}");
        }
    }

    [Fact]
    public void ABusPointedAlongTheDivertPaysItsLength()
    {
        ReleaseFocus.FlownSensitivity flown = Columns();
        (double3 nose0, double3 right0, double3 down0) = TiltedAxes();
        DivertFootprint tilted = Priced(flown, nose0, right0, down0);

        double3 alongCci = (nose0 * tilted.Axes.PerMetreAlong.X) + (right0 * tilted.Axes.PerMetreAlong.Y)
                           + (down0 * tilted.Axes.PerMetreAlong.Z);
        double3 nose = Vec.Unit(alongCci);
        double3 right = Vec.Unit(Vec.Cross(FlownPositionCci, nose));
        DivertFootprint aligned = Priced(flown, nose, right, Vec.Cross(nose, right));

        Assert.Equal(aligned.CostMetresPerSecond(4_000.0, 0.0), aligned.TrimCostMetresPerSecond(4_000.0, 0.0), 2);
    }

    [Fact]
    public void WithNoAxesADivertIsPricedAtTheCeiling()
    {
        Assert.True(DivertFootprint.TryFrom(Earth, Columns(), DivertFootprint.ArrivalClock.Pinned, true,
                                            out DivertFootprint footprint));

        Assert.Equal(DivertFootprint.AxisSumCeiling * footprint.CostMetresPerSecond(3_000.0, 1_000.0),
                     footprint.TrimCostMetresPerSecond(3_000.0, 1_000.0), 9);
        Assert.Equal(DivertFootprint.AxisSumCeiling, footprint.WorstAxisFactor(), 9);
    }

    [Fact]
    public void TheWalkIsPlannedAtTheTrimsPriceAndEachPassAtItsLength()
    {
        (double3 nose, double3 right, double3 down) = TiltedAxes();
        DivertFootprint footprint = Priced(Columns(), nose, right, down);

        ReachDisplay.Placed[] chain = [.. Enumerable.Range(0, 3).Select(k => new ReachDisplay.Placed(-2_500.0 * k, 0.0, 1))];
        ReleaseItinerary.Bus bus = new(420.0, 1_300.0, 6);

        ReachDisplay atLength = ReachDisplay.For(footprint, chain, bus, IcbmPhase.Coast, false, 6, 300.0, 0,
                                                 ReachHold.Unflown);
        ReachDisplay atTrim = ReachDisplay.For(footprint, chain, bus, IcbmPhase.Coast, false, 6, 300.0, 0,
                                               ReachHold.Unflown, priceAtTheTrim: true);

        double hopAtLength = atLength.Flown.Itinerary.Stops[1].HopMetresPerSecond;
        double hopAtTrim = atTrim.Flown.Itinerary.Stops[1].HopMetresPerSecond;
        double expected = footprint.TrimCostMetresPerSecond(-2_500.0, 0.0);

        Out.WriteLine($"hop at its length {hopAtLength:F2} m/s, at the trim {hopAtTrim:F2}; ring "
                      + $"{atLength.SemiMinorMetres:F0} -> {atTrim.SemiMinorMetres:F0} m, "
                      + $"{atTrim.LeftMetresPerSecond:F1} m/s left");

        Assert.Equal(expected, hopAtTrim, 2);
        Assert.True(hopAtTrim > hopAtLength * 1.2);
        double ringHop = Math.Min(atTrim.LeftMetresPerSecond / footprint.WorstAxisFactor(), BusTrim.MaxMetresPerSecond);
        Assert.Equal(ringHop, atTrim.HopMetresPerSecond, 9);
        Assert.True(atTrim.Flown.Itinerary.Stops[1].PassFlies < hopAtTrim);
        Assert.Equal(hopAtLength, atTrim.Flown.Itinerary.Stops[1].PassFlies, 6);
    }

    [Fact]
    public void APassIsBoundedByTheHopsLengthAndTheBudgetByItsPrice()
    {
        Assert.Equal(HopHold.WillFly, ReleaseLoop.CanFlyTheHop(11.0, 0.5, double.NaN, 20.0, 60.0, passMetresPerSecond: 8.0));
        Assert.Equal(HopHold.BeyondOnePass, ReleaseLoop.CanFlyTheHop(11.0, 0.5, double.NaN, 20.0, 60.0));
        Assert.Equal(HopHold.BeyondTheBudget,
                     ReleaseLoop.CanFlyTheHop(11.0, 0.5, double.NaN, 50.0, 60.0, passMetresPerSecond: 8.0));
    }
}
