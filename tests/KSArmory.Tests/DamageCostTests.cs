using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a burst costs the parts round it: the blast held under what each part intercepts, and the
/// fragments that hit it. The cases are the ones that every burst used to break outright.
/// </summary>
public class DamageCostTests
{
    // 29.8 km/s of ecliptic motion, oblique to everything, which is the general case.
    private static readonly double3 Carrier = new(29_800 * 0.6, 29_800 * 0.8, 0);

    // A 3 m tank's dry shell, at the density floor the engine puts a hollow part on.
    private static DamageablePart Tank(double3 at) => new(0, at, 5.7, 0.9e6, MassKg: 2000.0, PresentedAreaM2: 18.0);

    private static DamageablePart Fin(double3 at) => new(0, at, 0.5, 0.9e6, MassKg: 10.0, PresentedAreaM2: 0.4);

    private static double Cost(MunitionProfile munition, DamageablePart part, double3 burst,
                               double3 warheadVelocity, double3 nose, double3 partVelocity, int seed = 7,
                               double3 fuzeLead = default)
    {
        List<(int, double Share)> into = [];
        BlastDamage.Costs(new BurstContext(burst, 0.0, partVelocity, warheadVelocity, nose, 1.225, fuzeLead),
                          [part], munition, new Random(seed), into);
        return into.Count == 0 ? 0.0 : into[0].Share;
    }

    /// <summary>The overpressure law alone breaks the tank; its size is what saves it now.</summary>
    [Fact]
    public void A20MmBurstOnATanksSkinDentsItAndBreaksAFin()
    {
        MunitionProfile shell = BuiltIns.Cannon20Mm;
        double3 skin = new(5.7, 0, 0);

        Assert.True(BlastDamage.Share(shell.ChargeKg, 0.9e6, 0.0) >= 1.0);
        Assert.InRange(Cost(shell, Tank(Vec.Zero), skin, Vec.Zero, Vec.Zero, Vec.Zero), 0.0, 0.2);

        Assert.True(Cost(shell, Fin(new double3(5.7 + 0.5, 0, 0)), skin, Vec.Zero, Vec.Zero, Vec.Zero) >= 1.0);
    }

    [Fact]
    public void AnAmraamBreaksAPartTenMetresOffItsBeamAndOnlyMarksOneFiftyMetresOut()
    {
        MunitionProfile amraam = Catalogue.MunitionNamed("AIM120C");
        DamageablePart part = new(0, new double3(0, 10, 0), 2.0, 9e6, MassKg: 1500.0, PresentedAreaM2: 6.0);
        DamageablePart far = part with { PositionEcl = new double3(0, 50, 0) };
        double3 nose = new(1, 0, 0);

        double3 near = Fragmentation.FuzeLead(FragmentSpray.Of(amraam), nose * 600.0, Vec.Zero, 10.0);
        double3 wide = Fragmentation.FuzeLead(FragmentSpray.Of(amraam), nose * 600.0, Vec.Zero, 50.0);

        Assert.True(Cost(amraam, part, Vec.Zero, nose * 600.0, nose, Vec.Zero, fuzeLead: near) >= 1.0);
        Assert.InRange(Cost(amraam, far, Vec.Zero, nose * 600.0, nose, Vec.Zero, fuzeLead: wide), 0.0, 0.5);
    }

    [Fact]
    public void ThePlanetsMotionUnderBothChangesNothing()
    {
        MunitionProfile amraam = Catalogue.MunitionNamed("AIM120C");
        DamageablePart part = new(0, new double3(0, 14, 0), 2.0, 9e6, MassKg: 1500.0, PresentedAreaM2: 6.0);
        DamageablePart carried = part with { PositionEcl = part.PositionEcl + Carrier };
        double3 nose = new(1, 0, 0);

        double3 lead = Fragmentation.FuzeLead(FragmentSpray.Of(amraam), nose * 600.0, Vec.Zero, 14.0);
        double3 carriedLead = Fragmentation.FuzeLead(FragmentSpray.Of(amraam), (nose * 600.0) + Carrier, Carrier, 14.0);
        double still = Cost(amraam, part, Vec.Zero, nose * 600.0, nose, Vec.Zero, fuzeLead: lead);
        double moving = Cost(amraam, carried, Carrier, (nose * 600.0) + Carrier, nose, Carrier, fuzeLead: carriedLead);

        Assert.True(still > 0.0);

        Assert.Equal(still, moving, 9);
    }

    [Fact]
    public void ANuclearChargeIsJudgedOnItsPressureAlone()
    {
        MunitionProfile bomb = BuiltIns.NukeB61;
        DamageablePart part = Tank(new double3(600, 0, 0));
        double gap = 600.0 - part.RadiusMetres;

        Assert.Equal(BlastDamage.Share(bomb.ChargeKg, part.CrashTolerancePascals, gap),
                     Cost(bomb, part, Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero), 9);
    }

    [Fact]
    public void ABiggerPartTakesMoreToBreakAndAStrongerOneMore()
    {
        Assert.True(PartToughness.Joules(2000.0, 9e6) > PartToughness.Joules(10.0, 9e6));
        Assert.Equal(PartToughness.Joules(1000.0, 9e6) * 4.0, PartToughness.Joules(8000.0, 9e6), 6);
        Assert.Equal(PartToughness.Joules(1000.0, 9e6) * 2.0, PartToughness.Joules(1000.0, 72e6), 6);
    }
}
