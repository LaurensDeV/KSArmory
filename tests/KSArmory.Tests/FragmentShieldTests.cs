using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class FragmentShieldTests
{
    private static readonly ArmourMaterial Rolled = new()
    {
        Name = "Rolled", DisplayName = "Rolled", DensityKgPerM3 = 7850f, LambertCoefficient = 4000f,
        ResistancePascals = 5.0e9f,
    };

    private static (List<(int, double Share)> Shares, List<(int, int Hits)> Hits, List<(int, double JoulesPerHit)> Joules)
        Cost(DamageablePart part)
    {
        MunitionProfile amraam = Catalogue.MunitionNamed("AIM120C");
        double3 nose = new(1, 0, 0);
        double3 lead = Fragmentation.FuzeLead(FragmentSpray.Of(amraam), nose * 600.0, Vec.Zero, 10.0);
        BurstContext burst = new(Vec.Zero, 0.0, Vec.Zero, nose * 600.0, nose, 1.225, lead, 1.0);
        List<(int, double Share)> shares = [];
        List<(int, int Hits)> hits = [];
        List<(int, double JoulesPerHit)> joules = [];
        BlastDamage.Costs(burst, [part], amraam, new Random(7), shares, hits, joules);
        return (shares, hits, joules);
    }

    private static readonly DamageablePart Behind = new(0, new double3(0, 10, 0), 2.0, 9e6, MassKg: 1500.0,
                                                        PresentedAreaM2: 6.0);

    [Fact]
    public void NoPlateInTheWayCostsExactlyWhatItDidBefore()
    {
        var plain = Cost(Behind);
        var shielded = Cost(Behind with { Shield = default });

        Assert.Equal(plain.Shares, shielded.Shares);
        Assert.Equal(plain.Hits, shielded.Hits);
    }

    [Fact]
    public void AThickPlateInTheWayStopsEveryFragment()
    {
        var open = Cost(Behind);
        var shielded = Cost(Behind with { Shield = new ArmourLayer(Rolled, 0.05) });

        Assert.NotEmpty(open.Hits);
        Assert.Empty(shielded.Hits);
        Assert.True(shielded.Shares.Sum(s => s.Share) < open.Shares.Sum(s => s.Share));
    }

    [Fact]
    public void AThinPlateLetsFragmentsThroughWithLessEach()
    {
        var open = Cost(Behind);
        var shielded = Cost(Behind with { Shield = new ArmourLayer(Rolled, 0.002) });

        Assert.NotEmpty(shielded.Joules);
        Assert.True(shielded.Joules[0].JoulesPerHit < open.Joules[0].JoulesPerHit);
    }
}
