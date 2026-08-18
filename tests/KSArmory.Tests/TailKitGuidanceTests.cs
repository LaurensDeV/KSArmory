using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A guided tail kit steers a fall. It is the one steering round that is a <see cref="Slug"/>
/// rather than an <see cref="Interceptor"/> — no motor, the same ballistics and drag the bomb
/// sight flies, with a few g of fin authority added.
///
/// <para>What these pin is the pair of things that would be silently wrong: that the command is
/// differenced against the target's own motion rather than read off the round's ecliptic velocity,
/// and that a round without the mode still falls exactly as it did before.</para>
/// </summary>
public class TailKitGuidanceTests
{
    private const double Dt = 1.0 / 60.0;
    private const double PlanetRadius = 6_371_000.0;
    private static readonly double3 Centre = double3.Zero;

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Centre;
            surfaceRadius = PlanetRadius;
            return true;
        }
    }

    private static MunitionProfile Bomb(GuidanceMode guidance) => new()
    {
        Name = "TESTKIT",
        DisplayName = "test tail kit",
        Guidance = guidance,
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 120f,
        DragK = 0f,
        FuseRadius = 0f,
        ChargeKg = 250f,
        HitsTerrain = true,
        NavConstant = 3f,
        MaxLateralG = 3f,
        GravityCompensation = 0f,
    };

    private static double3 OnSurface(double3 direction) => Vec.Unit(direction) * PlanetRadius;

    /// <summary>Gravity toward the centre, which is all a falling store needs.</summary>
    private static double3 GravityAt(double3 positionEcl)
        => Vec.Unit(Centre - positionEcl) * 9.80665;

    private static double3 Drop(MunitionProfile munition, double3 aimEcl, double3 commonVelocity,
                                IGroundTest? ground, int maxFrames = 6000,
                                Func<double3, double3>? gravityAt = null)
    {
        gravityAt ??= GravityAt;
        double3 start = new(PlanetRadius + 2000.0, 0, 0);
        Slug bomb = new(start, commonVelocity, null, 1, start, commonVelocity)
        {
            Munition = munition,
            Ground = ground,
        };

        for (int i = 0; i < maxFrames && bomb.State == RoundState.Flying; i++)
        {
            // The aimpoint moves with the frame, exactly as a resampled ground anchor does — and
            // sampled at the END of the step, which is the instant SampleTarget reads the world
            // at. The round back-dates it from there.
            TargetState target = new(aimEcl + commonVelocity * ((i + 1) * Dt), commonVelocity, 0.0);
            bomb.Update(Dt, target, gravityAt(bomb.PositionEcl), commonVelocity, start, munition);
        }

        return bomb.PositionEcl;
    }

    [Fact]
    public void ATailKitSteersOntoAPointTheFallWouldHaveMissed()
    {
        double3 aim = OnSurface(new double3(PlanetRadius, 300.0, 0));

        double3 guided = Drop(Bomb(GuidanceMode.Inertial), aim, double3.Zero, new Ball());
        double3 unguided = Drop(Bomb(GuidanceMode.None), aim, double3.Zero, new Ball());

        double guidedMiss = Vec.Len(guided - aim);
        double unguidedMiss = Vec.Len(unguided - aim);

        Assert.True(unguidedMiss > 250.0,
                    $"the unguided fall should miss by about the 300 m offset, missed by {unguidedMiss:F1} m");
        Assert.True(guidedMiss < 25.0,
                    $"the tail kit should arrive on the designation, missed by {guidedMiss:F1} m");
    }

    /// <summary>
    /// The command is differenced against the target's own motion. A ground anchor carries the
    /// planet's ~29.8 km/s plus its spin, so steering on the round's ecliptic velocity alone reads
    /// that whole frame as closing speed and pulls full lateral g across it.
    ///
    /// <para>Everything here is comoving, so the answer must not depend on the frame at all. Two
    /// things are held flat to keep the comparison about the steering and nothing else: the ground
    /// is left out, being fixed in the ecliptic and so not carried with the rest, and gravity is a
    /// constant rather than read off position — the moving run drifts 596 km down-track in twenty
    /// seconds, which a central field would answer completely differently.</para>
    /// </summary>
    [Fact]
    public void TheCommandIsDifferencedAgainstTheAimpointsOwnMotion()
    {
        double3 aim = OnSurface(new double3(PlanetRadius, 300.0, 0));
        double3 carrier = new(0, 29_800.0, 0);
        double3 flat = new(-9.80665, 0, 0);

        double3 still = Drop(Bomb(GuidanceMode.Inertial), aim, double3.Zero, null, 1200, _ => flat);
        double3 moving = Drop(Bomb(GuidanceMode.Inertial), aim, carrier, null, 1200, _ => flat);

        // Both measured against their own aimpoint, so only the round's own flight is compared.
        double3 stillOffset = still - aim;
        double3 movingOffset = moving - (aim + carrier * (1200 * Dt));

        Assert.True(Vec.Len(stillOffset - movingOffset) < 1.0,
                    $"the frame leaked into the steering: {Vec.Len(stillOffset - movingOffset):F1} m apart");
    }

    /// <summary>
    /// A store without the mode is deaf to its aimpoint: it falls on ballistics alone, whatever it
    /// was released at. Steering shares <see cref="Slug"/> with every dumb bomb and shell in the
    /// arsenal, so the mode is the only thing separating them.
    /// </summary>
    [Fact]
    public void AStoreWithoutATailKitIsUnmovedByAnAimpoint()
    {
        double3 aim = OnSurface(new double3(PlanetRadius, 300.0, 0));

        double3 aimed = Drop(Bomb(GuidanceMode.None), aim, double3.Zero, new Ball());
        double3 ignored = Drop(Bomb(GuidanceMode.None), OnSurface(new double3(PlanetRadius, -900.0, 0)),
                               double3.Zero, new Ball());

        Assert.True(Vec.Len(aimed - ignored) < 1e-6,
                    "an unguided store went somewhere different for a different aimpoint");
    }

    /// <summary>
    /// Fin authority, not a motor. The profile's limit is what separates a bomb that nudges its
    /// fall from one that flies, and it is the number a tuning slider will reach.
    /// </summary>
    [Fact]
    public void AuthorityIsBoundedByTheProfile()
    {
        MunitionProfile munition = Bomb(GuidanceMode.Inertial);
        double3 r = new(-2000.0, 4000.0, 0);
        double3 v = new(-200.0, 0, 0);

        double3 command = Interceptor.GuidanceAccel(r, v, v, GravityAt(new double3(PlanetRadius, 0, 0)),
                                                    munition);

        Assert.True(Vec.Len(command) <= munition.MaxLateralAccel + 1e-9,
                    $"commanded {Vec.Len(command):F1} m/s^2 against a {munition.MaxLateralAccel:F1} limit");
    }
}
