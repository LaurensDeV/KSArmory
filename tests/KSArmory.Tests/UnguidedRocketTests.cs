using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// An unguided rocket is a <see cref="Slug"/> with a motor: released like a bomb, flown like a shell, and
/// pushed for its boost along the velocity it gained rather than the one it inherited.
/// </summary>
public class UnguidedRocketTests(ITestOutputHelper output)
{
    private const double Step = 1.0 / 60.0;

    private static Slug Released(double3 velocity, double3 inherited)
        => new(Vec.Zero, velocity, null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = BuiltIns.RocketZuni,
            LaunchFrameVelocityLocal = inherited,
            ReleaseHeadingEcl = new double3(1, 0, 0),
        };

    private static void Fly(Slug round, double seconds, double3 gravity, double density)
    {
        for (double t = 0.0; t < seconds && round.State == RoundState.Flying; t += Step)
        {
            round.Update(Step, null, gravity, Vec.Zero, Vec.Zero, BuiltIns.RocketZuni, density);
        }
    }

    [Fact]
    public void TheZuniIsReleasedRatherThanLaunched()
    {
        Assert.False(BuiltIns.RocketZuni.Powered);
        Assert.True(BuiltIns.RocketZuni.TotalBoostSeconds > 0f);
    }

    [Fact]
    public void AZuniBurnsOutNearMachTwoThroughSeaLevelAir()
    {
        MunitionProfile zuni = BuiltIns.RocketZuni;
        Slug round = Released(new double3(zuni.LaunchSpeed, 0, 0), Vec.Zero);

        Fly(round, zuni.TotalBoostSeconds, new double3(0, 0, -9.80665), 1.0);

        output.WriteLine($"burnout at {round.Speed:F0} m/s after {round.DistanceFlown:F0} m");
        Assert.InRange(round.Speed, 650.0, 750.0);
    }

    // Fired straight ahead off a craft sliding sideways at 250 m/s: the motor drives the rocket along its tube.
    [Fact]
    public void TheMotorPushesAlongWhatTheRocketGainedNotWhatItInherited()
    {
        double3 inherited = new(0, 250, 0);
        double3 tube = new(1, 0, 0);
        Slug round = Released(inherited + (tube * BuiltIns.RocketZuni.LaunchSpeed), inherited);

        Fly(round, BuiltIns.RocketZuni.TotalBoostSeconds, Vec.Zero, 0.0);

        double3 gained = round.VelocityEcl - inherited;
        double offTube = double.RadiansToDegrees(Math.Acos(Vec.Dot(Vec.Unit(gained), tube)));
        output.WriteLine($"gained {Vec.Len(gained):F0} m/s, {offTube:F3} deg off the tube");

        Assert.True(Vec.Len(gained) > 500.0, $"the motor added only {Vec.Len(gained):F0} m/s");
        Assert.True(offTube < 0.01, $"the rocket was driven {offTube:F2} deg off its tube");
    }

    [Fact]
    public void ARoundWithNoMotorIsGivenNoThrust()
    {
        var round = new Slug(Vec.Zero, new double3(100, 0, 0), null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = BuiltIns.NukeB61,
            LaunchFrameVelocityLocal = new double3(0, 300, 0),
        };

        for (int i = 0; i < 120; i++) round.Update(Step, null, Vec.Zero, Vec.Zero, Vec.Zero, BuiltIns.NukeB61, 0.0);

        Assert.Equal(100.0, round.VelocityEcl.X, 9);
        Assert.Equal(0.0, round.VelocityEcl.Y, 9);
    }

    [Fact]
    public void TheFinsAreFoldedAtReleaseAndOpenOnceItIsAway()
    {
        MunitionProfile zuni = BuiltIns.RocketZuni;
        Slug round = Released(new double3(zuni.LaunchSpeed, 0, 0), Vec.Zero);
        Assert.Equal(0.0, round.FinDeployment(zuni));

        Fly(round, zuni.FinDeploySeconds + 0.05, Vec.Zero, 1.0);
        Assert.Equal(1.0, round.FinDeployment(zuni));
    }

    private sealed class Flat(double radius) : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = radius;
            return true;
        }
    }

    // The sight flies the same round, so its pipper is where a rocket with its motor lands, and that is
    // further out than the motor-less fall from the same release. A sight told nothing of the craft's
    // own velocity would push the rocket along the craft's track and put the pipper somewhere else again.
    [Fact]
    public void TheSightFliesTheMotor()
    {
        const double radius = 6_371_000.0;
        double3 release = new(0, 0, radius + 1_500.0);
        double3 Gravity(double3 p) => p * (-9.80665 * radius * radius / Math.Pow(Vec.Len(p), 3));

        // Diving at 20 degrees at 200 m/s, the pod along the flight path.
        double3 along = Vec.Unit(new double3(Math.Cos(double.DegreesToRadians(20)), 0, -Math.Sin(double.DegreesToRadians(20))));
        double3 craft = along * 200.0;
        double3 overGround = craft + (along * BuiltIns.RocketZuni.LaunchSpeed);

        double3 Impact(MunitionProfile munition, double3 inherited)
        {
            Assert.True(BombSight.TryPredict(release, overGround, Vec.Zero, Vec.Zero, Vec.Zero, _ => Vec.Zero,
                                             munition, Gravity, _ => 1.0, new Flat(radius), 0.05,
                                             new List<double3>(), out double3 impact, inheritedOverGround: inherited));
            return impact;
        }

        MunitionProfile coasting = BuiltIns.RocketZuni.Copy();
        coasting.BoostSeconds = 0f;

        double withMotor = Vec.Len(Impact(BuiltIns.RocketZuni, craft) - release);
        double without = Vec.Len(Impact(coasting, craft) - release);
        double sideways = Vec.Len(Impact(BuiltIns.RocketZuni, new double3(0, 200, 0)) - Impact(BuiltIns.RocketZuni, craft));
        output.WriteLine($"pipper {withMotor:F0} m out with the motor, {without:F0} m without; "
                         + $"{sideways:F0} m apart if the craft's velocity is misread");

        Assert.True(withMotor > without + 100.0, $"the motor moved the pipper only {withMotor - without:F0} m");
        Assert.True(sideways > 100.0, "the inherited velocity changed nothing about the prediction");
    }
}
