using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class HydroRamTests
{
    // NASA TM X-52063's spheres, 7/32 in: steel 15.41e-4 lb, tungsten carbide 29.64e-4 lb.
    private const double Sphere = 0.00556;
    private const double SteelKg = 15.41e-4 * 0.45359;
    private const double TungstenKg = 29.64e-4 * 0.45359;
    private const double Water = 1000.0;

    [Fact]
    public void ASteelSphereCracksA0Point8Mm2014WallLoadedToYieldAtTableIsSpeed()
    {
        Assert.Equal(1463.0, HydroRam.CrackingSpeed(0.000794, Sphere, SteelKg, 1.0, Water), 0);
    }

    // Table I: 5,800 ft/s, 1,768 m/s, through the 1/16 in wall.
    [Fact]
    public void TwiceTheWallNeedsTableIsSpeed()
    {
        Assert.InRange(HydroRam.CrackingSpeed(0.001588, Sphere, SteelKg, 1.0, Water), 1768.0 * 0.98, 1768.0 * 1.02);
    }

    // Table I: 2,900 ft/s, 884 m/s, for tungsten carbide, which the fit reaches only through mass per area.
    [Fact]
    public void ATungstenSphereCracksTheWallNearTableIsSpeed()
    {
        Assert.InRange(HydroRam.CrackingSpeed(0.000794, Sphere, TungstenKg, 1.0, Water), 884.0 * 0.97, 884.0 * 1.03);
    }

    [Fact]
    public void AnUnloadedWallNeedsUnloadedTimesTheSpeed()
    {
        double loaded = HydroRam.CrackingSpeed(0.000794, Sphere, SteelKg, 1.0, Water);
        Assert.Equal(HydroRam.UnloadedSpeedFactor * loaded, HydroRam.CrackingSpeed(0.000794, Sphere, SteelKg, 0.0, Water), 6);
    }

    [Fact]
    public void AQuarterOfTheDamageNeedsTwiceTheSpeed()
    {
        double whole = HydroRam.CrackingSpeed(0.004, 0.02, 0.102, 0.5, 1146.0);
        Assert.Equal(2.0 * whole, HydroRam.CrackingSpeed(0.004, 0.02, 0.102, 0.5, 1146.0, damageScale: 0.25), 6);
    }

    [Fact]
    public void LiquidHydrogenTakesLessOffARoundThanLiquidOxygen()
    {
        double lox = HydroRam.DepositedShare(0.1, 0.01275, 1146.0, 3.0);
        double lh2 = HydroRam.DepositedShare(0.1, 0.01275, 72.0, 3.0);

        Assert.InRange(lox, 0.9, 1.0);
        Assert.True(lh2 < 0.3 * lox);
        Assert.Equal(0.0, HydroRam.DepositedShare(0.1, 0.01275, 1146.0, 0.0));
    }

    [Fact]
    public void TheLessUllageTheHigherTheCavityRaisesTheTank()
    {
        double tenth = HydroRam.CavityPascals(30_000.0, 30.0, 3.0, 4.0e5, 1.5, 0.004);
        double hundredth = HydroRam.CavityPascals(30_000.0, 30.0, 0.3, 4.0e5, 1.5, 0.004);
        double full = HydroRam.CavityPascals(30_000.0, 30.0, 0.0, 4.0e5, 1.5, 0.004);

        Assert.True(tenth < hundredth);
        Assert.True(hundredth <= full * 1.0001);
        Assert.InRange(full, 1.0e5, 1.0e6);
    }

    // A unit cylinder along x; up is +z and the surface at z = 0.
    private static bool Cylinder(double3 p) => Math.Abs(p.X) <= 1.0 && ((p.Y * p.Y) + (p.Z * p.Z)) <= 1.0;

    [Fact]
    public void APathUnderTheSurfaceIsWetAndOneAboveItIsDry()
    {
        double3 up = new(0, 0, 1), along = new(1, 0, 0);

        double under = HydroRam.WetPath(Cylinder, new double3(-1.0, 0, -0.5), along, up, 0.0, false, 0.0, 5.0);
        double over = HydroRam.WetPath(Cylinder, new double3(-1.0, 0, 0.5), along, up, 0.0, false, 0.0, 5.0);

        Assert.InRange(under, 1.95, 2.05);
        Assert.Equal(0.0, over);
    }

    [Fact]
    public void FloatingTheLiquidMeetsThePathAtItsShare()
    {
        double wet = HydroRam.WetPath(Cylinder, new double3(-1.0, 0, 0.5), new double3(1, 0, 0), Vec.Zero, 0.0,
                                      true, 0.3, 5.0);
        Assert.InRange(wet, 0.58, 0.62);
    }

    // Turned and carried as one body, the tank and the path meet the same liquid.
    [Fact]
    public void TurningAndMovingTheWholeTankChangesNothing()
    {
        doubleQuat turn = doubleQuat.CreateFromAxisAngle(Vec.Unit(new double3(0.3, -0.8, 0.5)), 1.1);
        double3 shift = new(17_800.0, 23_900.0, -400.0);
        doubleQuat back = doubleQuat.Inverse(turn);
        bool Moved(double3 p) => Cylinder(back * (p - shift));

        double3 entry = new(-1.0, 0.1, -0.4), along = Vec.Unit(new double3(1.0, 0.1, 0.05)), up = new(0, 0, 1);
        double still = HydroRam.WetPath(Cylinder, entry, along, up, 0.1, false, 0.0, 5.0);
        double moved = HydroRam.WetPath(Moved, (turn * entry) + shift, turn * along, turn * up,
                                        0.1 + Vec.Dot(shift, turn * up), false, 0.0, 5.0);

        Assert.True(still > 0.5);
        Assert.Equal(still, moved, 6);
    }
}
