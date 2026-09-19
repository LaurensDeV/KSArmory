using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Where a round stops when the ground under it is not flat.
/// </summary>
/// <remarks>
/// <para>The crossing is solved between two height samples a whole sub-step apart — 5.5 m of ground
/// at a 5,500 m/s arrival — so the round stops where that <em>chord</em> meets its path. The
/// prediction point-samples the height field. The two therefore stop on different surfaces,
/// separated by the ground's curvature over the span, and that is 57% of the walk's variance
/// (<c>docs/ACCURACY-PLAN.md</c> 3dt, 3dv).</para>
///
/// <para>On flat ground the chord IS the surface and there is nothing to find, which is why this
/// fixture puts a hill under the impact.</para>
/// </remarks>
public class ChordCrossingTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;
    private const double Mu = 3.986004418e14;

    /// <summary>
    /// Ground that is curved at the scale a sub-step covers, which is the only scale that matters
    /// here: the round crosses about 5 m of it between the two samples the chord joins, so a ridge
    /// whose curvature lives at kilometres is locally a straight line and has nothing to find.
    /// A metre of relief every few tens of metres is ordinary ground.
    /// </summary>
    private sealed class Rolling(double amplitude, double wavelength) : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;

            // Arc length along the track, from the angle about the pole the flight is in.
            double along = R * Math.Atan2(positionEcl.Y, positionEcl.X);

            surfaceRadius = R + amplitude * Math.Sin(2.0 * Math.PI * along / wavelength);
            return true;
        }

        /// <summary>Peak second derivative of the height, in metres per metre squared.</summary>
        public double Curvature => amplitude * Math.Pow(2.0 * Math.PI / wavelength, 2.0);
    }

    private static Rolling Ground(double amplitude) => new(amplitude, 40.0);

    private static double3 Fly(bool stopOnTheTerrain, double amplitude, out double stoppedOn)
    {
        // Steeply descending, so one 1 ms sub-step covers a few metres of ground -- which is the
        // span the chord is drawn across.
        double3 from = new(R + 4_000.0, 0, 0);
        double3 velocity = new(-2_900.0, 4_600.0, 0);

        MunitionProfile warhead = Arsenal.ReentryVehicleMk21.Copy();
        warhead.SubStepSeconds = 0.001f;

        Slug round = new(from, velocity, null, 1, from, Vec.Zero)
        {
            Munition = warhead,
            ResampleGroundNearImpact = true,
            SecondOrder = true,
            StopOnTheTerrain = stopOnTheTerrain,
        };

        Rolling ground = Ground(amplitude);
        RoundFields fields = new(
            GravityAt: (p, _) => Vec.Unit(-p) * (Mu / Vec.Len2(p)),
            AirDensityAt: (_, _) => 0.0,
            Ground: ground);

        for (int i = 0; i < 100_000 && round.State == RoundState.Flying; i++)
        {
            RoundDriver.Fly(round, 1.0 / 60.0, null, Vec.Unit(-round.PositionEcl) * (Mu / Vec.Len2(round.PositionEcl)),
                            Vec.Zero, Vec.Zero, warhead, 0.0, fields);
        }

        Assert.True(round.HitGround, "the round should have reached the ground");
        stoppedOn = round.GroundRadiusUsed;
        return round.PositionEcl;
    }

    /// <summary>
    /// The one that fails against the chord: on curved ground the round stops off the surface it
    /// claims to have stopped on, because the chord and the terrain are not the same thing.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void OnCurvedGroundTheRoundStopsOnTheTerrainRatherThanOnAChordOfIt(double amplitude)
    {
        double3 chord = Fly(false, amplitude, out double chordSurface);
        double3 terrain = Fly(true, amplitude, out double terrainSurface);

        Rolling truth = Ground(amplitude);

        Assert.True(truth.TryGround(chord, out _, out double underChord));
        Assert.True(truth.TryGround(terrain, out _, out double underTerrain));

        double chordOff = Vec.Len(chord) - underChord;
        double terrainOff = Vec.Len(terrain) - underTerrain;

        Out.WriteLine($"amplitude {amplitude} m over 40 m, curvature {truth.Curvature:F4} /m");
        Out.WriteLine($"   chord   : stops {chordOff * 1000.0:+0.000;-0.000} mm off the true surface, "
                      + $"and reports having stopped on {chordSurface - underChord:+0.000;-0.000} mm off it");
        Out.WriteLine($"   terrain : stops {terrainOff * 1000.0:+0.000;-0.000} mm off the true surface, "
                      + $"and reports {terrainSurface - underTerrain:+0.000;-0.000} mm off it");
        Out.WriteLine($"   the two land {Vec.Len(chord - terrain) * 1000.0:F3} mm apart");

        Assert.True(Math.Abs(terrainOff) < Math.Abs(chordOff),
                    $"solving against the terrain should stop nearer it: {terrainOff * 1000.0:F3} mm "
                    + $"against the chord's {chordOff * 1000.0:F3} mm");
    }

    /// <summary>
    /// And the pair the trace reports is matched for the first time: the radius the round says it
    /// stopped on is the radius that is actually under where it stopped.
    /// </summary>
    [Fact]
    public void TheRecordedSurfaceIsTheOneUnderTheLanding()
    {
        double3 landed = Fly(true, 2.0, out double recorded);

        Rolling truth = Ground(2.0);
        Assert.True(truth.TryGround(landed, out _, out double actually));

        Out.WriteLine($"recorded {recorded:F6} m, actually under it {actually:F6} m, "
                      + $"{(recorded - actually) * 1000.0:+0.000;-0.000;0.000} mm apart");

        Assert.Equal(actually, recorded, 3);
    }

    /// <summary>Flat ground has no curvature, so the chord is the surface and nothing may move.</summary>
    [Fact]
    public void OnFlatGroundItChangesNothing()
    {
        double3 chord = Fly(false, 0.0, out _);
        double3 terrain = Fly(true, 0.0, out _);

        Out.WriteLine($"flat ground: {Vec.Len(chord - terrain) * 1000.0:F6} mm apart");

        Assert.True(Vec.Len(chord - terrain) < 1e-6,
                    "with no curvature the chord and the terrain are the same thing");
    }
}
