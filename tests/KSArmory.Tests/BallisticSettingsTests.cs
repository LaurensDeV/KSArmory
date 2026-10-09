using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

namespace KSArmory.Tests;

public class BallisticSettingsTests
{
    private static readonly AimSite Chaco = new("Earth", -24.0, -62.0, "Chaco");
    private static readonly AimSite East = new("Earth", -24.1, -61.9, "");
    private static readonly AimSite Moon = new("Luna", 3.5, 170.25, "crater");

    private static object NonDefault(FieldInfo f, object current) => current switch
    {
        bool b => !b,
        float x => x + 1.5f,
        double x => x + 1.25,
        _ => throw new NotSupportedException(
                 $"IcbmConfig.{f.Name} is a {f.FieldType.Name}; teach BallisticSettings to save it"),
    };

    private static Dictionary<string, BallisticSettings> RoundTrip(BallisticSettings setup)
    {
        string text = BallisticSettings.Write(new Dictionary<string, BallisticSettings> { ["Rocket"] = setup });
        Assert.True(BallisticSettings.TryRead(text, out Dictionary<string, BallisticSettings> read, out string why), why);
        return read;
    }

    private static (IcbmConfig Config, TargetSet Targets) Applied(BallisticSettings setup)
    {
        IcbmConfig config = new();
        TargetSet targets = new();
        setup.ApplyTo(config, targets);
        return (config, targets);
    }

    [Fact]
    public void EveryIcbmConfigFieldButArmedSurvivesARoundTripWithANonDefaultValue()
    {
        IcbmConfig chosen = new();
        FieldInfo[] all = typeof(IcbmConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (FieldInfo f in all) f.SetValue(chosen, NonDefault(f, f.GetValue(chosen)!));

        (IcbmConfig back, _) = Applied(RoundTrip(BallisticSettings.From(chosen, [], 0))["Rocket"]);

        IcbmConfig defaults = new();
        Assert.Equal(all.Length - 1, BallisticSettings.SavedFields.Count());
        foreach (FieldInfo f in BallisticSettings.SavedFields)
        {
            Assert.NotEqual(f.GetValue(defaults), f.GetValue(back));
            Assert.Equal(f.GetValue(chosen), f.GetValue(back));
        }
    }

    [Fact]
    public void ArmedIsNeitherWrittenNorRestored()
    {
        IcbmConfig armed = new() { Armed = true };
        string text = BallisticSettings.Write(new Dictionary<string, BallisticSettings>
        {
            ["Rocket"] = BallisticSettings.From(armed, [new TargetSet.Entry(Chaco, 6)], 0),
        });

        Assert.DoesNotContain("\"Armed\"", text);

        JsonObject edited = JsonNode.Parse(text)!.AsObject();
        edited["Rocket"]!["Config"]!["Armed"] = true;

        List<string> faults = [];
        Assert.True(BallisticSettings.TryRead(edited.ToJsonString(), out Dictionary<string, BallisticSettings> read,
                                              out _, faults));

        IcbmConfig onto = new() { Armed = true };
        read["Rocket"].ApplyTo(onto, new TargetSet());

        Assert.False(onto.Armed);
        Assert.Contains(faults, f => f.Contains("Armed"));
    }

    [Fact]
    public void TargetsTheirWarheadsAndTheLeadComeBack()
    {
        TargetSet set = new();
        set.SetOnly(Chaco, 2);
        set.TryAdd(East);
        set.TryAdd(Moon);
        set.SetWarheads(1, 3, 6);
        set.SetWarheads(2, 1, 6);
        set.SetLead(2);

        (_, TargetSet back) = Applied(RoundTrip(BallisticSettings.From(new IcbmConfig(), set.Entries, set.LeadIndex))["Rocket"]);

        Assert.Equal(set.Entries, back.Entries);
        Assert.Equal(2, back.LeadIndex);
        Assert.Equal(Moon, back.Primary);
        Assert.Equal(6, back.Assigned);
    }

    [Fact]
    public void AnOlderFileWithoutASettingLeavesItAtItsDefault()
    {
        IcbmConfig chosen = new() { Loft = 1.4, AutoStage = false };
        string text = BallisticSettings.Write(new Dictionary<string, BallisticSettings>
        {
            ["Rocket"] = BallisticSettings.From(chosen, [], 0),
        });

        JsonObject older = JsonNode.Parse(text)!.AsObject();
        older["Rocket"]!["Config"]!.AsObject().Remove(nameof(IcbmConfig.Loft));
        older["Rocket"]!.AsObject().Remove("Targets");
        older["Rocket"]!.AsObject().Remove("Lead");

        List<string> faults = [];
        Assert.True(BallisticSettings.TryRead(older.ToJsonString(), out Dictionary<string, BallisticSettings> read,
                                              out _, faults));

        // Applied over a config already holding other values: a missing field goes back to its
        // default, never keeps whatever was there.
        IcbmConfig onto = new() { Loft = 1.9 };
        TargetSet targets = new();
        targets.SetOnly(East, 6);
        read["Rocket"].ApplyTo(onto, targets);

        Assert.Equal(new IcbmConfig().Loft, onto.Loft);
        Assert.False(onto.AutoStage);
        Assert.Equal(0, targets.Count);
        Assert.Empty(faults);
    }

    [Fact]
    public void OnlyASettingMovedOffItsDefaultIsWritten()
    {
        string text = BallisticSettings.Write(new Dictionary<string, BallisticSettings>
        {
            ["Rocket"] = BallisticSettings.From(new IcbmConfig { Loft = 1.3 }, [], 0),
        });

        Assert.Contains($"\"{nameof(IcbmConfig.Loft)}\"", text);
        Assert.DoesNotContain(nameof(IcbmConfig.ShortShotPushesThroughAStall), text);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"Rocket\"")]
    public void AFileThatCannotBeTrustedIsRefusedWithAReason(string text)
    {
        Assert.False(BallisticSettings.TryRead(text, out Dictionary<string, BallisticSettings> read, out string why));
        Assert.Empty(read);
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void AnEmptyFileHoldsNoComputers(string? text)
    {
        Assert.True(BallisticSettings.TryRead(text, out Dictionary<string, BallisticSettings> read, out _));
        Assert.Empty(read);
    }

    [Fact]
    public void WhatCannotBeReadIsLeftOutAndNamedWhileTheRestIsKept()
    {
        const string text = """
        {
          "Broken": 7,
          "Rocket": {
            "Config": { "Loft": "high", "AutoStage": false, "NoSuchSetting": true, "MinArrivalAngleDeg": 12 },
            "Targets": [
              { "Body": "Earth", "LatitudeDeg": -24, "LongitudeDeg": -62, "Label": "Chaco", "Warheads": 4 },
              { "Body": "", "LatitudeDeg": 1, "LongitudeDeg": 2, "Warheads": 1 },
              { "Body": "Earth", "LatitudeDeg": 120, "LongitudeDeg": 2, "Warheads": 1 },
              { "Body": "Earth", "LatitudeDeg": 10, "LongitudeDeg": 20, "Warheads": -3 },
              "not a target"
            ],
            "Lead": 9
          }
        }
        """;

        List<string> faults = [];
        Assert.True(BallisticSettings.TryRead(text, out Dictionary<string, BallisticSettings> read, out _, faults));

        Assert.False(read.ContainsKey("Broken"));
        (IcbmConfig config, TargetSet targets) = Applied(read["Rocket"]);

        Assert.Equal(new IcbmConfig().Loft, config.Loft);
        Assert.False(config.AutoStage);
        Assert.Equal(12.0, config.MinArrivalAngleDeg);

        Assert.Equal(2, targets.Count);
        Assert.Equal(4, targets.Entries[0].Warheads);
        Assert.Equal(0, targets.Entries[1].Warheads);
        Assert.Equal(0, targets.LeadIndex);

        Assert.Contains(faults, f => f.Contains("Broken"));
        Assert.Contains(faults, f => f.Contains("Loft"));
        Assert.Contains(faults, f => f.Contains("NoSuchSetting"));
        Assert.Contains(faults, f => f.Contains("target 2"));
        Assert.Contains(faults, f => f.Contains("target 3"));
        Assert.Contains(faults, f => f.Contains("target 5"));
        Assert.Contains(faults, f => f.Contains("lead 9"));
    }

    [Fact]
    public void NoMoreThanABusesTargetsAreRestored()
    {
        JsonArray seven = [];
        for (int i = 0; i < TargetSet.MaxTargets + 1; i++)
        {
            seven.Add(new JsonObject { ["Body"] = "Earth", ["LatitudeDeg"] = i, ["LongitudeDeg"] = 0.0, ["Warheads"] = 1 });
        }

        JsonObject file = new() { ["Rocket"] = new JsonObject { ["Targets"] = seven } };

        List<string> faults = [];
        Assert.True(BallisticSettings.TryRead(file.ToJsonString(), out Dictionary<string, BallisticSettings> read,
                                              out _, faults));

        Assert.Equal(TargetSet.MaxTargets, Applied(read["Rocket"]).Targets.Count);
        Assert.Single(faults);
    }

    [Fact]
    public void ASetupDiffersOnlyWhenSomethingInItChanged()
    {
        IcbmConfig config = new();
        TargetSet set = new();
        set.SetOnly(Chaco, 6);

        BallisticSettings a = BallisticSettings.From(config, set.Entries, 0);
        Assert.False(a.Differs(BallisticSettings.From(config, set.Entries, 0)));
        Assert.False(a.Differs(RoundTrip(a)["Rocket"]));

        config.Loft = 1.3;
        Assert.True(a.Differs(BallisticSettings.From(config, set.Entries, 0)));

        set.SetWarheads(0, 5, 6);
        Assert.True(a.Differs(BallisticSettings.From(new IcbmConfig(), set.Entries, 0)));
    }

    [Fact]
    public void ARestoredSetKeepsItsCountsBeforeAnyWarheadIsAboard()
    {
        TargetSet set = new();
        set.Restore([new TargetSet.Entry(Chaco, 4), new TargetSet.Entry(East, 2), new TargetSet.Entry(AimSite.None, 3)], 1);

        Assert.Equal(2, set.Count);
        Assert.Equal(6, set.Assigned);
        Assert.Equal(East, set.Primary);
    }
}
