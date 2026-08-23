using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Where a round stops when the ground under it is not the sphere the frame cached.
///
/// <para><see cref="IGroundTest"/> answers with a sphere and promises it is the surface "over the
/// few metres of ground track a falling round covers in one frame". A re-entry body covers about
/// 120 m of it at 7 km/s, and across that the real field departs from the sphere — so the sphere
/// misplaces the crossing, and can report one that never happened.</para>
///
/// <para>Both directions are here because they fail in opposite ways: terrain falling away under
/// the round makes the sphere burst it in the air, terrain rising under it makes the sphere bury
/// it. Only the second was measured in flight — 19.6 m below its own surface — and a test for the
/// one that was seen would pass against a round that still gets the other wrong.</para>
/// </summary>
public class GroundCrossingTests
{
    private const double PlanetRadius = 6_371_000.0;
    private const double Dt = 1.0 / 60.0;

    private static readonly double3 Centre = double3.Zero;

    /// <summary>
    /// Ground that slopes along the round's track: the surface radius changes with the
    /// along-track coordinate, so a sphere taken at one point is wrong everywhere else.
    /// </summary>
    private sealed class Slope(double metresPerMetre) : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Centre;
            surfaceRadius = PlanetRadius + metresPerMetre * positionEcl.Y;
            return true;
        }

        public double SurfaceUnder(double3 positionEcl) => PlanetRadius + metresPerMetre * positionEcl.Y;
    }

    private static MunitionProfile Warhead() => new()
    {
        Name = "TESTRV",
        DisplayName = "test re-entry body",
        Guidance = GuidanceMode.None,
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 120f,
        DragK = 0f,
        FuseRadius = 0f,
        ChargeKg = 300f,
        HitsTerrain = true,
    };

    /// <summary>A shallow arrival, which is what makes a metre of height eight metres of ground.</summary>
    private static Slug Arriving(IGroundTest ground, double altitude, double speed = 7000.0)
    {
        const double gammaDeg = 7.1;
        double gamma = double.DegreesToRadians(gammaDeg);

        double3 start = new(PlanetRadius + altitude, 0, 0);
        double3 velocity = new(-speed * Math.Sin(gamma), speed * Math.Cos(gamma), 0);

        return new Slug(start, velocity, null, 1, start, Vec.Zero)
        {
            Munition = Warhead(),
            Ground = ground,
        };
    }

    private static void Fly(Slug round, int frames = 4000)
    {
        for (int i = 0; i < frames && round.State == RoundState.Flying; i++)
        {
            double3 gravity = Vec.Unit(Centre - round.PositionEcl) * 9.81;
            round.Update(Dt, null, gravity, Vec.Zero, Vec.Zero, round.Munition);
        }
    }

    /// <summary>
    /// The measured fault. Ground rising under the round is higher than the sphere sampled behind
    /// it, so the sphere lets the round through terrain that is really there and it ends up buried.
    /// In flight that was 19.6 m, which at this arrival angle is about 157 m of ground.
    /// </summary>
    [Fact]
    public void ARoundOnRisingGroundStopsOnTheSurfaceRatherThanUnderIt()
    {
        Slope ground = new(0.06);
        Slug round = Arriving(ground, altitude: 400.0);

        Fly(round);

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.True(round.HitGround);

        double depth = ground.SurfaceUnder(round.PositionEcl) - Vec.Len(round.PositionEcl - Centre);
        Assert.True(depth < 1.0, $"buried {depth:F1} m below the ground it should have burst on");
    }

    /// <summary>
    /// The other direction, which no flight has shown and which the same sphere causes. Ground
    /// falling away is lower than the sphere sampled behind it, so the sphere reports a crossing
    /// into terrain that is not there and the round bursts in mid-air above it.
    /// </summary>
    [Fact]
    public void ARoundOnFallingGroundDoesNotBurstInTheAirAboveIt()
    {
        Slope ground = new(-0.06);
        Slug round = Arriving(ground, altitude: 400.0);

        Fly(round);

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.True(round.HitGround);

        double height = Vec.Len(round.PositionEcl - Centre) - ground.SurfaceUnder(round.PositionEcl);
        Assert.True(height < 1.0, $"burst {height:F1} m in the air above the ground");
    }

    /// <summary>
    /// The round and its predictor must stop on the same rule, because the two disagreeing about
    /// where the ground is <em>is</em> the miss. Flat ground is the case where the sphere is exact,
    /// so nothing here should move — a refinement that shifts this has changed the common case.
    /// </summary>
    [Fact]
    public void FlatGroundIsUnchanged()
    {
        Slope ground = new(0.0);
        Slug round = Arriving(ground, altitude: 400.0);

        Fly(round);

        Assert.Equal(RoundState.Detonated, round.State);

        double offset = Vec.Len(round.PositionEcl - Centre) - PlanetRadius;
        Assert.True(Math.Abs(offset) < 0.5, $"burst {offset:F2} m off a flat surface");
    }
}
