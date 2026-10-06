using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The sky dispatches' kinds as the shader tests them, against the names CloudPass sends. A threshold
/// out of step sends one kind down another's branch, and a mark offset that runs on a sky dispatch
/// draws it off the screen -- which is what every northern aurora was.
/// </summary>
public class SkyDispatchShaderTests
{
    private static string Shader()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "KSArmory", "Shaders");
        return File.ReadAllText(Path.Combine(dir, "KSArmoryCloud.comp"));
    }

    private static readonly Dictionary<string, float> Kinds = new()
    {
        ["DrawXRayGlow"] = SkyDispatch.Glow,
        ["DrawAurora"] = SkyDispatch.Aurora,
        ["DrawDebris"] = SkyDispatch.Debris,
        ["DrawRedWave"] = SkyDispatch.RedWave,
    };

    [Fact]
    public void EachKindsBranchCatchesItAndNoOther()
    {
        string source = Shader();
        MatchCollection tests = Regex.Matches(source, @"if \(pc\.FireSun\.z > ([0-9.]+)\) (Draw\w+)\(at, size\);");
        Assert.NotEmpty(tests);

        double above = double.PositiveInfinity;
        foreach (Match m in tests)
        {
            double threshold = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            float kind = Kinds[m.Groups[2].Value];

            // Tested from the highest down: this one's threshold is under its kind and over the next.
            Assert.True(threshold < kind && threshold > kind - 1.0, $"{m.Groups[2].Value} caught at > {threshold}");
            Assert.True(threshold < above, "the kinds are tested from the highest down");
            above = threshold;
        }

        // And what none of the thresholds catch falls through to the lowest kind.
        Assert.Matches(@"else DrawXRayGlow\(at, size\);", source);
    }

    [Fact]
    public void TheMarkOffsetNeverMovesASkyDispatch()
    {
        Match offset = Regex.Match(Shader(), @"if \((pc\.FireSun\.w > 0\.0[^{]*)\)\s*\{\s*at \+= ivec2\(pc\.FireSun\.xy\);");

        Assert.True(offset.Success, "no mark offset found");
        Assert.Contains("pc.CentreRadius.w >= 0.0", offset.Groups[1].Value);
    }
}
