using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The shader reckons the cloud's age through its stand for itself, having no room left in its push
/// constants to be told it; these are the C# numbers it restates, compared. A stand the shader
/// thinks ends at another time shears the cloud on a clock the bound does not know, and the sheared
/// part is cut off at the bounding sphere.
/// </summary>
public class MushroomCloudShaderTests
{
    private static double Constant(string name)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "KSArmory", "Shaders");
        string source = File.ReadAllText(Path.Combine(dir, "KSArmoryCloud.comp"));
        Match m = Regex.Match(source, @"const float " + name + @" = ([0-9.]+);");

        Assert.True(m.Success, $"no 'const float {name}' in the shader");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheShaderKnowsTheStandAsTheLawDoes()
    {
        Assert.Equal(MushroomCloud.RiseSeconds, Constant("StandBeginsSeconds"), 9);
        Assert.Equal(MushroomCloud.StandSeconds, Constant("StandLastsSeconds"), 9);
        Assert.Equal(MushroomCloud.AgedShear, Constant("AgedShear"), 9);
        Assert.Equal(MushroomCloud.BoundMargin, Constant("BoundMargin"), 9);
    }

    /// <summary>The dust ring runs out at the blast front's speed of sound, not one of its own.</summary>
    [Fact]
    public void TheDustRingRunsAtTheFrontsSpeedOfSound()
    {
        Assert.Equal(BlastWave.SoundMetresPerSecond, Constant("DustFrontSpeed"), 9);
    }

    /// <summary>
    /// The shader is pushed the risen bound and grows its march's from it, so the risen one grown
    /// has to be the bound, at every age and at any yield merging bursts can reach.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(340.0)]
    [InlineData(10000.0)]
    public void TheRisenBoundGrownIsTheBound(double yieldKt)
    {
        double risen = MushroomCloud.RisenBound(yieldKt);

        for (double age = 1.0; age < MushroomCloud.LifeSeconds; age += 1.0)
        {
            double grown = MushroomCloud.GrownBound(risen, MushroomCloud.At(yieldKt * 1.0e6, age), age);
            Assert.Equal(MushroomCloud.DrawnBound(yieldKt, age), grown, 6);
        }
    }

    [Fact]
    public void TheBoundHoldsTheShearedCloud()
    {
        double age = MushroomCloud.LifeSeconds - MushroomCloud.FadeOutSeconds;
        MushroomCloud.Shape shape = MushroomCloud.At(0.3e6, age);
        double reach = Math.Sqrt((shape.CapCentre * shape.CapCentre)
                                 + Math.Pow(shape.CapRadius + shape.CapTube, 2.0));
        double sheared = reach * (1.0 + (MushroomCloud.AgedShear * MushroomCloud.Aged(age)));

        Assert.True(MushroomCloud.DrawnBound(0.3, age) > sheared);
    }
}
