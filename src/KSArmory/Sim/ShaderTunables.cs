namespace KSArmory;

/// <summary>
/// The look of the nuclear cloud, as numbers that can be changed while the game runs.
///
/// <para>Each is a GLSL specialization constant: declared with a <c>constant_id</c> and a default in
/// the shader, and overridden when the pipeline is built. So a change is a pipeline rebuild — a few
/// milliseconds, no recompile — which is what lets <c>Ksa/Bridge.cs</c>'s <c>tune</c> try a value,
/// photograph it and try another, and the chosen one is then written back into the source.
/// <c>ShaderTunablesTests</c> holds this list and the shader's declarations to one another.</para>
///
/// <para>A session's overrides only; nothing here is saved, because a tuned value that is worth
/// keeping belongs in the shader where everybody gets it.</para>
/// </summary>
public static class ShaderTunables
{
    /// <summary>One constant: its name in the shader, its id, and the shader's own default.</summary>
    public readonly record struct Tunable(string Name, int ConstantId, double Default, bool Integer = false);

    /// <summary>
    /// What the pass writes instead of the picture, for looking at one of its terms on its own:
    /// 0 the picture, 1 how much cloud covers each pixel, 2 how deep in it the cloud is, 3 the
    /// weather mask, 4 how much sunlight reaches the cloud, 5 the fireball's share of its light.
    /// </summary>
    public const string DebugView = "DebugView";

    /// <summary>Every tunable, in constant-id order.</summary>
    public static readonly Tunable[] All =
    [
        new(DebugView, 0, 0.0, Integer: true),
        new("FootFlare", 1, 2.5),
        new("FootFlareReach", 2, 0.07),
        new("FalloutReach", 3, 2.3),
        new("FalloutDensity", 4, 0.30),
        new("FireGain", 5, 0.110),
        new("FireCeiling", 6, 6.0),
        new("GlareFloorNits", 7, 1.1),
        new("GlareCoreNits", 8, 3.0),
        new("GlareOverexposure", 9, 1.6),
        new("GlareCoreDeg", 10, 6.0),
        new("GlareWhiteDeg", 11, 20.0),
        new("HaloGain", 12, 0.8),
        new("HaloMost", 13, 0.85),
        new("WeatherDepthSpread", 14, 0.15),
        new("ScorchDepth", 15, 0.85),
        new("Extinction", 16, 0.022),
        new("NewShare", 17, 0.125),
        new("ChurnBoost", 18, 5.0),
        new("FireGlowNits", 19, 3.0),
        new("SootDepth", 20, 0.95),
        new("SootSeconds", 21, 10.0),
        new("NoxStrength", 22, 0.70),
        new("NoxSeconds", 23, 22.0),
        new("DustDensity", 24, 2.0),
        new("DustSeconds", 25, 14.0),
        new("CondensationDensity", 26, 0.35),
        new("CapWhiteness", 27, 0.55),
        new("SmokeDensity", 28, 1.5),
        new("CapShadowDensity", 29, 0.5),
        new("VioletNits", 30, 4.0),
        new("DeckGlow", 31, 0.35),
        new("InflowDensity", 32, 0.45),
    ];

    private static readonly Dictionary<string, double> Overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bumped on every change, so the pass knows to rebuild.</summary>
    public static int Generation { get; private set; }

    /// <summary>Sets one, or says why not.</summary>
    public static bool TrySet(string name, double value, out string trouble)
    {
        trouble = string.Empty;

        if (Find(name) is not { } tunable)
        {
            trouble = $"no tunable '{name}' -- one of {string.Join(", ", All.Select(t => t.Name))}";
            return false;
        }

        if (!double.IsFinite(value))
        {
            trouble = $"{tunable.Name} must be a finite number";
            return false;
        }

        Overrides[tunable.Name] = tunable.Integer ? Math.Round(value) : value;
        Generation++;
        return true;
    }

    /// <summary>Forgets every override, back to the shader's own numbers.</summary>
    public static void Reset()
    {
        if (Overrides.Count == 0) return;

        Overrides.Clear();
        Generation++;
    }

    /// <summary>What a tunable is now: the override if there is one, else the shader's default.</summary>
    public static double Value(Tunable tunable)
        => Overrides.TryGetValue(tunable.Name, out double v) ? v : tunable.Default;

    /// <summary>Only the overridden ones, which are all a pipeline has to be told.</summary>
    public static IEnumerable<(Tunable Tunable, double Value)> Overridden()
        => All.Where(t => Overrides.ContainsKey(t.Name)).Select(t => (t, Overrides[t.Name]));

    /// <summary>A tunable by name, ignoring case, or null.</summary>
    public static Tunable? Find(string name)
        => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } t
               ? t
               : null;
}
