using Xunit;

namespace KSArmory.Tests;

/// <summary>A mod's Bodies.xml, read -- and the one this mod ships.</summary>
[Collection("BodyCatalogue")]
public class BodyReaderTests
{
    [Fact]
    public void ABodiesFileIsToldFromAWeaponPack()
    {
        Assert.True(BodyReader.IsBodies("<?xml version=\"1.0\"?><!-- c --><Bodies><Body Id=\"X\"/></Bodies>"));
        Assert.False(BodyReader.IsBodies("<WeaponPack Schema=\"1\"/>"));
        Assert.False(BodyReader.IsBodies("not xml"));
    }

    [Fact]
    public void ABadAttributeCostsThatBodyAloneAndSaysWhy()
    {
        (List<(string Id, BodyTraits Traits)> bodies, List<PackFault> faults) = BodyReader.Read(
            """
            <Bodies>
              <Body Id="Good" FieldTesla="2e-5" Airglow="Hydrogen" Surface="false" />
              <Body Id="Typo" FieldTelsa="2e-5" />
              <Body Id="Glow" Airglow="Plasma" />
              <Body FieldTesla="1e-5" />
            </Bodies>
            """, "test");

        Assert.Single(bodies);
        Assert.Equal("Good", bodies[0].Id);
        Assert.Equal(2e-5, bodies[0].Traits.FieldTesla);
        Assert.Equal(Airglow.Hydrogen, bodies[0].Traits.Airglow);
        Assert.False(bodies[0].Traits.HasSurface);

        Assert.Equal(3, faults.Count);
        Assert.Contains(faults, f => f.Name == "Typo" && f.Reason.Contains("FieldTelsa"));
        Assert.Contains(faults, f => f.Name == "Glow" && f.Reason.Contains("OxygenNitrogen"));
        Assert.Contains(faults, f => f.Reason.Contains("no Id"));
    }

    [Fact]
    public void ABodyNobodyDescribedGetsTheNeutralDefault()
    {
        BodyCatalogue.Clear();
        BodyTraits t = BodyCatalogue.For("Nowhere");

        Assert.False(t.HasField);
        Assert.Equal(Airglow.Neutral, t.Airglow);
        Assert.True(t.HasSurface);
    }

    [Fact]
    public void ALaterModRestatesABodyAndTheAuditFindsOnesThatAreNotThere()
    {
        BodyCatalogue.Clear();
        BodyCatalogue.Register("<Bodies><Body Id=\"A\" FieldTesla=\"1e-5\"/><Body Id=\"Gone\"/></Bodies>", "first");
        List<PackFault> replaced = BodyCatalogue.Register("<Bodies><Body Id=\"A\" FieldTesla=\"2e-5\"/></Bodies>", "second");

        Assert.Equal(2e-5, BodyCatalogue.For("A").FieldTesla);
        Assert.Contains(replaced, f => f.Reason.Contains("first"));

        List<PackFault> audit = BodyCatalogue.Audit(id => id == "A");
        Assert.Single(audit);
        Assert.Equal("Gone", audit[0].Name);
        BodyCatalogue.Clear();
    }

    /// <summary>
    /// The shipped file reads clean and gives the home body its field: a parse error there would take
    /// the aurora away from every player with nothing to say so but a warning.
    /// </summary>
    [Fact]
    public void TheShippedFileGivesItsBodiesTheirFields()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "KSArmory.sln"))) at = at.Parent;
        Assert.NotNull(at);

        string xml = File.ReadAllText(Path.Combine(at!.FullName, "src", "KSArmory", "KSArmory", "Bodies.xml"));
        Assert.True(BodyReader.IsBodies(xml));

        (List<(string Id, BodyTraits Traits)> bodies, List<PackFault> faults) = BodyReader.Read(xml, "KSArmory");
        Assert.Empty(faults);
        Assert.Contains(bodies, b => b.Traits.HasField && b.Traits.Airglow == Airglow.OxygenNitrogen);
        Assert.All(bodies, b => Assert.True(b.Traits.Gamma is > 1.0 and < 2.0));
    }
}
