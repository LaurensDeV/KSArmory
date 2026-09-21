using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What step a falling store asks the world to be held to, and what a coarse one costs it.
///
/// <para>Both step fields default to <see cref="Interceptor.MaxFaithfulStep"/>, which bounds how
/// far a round may move before it steps over its own <see cref="MunitionProfile.FuseRadius"/>. A
/// store with no proximity fuse is not bounded by that at all, and leaving it there asked
/// <see cref="WarpPolicy"/> to hold the world at about 19x for a fall lasting minutes — which ends
/// in the store being abandoned.</para>
/// </summary>
public class StoreWarpStepTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 p, out double3 c, out double r)
        {
            c = double3.Zero;
            r = R;
            return true;
        }
    }

    private static double Density(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8000.0);
    private static double3 Grav(double3 p) => Vec.Unit(double3.Zero - p) * 9.80665;

    private static MunitionProfile Store(float maxStep) => new()
    {
        Name = "TESTSTORE",
        DisplayName = "test store",
        Guidance = GuidanceMode.Inertial,
        LaunchSpeed = 4f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 300f,
        DragK = 1.25e-4f,
        FuseRadius = 0f,
        ChargeKg = 300_000f,
        HitsTerrain = true,
        NavConstant = 3f,
        MaxLateralG = 0.4f,
        MaxFaithfulStepSeconds = maxStep,
    };

    private static double3 Fall(double frameSeconds, out double age)
    {
        MunitionProfile store = Store((float)frameSeconds);
        double3 start = new(R + 250_000.0, 0, 0);

        Slug bomb = new(start, Vec.Zero, null, 1, start, Vec.Zero)
        {
            Munition = store,
            Ground = new Ball(),
            AirDensityAt = (p, _) => Density(p),
            GravityAt = (p, _) => Grav(p),
        };

        for (int i = 0; i < 200_000 && bomb.State == RoundState.Flying; i++)
        {
            bomb.Update(frameSeconds, null, Grav(bomb.PositionEcl), Vec.Zero, start, store,
                        Density(bomb.PositionEcl));
        }

        age = bomb.Age;
        return bomb.PositionEcl;
    }

    /// <summary>
    /// A long frame costs the fall nothing, because the round sub-steps at its own
    /// <see cref="MunitionProfile.SubStep"/> whatever the frame is — and
    /// <see cref="MunitionProfile.MaxSubSteps"/> is derived so the two stay consistent.
    ///
    /// <para>This is what makes the shipped value a fidelity question rather than a gamble: the
    /// store is allowed to take the world's long steps because taking them changes nothing.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(5.0)]
    [InlineData(8.0)]
    [InlineData(20.0)]
    public void ALongFrameCostsTheFallNothing(double frameSeconds)
    {
        double3 reference = Fall(0.32, out double referenceAge);
        double3 coarse = Fall(frameSeconds, out double coarseAge);

        double moved = Vec.Len(coarse - reference);
        Out.WriteLine($"{frameSeconds,5} s frames: landed {moved:F2} m from the 0.32 s reference, "
                      + $"age {coarseAge:F1} s against {referenceAge:F1} s");

        Assert.True(moved < 5.0, $"the landing moved {moved:F1} m");
        Assert.True(Math.Abs(coarseAge - referenceAge) < 0.5,
                    $"the fall took {coarseAge:F1} s against {referenceAge:F1} s");
    }

    /// <summary>
    /// The shipped store asks for a step it can actually use, rather than the fuse default.
    ///
    /// <para>Pinned against <see cref="Interceptor.MaxFaithfulStep"/> rather than against a number,
    /// because what is wrong is inheriting the interceptor's, not any particular value.</para>
    /// </summary>
    [Fact]
    public void TheShippedStoreDoesNotAskForAProximityFusesStep()
    {
        MunitionProfile b61 = Arsenal.NukeB61;

        Assert.Equal(0f, b61.FuseRadius);
        Assert.True(b61.MaxFaithfulStepSeconds > Interceptor.MaxFaithfulStep,
                    $"a store with no proximity fuse should not integrate at the fuse step: "
                    + $"{b61.MaxFaithfulStepSeconds} s");
        Assert.True(b61.PreferredStep > Interceptor.MaxFaithfulStep,
                    $"nor hold the world to it: {b61.PreferredStep} s");
    }

    /// <summary>
    /// Which rounds survive a world the mod cannot keep up with, as the classification the rule
    /// reads: a round the ground stops has no target to be flown through, so it is kept and lags.
    ///
    /// <para><c>WeaponSystem.AbandonFlight</c> is under <c>Ksa/</c> and out of reach here, so what
    /// this pins is the half that decides — that the shipped stores and shells are terrain-stopped
    /// and the guided missiles are not. A missile quietly gaining <c>HitsTerrain</c> would start
    /// surviving a warp it cannot be steered through.</para>
    /// </summary>
    [Fact]
    public void OnlyRoundsTheGroundStopsSurviveAWorldThatOutranThem()
    {
        Assert.True(Arsenal.NukeB61.HitsTerrain);
        Assert.True(Arsenal.ReentryVehicleMk21.HitsTerrain);
        Assert.True(Arsenal.Shell5In54.HitsTerrain);

        Assert.False(Arsenal.Missile9J.HitsTerrain);
        Assert.False(Arsenal.Missile120C.HitsTerrain);
        Assert.False(Arsenal.MissileAgm88.HitsTerrain);
        Assert.False(Arsenal.Cannon20Mm.HitsTerrain);
    }

    /// <summary>
    /// And the world is still held down once there is air, which is where the fall is decided.
    /// <see cref="Slug.FaithfulStepSeconds"/> does that per round rather than per profile, so a
    /// long coast and a fine entry come out of the same weapon with nothing to configure.
    /// </summary>
    [Fact]
    public void TheWorldIsStillHeldDownOnceThereIsAir()
    {
        MunitionProfile store = Store(8f);
        double3 high = new(R + 250_000.0, 0, 0);
        double3 low = new(R + 2_000.0, 0, 0);

        Slug coasting = new(high, Vec.Zero, null, 1, high, Vec.Zero) { Munition = store, Ground = new Ball() };
        coasting.Update(1.0, null, Grav(high), Vec.Zero, high, store, Density(high));

        Slug entering = new(low, Vec.Zero, null, 1, low, Vec.Zero) { Munition = store, Ground = new Ball() };
        entering.Update(0.05, null, Grav(low), Vec.Zero, low, store, Density(low));

        Out.WriteLine($"coasting asks {coasting.FaithfulStepSeconds:F3} s, "
                      + $"entering asks {entering.FaithfulStepSeconds:F3} s");

        Assert.Equal(store.PreferredStep, coasting.FaithfulStepSeconds, 6);
        Assert.Equal(Medium.FaithfulStepInAir, entering.FaithfulStepSeconds, 6);
    }
}
