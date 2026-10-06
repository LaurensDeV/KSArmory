using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>Where the liquid in a holed tank stands, and what runs out of it.</summary>
public class TankLeakTests
{
    // A 3 m wide, 10 m tall tank standing on the origin, z up.
    private static readonly double3[] Standing =
        TankLeak.FillPoints(new double3(-1.5, -1.5, 0.0), new double3(1.5, 1.5, 10.0), 4096);

    private static readonly double3 Up = new(0, 0, 1);
    private static readonly double3 Side = new(1, 0, 0);

    private static double Surface(double3[] points, double3 up, double fill)
        => TankLeak.SurfaceHeight(points, up, fill, new double[points.Length]);

    [Theory]
    [InlineData(0.25, 2.5)]
    [InlineData(0.5, 5.0)]
    [InlineData(0.9, 9.0)]
    public void AStandingTanksSurfaceIsItsFillOfItsHeight(double fill, double height)
    {
        Assert.Equal(height, Surface(Standing, Up, fill), 1);
    }

    /// <summary>
    /// On its side a half-full tank's surface is through its axis, and a quarter fill is well under a
    /// quarter of its diameter from the bottom, because a lying cylinder is narrow at the bottom.
    /// </summary>
    [Fact]
    public void ALyingTanksSurfaceFollowsItsRoundSection()
    {
        Assert.Equal(0.0, Surface(Standing, Side, 0.5), 1);

        double quarter = Surface(Standing, Side, 0.25) + 1.5;
        Assert.InRange(quarter, 0.85, 1.0);
    }

    [Fact]
    public void AnEmptyTankHasNoSurfaceAndAFullOneIsFullToTheTop()
    {
        Assert.Equal(double.NegativeInfinity, Surface(Standing, Up, 0.0));
        Assert.Equal(10.0, Surface(Standing, Up, 1.0), 1);
    }

    /// <summary>
    /// A hole in the skin is in the tank; one in a raceway standing proud of it on the same part is
    /// not. The box is widened by the raceway, as a real part's is, and the tank is the narrower side.
    /// </summary>
    [Fact]
    public void OnlyAHoleInTheTanksOwnSkinCounts()
    {
        double3 min = new(-3.03, -1.5, -1.593), max = new(3.03, 1.5, 1.593);

        Assert.True(TankLeak.IsInTank(min, max, new double3(1.1, 0.0, -1.5)));
        Assert.True(TankLeak.IsInTank(min, max, new double3(-2.9, 1.02, 1.02)));
        Assert.False(TankLeak.IsInTank(min, max, new double3(1.1, 0.0, -1.59)));
        Assert.False(TankLeak.IsInTank(min, max, new double3(3.4, 0.0, 0.0)));
    }

    /// <summary>
    /// A sphere half full stands at its middle; a tenth full, at the height of a spherical cap of a
    /// tenth its volume — 0.39 of the radius from the bottom, where a cylinder of its size stands at 0.2.
    /// </summary>
    [Theory]
    [InlineData(0.5, 0.0)]
    [InlineData(0.1, -0.609)]
    public void ASphericalTanksSurfaceFollowsItsShape(double fill, double height)
    {
        double3[] sphere = TankLeak.FillPoints(TankShape.Sphere(1.0), 4096);

        Assert.Equal(4096, sphere.Length);
        Assert.Equal(height, Surface(sphere, new double3(0, 1, 0), fill), 1);
    }

    [Fact]
    public void ASphereLeavesItsBoxsCornersOut()
    {
        TankShape sphere = TankShape.Sphere(1.0);

        Assert.True(sphere.Contains(new double3(0.0, 0.99, 0.0)));
        Assert.False(sphere.Contains(new double3(0.7, 0.7, 0.3)));
    }

    /// <summary>A cone narrows toward its top, and its domes end at their height past the straight.</summary>
    [Fact]
    public void AConeNarrowsAndItsDomesEnd()
    {
        TankShape cone = TankShape.Conical(10.0, 2.0, 1.0, 0.5);

        Assert.Equal(8.5, cone.Straight, 9);
        Assert.True(cone.Contains(new double3(-4.0, 1.9, 0.0)));
        Assert.False(cone.Contains(new double3(4.0, 1.9, 0.0)));
        Assert.True(cone.Contains(new double3(4.25 + 0.45, 0.0, 0.0)));
        Assert.False(cone.Contains(new double3(4.25 + 0.55, 0.0, 0.0)));
    }

    /// <summary>
    /// A template sized to its volume model and not its mesh — the stock 3 m tank's is 3 m in radius —
    /// fitted into its part's box keeps its kind and proportions and takes the box's size.
    /// </summary>
    [Fact]
    public void AFittedShapeTakesTheBoxsSizeAndKeepsItsKind()
    {
        TankShape fitted = TankShape.Conical(6.0, 3.0, 3.0, 1.0 / Math.Sqrt(2.0)).FitTo(6.0, 1.5);
        (double low, double high, double radius) = fitted.Extent;

        Assert.Equal(6.0, high - low, 9);
        Assert.Equal(1.5, radius, 9);
        Assert.Equal(fitted.RadiusBase, fitted.RadiusTop, 9);

        TankShape cone = TankShape.Conical(10.0, 2.0, 1.0, 0.5).FitTo(5.0, 1.0);
        Assert.Equal(0.5, cone.RadiusTop / cone.RadiusBase, 9);
    }

    [Fact]
    public void TheLiquidFillsInsideTheWall()
    {
        TankShape inside = TankShape.Sphere(1.5).Inside(0.01);

        Assert.Equal(1.49, inside.RadiusBase, 9);
        Assert.False(inside.Contains(new double3(0.0, 1.495, 0.0)));
    }

    /// <summary>
    /// Bernoulli through a 3 cm hole ten metres down in water, the tank's pressure on top of the
    /// weight above it: √(2·20000/1000 + 2·9.81·10) = 15.4 m/s, and 26.5 kg/s through the necked jet.
    /// </summary>
    [Fact]
    public void TheFlowIsBernoullisThroughANeckedJet()
    {
        Assert.Equal(Math.Sqrt(40.0 + (2 * 9.81 * 10.0)), TankLeak.JetSpeed(10.0, 9.81, 1000.0), 9);
        Assert.Equal(26.5, TankLeak.FlowKgPerSecond(10.0, 9.81, 1000.0, 0.03, 1.0), 1);
    }

    /// <summary>
    /// A thruster's tenth-of-a-second pulse in a coast leaves the liquid floating; a burn settles it
    /// within a few seconds.
    /// </summary>
    [Fact]
    public void TheLiquidFollowsABurnAndNotAPulse()
    {
        double3 pulse = new(0.25, 0, 0), burn = new(9.81, 0, 0);

        double3 afterPulse = TankLeak.Settle(Vec.Zero, pulse, 0.1);
        Assert.True(TankLeak.Weightless(Vec.Len(afterPulse)), $"{Vec.Len(afterPulse):F3} m/s2");

        double3 settled = Vec.Zero;
        for (int i = 0; i < 50; i++) settled = TankLeak.Settle(settled, burn, 0.1);
        Assert.InRange(Vec.Len(settled), 9.0, 9.81);

        Assert.Equal(settled, TankLeak.Settle(settled, burn, 0.0));
    }

    /// <summary>The weight above the hole still matters: ten metres down leaks well over twice a few centimetres.</summary>
    [Fact]
    public void DepthStillMattersUnderThePressure()
    {
        double shallow = TankLeak.FlowKgPerSecond(0.05, 9.81, 1000.0, 0.03, 1.0);
        double deep = TankLeak.FlowKgPerSecond(10.0, 9.81, 1000.0, 0.03, 1.0);

        Assert.True(deep > 2.3 * shallow, $"{deep:F1} against {shallow:F1}");
    }

    [Fact]
    public void NothingRunsOutAboveASettledSurface()
    {
        Assert.Equal(0.0, TankLeak.FlowKgPerSecond(-0.5, 9.81, 1000.0, 0.03, 1.0));
        Assert.Equal(0.0, TankLeak.JetSpeed(-0.5, 9.81, 1000.0));
    }

    /// <summary>
    /// Floating, the liquid wets the walls and covers a hole about as often as the tank is full: any
    /// hole leaks, at the pressure's speed, scaled by the fill — and an empty tank leaks nothing.
    /// </summary>
    [Fact]
    public void InZeroGAnyHoleLeaksAsOftenAsTheTankIsFull()
    {
        double full = TankLeak.FlowKgPerSecond(-3.0, 0.0, 1000.0, 0.03, 1.0);
        double quarter = TankLeak.FlowKgPerSecond(-3.0, 0.0, 1000.0, 0.03, 0.25);

        Assert.Equal(Math.Sqrt(40.0), TankLeak.JetSpeed(-3.0, 0.0, 1000.0), 9);
        Assert.True(full > 0.0);
        Assert.Equal(0.25 * full, quarter, 9);
        Assert.Equal(0.0, TankLeak.FlowKgPerSecond(-3.0, 0.0, 1000.0, 0.03, 0.0));
    }

    /// <summary>
    /// Drained through a hole halfway up, a full tank empties to half and stops there; through its
    /// bottom it empties. Stepped as the game steps it, a tenth of a second at a time.
    /// </summary>
    [Theory]
    [InlineData(5.0, 0.5)]
    [InlineData(0.05, 0.0)]
    public void ATankDrainsDownToItsHoleAndNoFurther(double holeHeight, double endFill)
    {
        const double volume = Math.PI * 1.5 * 1.5 * 10.0;
        const double density = 800.0;
        double mass = volume * density;
        double[] scratch = new double[Standing.Length];

        for (int step = 0; step < 200_000; step++)
        {
            double surface = TankLeak.SurfaceHeight(Standing, Up, mass / density / volume, scratch);
            double flow = TankLeak.FlowKgPerSecond(surface - holeHeight, 9.81, density, 0.1, mass / density / volume);
            if (flow <= 0.0) break;

            mass = Math.Max(mass - (flow * 0.1), 0.0);
        }

        Assert.Equal(endFill, mass / density / volume, 1);
    }

    /// <summary>Tipped over, the same hole halfway along the tank is at the bottom of it, and drains it.</summary>
    [Fact]
    public void TippingTheTankOverMovesItsHolesUnderTheSurface()
    {
        double3 hole = new(-1.5, 0.0, 5.0);

        Assert.True(Vec.Dot(hole, Up) > Surface(Standing, Up, 0.3));
        Assert.True(Vec.Dot(hole, Side) < Surface(Standing, Side, 0.3));
    }
}
