using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Flight through a medium other than sea-level air: vacuum, thin atmosphere and water.
///
/// <para>The density ratio is a multiple of sea-level air, so one scale covers everything —
/// vacuum is 0, sea level is 1, and the ocean is roughly 840. A torpedo is therefore an ordinary
/// profile with a much smaller drag coefficient and a neutral density, not a special case in the
/// flight model.</para>
/// </summary>
public class MediumTests
{
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);
    private static readonly double3 Down = new(0, 0, -9.81);

    /// <summary>Sea water against sea-level air: 1028.13 / 1.225, KSA's own defaults.</summary>
    private const double Water = 1028.13 / 1.225;

    public enum Kind { GuidedMissile, KineticSlug }

    public static TheoryData<Kind> AllKinds => [Kind.GuidedMissile, Kind.KineticSlug];

    private static IProjectile Make(Kind kind, double3 velocity) => kind switch
    {
        Kind.GuidedMissile => new Interceptor(Vec.Zero, velocity, TargetHandle, 1, Vec.Zero, Vec.Zero) { Munition = BuiltIns.Missile57E6 },
        _ => new Slug(Vec.Zero, velocity, TargetHandle, 1, Vec.Zero, Vec.Zero) { Munition = BuiltIns.Cannon30Mm },
    };

    private static IProjectile Fly(Kind kind, MunitionProfile munition, double3 velocity,
                                   double3 gravity, double medium, double seconds)
    {
        IProjectile round = Make(kind, velocity);
        const double dt = 1.0 / 60.0;

        for (double t = 0.0; t < seconds && round.State == RoundState.Flying; t += dt)
        {
            round.Update(dt, null, gravity, Vec.Zero, Vec.Zero, munition, medium);
        }
        return round;
    }

    private static MunitionProfile Coasting(float dragK) => new()
    {
        Name = "test", DisplayName = "test",
        BoostSeconds = 0f, BoostAccel = 0f, DragK = dragK, MaxFlightSeconds = 60f,
    };

    // ---- Drag scales with the medium -------------------------------------

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void WaterScrubsFarMoreSpeedThanAir(Kind kind)
    {
        MunitionProfile munition = Coasting(1.0e-5f);

        double inAir = Fly(kind, munition, new double3(300, 0, 0), NoGravity, 1.0, 1.0).Speed;
        double inWater = Fly(kind, munition, new double3(300, 0, 0), NoGravity, Water, 1.0).Speed;

        Assert.True(inWater < inAir * 0.5,
            $"{kind}: water left {inWater:F0} m/s against air's {inAir:F0} m/s");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void VacuumScrubsNothing(Kind kind)
    {
        MunitionProfile munition = Coasting(3.0e-4f);

        double speed = Fly(kind, munition, new double3(300, 0, 0), NoGravity, 0.0, 2.0).Speed;

        Assert.Equal(300.0, speed, 6);
    }

    /// <summary>
    /// A torpedo is an ordinary profile: its coefficient is tuned for the medium it swims in, so
    /// it holds speed in water the way a missile holds speed in air.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ATorpedoTunedForWaterHoldsItsSpeedThere(Kind kind)
    {
        // 840x the medium, so ~840x less coefficient for the same deceleration.
        MunitionProfile torpedo = Coasting(1.0e-5f / (float)Water);

        double speed = Fly(kind, torpedo, new double3(80, 0, 0), NoGravity, Water, 2.0).Speed;

        Assert.InRange(speed, 79.0, 80.0);
    }

    // ---- Buoyancy ---------------------------------------------------------

    /// <summary>Off by default, so nothing that only ever flies through air changes.</summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void WithoutANeutralDensityGravityIsUntouched(Kind kind)
    {
        MunitionProfile munition = Coasting(0f);
        Assert.Equal(0f, munition.NeutralDensityRatio);

        IProjectile round = Fly(kind, munition, Vec.Zero, Down, 1.0, 1.0);

        // One second of free fall, whatever the medium says.
        Assert.InRange(round.VelocityEcl.Z, -9.9, -9.7);
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ARoundAtItsNeutralDensityNeitherSinksNorRises(Kind kind)
    {
        MunitionProfile torpedo = Coasting(0f);
        torpedo.NeutralDensityRatio = (float)Water;

        IProjectile round = Fly(kind, torpedo, Vec.Zero, Down, Water, 3.0);

        Assert.True(Math.Abs(round.VelocityEcl.Z) < 0.05,
            $"{kind}: a neutrally buoyant round reached {round.VelocityEcl.Z:F3} m/s vertically");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ARoundDenserThanItsMediumStillSinks(Kind kind)
    {
        MunitionProfile heavy = Coasting(0f);
        heavy.NeutralDensityRatio = (float)Water * 2f;      // twice as dense as the water

        IProjectile round = Fly(kind, heavy, Vec.Zero, Down, Water, 1.0);

        Assert.True(round.VelocityEcl.Z < -1.0, $"{kind}: it did not sink ({round.VelocityEcl.Z:F2} m/s)");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ARoundLighterThanItsMediumRises(Kind kind)
    {
        MunitionProfile buoyant = Coasting(0f);
        buoyant.NeutralDensityRatio = (float)Water / 2f;    // half as dense as the water

        IProjectile round = Fly(kind, buoyant, Vec.Zero, Down, Water, 1.0);

        Assert.True(round.VelocityEcl.Z > 1.0, $"{kind}: it did not rise ({round.VelocityEcl.Z:F2} m/s)");
    }

    /// <summary>
    /// The same torpedo falls normally through air and hangs in water, which is what makes an
    /// air-dropped torpedo behave sensibly on both sides of the surface.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ATorpedoFallsThroughAirAndHangsInWater(Kind kind)
    {
        MunitionProfile torpedo = Coasting(0f);
        torpedo.NeutralDensityRatio = (float)Water;

        double inAir = Fly(kind, torpedo, Vec.Zero, Down, 1.0, 1.0).VelocityEcl.Z;
        double submerged = Fly(kind, torpedo, Vec.Zero, Down, Water, 1.0).VelocityEcl.Z;

        Assert.InRange(inAir, -9.9, -9.7);
        Assert.True(Math.Abs(submerged) < 0.05);
    }
}
