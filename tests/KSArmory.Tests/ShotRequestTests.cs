using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The one line the harness sends the game, read.
///
/// <para>Load-bearing because the flight it starts is seven minutes long: a request read wrongly
/// aims somewhere else and the run reports a failure that is nothing but a typo, or — worse —
/// reports a pass against a target nobody asked for.</para>
/// </summary>
public class ShotRequestTests
{
    /// <summary>
    /// A request has to know whether the operator named a place, because the scenario shoots at a
    /// defended site when one is in the scene and must not do that over the top of an explicit aim.
    /// </summary>
    [Fact]
    public void AParsedAimIsMarkedAsGivenAndTheDefaultIsNot()
    {
        Assert.False(ShotRequest.Default.AimWasGiven);

        Assert.True(ShotRequest.TryParse("26.485S,68.148W", out ShotRequest shot, out string trouble), trouble);
        Assert.True(shot.AimWasGiven);
    }

    /// <summary>A bare scenario name is the flown aim point, which is what makes a run comparable.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoArgumentsIsTheDefaultShot(string? arguments)
    {
        Assert.True(ShotRequest.TryParse(arguments, out ShotRequest shot, out string trouble));
        Assert.Equal("", trouble);
        Assert.Equal(ShotRequest.Default, shot);
    }

    /// <summary>
    /// A hemisphere letter and a sign are the same coordinate. Both are written on maps, and a
    /// harness that only takes one of them is one nobody can type from what is in front of them.
    /// </summary>
    [Theory]
    [InlineData("26.485S,68.148W")]
    [InlineData("-26.485,-68.148")]
    [InlineData(" 26.485 s , 68.148 w ")]
    public void AHemisphereLetterMeansTheSign(string arguments)
    {
        Assert.True(ShotRequest.TryParse(arguments, out ShotRequest shot, out _));
        Assert.Equal(-26.485, shot.LatitudeDeg, 6);
        Assert.Equal(-68.148, shot.LongitudeDeg, 6);
        Assert.Equal(ShotRequest.DefaultBarMetres, shot.BarMetres);
    }

    /// <summary>
    /// The wrong hemisphere letter is refused rather than dropped. A longitude written with an S on
    /// it is somebody's mistake, and ignoring the letter aims a quarter of the way round the planet
    /// from where they meant.
    /// </summary>
    [Theory]
    [InlineData("26.485E,68.148W")]
    [InlineData("26.485N,68.148S")]
    [InlineData("26.485X,68.148W")]
    public void AHemisphereLetterFromTheOtherAxisIsRefused(string arguments)
    {
        Assert.False(ShotRequest.TryParse(arguments, out _, out string trouble));
        Assert.NotEqual("", trouble);
    }

    /// <summary>The bar is arguable, so it can be argued with from the request line.</summary>
    [Fact]
    public void AThirdFieldIsTheBarInKilometres()
    {
        Assert.True(ShotRequest.TryParse("10,20,1.5", out ShotRequest shot, out _));
        Assert.Equal(1500.0, shot.BarMetres, 6);
    }

    /// <summary>
    /// Everything a mistyped request can be, refused with something to act on. A scenario that
    /// silently falls back to the default aim point on a bad coordinate spends the whole flight
    /// proving something nobody asked about.
    /// </summary>
    [Theory]
    [InlineData("26.485S")]
    [InlineData("26.485S,68.148W,5,7")]
    [InlineData("north,68.148W")]
    [InlineData("91,0")]
    [InlineData("0,181")]
    [InlineData(",0")]
    [InlineData("0,0,0")]
    [InlineData("0,0,-5")]
    [InlineData("0,0,wide")]
    public void AnythingElseIsRefusedWithAReason(string arguments)
    {
        Assert.False(ShotRequest.TryParse(arguments, out _, out string trouble));
        Assert.NotEqual("", trouble);
    }

    /// <summary>
    /// The limits are inclusive. A pole and the date line are places, and a harness that will not
    /// aim at them is refusing the two coordinates most likely to be typed as a test.
    /// </summary>
    [Theory]
    [InlineData("90N,180E")]
    [InlineData("90S,180W")]
    public void TheEndsOfTheRangeAreStillPlaces(string arguments)
    {
        Assert.True(ShotRequest.TryParse(arguments, out _, out _));
    }

    /// <summary>
    /// What the verdict line quotes back. It has to carry the bar, or a pass says nothing about
    /// what it was a pass against.
    /// </summary>
    [Fact]
    public void TheDescriptionCarriesTheHemispheresAndTheBar()
    {
        ShotRequest.TryParse("26.485S,68.148W,3", out ShotRequest shot, out _);

        string said = shot.Describe();

        Assert.Contains("26.485S", said);
        Assert.Contains("68.148W", said);
        Assert.Contains("3.0 km", said);
    }
}
