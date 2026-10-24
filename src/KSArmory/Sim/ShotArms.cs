using System.Globalization;
using System.Reflection;

namespace KSArmory;

/// <summary>
/// Which variant each rocket in a multi-rocket world flies, so two arms can be compared inside one
/// run rather than across a night of them.
///
/// <para>Two batches three hours apart on identical code put the same baseline at 14.49 km and
/// 5.43 km; a within-run split cancels frame pacing, warp history, solver load and weather.</para>
///
/// <para><b>Text in, no file access</b>, so every refusal is testable headlessly. Blind to anything
/// the rockets share: the terrain under the target, the system, the build.</para>
/// </summary>
internal sealed class ShotArms
{
    /// <summary>One craft's variant: what to call it, and what it does to that craft's settings.</summary>
    internal readonly record struct Arm(string Name, IReadOnlyList<Setting> Settings)
    {
        public string Describe()
            => Settings.Count == 0
                   ? Name
                   : $"{Name} ({string.Join(", ", Settings.Select(s => $"{s.Field}={s.Value}"))})";
    }

    /// <summary>One field assignment, unresolved — the field is looked up when it is applied.</summary>
    internal readonly record struct Setting(string Field, string Value);

    private readonly List<Arm> _arms;

    private ShotArms(List<Arm> arms) => _arms = arms;

    public int Count => _arms.Count;

    public IReadOnlyList<Arm> All => _arms;

    /// <summary>
    /// Which arm the rocket at <paramref name="rosterIndex"/> flies.
    ///
    /// <para><b>Alternating, never blocked</b>: a rocket's place in the roster was worth 175x in miss,
    /// the first landing at 0.09 km and the eighth at 15.81. <paramref name="phase"/> is the shot's
    /// sequence number, so over a batch each arm draws each position equally.</para>
    /// </summary>
    public Arm For(int rosterIndex, int phase = 0)
    {
        if (_arms.Count == 0) throw new InvalidOperationException("no arms");

        int i = rosterIndex + phase;

        // Wrapped rather than %: a negative index is a caller's bug, not a reason to throw in a frame hook.
        return _arms[(int)(((long)i % _arms.Count + _arms.Count) % _arms.Count)];
    }

    /// <summary>
    /// Reads a spec: arms separated by <c>|</c>, each a name and optionally
    /// <c>:field=value,field=value</c>.
    ///
    /// <para><c>base|sub:WarheadSubStepMs=2</c> is two arms, the first leaving the settings alone.</para>
    /// </summary>
    public static bool TryParse(string? spec, out ShotArms arms, out string fault)
    {
        arms = null!;
        fault = "";

        if (string.IsNullOrWhiteSpace(spec))
        {
            fault = "no arms given";
            return false;
        }

        List<Arm> parsed = [];

        foreach (string piece in spec.Split('|', StringSplitOptions.RemoveEmptyEntries
                                                | StringSplitOptions.TrimEntries))
        {
            int colon = piece.IndexOf(':');
            string name = (colon < 0 ? piece : piece[..colon]).Trim();

            if (name.Length == 0)
            {
                fault = $"an arm in '{spec}' has no name";
                return false;
            }

            if (parsed.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                fault = $"two arms are both called '{name}', so nothing could tell them apart";
                return false;
            }

            List<Setting> settings = [];

            if (colon >= 0)
            {
                foreach (string pair in piece[(colon + 1)..]
                             .Split(',', StringSplitOptions.RemoveEmptyEntries
                                         | StringSplitOptions.TrimEntries))
                {
                    int equals = pair.IndexOf('=');

                    if (equals <= 0 || equals == pair.Length - 1)
                    {
                        fault = $"'{pair}' in arm '{name}' is not field=value";
                        return false;
                    }

                    settings.Add(new Setting(pair[..equals].Trim(), pair[(equals + 1)..].Trim()));
                }
            }

            parsed.Add(new Arm(name, settings));
        }

        arms = new ShotArms(parsed);
        return true;
    }

    /// <summary>
    /// Writes one arm's settings onto a craft's own configuration, and says what it could not.
    ///
    /// <para>By reflection, so an arm can vary any field without the tool learning it. A field that will
    /// not resolve is a fault, not skipped: a shot flown on the other arm's settings is worse than none.</para>
    /// </summary>
    public static bool TryApply(in Arm arm, IcbmConfig config, out string fault)
    {
        fault = "";

        foreach (Setting setting in arm.Settings)
        {
            FieldInfo? field = typeof(IcbmConfig).GetField(
                setting.Field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (field is null)
            {
                fault = $"arm '{arm.Name}' sets '{setting.Field}', which is not a setting on IcbmConfig";
                return false;
            }

            if (!TryConvert(setting.Value, field.FieldType, out object? value))
            {
                fault = $"arm '{arm.Name}' sets {field.Name} to '{setting.Value}', "
                        + $"which is not a {field.FieldType.Name}";
                return false;
            }

            field.SetValue(config, value);
        }

        return true;
    }

    private static bool TryConvert(string text, Type type, out object? value)
    {
        value = null;

        // Invariant culture: the spec is written in a script and read on whatever machine flies it.
        if (type == typeof(bool) && bool.TryParse(text, out bool b)) value = b;
        else if (type == typeof(double)
                 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            value = d;
        }
        else if (type == typeof(float)
                 && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
        {
            value = f;
        }
        else if (type == typeof(int)
                 && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
        {
            value = i;
        }
        else if (type == typeof(string)) value = text;

        return value is not null;
    }
}
