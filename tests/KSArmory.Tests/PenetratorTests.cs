using Xunit;

namespace KSArmory.Tests;

public class PenetratorTests
{
    private static readonly ArmourMaterial Rolled = new()
    {
        Name = "Rolled", DisplayName = "Rolled", DensityKgPerM3 = 7850f, LambertCoefficient = 4000f,
        ResistancePascals = 5.0e9f,
    };

    private const double Dart = 0.1, Across = 0.01275, Long = 4.0, Leftover = 6000.0;

    private static LayerCrossing Plain(int index) => new(index, 1.0e5, default, default, 1.0, 0.0);

    private static LayerCrossing Plate(int index, double thickness, double cos = 1.0) =>
        new(index, 1.0e5, default, new ArmourLayer(Rolled, thickness), cos, thickness);

    [Fact]
    public void PartsWithNoArmourEachKeepHalfAsBefore()
    {
        List<CrossingResult> results = [];
        double left = Penetrator.Walk([Plain(0), Plain(1)], Dart, Across, Long, 1113.0, 1.0, Leftover, null, results);

        double energy = 0.5 * Dart * 1113.0 * 1113.0;
        Assert.Equal(PartToughness.KineticCoupling * energy / 1.0e5, results[0].Share, 9);
        Assert.Equal(PartToughness.KineticCoupling * energy * 0.5 / 1.0e5, results[1].Share, 9);
        Assert.Equal(1113.0 * 0.5, left, 6);
    }

    [Fact]
    public void AThickPlateStopsTheDartAndNothingBehindItIsReached()
    {
        List<CrossingResult> results = [];
        double left = Penetrator.Walk([Plate(0, 0.06), Plain(1)], Dart, Across, Long, 1113.0, 1.0, Leftover, null, results);

        Assert.Equal(0.0, left);
        CrossingResult stop = Assert.Single(results);
        Assert.Equal(CrossingOutcome.Stopped, stop.Outcome);
        Assert.True(stop.DentMetres > 0.0 && stop.DentMetres < 0.06);
    }

    [Fact]
    public void AThinPlateSlowsTheDartBeforeTheTankBehindItRams()
    {
        TankWall wall = new(0.004, 418.8e6, 2.79e5);
        double rammedAt = 0.0;
        List<CrossingResult> results = [];
        Penetrator.Walk([Plate(0, 0.01), new LayerCrossing(1, 1.0e5, wall, default, 1.0, 0.004)], Dart, Across, Long,
                        1113.0, 1.0, Leftover, (_, v) => { rammedAt = v; return 0.5; }, results);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Share > 0.0);
        Assert.InRange(rammedAt, 1.0, 1112.0);
        Assert.Equal(0.0, results[1].Share);
    }

    [Fact]
    public void ACrossingAlreadyJudgedStoppedStopsWhateverTheLawSays()
    {
        List<CrossingResult> results = [];
        LayerCrossing judged = Plate(0, 0.001) with { Judged = false };
        double left = Penetrator.Walk([judged, Plain(1)], Dart, Across, Long, 1113.0, 1.0, Leftover, null, results);

        Assert.Equal(0.0, left);
        Assert.Single(results);
    }

    [Fact]
    public void AGrazingDartThatDoesNotGetThroughGlances()
    {
        List<CrossingResult> results = [];
        Penetrator.Walk([Plate(0, 0.05, Math.Cos(80.0 * Math.PI / 180.0))], Dart, Across, Long, 600.0, 1.0, Leftover,
                        null, results);

        Assert.Equal(CrossingOutcome.Glanced, Assert.Single(results).Outcome);
    }
}
