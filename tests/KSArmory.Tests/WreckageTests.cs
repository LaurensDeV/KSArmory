using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a burst broke off is wreckage, and the names the engine gives the pieces are the only
/// record of where they came from.
/// </summary>
public class WreckageTests
{
    /// <summary>The flown case: a piece of a piece of a piece of the drone that was hit.</summary>
    [Fact]
    public void APieceIsWreckageHoweverManySplitsDown()
    {
        Wreckage wreckage = new();
        wreckage.Broke("AD Test Drone 1");

        Assert.True(wreckage.IsPiece("AD Test Drone 1_3"));
        Assert.True(wreckage.IsPiece("AD Test Drone 1_3_1_7"));
    }

    /// <summary>Losing parts is not being destroyed: what is left of the craft is still a target.</summary>
    [Fact]
    public void TheBrokenCraftItselfIsStillATarget()
    {
        Wreckage wreckage = new();
        wreckage.Broke("AD Test Drone 1");

        Assert.False(wreckage.IsPiece("AD Test Drone 1"));
    }

    /// <summary>
    /// A rocket that stages names its stages the same way, and a stage nobody shot at is not
    /// wreckage -- nor is a craft whose name merely starts the same.
    /// </summary>
    [Fact]
    public void OnlyPiecesOfACraftThisModBrokeAreWreckage()
    {
        Wreckage wreckage = new();
        wreckage.Broke("Rocket_1");

        Assert.True(wreckage.IsPiece("Rocket_1_2"));
        Assert.False(wreckage.IsPiece("Rocket_2"));
        Assert.False(wreckage.IsPiece("Rocket"));
        Assert.False(wreckage.IsPiece("Rocket_1a"));
        Assert.False(wreckage.IsPiece("Rocket_1_"));
    }

    [Fact]
    public void NothingIsWreckageUntilSomethingIsBroken()
    {
        Wreckage wreckage = new();

        Assert.False(wreckage.IsPiece("AD Test Drone 1_3"));
        Assert.False(wreckage.IsPiece(null));
        Assert.False(wreckage.IsPiece(""));
    }

    [Fact]
    public void ClearingForgetsWhatWasBroken()
    {
        Wreckage wreckage = new();
        wreckage.Broke("AD Test Drone 1");
        wreckage.Clear();

        Assert.False(wreckage.IsPiece("AD Test Drone 1_3"));
    }
}
