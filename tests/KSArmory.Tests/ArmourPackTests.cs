using Xunit;

namespace KSArmory.Tests;

public class ArmourPackTests
{
    private const string Steel =
        """<ArmourMaterial Name="Steel" DisplayName="Rolled steel" DensityKgPerM3="7850" LambertCoefficient="4000" ResistanceMPa="5000" />""";

    private static string Wrap(string body) => $"""<WeaponPack Schema="1">{body}</WeaponPack>""";

    private static PackContents Read(string xml) => PackReader.Read(xml, "TestPack", [], [], [], []);

    [Fact]
    public void APlateNamesItsMaterialAndBringsNoComponentRow()
    {
        PackContents pack = Read(Wrap(Steel + """<Armour PartId="TestPack_Prefab_Plate" Material="Steel" ThicknessMm="25" />"""));

        Assert.Empty(pack.Faults);
        ArmourMaterial steel = Assert.Single(pack.ArmourMaterials);
        Assert.Equal(("TestPack:Steel", 7850f, 4000f, 5.0e9f), (steel.Name, steel.DensityKgPerM3, steel.LambertCoefficient,
                                                                 steel.ResistancePascals));
        ArmourProfile plate = Assert.Single(pack.Armour);
        Assert.Equal(("TestPack:Steel", 25f), (plate.Material, plate.ThicknessMm));
        Assert.Empty(pack.Components);
    }

    [Fact]
    public void AMisspeltAttributeIsRefusedRatherThanDefaulted()
    {
        PackContents pack = Read(Wrap(
            """<ArmourMaterial Name="Steel" DisplayName="Steel" DensityKgPerM3="7850" LambertCoeficient="4000" />"""));

        Assert.Empty(pack.ArmourMaterials);
        Assert.NotEmpty(pack.Faults);
    }

    [Fact]
    public void APlateOfAMaterialNobodyDeclaresIsRefused()
    {
        PackContents pack = Read(Wrap("""<Armour PartId="TestPack_Prefab_Plate" Material="Unobtainium" ThicknessMm="25" />"""));

        Assert.Empty(pack.Armour);
        Assert.Contains(pack.Faults, f => f.Element == "Armour");
    }

    [Fact]
    public void APlateWithNoThicknessIsRefused()
    {
        PackContents pack = Read(Wrap(Steel + """<Armour PartId="TestPack_Prefab_Plate" Material="Steel" />"""));

        Assert.Empty(pack.Armour);
    }

    [Fact]
    public void APlateMayNameAMaterialInAnotherFileOfItsPack()
    {
        var read = PackFiles.Read(
        [
            ("a.xml", Wrap("""<Armour PartId="TestPack_Prefab_Plate" Material="Steel" ThicknessMm="25" />""")),
            ("b.xml", Wrap(Steel)),
        ], "TestPack");

        Assert.All(read, f => Assert.Empty(f.Contents.Faults));
        Assert.Single(read[0].Contents.Armour);
    }
}
