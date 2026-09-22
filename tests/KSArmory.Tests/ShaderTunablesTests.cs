using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The tunables and the shaders are two lists of the same numbers, in C# and in GLSL; this is the
/// only thing that compares them. A default that disagrees is a value the bridge reports and the
/// picture does not use, and an id that disagrees tunes a different constant.
/// </summary>
public class ShaderTunablesTests
{
    private static readonly Regex Declared = new(
        @"layout \(constant_id = (\d+)\) const (float|int) (\w+) = ([-0-9.]+);", RegexOptions.Compiled);

    private static Dictionary<string, (int Id, double Value)> Shaders()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "KSArmory", "Shaders");
        Dictionary<string, (int, double)> found = [];

        foreach (string file in Directory.GetFiles(dir, "*.comp"))
        {
            foreach (Match m in Declared.Matches(File.ReadAllText(file)))
            {
                found[m.Groups[3].Value] = (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                                            double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
            }
        }

        return found;
    }

    [Fact]
    public void EveryTunableIsDeclaredInAShaderWithItsIdAndDefault()
    {
        Dictionary<string, (int Id, double Value)> shaders = Shaders();

        foreach (ShaderTunables.Tunable t in ShaderTunables.All)
        {
            Assert.True(shaders.TryGetValue(t.Name, out var declared), $"{t.Name} is in no shader");
            Assert.Equal(t.ConstantId, declared.Id);
            Assert.Equal(t.Default, declared.Value, 9);
        }
    }

    [Fact]
    public void EveryShaderConstantIsATunable()
    {
        foreach (string name in Shaders().Keys)
        {
            Assert.True(ShaderTunables.Find(name) is not null, $"{name} is declared but not listed");
        }
    }

    [Fact]
    public void NoTwoShareAnId()
    {
        Assert.Equal(ShaderTunables.All.Length, ShaderTunables.All.Select(t => t.ConstantId).Distinct().Count());
    }

    [Fact]
    public void ASetIsSeenAndResetPutsItBack()
    {
        int before = ShaderTunables.Generation;

        Assert.True(ShaderTunables.TrySet("halogain", 1.5, out _));
        Assert.Equal(1.5, ShaderTunables.Value(ShaderTunables.Find("HaloGain")!.Value));
        Assert.True(ShaderTunables.Generation > before);

        ShaderTunables.Reset();
        Assert.Equal(0.8, ShaderTunables.Value(ShaderTunables.Find("HaloGain")!.Value));
        Assert.False(ShaderTunables.TrySet("NoSuch", 1.0, out _));
    }
}
