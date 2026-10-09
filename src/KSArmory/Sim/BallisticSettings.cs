using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KSArmory;

/// <summary>
/// One ballistic computer's setup, flattened so it can be written into a save and read back: every
/// setting moved off its default, the places it is aimed at and which one leads.
///
/// <para><b>Never the flight.</b> <see cref="IcbmConfig.Armed"/> is not written and always reads back
/// off, because a load is not a launch; a computer saved mid-burn or mid-coast comes back disarmed
/// with its targets, and flies nothing until armed again.</para>
///
/// <para>The settings are written by field name, so a field added to <see cref="IcbmConfig"/> is saved
/// with nothing to add here. One still at its default is not written, so a default changed in a later
/// release reaches every save that never moved it; one the file names that no longer exists is reported
/// and skipped.</para>
/// </summary>
internal sealed class BallisticSettings
{
    private const string ConfigKey = "Config";
    private const string TargetsKey = "Targets";
    private const string LeadKey = "Lead";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly JsonObject _config;
    private readonly List<TargetSet.Entry> _targets;
    private readonly int _lead;

    private BallisticSettings(JsonObject config, List<TargetSet.Entry> targets, int lead)
    {
        _config = config;
        _targets = targets;
        _lead = lead;
    }

    /// <summary>Every <see cref="IcbmConfig"/> field the file may carry — all of them but the arm switch.</summary>
    public static IEnumerable<FieldInfo> SavedFields
        => typeof(IcbmConfig).GetFields(BindingFlags.Public | BindingFlags.Instance)
                             .Where(f => f.Name != nameof(IcbmConfig.Armed));

    public IReadOnlyList<TargetSet.Entry> Targets => _targets;

    public int Lead => _lead;

    public static BallisticSettings From(IcbmConfig config, IReadOnlyList<TargetSet.Entry> targets, int lead)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(targets);

        IcbmConfig defaults = new();
        JsonObject saved = [];
        foreach (FieldInfo f in SavedFields)
        {
            object? value = f.GetValue(config);
            if (Equals(value, f.GetValue(defaults))) continue;

            saved[f.Name] = value switch
            {
                bool b => JsonValue.Create(b),
                float x => JsonValue.Create((double)x),
                double x => JsonValue.Create(x),
                _ => throw new NotSupportedException(
                         $"IcbmConfig.{f.Name} is a {f.FieldType.Name}, which BallisticSettings cannot write"),
            };
        }

        List<TargetSet.Entry> kept = [.. targets.Where(e => e.Site.IsSet).Take(TargetSet.MaxTargets)];
        return new BallisticSettings(saved, kept, lead >= 0 && lead < kept.Count ? lead : 0);
    }

    /// <summary>
    /// Puts this setup on a computer: every setting the file names, the rest at their defaults, the
    /// arm switch off, and the target set replaced.
    /// </summary>
    public void ApplyTo(IcbmConfig config, TargetSet targets)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(targets);

        IcbmConfig defaults = new();
        foreach (FieldInfo f in SavedFields)
        {
            f.SetValue(config, _config.TryGetPropertyValue(f.Name, out JsonNode? node) && node is not null
                                   ? Value(node, f.FieldType)
                                   : f.GetValue(defaults));
        }

        config.Armed = false;
        targets.Restore(_targets, _lead);
    }

    public bool Differs(BallisticSettings other)
        => other is null || ToNode().ToJsonString() != other.ToNode().ToJsonString();

    /// <summary>A file's worth of computers, keyed on whatever the caller keys craft by.</summary>
    public static string Write(IReadOnlyDictionary<string, BallisticSettings> crafts)
    {
        ArgumentNullException.ThrowIfNull(crafts);

        JsonObject root = [];
        foreach (KeyValuePair<string, BallisticSettings> kv in crafts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            root[kv.Key] = kv.Value.ToNode();
        }

        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// Reads a file written by <see cref="Write"/>. False only when nothing in it can be trusted; a
    /// craft, a setting or a target that cannot be read is left out and named in <paramref name="faults"/>.
    /// </summary>
    public static bool TryRead(string? json, out Dictionary<string, BallisticSettings> crafts,
                               out string why, List<string>? faults = null)
    {
        crafts = [];
        why = "";
        faults ??= [];

        if (string.IsNullOrWhiteSpace(json)) return true;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            why = $"not JSON ({e.Message})";
            return false;
        }

        if (root is not JsonObject byCraft)
        {
            why = "not an object of craft names";
            return false;
        }

        foreach (KeyValuePair<string, JsonNode?> kv in byCraft)
        {
            if (kv.Value is not JsonObject entry)
            {
                faults.Add($"{kv.Key}: not an object, left out");
                continue;
            }

            crafts[kv.Key] = ReadOne(kv.Key, entry, faults);
        }

        return true;
    }

    private static BallisticSettings ReadOne(string craft, JsonObject entry, List<string> faults)
    {
        Dictionary<string, FieldInfo> fields = SavedFields.ToDictionary(f => f.Name);
        JsonObject config = [];

        if (entry[ConfigKey] is JsonObject saved)
        {
            foreach (KeyValuePair<string, JsonNode?> kv in saved)
            {
                if (!fields.TryGetValue(kv.Key, out FieldInfo? f))
                {
                    faults.Add(kv.Key == nameof(IcbmConfig.Armed)
                                   ? $"{craft}: Armed is never restored"
                                   : $"{craft}: no setting called {kv.Key}, skipped");
                    continue;
                }

                if (kv.Value is null || !Readable(kv.Value, f.FieldType))
                {
                    faults.Add($"{craft}: {kv.Key} is not a {Kind(f.FieldType)}, left at its default");
                    continue;
                }

                config[kv.Key] = kv.Value.DeepClone();
            }
        }
        else if (entry[ConfigKey] is not null)
        {
            faults.Add($"{craft}: {ConfigKey} is not an object, every setting left at its default");
        }

        List<TargetSet.Entry> targets = [];
        if (entry[TargetsKey] is JsonArray list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (targets.Count == TargetSet.MaxTargets)
                {
                    faults.Add($"{craft}: more than {TargetSet.MaxTargets} targets, the rest left out");
                    break;
                }

                if (TryTarget(list[i], out TargetSet.Entry target, out string bad)) targets.Add(target);
                else faults.Add($"{craft}: target {i + 1} {bad}, left out");
            }
        }
        else if (entry[TargetsKey] is not null)
        {
            faults.Add($"{craft}: {TargetsKey} is not a list, no targets restored");
        }

        int lead = 0;
        if (entry[LeadKey] is JsonValue v && v.TryGetValue(out int saidLead))
        {
            if (saidLead >= 0 && saidLead < targets.Count) lead = saidLead;
            else if (targets.Count > 0) faults.Add($"{craft}: lead {saidLead} names no target, the first leads");
        }

        return new BallisticSettings(config, targets, lead);
    }

    private static bool TryTarget(JsonNode? node, out TargetSet.Entry target, out string bad)
    {
        target = default;

        if (node is not JsonObject o)
        {
            bad = "is not an object";
            return false;
        }

        string body = o["Body"] is JsonValue b && b.TryGetValue(out string? s) ? s ?? "" : "";
        double lat = Number(o["LatitudeDeg"]);
        double lon = Number(o["LongitudeDeg"]);
        string label = o["Label"] is JsonValue l && l.TryGetValue(out string? t) ? t ?? "" : "";
        int warheads = o["Warheads"] is JsonValue w && w.TryGetValue(out int n) ? n : 0;

        if (string.IsNullOrWhiteSpace(body)) bad = "names no body";
        else if (!double.IsFinite(lat) || Math.Abs(lat) > 90.0) bad = "has no latitude";
        else if (!double.IsFinite(lon)) bad = "has no longitude";
        else
        {
            bad = "";
            target = new TargetSet.Entry(new AimSite(body, lat, lon, label), Math.Max(0, warheads));
            return true;
        }

        return false;
    }

    private JsonObject ToNode()
    {
        JsonArray targets = [];
        foreach (TargetSet.Entry e in _targets)
        {
            targets.Add(new JsonObject
            {
                ["Body"] = e.Site.BodyName,
                ["LatitudeDeg"] = e.Site.LatitudeDeg,
                ["LongitudeDeg"] = e.Site.LongitudeDeg,
                ["Label"] = e.Site.Label ?? "",
                ["Warheads"] = e.Warheads,
            });
        }

        return new JsonObject
        {
            [ConfigKey] = _config.DeepClone(),
            [TargetsKey] = targets,
            [LeadKey] = _lead,
        };
    }

    private static double Number(JsonNode? node)
        => node is JsonValue v && v.TryGetValue(out double d) ? d : double.NaN;

    private static bool Readable(JsonNode node, Type type)
    {
        if (node is not JsonValue v) return false;
        if (type == typeof(bool)) return v.GetValueKind() is JsonValueKind.True or JsonValueKind.False;
        return v.GetValueKind() == JsonValueKind.Number && double.IsFinite(v.GetValue<double>());
    }

    private static object Value(JsonNode node, Type type)
        => type == typeof(bool) ? node.GetValue<bool>()
         : type == typeof(float) ? (object)(float)node.GetValue<double>()
         : node.GetValue<double>();

    private static string Kind(Type type) => type == typeof(bool) ? "true or false" : "finite number";
}
