using Xunit;

namespace KSArmory.Tests;

public class SaveRepairTests
{
    private static readonly string[] Mk42Now =
    [
        "KSArmory_Mk42Base_SubPart", "KSArmory_Mk42Turret_SubPart", "KSArmory_Mk42Cannon_SubPart",
        "KSArmory_Mk42Barrel_SubPart",
    ];

    // A Mk 42 as a save written before the shell subparts went has it, in KSA's own layout and line endings.
    private static string Save(int shells, string partTag = "PartRef")
    {
        List<string> lines =
        [
            "      <RootPartRef InstanceOf=\"CoreFuelTankA_Prefab_Tank\" LocalInstanceId=\"1\" Stage=\"0\">",
            $"        <{partTag} InstanceOf=\"KSArmory_Prefab_Mk42\" LocalInstanceId=\"6\" Stage=\"0\">",
            "          <Transform>",
            "            <Position X=\"1.92\" Y=\"0\" Z=\"0\" />",
            "          </Transform>",
            "          <PartConnectorRef Index=\"0\" ConnectedLocalInstanceId=\"1\" />",
            "          <SubPartRef InstanceOf=\"KSArmory_Mk42Base_SubPart\" LocalInstanceId=\"7\" Stage=\"0\" />",
            "          <SubPartRef InstanceOf=\"KSArmory_Mk42Turret_SubPart\" LocalInstanceId=\"8\" Stage=\"0\" />",
            "          <SubPartRef InstanceOf=\"KSArmory_Mk42Cannon_SubPart\" LocalInstanceId=\"9\" Stage=\"0\" />",
            "          <SubPartRef InstanceOf=\"KSArmory_Mk42Barrel_SubPart\" LocalInstanceId=\"10\" Stage=\"0\" />",
        ];

        for (int i = 0; i < shells; i++)
        {
            lines.Add($"          <SubPartRef InstanceOf=\"KSArmory_Mk42Shell_SubPart\" LocalInstanceId=\"{11 + i}\" Stage=\"0\" />");
        }

        lines.Add($"        </{partTag}>");
        lines.Add("        <SubPartRef InstanceOf=\"CoreFuelTankA_Subpart_EndcapLF2WA\" LocalInstanceId=\"3\" Stage=\"0\" />");
        lines.Add("      </RootPartRef>");
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static IReadOnlyList<string>? Mk42Only(string id) => id == "KSArmory_Prefab_Mk42" ? Mk42Now : null;

    [Fact]
    public void ASaveFromBeforeTheShellsWentLosesThemAndNothingElse()
    {
        SaveRepair.Result result = SaveRepair.Repair(Save(20), Mk42Only);

        Assert.Equal(1, result.PartsRepaired);
        Assert.Equal(20, result.EntriesDropped);
        Assert.Equal(Save(0), result.Text);
    }

    [Fact]
    public void ARootPartIsRepairedAsReadilyAsAPassenger()
        => Assert.Equal(Save(0, "RootPartRef"), SaveRepair.Repair(Save(20, "RootPartRef"), Mk42Only).Text);

    [Fact]
    public void ASaveThatAlreadyMatchesIsNotRewritten()
        => Assert.Null(SaveRepair.Repair(Save(0), Mk42Only).Text);

    [Fact]
    public void FewerSavedThanDeclaredLoadsAsItIs()
        => Assert.Null(SaveRepair.Repair(Save(0), _ => [.. Mk42Now, "KSArmory_Mk42Extra_SubPart"]).Text);

    [Fact]
    public void APartNothingAnswersForIsLeftAlone()
        => Assert.Null(SaveRepair.Repair(Save(20), _ => null).Text);

    [Fact]
    public void ASubpartCarryingItsOwnStateIsNeverDropped()
    {
        string save = Save(20).Replace(
            "<SubPartRef InstanceOf=\"KSArmory_Mk42Shell_SubPart\" LocalInstanceId=\"30\" Stage=\"0\" />",
            "<SubPartRef InstanceOf=\"KSArmory_Mk42Shell_SubPart\" LocalInstanceId=\"30\" Stage=\"0\">");

        SaveRepair.Result result = SaveRepair.Repair(save, Mk42Only);

        Assert.Null(result.Text);
        Assert.Equal(1, result.PartsRefused);
    }

    [Fact]
    public void APartWhoseSubpartsCarryStateAndStillFitSaysNothing()
    {
        string save = Save(0).Replace(
            "<SubPartRef InstanceOf=\"KSArmory_Mk42Barrel_SubPart\" LocalInstanceId=\"10\" Stage=\"0\" />",
            "<SubPartRef InstanceOf=\"KSArmory_Mk42Barrel_SubPart\" LocalInstanceId=\"10\" Stage=\"0\">\r\n"
            + "            <Recoil X=\"0\" />\r\n          </SubPartRef>");

        SaveRepair.Result result = SaveRepair.Repair(save, Mk42Only);

        Assert.Null(result.Text);
        Assert.Empty(result.Report);
    }

    [Fact]
    public void ARenamedSubpartStillLinesUpWithWhatItWasCalledBefore()
        => Assert.Equal(SaveRepair.Canonical("KSArmory_Missile_SubPart"), SaveRepair.Canonical("KSArmory_Subpart_Missile00"));
}
