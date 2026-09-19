using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A store released from a climb goes up before it comes down, so its body turns through half a
/// turn from the attitude it left the rack at. Derived afresh from the release each frame, that
/// half turn has no axis: the body rolled 2.2 deg about its nose in a single frame near the end of
/// this fall, as the tail kit moved the flight about the reversed heading.
/// </summary>
public class ThrownStoreAttitudeTests
{
    private const double Dt = 1.0 / 60.0;
    private const double PlanetRadius = 6_371_000.0;

    // The mesh's own up: square to the nose, so it is what a roll about the nose moves.
    private static readonly double3 MeshUp = new(0, 0, 1);

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = PlanetRadius;
            return true;
        }
    }

    /// <summary>
    /// Straight up at 153 m/s with the rack's ejector push across it, guided onto a point to one
    /// side -- the release the drop scenario flies.
    /// </summary>
    [Fact]
    public void AStoreThrownStraightUpTurnsOverWithoutSpinning()
    {
        MunitionProfile b61 = Catalogue.MunitionNamed("B61");
        double3 up = new(1, 0, 0);
        double3 start = up * (PlanetRadius + 1000.0);

        Slug bomb = new(start, (up * 153.0) + new double3(0, 0, 3.1), null, 1, start, Vec.Zero)
        {
            Munition = b61,
            Ground = new Ball(),
        };

        TargetState aim = new(new double3(PlanetRadius, 300.0, 0.0), Vec.Zero, 0.0);

        doubleQuat drawn = TubeGeometry.ReleaseAttitudeEcl(up, doubleQuat.Identity, doubleQuat.Identity);
        double worstNose = 0.0, worstRoll = 0.0, noseAt = 0.0, rollAt = 0.0;

        for (int i = 0; i < 60 * 120 && bomb.State == RoundState.Flying; i++)
        {
            bomb.Update(Dt, aim, Vec.Unit(-bomb.PositionEcl) * 9.80665, Vec.Zero, start, b61);

            doubleQuat next = BodyAttitude.Turn(drawn, bomb.VelocityLocal, 1.0, Dt);

            double3 noseWas = drawn * FireGeometry.NoseAxis;
            double3 noseNow = next * FireGeometry.NoseAxis;
            double nose = Vec.AngleBetween(noseWas, noseNow);

            // Whatever the turn of the nose does not account for is roll about it.
            double3 carriedUp = Vec.RotationFromTo(noseWas, noseNow) * (drawn * MeshUp);
            double roll = Vec.AngleBetween(carriedUp, next * MeshUp);

            if (nose > worstNose) (worstNose, noseAt) = (nose, bomb.Age);
            if (roll > worstRoll) (worstRoll, rollAt) = (roll, bomb.Age);

            drawn = next;
        }

        Assert.Equal(RoundState.Detonated, bomb.State);

        Assert.True(double.RadiansToDegrees(worstNose) < 15.0,
                    $"the nose jumped {double.RadiansToDegrees(worstNose):F1} deg in one frame, "
                    + $"{noseAt:F1} s into the flight");
        Assert.True(double.RadiansToDegrees(worstRoll) < 1.0,
                    $"the body rolled {double.RadiansToDegrees(worstRoll):F1} deg about its nose in one "
                    + $"frame, {rollAt:F1} s into the flight");
    }
}
