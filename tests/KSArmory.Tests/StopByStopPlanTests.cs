using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a free walk's plan is made of once it is remade after the split (<c>docs/WIDE-SPREAD.md</c> step 5):
/// <see cref="IcbmConfig.PriceTheTankStopByStop"/>, <see cref="IcbmConfig.FreeHopsMeasureTheirSlots"/> and
/// <see cref="IcbmConfig.WalkShrinksAfterTheSplit"/>.
/// </summary>
public class StopByStopPlanTests
{
    private const double Gate = 420.0;

    // The Heavy bus full: 134.3 kg on 2.9 t, 2,055 m/s on its dearest axis.
    private const double TankKg = 134.3, MassKg = 2_900.0, Ve = 2_055.0;

    private static ReleaseItinerary.Bus Bus(double budget, double tankKg = double.NaN, double storeKg = 0.0)
        => new(Gate, double.NaN, 6, BudgetMetresPerSecond: budget, SpentMetresPerSecond: 0.0,
               TankKg: tankKg, MassKg: MassKg, TankExhaustVelocity: Ve, StoreMassKg: storeKg);

    [Theory]
    [InlineData(8_000.0)]
    [InlineData(15_000.0)]
    [InlineData(22_000.0)]
    [InlineData(40_000.0)]
    public void AtOneMassTheTankInKilogramsAffordsWhatTheCeilingDoes(double spacingMetres)
    {
        double ceiling = ReleaseLoop.TankCeilingMetresPerSecond(0.0, TankKg, MassKg, Ve);

        ReleaseItinerary inMetresPerSecond = ReleaseItinerary.Chain(6, spacingMetres, [1_000.0], Bus(ceiling));
        ReleaseItinerary inKilograms = ReleaseItinerary.Chain(6, spacingMetres, [1_000.0],
                                                              Bus(double.PositiveInfinity, TankKg));

        Assert.Equal(inMetresPerSecond.BudgetFits, inKilograms.BudgetFits);
    }

    [Fact]
    public void WarheadsLeavingLetTheSameTankPayForMoreStops()
    {
        // 22 km hops at 1,000 m per m/s: 22 m/s each, four of them at the full mass.
        ReleaseItinerary full = ReleaseItinerary.Chain(6, 22_000.0, [1_000.0], Bus(double.PositiveInfinity, TankKg));
        ReleaseItinerary shedding = ReleaseItinerary.Chain(6, 22_000.0, [1_000.0],
                                                           Bus(double.PositiveInfinity, TankKg, storeKg: 250.0));

        Assert.Equal(4, full.BudgetFits);
        Assert.Equal(6, shedding.BudgetFits);
    }

    [Fact]
    public void ALighterBusBurnsAHopInLessOfItsSlot()
    {
        ReleaseItinerary.Bus bus = Bus(double.PositiveInfinity, TankKg) with { HopAccelerationMetresPerSecond2 = 0.5 };

        Assert.Equal(ReleaseItinerary.HopOverheadSeconds + 40.0, bus.SlotFor(20.0), 9);
        Assert.Equal(ReleaseItinerary.HopOverheadSeconds + 20.0, bus.SlotFor(20.0, MassKg / 2.0), 9);
        Assert.Equal(ReleaseItinerary.HopOverheadSeconds + 40.0, (bus with { TankKg = double.NaN }).SlotFor(20.0, MassKg / 2.0), 9);
    }

    // What the trim pays for an along-track kilometre at 6,181 km against the time to go, free clock, the nose held
    // where it was at cutoff (m/s): flown from cutoff on the vacuum arc.
    private static readonly double[] Seconds = [1_484, 1_303, 1_121, 940, 758, 577, 396];
    private static readonly double[] Costs = [0.449, 0.476, 0.513, 0.570, 0.659, 0.813, 1.130];

    [Fact]
    public void AMeasuredSlotCurveNeverPricesAHopUnderWhatItCosts()
    {
        ReleaseItinerary.SlotCurve? curve = ReleaseItinerary.SlotCurve.From([Seconds[0], Seconds[3], Seconds[6]],
                                                                            [Costs[0], Costs[3], Costs[6]]);
        Assert.NotNull(curve);

        for (int i = 0; i < Seconds.Length; i++)
        {
            double priced = Costs[0] / curve.ShareAt(Seconds[i]);
            Assert.True(priced >= Costs[i] - 1e-9, $"{Seconds[i]} s: priced {priced:F3} under {Costs[i]:F3}");
        }
    }

    [Fact]
    public void AMeasuredSlotCurvePricesTheMiddleOfTheCoastCloserThanTheWorstCase()
    {
        ReleaseItinerary.SlotCurve? curve = ReleaseItinerary.SlotCurve.From(Seconds, Costs);
        Assert.NotNull(curve);

        double worstCase = Math.Pow(Seconds[3] / Seconds[0], ReleaseItinerary.FreeSlotExponent);
        Assert.Equal(Costs[0] / Costs[3], curve.ShareAt(Seconds[3]), 9);
        Assert.True(curve.ShareAt(Seconds[3]) > worstCase * 1.2);
    }

    [Fact]
    public void ASlotCurveIsNeverCheaperThanTheReachItWasMeasuredWith()
    {
        ReleaseItinerary.SlotCurve? cheaper = ReleaseItinerary.SlotCurve.From([2_000.0, 1_000.0], [1.0, 0.8]);
        Assert.NotNull(cheaper);

        Assert.Equal(1.0, cheaper.ShareAt(3_000.0));
        Assert.Equal(1.0, cheaper.ShareAt(1_500.0));
        Assert.Equal(Math.Min(1.0, 1.25 * Math.Pow(0.5, ReleaseItinerary.FreeSlotExponent)), cheaper.ShareAt(500.0), 9);
        Assert.Null(ReleaseItinerary.SlotCurve.From([2_000.0, 1_000.0], [1.0, 0.0]));
        Assert.Null(ReleaseItinerary.SlotCurve.From([1_000.0, 2_000.0], [1.0, 1.1]));
    }

    [Fact]
    public void AHopIsSolvedAgainWhileItsLastRoundSpentMoreThanOnePassAndNoMoreThanFourTimes()
    {
        Assert.True(ReleaseLoop.ResolvesTheHopAgain(0, 178.8));
        Assert.True(ReleaseLoop.ResolvesTheHopAgain(1, BusTrim.MaxMetresPerSecond + 0.1));
        Assert.False(ReleaseLoop.ResolvesTheHopAgain(1, BusTrim.MaxMetresPerSecond));
        Assert.False(ReleaseLoop.ResolvesTheHopAgain(ReleaseLoop.MaxHopRounds, 178.8));
    }

    [Fact]
    public void OnceATurningBusHasSplitOnlyTheSameStopsOrFewerUnderTheFlagReplaceTheWalk()
    {
        Assert.True(ReleaseLoop.MayReplan(turnsAtTheSplit: false, didSplit: true, 3, 5, mayShrink: false));
        Assert.True(ReleaseLoop.MayReplan(turnsAtTheSplit: true, didSplit: false, 3, 5, mayShrink: false));
        Assert.True(ReleaseLoop.MayReplan(turnsAtTheSplit: true, didSplit: true, 5, 5, mayShrink: false));

        Assert.False(ReleaseLoop.MayReplan(turnsAtTheSplit: true, didSplit: true, 3, 5, mayShrink: false));
        Assert.True(ReleaseLoop.MayReplan(turnsAtTheSplit: true, didSplit: true, 3, 5, mayShrink: true));
        Assert.False(ReleaseLoop.MayReplan(turnsAtTheSplit: true, didSplit: true, 6, 5, mayShrink: true));
        Assert.False(ReleaseLoop.MayReplan(turnsAtTheSplit: true, didSplit: true, 0, 5, mayShrink: true));
    }
}
