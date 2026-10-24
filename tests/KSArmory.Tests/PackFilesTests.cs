using Xunit;

namespace KSArmory.Tests;

[Collection("catalogue")]
public class PackFilesTests
{
    // The launcher's file sorts first, so a file read on its own sees no sensor yet.
    private static (string, string) Launcher(string source) => ("a-launcher.xml", $"""
        <WeaponPack Schema="1">
          <Launcher PartId="{source}_Prefab_Rail" DisplayName="Rail" Munition="Bolt" Sensor="Eye">
            <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
          </Launcher>
        </WeaponPack>
        """);

    private static readonly (string, string) Library = ("b-library.xml", """
        <WeaponPack Schema="1">
          <Munition Name="Bolt" DisplayName="Bolt" MassKg="10" CalibreMm="100" DragCoefficient="0.3" />
          <Sensor Name="Eye" DisplayName="Eye" Range="9000" />
        </WeaponPack>
        """);

    [Fact]
    public void APacksLauncherMayNameASensorInAFileReadAfterIt()
    {
        var results = PackFiles.Register([Library, Launcher("SplitPack")], "SplitPack");

        Assert.All(results, r => Assert.True(r.Result.Complete, string.Join("; ", r.Result.Faults)));
        Assert.Equal(["a-launcher.xml", "b-library.xml"], results.Select(r => r.FileName));
        LauncherProfile? rail = Catalogue.LauncherForPart("SplitPack_Prefab_Rail");
        Assert.NotNull(rail);
        Assert.Equal(9000f, Catalogue.LoadoutFor(rail).Sensor.Range);
    }

    // Read one file at a time, the same pack is refused: the test above depends on reading them together.

    [Fact]
    public void ReadOneFileAtATimeTheSamePackIsRefused()
    {
        (string _, string launcher) = Launcher("LonePack");

        Assert.False(Armoury.Register(launcher, "LonePack").Complete);
        Assert.Null(Catalogue.LauncherForPart("LonePack_Prefab_Rail"));
    }

    [Fact]
    public void ACodePackRegisteringSeveralTextsGetsTheSameCrossFileReading()
    {
        IReadOnlyList<PackResult> results = Armoury.Register([Launcher("CodePack"), Library], "CodePack");

        Assert.All(results, r => Assert.True(r.Complete, string.Join("; ", r.Faults)));
        Assert.NotNull(Catalogue.LauncherForPart("CodePack_Prefab_Rail"));
    }
}
