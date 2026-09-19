using System.Globalization;
using System.Reflection;

namespace KSArmory;

/// <summary>
/// Which variant each rocket in a multi-rocket world flies, so two arms can be compared inside one
/// run rather than across a night of them.
///
/// <para><b>This exists because the between-run comparison stopped working.</b> Two batches flown
/// three hours apart on identical code put the same baseline at 14.49 km and 5.43 km — a 2.7x swing
/// — while the arm being tested moved by less. Nothing under about 3x is readable that way, and
/// everything worth flying is under 3x. A within-run split cancels the frame pacing, the warp
/// history, the solver load and the weather by construction, which is why 8z's terminator table
/// reproduced identically on both arms while the arm medians swung by a factor of three.</para>
///
/// <para><b>Text in, no file access</b>, the same rule <see cref="ShotRequest"/> and
/// <see cref="PackReader"/> follow: the harness's whole spec is one line, so every refusal is
/// testable without spending a seven-minute flight discovering it.</para>
///
/// <para>What this cannot split is anything the rockets share. <c>tools/ab-shot.py</c> splits tubes
/// within one bus and is blind to everything upstream of the release; this splits <em>craft</em>,
/// each with its own computer, trim and correction loop, and is blind to anything shared by the
/// world — the terrain under the target, the system, the build.</para>
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
    /// <para><b>Alternating, never blocked.</b> A rocket's place in the roster is worth 175x in
    /// miss — the first lands at 0.09 km and the eighth at 15.81, monotone across every arm ever
    /// flown — so giving one arm the first four rockets and the other the last four measures the
    /// gradient and calls it the change.</para>
    ///
    /// <para><paramref name="phase"/> rotates the assignment between shots, so over a batch each
    /// arm draws each position equally rather than one of them owning the odd places. It is the
    /// shot's own sequence number; a batch that always passed zero would leave the residual bias
    /// alternation cannot remove.</para>
    /// </summary>
    public Arm For(int rosterIndex, int phase = 0)
    {
        if (_arms.Count == 0) throw new InvalidOperationException("no arms");

        int i = rosterIndex + phase;

        // Not the % operator: a negative index is a caller's bug rather than a reason to throw
        // inside a frame hook, and the far side of the roster is a defensible answer to it.
        return _arms[(int)(((long)i % _arms.Count + _arms.Count) % _arms.Count)];
    }

    /// <summary>
    /// Reads a spec: arms separated by <c>|</c>, each a name and optionally
    /// <c>:field=value,field=value</c>.
    ///
    /// <para><c>base|trim:TrimCeilingFromBudget=true</c> is two arms, the first leaving the
    /// settings exactly as the scenario left them.</para>
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
    /// <para>By reflection, so an arm is flyable the moment the setting it varies exists — the
    /// alternative is a tool that has to be taught every experiment, and the experiments are the
    /// part that changes. A field that will not resolve is reported rather than skipped: a shot
    /// flown on the settings of the arm it was meant to differ from is worse than no shot.</para>
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

        // Invariant throughout, because the spec is written down in a script and read back on
        // whatever machine flies it. A decimal comma would silently retarget the shot.
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
