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

    /// <summary>
    /// A bar with no aim, which is what a night flown at whatever the scene defends needs: the shot is judged
    /// and the site is not moved. <c>mirv::5</c> -- an empty aim and a bar -- was refused as one field.
    /// </summary>
    [Theory]
    [InlineData("5", 5_000.0)]
    [InlineData(" 2.5 ", 2_500.0)]
    public void ABarOnItsOwnJudgesTheShotWithoutNamingAnAim(string arguments, double metres)
    {
        Assert.True(ShotRequest.TryParse(arguments, out ShotRequest shot, out string trouble), trouble);
        Assert.Equal(metres, shot.BarMetres, 6);
        Assert.False(shot.AimWasGiven);
        Assert.Equal(ShotRequest.Default.LatitudeDeg, shot.LatitudeDeg, 6);
        Assert.Equal(ShotRequest.Default.LongitudeDeg, shot.LongitudeDeg, 6);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("0")]
    [InlineData("-3")]
    public void ASingleFieldThatIsNotAPositiveBarIsRefused(string arguments)
    {
        Assert.False(ShotRequest.TryParse(arguments, out _, out string trouble));
        Assert.Contains("km", trouble);
    }

    /// <summary>
    /// Several places, separated by ';'. One segment must still take the old path exactly, which is
    /// what lets every scenario line ever written keep working.
    /// </summary>
    [Fact]
    public void SeveralPlacesAreSeparatedBySemicolons()
    {
        Assert.True(ShotRequest.TryParse("24.0S,62.0W;25.0S,63.0W;26.0S,64.0W", out ShotRequest shot, out string why));
        Assert.Equal("", why);

        Assert.Equal(-24.0, shot.LatitudeDeg, 6);
        Assert.Equal(-62.0, shot.LongitudeDeg, 6);
        Assert.Equal(3, shot.Targets.Count);
        Assert.Equal(-26.0, shot.Targets[2].LatitudeDeg, 6);
        Assert.Equal(-64.0, shot.Targets[2].LongitudeDeg, 6);
    }

    [Fact]
    public void OneTargetIsOneTargetAndNothingElseChanges()
    {
        Assert.True(ShotRequest.TryParse("24.0S,62.0W", out ShotRequest shot, out _));

        Assert.Single(shot.Targets);
        Assert.Equal(-24.0, shot.Targets[0].LatitudeDeg, 6);
        Assert.True(shot.AimWasGiven);
    }

    /// <summary>A shot at whatever the scene defends names no place, so it has none to list.</summary>
    [Fact]
    public void ARequestWithNoAimListsNoTargets()
    {
        Assert.True(ShotRequest.TryParse("", out ShotRequest bare, out _));
        Assert.Empty(bare.Targets);

        Assert.True(ShotRequest.TryParse("0.5", out ShotRequest barOnly, out _));
        Assert.Empty(barOnly.Targets);
        Assert.False(barOnly.AimWasGiven);
    }

    /// <summary>The bar belongs to the shot, not to a target, wherever it is written.</summary>
    [Fact]
    public void TheBarIsTheShotsWhicheverTargetCarriesIt()
    {
        Assert.True(ShotRequest.TryParse("24.0S,62.0W;25.0S,63.0W,0.25", out ShotRequest shot, out _));

        Assert.Equal(2, shot.Targets.Count);
        Assert.Equal(250.0, shot.BarMetres, 6);
    }

    [Fact]
    public void ABusCannotBeSentToMorePlacesThanItHasWarheads()
    {
        string many = string.Join(";", Enumerable.Range(0, TargetSet.MaxTargets + 1)
                                                 .Select(i => $"{20 + i}.0S,62.0W"));

        Assert.False(ShotRequest.TryParse(many, out _, out string why));
        Assert.Contains("at most", why);
    }

    /// <summary>A refusal names which target was wrong, or the next attempt is a guess.</summary>
    [Fact]
    public void ARefusalNamesTheTargetThatWasWrong()
    {
        Assert.False(ShotRequest.TryParse("24.0S,62.0W;95.0S,63.0W", out _, out string why));
        Assert.Contains("target 2", why);
    }
}
