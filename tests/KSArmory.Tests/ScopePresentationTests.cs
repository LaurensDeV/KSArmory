using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which sets put a picture in front of the operator, and which do not.
///
/// <para>The panel shows the tab only where a set presents a picture. A seeker head rides the
/// round and a designation set has no array, so neither has a scope of tracks to offer — showing
/// one paints a search that never happened.</para>
/// </summary>
public class ScopePresentationTests
{
    /// <summary>
    /// The default, and the reason it is the default: a profile that says nothing about this shows
    /// nothing. Most sensors in an arsenal are seeker heads and sights, and none of them is read
    /// out as a scope.
    /// </summary>
    [Fact]
    public void ASensorThatSaysNothingPresentsNothing()
    {
        SensorProfile quiet = new() { Name = "TEST", DisplayName = "test set" };

        Assert.Equal(ScopePresentation.None, quiet.Scope);
    }

    /// <summary>
    /// A seeker rides the round and cues the shooter with a growl and a reticle. Nobody reads a
    /// Sidewinder's seeker out as tracks, and the panel should not either.
    /// </summary>
    [Theory]
    [InlineData("AIM9SEEK")]
    [InlineData("AIM120SEEK")]
    public void AHomingSeekerIsNotReadOutAsAScope(string sensor)
    {
        Assert.Equal(ScopePresentation.None, Catalogue.SensorNamed(sensor).Scope);
    }

    /// <summary>
    /// The exception, and the reason this is an enum rather than a flag. An anti-radiation seeker
    /// is passive and its whole output is a list of who is radiating — which a HARM genuinely does
    /// display to the crew, and which is not a search picture.
    /// </summary>
    [Fact]
    public void AnAntiRadiationSeekerShowsItsEmittersAndSaysSo()
    {
        Assert.Equal(ScopePresentation.Emitters, Catalogue.SensorNamed("AGM88SEEK").Scope);
    }

    /// <summary>A gun with its own search-and-track set is the case the tab was built for.</summary>
    [Fact]
    public void ASetThatActuallySearchesShowsASearchPicture()
    {
        Assert.Equal(ScopePresentation.Search, Catalogue.SensorNamed("VPS2").Scope);
    }

    /// <summary>
    /// A post-boost vehicle designates for its warheads and has no array to sweep. This is the one
    /// that prompted the change: a rocket with nothing resembling a radar was given a radar tab.
    /// </summary>
    [Fact]
    public void ADesignationSetHasNoArrayAndSoNoPicture()
    {
        Assert.Equal(ScopePresentation.None, Catalogue.SensorNamed("MIRVBUS").Scope);
    }

    /// <summary>
    /// A seeker head never presents a search picture, whatever else it does. It rides the round and
    /// looks where the rail points; it is not searching on the operator's behalf.
    ///
    /// <para>Deliberately not asserted against <see cref="LauncherProfile.RadarMarker"/>, which
    /// says a launcher has a <em>separately animated</em> array rather than that it has a radar:
    /// the Phalanx's set lives in the radome that elevates with its barrels, so it searches and
    /// declares no marker.</para>
    /// </summary>
    [Fact]
    public void ASeekerHeadNeverPresentsASearchPicture()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            SensorProfile sensor = Catalogue.SensorNamed(launcher.Sensor);
            if (sensor.BoresightSource != BoresightMode.PartForward) continue;

            Assert.True(sensor.Scope != ScopePresentation.Search,
                        $"{launcher.PartId} carries a seeker that claims to search");
        }
    }
}
