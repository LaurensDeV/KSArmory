using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which way a round points. Every round but a bomb leaves its tube at 25 m/s or more and has an
/// airflow from the first frame; a store that is released rather than fired has none, and a rule
/// that takes any airspeed above a millimetre a second as a heading points it wherever the
/// residual happened to lie.
/// </summary>
public class BodyAttitudeTests
{
    private const double Dt = 1.0 / 60.0;

    private static readonly double3 Forward = new(0, 1, 0);
    private static readonly double3 Down = new(0, 0, -1);

    private static readonly doubleQuat AlongForward = FireGeometry.RotationFromNose(Forward);

    /// <summary>
    /// A bomb at the instant of release has centimetres per second of airspeed in whatever
    /// direction the ejector left it, and pointing along that puts it sideways.
    /// </summary>
    [Fact]
    public void AStoreWithNoAirflowKeepsTheAttitudeItLeftAt()
    {
        double3 dribble = new(0.04, -0.02, 0.31);

        Assert.True(Vec.Len(dribble) > 1e-3, "the old guard would have accepted this as a heading");

        double3 nose = Nose(BodyAttitude.Turn(AlongForward, dribble, 1.0, 1.0));

        Assert.True(Vec.Len(nose - Forward) < 1e-9, $"pointed {Fmt(nose)} instead of along the rack");
    }

    /// <summary>Once it is really flying, the airflow decides and nothing else.</summary>
    [Fact]
    public void AtSpeedTheAirflowDecides()
    {
        doubleQuat attitude = AlongForward;
        for (int i = 0; i < 60; i++) attitude = BodyAttitude.Turn(attitude, Down * 300.0, 1.0, Dt);

        double3 nose = Nose(attitude);
        Assert.True(Vec.Len(nose - Down) < 1e-6, $"pointed {Fmt(nose)} instead of down");
    }

    /// <summary>
    /// And it noses over rather than snapping. A threshold alone would flip the body through 90°
    /// between two frames as the bomb passed it; a store swings over as its fins take hold.
    /// </summary>
    [Fact]
    public void ItNosesOverInsteadOfSnapping()
    {
        doubleQuat attitude = AlongForward;
        double worst = 0.0;

        // Straight down, accelerating under gravity, sampled every frame.
        for (double t = 0.0; t < 6.0; t += Dt)
        {
            doubleQuat next = BodyAttitude.Turn(attitude, Down * (9.81 * t), 1.0, Dt);

            worst = Math.Max(worst, Vec.AngleBetween(Nose(attitude), Nose(next)));
            attitude = next;
        }

        // It ends up pointing down...
        Assert.True(Vec.Len(Nose(attitude) - Down) < 1e-6, $"ended up {Fmt(Nose(attitude))}");

        // ...having got there without ever jumping. Whole degrees per frame, not tens.
        Assert.True(double.RadiansToDegrees(worst) < 3.0,
                    $"jumped {double.RadiansToDegrees(worst):F1} deg in one frame");
    }

    /// <summary>
    /// Over the top of a vertical throw the airflow reverses between two frames. There is no single
    /// turn between opposite directions, so the body has to go over rather than flip.
    /// </summary>
    [Fact]
    public void ARoundGoingBackwardsTurnsOverRatherThanFlipping()
    {
        doubleQuat attitude = AlongForward;
        double worst = 0.0;

        for (double t = 0.0; t < 3.0; t += Dt)
        {
            doubleQuat next = BodyAttitude.Turn(attitude, Forward * -21.0, 1.0, Dt);

            worst = Math.Max(worst, Vec.AngleBetween(Nose(attitude), Nose(next)));
            attitude = next;
        }

        Assert.True(Vec.Len(Nose(attitude) + Forward) < 1e-6, $"ended up {Fmt(Nose(attitude))}");
        Assert.True(double.RadiansToDegrees(worst) < 15.0,
                    $"turned {double.RadiansToDegrees(worst):F1} deg in one frame");
    }

    /// <summary>Nothing usable leaves the attitude as it was, rather than a rotation of NaNs.</summary>
    [Fact]
    public void NothingUsableLeavesTheAttitudeAlone()
    {
        Assert.Equal(AlongForward, BodyAttitude.Turn(AlongForward, new double3(double.NaN, 0, 0), 1.0, Dt));
        Assert.Equal(AlongForward, BodyAttitude.Turn(AlongForward, Down * 300.0, 1.0, double.NaN));
        Assert.Equal(AlongForward, BodyAttitude.Turn(AlongForward, Down * 300.0, double.NaN, Dt));
    }

    /// <summary>
    /// The invariant the whole thing rests on: a store released with no airspeed must be drawn at
    /// the attitude it was seated at, or it snaps the instant it lets go.
    ///
    /// <para>The seat it must not move from is the tube's, not the sensor's boresight. A
    /// <c>PartForward</c> sensor boresights on the part's +X — the mounting face's outward normal
    /// — while a tube points along +Y, so the two are perpendicular on every craft at every
    /// attitude, and a bomb released along the boresight is drawn across its own axis for the
    /// whole of its fall.</para>
    /// </summary>
    [Fact]
    public void AReleasedStoreIsDrawnWhereItWasSeated()
    {
        double3 tubeAxis = TubeGeometry.TubeAxisPodFrame(Arsenal.NukeRack);

        // What TrySeatMissile writes while the bomb hangs on the rack.
        doubleQuat seated = FireGeometry.RotationFromNose(tubeAxis);

        // What the body is drawn at on the frame it is released, with no airspeed yet.
        doubleQuat released = BodyAttitude.Turn(
            TubeGeometry.ReleaseAttitudeEcl(tubeAxis, doubleQuat.Identity, doubleQuat.Identity),
            Vec.Zero, 1.0, Dt);

        Assert.True(Vec.AngleBetween(Nose(seated), Nose(released)) < 1e-9,
                    "a released store must not move from where it was seated");

        // And the boresight, the other value to hand here, is exactly across it.
        doubleQuat boresighted = TubeGeometry.ReleaseAttitudeEcl(TubeGeometry.TraverseAxis,
                                                                 doubleQuat.Identity, doubleQuat.Identity);

        Assert.Equal(90.0, double.RadiansToDegrees(Vec.AngleBetween(Nose(seated), Nose(boresighted))), 6);
    }

    /// <summary>
    /// A warhead released from a bus in orbit keeps the attitude it was let go with.
    ///
    /// <para>Nothing weathervanes in vacuum. Keyed on speed alone this is 7.3 km/s of "airflow"
    /// across the launcher, and the round snaps onto its orbital velocity the instant it
    /// separates — which is a reentry vehicle flying sideways, seen in flight.</para>
    /// </summary>
    [Fact]
    public void AStoreReleasedInVacuumDoesNotWeathervaneOntoItsOrbitalVelocity()
    {
        double3 orbital = new(7330.0, 0, 0);          // prograde, square across the tube

        Assert.True(Vec.Len(orbital) > BodyAttitude.FullAuthoritySpeed,
                    "the speed-only rule would have handed this full authority");

        // A real thermosphere rather than a hard zero, so this exercises the dynamic-pressure rule
        // and not the guard in front of it: at 1e-11 of sea level, 7.3 km/s is 5e-4 of the band.
        double3 vacuum = Nose(BodyAttitude.Turn(AlongForward, orbital, 1e-11, 10.0));
        Assert.True(Vec.Len(vacuum - Forward) < 1e-9,
                    $"released in vacuum it must keep the tube attitude, got {Fmt(vacuum)}");

        Assert.True(Vec.Len(Nose(BodyAttitude.Turn(AlongForward, orbital, 0.0, 10.0)) - Forward) < 1e-9,
                    "and a hard zero density must not divide by anything either");

        // ...and the same round low down, where there IS air, still noses over as it always did.
        double3 inAir = Nose(BodyAttitude.Turn(AlongForward, orbital, 1.0, 1.0));
        Assert.True(Vec.Len(inAir - Vec.Unit(orbital)) < 1e-9,
                    $"in air the airflow still decides, got {Fmt(inAir)}");
    }

    /// <summary>The sea-level band is unchanged, so every round fired before vacuum behaves alike.</summary>
    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(41.0, 1.0)]
    public void TheSeaLevelBandIsExactlyWhereItWas(double speed, double authority)
    {
        Assert.Equal(authority, BodyAttitude.Authority(Down * speed, mediumDensityRatio: 1.0), 12);
    }

    // Where a rotation carries the body mesh's nose, which is what is actually seen.
    private static double3 Nose(doubleQuat q) => double3.Transform(FireGeometry.NoseAxis, q);

    private static string Fmt(double3 v) => $"({v.X:F3}, {v.Y:F3}, {v.Z:F3})";
}
