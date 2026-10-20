using Xunit;

namespace KSArmory.Tests;

public class PictureChoiceTests
{
    private sealed class Seen : TrackState
    {
        public required object Handle { get; init; }
    }

    private static readonly object Booster = new();
    private static readonly object Airliner = new();
    private static readonly object Wingman = new();
    private static readonly object Drone = new();

    private static Seen Threat(object who, double secondsToCpa, double range)
        => new() { Handle = who, IsThreat = true, TimeToClosestApproach = secondsToCpa, Range = range };

    private static Seen Passing(object who, double range, Allegiance side = Allegiance.Unknown)
        => new() { Handle = who, IsThreat = false, Range = range, Allegiance = side };

    private static List<Seen> Merged(params IReadOnlyList<Seen>[] sets)
    {
        List<Seen> picture = [];
        PictureChoice.Merge(sets, t => t.Handle, picture);
        return picture;
    }

    private static object? Chosen(List<Seen> picture, object? held = null)
    {
        int i = PictureChoice.Choose(picture, t => t.Handle, held);
        return i >= 0 ? picture[i].Handle : null;
    }

    [Fact]
    public void TheSameContactSeenByTwoSetsIsOneEntryCarryingTheMoreUrgentSighting()
    {
        List<Seen> picture = Merged([Passing(Booster, 200_000)], [Threat(Booster, 30, 180_000), Passing(Airliner, 50_000)]);

        Assert.Equal(2, picture.Count);
        Seen booster = Assert.Single(picture, t => ReferenceEquals(t.Handle, Booster));
        Assert.True(booster.IsThreat);
    }

    [Fact]
    public void AHostileThreatBeatsANearerPasserBy()
    {
        List<Seen> picture = Merged([Passing(Airliner, 20_000)], [Threat(Booster, 60, 250_000)]);

        Assert.Same(Booster, Chosen(picture));
    }

    [Fact]
    public void WithNoThreatTheNearestPasserByIsHandedOut()
    {
        List<Seen> picture = Merged([Passing(Airliner, 80_000), Passing(Drone, 30_000)]);

        Assert.Same(Drone, Chosen(picture));
    }

    [Fact]
    public void AFriendlyIsNeverHandedOutHoweverNear()
    {
        Assert.Null(Chosen(Merged([Passing(Wingman, 2_000, Allegiance.Friendly)])));
        Assert.Same(Airliner, Chosen(Merged([Passing(Wingman, 2_000, Allegiance.Friendly), Passing(Airliner, 90_000)])));
    }

    [Fact]
    public void AContactAlreadyHandedOutIsKeptAgainstOneOnlySlightlyMoreUrgent()
    {
        List<Seen> picture = Merged([Threat(Booster, 40, 200_000), Threat(Drone, 39.5, 20_000)]);

        Assert.Same(Drone, Chosen(picture));
        Assert.Same(Booster, Chosen(picture, held: Booster));
    }

    [Fact]
    public void AnEmptyPictureHandsOutNothing()
    {
        Assert.Null(Chosen(Merged()));
    }
}
