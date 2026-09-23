using KSArmory;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The mark's drawn reach and the footprint its dispatch is bounded to have to agree.
///
/// <para>They live on opposite sides of the CPU/GPU seam — one is a GLSL constant in
/// <c>KSArmoryCloud.comp</c> and the other a C# one in <c>CloudPass</c> — so nothing but this
/// compares them. If the plume ever runs further than the footprint, the mark is silently cropped
/// at a straight edge partway along itself, which reads as terrain rather than as a fault.</para>
/// </summary>
public class ScorchFootprintTests
{
    private static string ShaderSource()
    {
        string here = System.AppContext.BaseDirectory;

        for (System.IO.DirectoryInfo? at = new(here); at is not null; at = at.Parent)
        {
            string path = System.IO.Path.Combine(at.FullName, "src", "KSArmory", "Shaders",
                                                 "KSArmoryCloud.comp");
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path);
        }

        return string.Empty;
    }

    private static double ConstantNamed(string source, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            source, @"const\s+float\s+" + name + @"\s*=\s*([0-9.]+)\s*;");

        Assert.True(match.Success, $"no 'const float {name}' in the shader");

        return double.Parse(match.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheFootprintCoversEverythingTheMarkDraws()
    {
        string source = ShaderSource();
        Assert.False(string.IsNullOrEmpty(source), "could not find the shader beside the tests");

        double reach = ConstantNamed(source, "PlumeReach");
        double mouth = ConstantNamed(source, "PlumeMouth");
        double width = ConstantNamed(source, "PlumeWidth");
        double tip = ConstantNamed(source, "PlumeTipWander");
        double edge = ConstantNamed(source, "PlumeEdgeWander");
        double cut = ConstantNamed(source, "LateralCut");

        // CloudPass.ScorchScreenReach, ScorchScreenWidth and ScorchScreenBehind, in patch radii.
        // The box is oriented along the wind, so they are separate: downwind it has to cover the
        // plume's run and its wandering tip, across it the widest the soft edge reaches when the
        // wander pushes it out, and upwind the burned patch's ragged rim.
        const double Reach = 3.3;
        const double Across = 1.8;
        const double Behind = 1.2;

        double runs = reach * (1.0 + tip);
        double spreads = (mouth + width) * (cut + edge);

        Assert.True(Reach >= runs, $"the plume runs {runs:F2} radii downwind and the box covers {Reach}");
        Assert.True(Across >= spreads, $"the plume reaches {spreads:F2} radii across and the box covers {Across}");

        // The patch's ragged rim wanders up to 0.15 of a radius past the radius itself.
        Assert.True(Behind >= 1.0 + 0.15, "the box must cover the patch's ragged rim upwind");
        Assert.True(Reach >= 1.0 && Across >= 1.0, "the box must cover the burned patch");
    }
}
