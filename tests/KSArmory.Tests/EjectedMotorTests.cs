using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A round an ejector throws out of its mounting face, whose motor lights after a delay: it falls clear,
/// then flies along its tube rather than along the fall and the push it was given.
/// </summary>
public class EjectedMotorTests
{
    private const double Step = 0.02;
    private const double Delay = 0.5;

    // Level flight along +X at 250 m/s, the tube along the track, thrown 6 m/s out of the face (-Z, down)
    // with gravity -Z and sea-level air: the shape of an AMRAAM leaving a bay.
    private static readonly double3 Carrier = new(250, 0, 0);
    private static readonly double3 Tube = new(1, 0, 0);
    private static readonly double3 Push = new(0, 0, -6);
    private static readonly double3 Gravity = new(0, 0, -9.81);

    private static Interceptor Ejected(double delay, double3 inheritedReference) =>
        new(Vec.Zero, Carrier + Push, null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = BuiltIns.Missile120C,
            ReleaseHeadingEcl = Tube,
            LaunchFrameVelocityLocal = inheritedReference,
            MotorDelaySeconds = delay,
        };

    private static void Fly(Interceptor round, double seconds)
    {
        int steps = (int)Math.Round(seconds / Step);
        for (int i = 0; i < steps; i++)
        {
            round.Update(Step, null, Gravity, Vec.Zero, Vec.Zero, BuiltIns.Missile120C, 1.0);
        }
    }

    [Fact]
    public void NothingPushesItAlongTheTrackBeforeTheMotorLights()
    {
        Interceptor round = Ejected(Delay, Carrier + Push);

        Fly(round, 0.44);

        Assert.False(((IProjectile)round).MotorBurning);
        Assert.True(round.VelocityEcl.X <= Carrier.X, $"gained {round.VelocityEcl.X - Carrier.X:F2} m/s along the track unlit");
    }

    /// <summary>
    /// Measured from release, "gained" holds the fall and the push, so a delayed motor would light pointing
    /// down and keep pushing that way. Measured from ignition it lights along the tube.
    /// </summary>
    [Fact]
    public void ADelayedMotorLightsAlongTheTubeAndNotDownTheFall()
    {
        Interceptor round = Ejected(Delay, Carrier + Push);

        Fly(round, Delay);
        double3 atIgnition = round.VelocityEcl;
        Fly(round, 0.1);
        double3 gained = round.VelocityEcl - atIgnition - (Gravity * 0.1);

        double offTube = Vec.AngleBetween(gained, Tube) * 180.0 / Math.PI;
        Assert.True(offTube < 15.0, $"the motor pushed {offTube:F1} deg off the tube");
    }

    [Theory]
    [InlineData(0.3, false)]
    [InlineData(0.6, true)]
    [InlineData(7.0, false)]
    public void TheMotorBurnsFromTheDelayForItsWholeBurn(double age, bool burning)
    {
        Interceptor round = Ejected(Delay, Carrier + Push);

        Fly(round, age);

        Assert.Equal(burning, ((IProjectile)round).MotorBurning);
    }

    /// <summary>Separation is counted from ignition: a round still falling unlit does not turn.</summary>
    [Fact]
    public void ItDoesNotSteerUntilItHasLitAndSeparated()
    {
        Interceptor round = Ejected(Delay, Carrier + Push);
        Aimpoint aim = Aimpoint.AtPoint(new double3(8000, 2000, 0), 5.0);
        round.Aimpoint = aim;
        double gate = Delay + BuiltIns.Missile120C.SeparationSeconds;

        bool steeredEarly = false, steeredLater = false;
        int steps = (int)Math.Round((gate + 0.5) / Step);
        for (int i = 0; i < steps; i++)
        {
            round.Update(Step, aim.ToTargetState(), Gravity, Vec.Zero, Vec.Zero, BuiltIns.Missile120C, 1.0);
            bool steering = !round.SteeringCommandEcl.Equals(Vec.Zero);
            if (round.Age < gate - 1e-9) steeredEarly |= steering;
            else steeredLater |= steering;
        }

        Assert.False(steeredEarly);
        Assert.True(steeredLater);
    }

    [Fact]
    public void AnEjectionIsAPushOutOfTheMountingFaceAtItsSpeed()
    {
        double3 normal = new(0, 0, -2);

        Assert.Equal(new double3(0, 0, -6), FireGeometry.Ejection(normal, 6.0));
        Assert.Equal(Vec.Zero, FireGeometry.Ejection(normal, 0.0));
    }
}
