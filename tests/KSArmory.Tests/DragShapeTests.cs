using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The engine's drag, by the direction the air meets the body from and how the body is turning.
/// </summary>
public class DragShapeTests
{
    // The test drone's box: 10.58 x 3.20 x 3.26 m, nose on +X, faces weighted 0.3 nose, 1.0 tail,
    // 1.2 sides, and a tenth of the surface area as skin.
    private static DragShape Drone(doubleQuat attitude, double3 rates = default) => new(
        Positive: new double3(0.3 * 8.19, 1.2 * 34.5, 1.2 * 33.9),
        Negative: new double3(1.0 * 8.19, 1.2 * 34.5, 1.2 * 33.9),
        Skin: 0.1 * 2.0 * (8.19 + 34.5 + 33.9),
        Body2Ecl: attitude,
        BodyRates: rates,
        Mass: 1000.0,
        ReferenceDensity: 1.225,
        ExhaustVelocity: 0.0,
        PropellantMass: 0.0);

    [Fact]
    public void AnEngineBurnsItsMassAwayUntilThePropellantIsGone()
    {
        DragShape rocket = Drone(doubleQuat.Identity) with { ExhaustVelocity = 2000.0, PropellantMass = 150.0 };

        // 20 m/s² on a tonne from a 2 km/s exhaust is 10 kg a second, which lasts 15 s.
        double flow = rocket.MassFlowFor(new double3(0, 20, 0));
        Assert.Equal(10.0, flow, 9);
        Assert.Equal(15.0, rocket.BurnSeconds(flow), 9);
        Assert.Equal(900.0, rocket.MassAt(flow, 10.0), 9);
        Assert.Equal(850.0, rocket.MassAt(flow, 40.0), 9);

        DragShape unpowered = Drone(doubleQuat.Identity);
        Assert.Equal(0.0, unpowered.MassFlowFor(new double3(0, 20, 0)));
        Assert.Equal(1000.0, unpowered.MassAt(0.0, 40.0));
    }

    [Fact]
    public void AirOnTheNoseMeetsTheNoseFace()
    {
        DragShape drone = Drone(doubleQuat.Identity);
        Assert.Equal(drone.Positive.X + drone.Skin, drone.AreaFacing(new double3(5, 0, 0)), 9);
    }

    [Fact]
    public void AirFromAsternMeetsTheTail()
    {
        DragShape drone = Drone(doubleQuat.Identity);
        Assert.Equal(drone.Negative.X + drone.Skin, drone.AreaFacing(new double3(-5, 0, 0)), 9);
    }

    [Fact]
    public void TheFlownDronesAreaIsReproduced()
    {
        // Logged in flight against the engine's own ComputeCdA: 65.803 m² for this airflow in the body.
        DragShape drone = Drone(doubleQuat.Identity);
        Assert.Equal(65.8, drone.AreaFacing(new double3(-0.389, 0.280, -0.878)), 1);
    }

    [Fact]
    public void TheAreaTurnsWithTheCraft()
    {
        // Nose pointed along +Y: air along +Y now meets the nose, not a side.
        DragShape drone = Drone(Vec.RotationFromTo(new double3(1, 0, 0), new double3(0, 1, 0)));

        Assert.Equal(drone.Positive.X + drone.Skin, drone.AreaFacing(new double3(0, 3, 0)), 6);
    }

    [Fact]
    public void TheAreaTurnsWithTheBodyRates()
    {
        // A quarter turn a second about the body's +Z: a second on, the nose is where +Y was.
        DragShape drone = Drone(doubleQuat.Identity, new double3(0, 0, Math.PI / 2.0));

        Assert.Equal(drone.Positive.Y + drone.Skin, drone.AreaFacing(new double3(0, 3, 0)), 6);
        Assert.Equal(drone.Positive.X + drone.Skin, drone.AreaFacing(new double3(0, 3, 0), 1.0), 6);
    }

    [Fact]
    public void AThrustFixedToTheBodyTurnsWithIt()
    {
        DragShape drone = Drone(Vec.RotationFromTo(new double3(1, 0, 0), new double3(0, 0, 1)),
                                new double3(0, 0, Math.PI / 2.0));
        double3 thrust = new(0, 0, 12);   // along the nose

        Assert.Equal(thrust, drone.Carry(thrust, 0.0));
        double3 later = drone.Carry(thrust, 1.0);
        Assert.Equal(12.0, Vec.Len(later), 9);
        Assert.Equal(1.0, Vec.Dot(Vec.Unit(later), drone.Body2Ecl * (doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), Math.PI / 2.0) * new double3(1, 0, 0))), 9);
    }

    [Fact]
    public void ItsDragIsHalfTheDensityTimesAreaAndSpeedSquaredOverMass()
    {
        DragShape drone = Drone(doubleQuat.Identity);
        double3 air = new(-200, 0, 0);

        double3 drag = drone.DragAcceleration(air, 0.4);

        double expected = 0.5 * 1.225 * 0.4 * (drone.Negative.X + drone.Skin) * 200.0 * 200.0 / 1000.0;
        Assert.Equal(expected, drag.X, 9);
        Assert.Equal(0.0, drag.Y, 12);
        Assert.Equal(Vec.Zero, drone.DragAcceleration(air, 0.0));
    }

    [Fact]
    public void ANonsenseShapeIsNotUsable()
    {
        Assert.False(Drone(doubleQuat.Identity) with { Positive = new double3(double.NaN, 0, 0) } is { IsUsable: true });
        Assert.False(new DragShape(Vec.Zero, Vec.Zero, 0.0, doubleQuat.Identity, Vec.Zero, 1.0, 1.225, 0.0, 0.0).IsUsable);
        Assert.False((Drone(doubleQuat.Identity) with { Mass = 0.0 }).IsUsable);
        Assert.False((Drone(doubleQuat.Identity) with { PropellantMass = 1000.0 }).IsUsable);
        Assert.False((Drone(doubleQuat.Identity) with { ReferenceDensity = double.NaN }).IsUsable);
        Assert.True(Drone(doubleQuat.Identity).IsUsable);
    }
}
