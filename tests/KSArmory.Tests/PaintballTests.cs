using Xunit;

namespace KSArmory.Tests;

public class PaintballTests
{
    [Fact]
    public void ATeamPaintsInOneColourEverySession()
    {
        Assert.Equal(Paintball.HueFor("Blue"), Paintball.HueFor("Blue"));
        Assert.NotEqual(Paintball.HueFor("Blue"), Paintball.HueFor("Red"));
        Assert.InRange(Paintball.HueFor("Blue"), 0f, 1f);
    }

    [Fact]
    public void AWarheadsSplatIsBiggerThanAShells()
    {
        double shell = Paintball.SplatRadius(0.011, 20.0);
        double missile = Paintball.SplatRadius(8.6, 0.0);

        Assert.InRange(shell, 0.08, 0.2);
        Assert.True(missile > shell * 5.0);
        Assert.True(Paintball.SplatRadius(300_000.0, 0.0) <= 30.0);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.33f)]
    [InlineData(0.66f)]
    public void PaintIsBrightAndNotGrey(float hue)
    {
        (float r, float g, float b) = Paintball.Colour(hue);

        Assert.True(Math.Max(r, Math.Max(g, b)) > 0.95f);
        Assert.True(Math.Min(r, Math.Min(g, b)) < 0.2f);
    }
}
