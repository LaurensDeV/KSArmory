using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Deciding which craft a launcher went to when a decoupler split the one it was on.
/// </summary>
public class PlatformHandoverTests
{
    private static HandoverCandidate At(int craft, double metres, bool crewed = false, int ordinal = 0)
        => new(craft, ordinal, metres, crewed);

    [Fact]
    public void NothingCarryingItMovesNothing()
    {
        Handover h = PlatformHandover.Choose([]);
        Assert.Equal(HandoverVerdict.NotFound, h.Verdict);
    }

    [Fact]
    public void OneCandidateIsTheAnswer()
    {
        Handover h = PlatformHandover.Choose([At(7, 12.0, ordinal: 2)]);

        Assert.Equal(HandoverVerdict.Move, h.Verdict);
        Assert.Equal(7, h.CraftIndex);
        Assert.Equal(2, h.Ordinal);
    }

    /// <summary>A launcher another system already runs is that system's, not this one's.</summary>
    [Fact]
    public void ACrewedCandidateIsNotOurs()
    {
        Handover h = PlatformHandover.Choose([At(1, 5.0, crewed: true)]);
        Assert.Equal(HandoverVerdict.NotFound, h.Verdict);
    }

    [Fact]
    public void ACandidateTooFarAwayIsSomebodyElsesLauncher()
    {
        Handover h = PlatformHandover.Choose([At(1, PlatformHandover.MaxMetres + 1.0)]);
        Assert.Equal(HandoverVerdict.NotFound, h.Verdict);
    }

    [Fact]
    public void TheNearerOfTwoSeparatedCandidatesWins()
    {
        Handover h = PlatformHandover.Choose([At(1, 900.0), At(2, 20.0)]);

        Assert.Equal(HandoverVerdict.Move, h.Verdict);
        Assert.Equal(2, h.CraftIndex);
    }

    /// <summary>
    /// Two that cannot be told apart are refused. Choosing wrongly strands the operator's settings
    /// on one craft and leaves the other to be crewed fresh with defaults — worse than not moving.
    /// </summary>
    [Fact]
    public void TwoCoincidentCandidatesAreRefusedRatherThanGuessed()
    {
        Handover h = PlatformHandover.Choose([At(1, 20.0), At(2, 30.0)]);

        Assert.Equal(HandoverVerdict.Ambiguous, h.Verdict);
        Assert.Contains("two craft carry it", h.Why);
    }

    [Fact]
    public void AnAlreadyCrewedNeighbourDoesNotMakeItAmbiguous()
    {
        Handover h = PlatformHandover.Choose([At(1, 20.0), At(2, 30.0, crewed: true)]);

        Assert.Equal(HandoverVerdict.Move, h.Verdict);
        Assert.Equal(1, h.CraftIndex);
    }
}
