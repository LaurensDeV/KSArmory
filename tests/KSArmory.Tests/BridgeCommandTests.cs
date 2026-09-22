using System.Text.Json;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What the bridge reads out of a command file. A refusal here is a reply the agent can read,
/// where a crash in the game's frame loop would be the game.
/// </summary>
public class BridgeCommandTests
{
    [Fact]
    public void ACommandCarriesItsIdNameAndArguments()
    {
        Assert.True(BridgeCommand.TryParse("""{"id":"7","cmd":"Burst","kt":20,"east_m":"3000"}""",
                                           out BridgeCommand? c, out _));

        Assert.Equal("7", c!.Id);
        Assert.Equal("burst", c.Name);
        Assert.Equal(20.0, c.Number("kt", 0.0));
        Assert.Equal(3000.0, c.Number("east_m", 0.0));
        Assert.Equal(-1.0, c.Number("north_m", -1.0));
    }

    [Theory]
    [InlineData("""{"cmd":"status"}""")]
    [InlineData("""{"id":"a/b","cmd":"status"}""")]
    [InlineData("""{"id":"1"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""not json""")]
    public void AnUnreadableCommandIsRefusedWithAReason(string text)
    {
        Assert.False(BridgeCommand.TryParse(text, out _, out string trouble));
        Assert.NotEmpty(trouble);
    }

    [Fact]
    public void ASettingIsSetByNameFromWhateverTypeItArrivesAs()
    {
        Config config = new();

        Assert.True(BridgeCommand.TrySetField(config, "nuclearblackout", Json("false"), out _));
        Assert.False(config.NuclearBlackout);

        Assert.True(BridgeCommand.TrySetField(config, "ShaderPass", Json("0"), out _));
        Assert.Equal(0f, config.ShaderPass);

        Assert.Equal("False", BridgeCommand.FieldText(config, "NuclearBlackout"));
    }

    [Fact]
    public void AFieldThatIsNotThereIsRefused()
    {
        Assert.False(BridgeCommand.TrySetField(new Config(), "NoSuchThing", Json("1"), out string trouble));
        Assert.Contains("NoSuchThing", trouble);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
