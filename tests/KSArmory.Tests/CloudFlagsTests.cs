using System.Text.RegularExpressions;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The cloud's packed flag float, both ends: every combination read back exactly, the front to its
/// largest exact radius, and the shader decoding with the same divisors the packer multiplies by.
/// </summary>
public class CloudFlagsTests
{
    [Fact]
    public void EveryCombinationReadsBackExactly()
    {
        foreach (bool water in new[] { false, true })
        foreach (bool weather in new[] { false, true })
        foreach (bool first in new[] { false, true })
        foreach (int dry in new[] { 0, 3, 7 })
        foreach (double shock in new[] { 0.0, 1.0, 12_345.0, CloudFlags.MostShockMetres })
        {
            var back = CloudFlags.Unpack(CloudFlags.Pack(water, weather, first, shock, dry / 7.0));
            Assert.Equal((water, weather, first, dry, shock),
                         (back.Water, back.Weather, back.First, (int)Math.Round(back.Dryness * CloudFlags.DrySteps),
                          back.ShockMetres));
        }
    }

    [Fact]
    public void AFrontPastTheLargestExactRadiusIsHeldThere()
    {
        Assert.Equal(CloudFlags.MostShockMetres, CloudFlags.Unpack(CloudFlags.Pack(false, true, false, 1.0e6)).ShockMetres);
        Assert.True(CloudFlags.Pack(true, false, true, CloudFlags.MostShockMetres, 1.0) < 0.0f);
    }

    [Fact]
    public void TheShaderDecodesWhatIsPacked()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "KSArmory", "Shaders");
        string source = File.ReadAllText(Path.Combine(dir, "KSArmoryCloud.comp"));

        Assert.Matches(new Regex(@"mod\(cloudFlags, 64\.0\)"), source);
        Assert.Matches(new Regex(@"floor\(cloudFlags / 64\.0\)"), source);
        Assert.Matches(new Regex(@"floor\(flagBits / 8\.0\) / 7\.0"), source);
    }
}
