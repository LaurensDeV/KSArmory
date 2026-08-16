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
    private static readonly double3 Forward = new(0, 1, 0);
    private static readonly double3 Down = new(0, 0, -1);

    /// <summary>
    /// A bomb at the instant of release has centimetres per second of airspeed in whatever
    /// direction the ejector left it, and pointing along that puts it sideways.
    /// </summary>
    [Fact]
    public void AStoreWithNoAirflowKeepsTheHeadingItLeftOn()
    {
        double3 dribble = new(0.04, -0.02, 0.31);

        Assert.True(Vec.Len(dribble) > 1e-3, "the old guard would have accepted this as a heading");

        double3 heading = BodyAttitude.Heading(dribble, Forward);

        Assert.True(Vec.Len(heading - Forward) < 1e-9,
                    $"pointed {Fmt(heading)} instead of along the rack");
    }

    /// <summary>Once it is really flying, the airflow decides and nothing else.</summary>
    [Fact]
    public void AtSpeedTheAirflowDecides()
    {
        double3 heading = BodyAttitude.Heading(Down * 300.0, Forward);

        Assert.True(Vec.Len(heading - Down) < 1e-9, $"pointed {Fmt(heading)} instead of down");
    }

    /// <summary>
    /// And it noses over rather than snapping. A threshold alone would flip the body through 90°
    /// between two frames as the bomb passed it; a store swings over as its fins take hold.
    /// </summary>
    [Fact]
    public void ItNosesOverInsteadOfSnapping()
    {
        double3 last = Forward;
        double worst = 0.0;

        // Straight down, accelerating under gravity, sampled every frame.
        for (double t = 0.0; t < 6.0; t += 1.0 / 60.0)
        {
            double3 heading = BodyAttitude.Heading(Down * (9.81 * t), Forward);

            worst = Math.Max(worst, Vec.AngleBetween(last, heading));
            last = heading;
        }

        // It ends up pointing down...
        Assert.True(Vec.Len(last - Down) < 1e-6, $"ended up {Fmt(last)}");

        // ...having got there without ever jumping. Whole degrees per frame, not tens.
        Assert.True(double.RadiansToDegrees(worst) < 3.0,
                    $"jumped {double.RadiansToDegrees(worst):F1} deg in one frame");
    }

    /// <summary>
    /// A round released backwards cancels to nothing halfway across the band. Rare, and the
    /// release attitude is the better answer than a zero vector normalised into anything.
    /// </summary>
    [Fact]
    public void OpposedDirectionsDoNotCancelToNothing()
    {
        double3 heading = BodyAttitude.Heading(Forward * -21.0, Forward);

        Assert.True(Vec.Len2(heading) > 0.5, "a heading must be a direction");
        Assert.True(double.IsFinite(heading.X) && double.IsFinite(heading.Y)
                    && double.IsFinite(heading.Z));
    }

    /// <summary>Nothing usable either way still yields a direction rather than a zero vector.</summary>
    [Fact]
    public void ThereIsAlwaysAHeading()
    {
        double3 heading = BodyAttitude.Heading(new double3(double.NaN, 0, 0), Vec.Zero);

        Assert.True(Vec.Len2(heading) > 0.5);
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
        double3 tubeAxis = TubeGeometry.TubeAxisPodFrame(Arsenal.BombRack);

        // What TrySeatMissile writes while the bomb hangs on the rack.
        doubleQuat seated = FireGeometry.RotationFromNose(tubeAxis);

        // What the body is drawn at on the frame it is released, with no airspeed yet.
        doubleQuat released = FireGeometry.RotationFromNose(BodyAttitude.Heading(Vec.Zero, tubeAxis));

        Assert.True(Vec.AngleBetween(Turn(seated), Turn(released)) < 1e-9,
                    "a released store must not move from where it was seated");

        // And the boresight, the other value to hand here, is exactly across it.
        doubleQuat boresighted =
            FireGeometry.RotationFromNose(BodyAttitude.Heading(Vec.Zero, TubeGeometry.TraverseAxis));

        Assert.Equal(90.0, double.RadiansToDegrees(
                         Vec.AngleBetween(Turn(seated), Turn(boresighted))), 6);
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
        double3 tube = Forward;

        Assert.True(Vec.Len(orbital) > BodyAttitude.FullAuthoritySpeed,
                    "the speed-only rule would have handed this full authority");

        // A real thermosphere rather than a hard zero, so this exercises the dynamic-pressure rule
        // and not the guard in front of it: at 1e-11 of sea level, 7.3 km/s is 5e-4 of the band.
        double3 vacuum = BodyAttitude.Heading(orbital, tube, mediumDensityRatio: 1e-11);
        Assert.True(Vec.Len(vacuum - tube) < 1e-9,
                    $"released in vacuum it must keep the tube attitude, got {Fmt(vacuum)}");

        Assert.True(Vec.Len(BodyAttitude.Heading(orbital, tube, 0.0) - tube) < 1e-9,
                    "and a hard zero density must not divide by anything either");

        // ...and the same round low down, where there IS air, still noses over as it always did.
        double3 inAir = BodyAttitude.Heading(orbital, tube, mediumDensityRatio: 1.0);
        Assert.True(Vec.Len(inAir - Vec.Unit(orbital)) < 1e-9,
                    $"in air the airflow still decides, got {Fmt(inAir)}");
    }

    /// <summary>The sea-level band is unchanged, so every round fired before vacuum behaves alike.</summary>
    [Theory]
    [InlineData(1.0, true)]
    [InlineData(41.0, false)]
    public void TheSeaLevelBandIsExactlyWhereItWas(double speed, bool keepsRelease)
    {
        double3 heading = BodyAttitude.Heading(Down * speed, Forward, mediumDensityRatio: 1.0);
        Assert.Equal(keepsRelease, Vec.Len(heading - Forward) < 1e-9);
    }

    // Where a rotation carries the body mesh's nose, which is what is actually seen.
    private static double3 Turn(doubleQuat q) => double3.Transform(FireGeometry.NoseAxis, q);

    private static string Fmt(double3 v) => $"({v.X:F3}, {v.Y:F3}, {v.Z:F3})";
}
