using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A designation that arrives after the store has left the rack.
///
/// <para>These fly the round the way <c>WeaponSystem</c> does, reading each step's target back
/// off the round's own aimpoint, so a retarget that fails to move the handle shows up as a store
/// still falling ballistically.</para>
/// </summary>
public class PostReleaseAimTests(ITestOutputHelper Out)
{
    private const double Dt = 1.0 / 60.0;
    private const double PlanetRadius = 6_371_000.0;

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = double3.Zero;
            surfaceRadius = PlanetRadius;
            return true;
        }
    }

    private static MunitionProfile Kit() => new()
    {
        Name = "TESTKIT",
        DisplayName = "test tail kit",
        Guidance = GuidanceMode.Inertial,
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 600f,
        DragK = 1.25e-4f,
        FuseRadius = 0f,
        ChargeKg = 250f,
        HitsTerrain = true,
        NavConstant = 3f,
        MaxLateralG = 0.4f,
        GravityCompensation = 0f,
    };

    private static double3 GravityAt(double3 p) => Vec.Unit(double3.Zero - p) * 9.80665;

    private static TailKitReach Reach(MunitionProfile kit, double3 at, double3 velocity)
        => TailKitReach.Fly(at, velocity, Vec.Zero, Vec.Zero, Vec.Zero, _ => Vec.Zero, kit,
                            GravityAt, _ => 1.0, new Ball(), 0.05, []);

    // Drops from `height`, and `sendAfter` seconds in, retargets onto `aim`. A negative sendAfter
    // never retargets. Returns where it stopped, and the reach measured at the moment it was sent.
    private static double3 Drop(MunitionProfile kit, double height, double sendAfter, double3 aim,
                                out TailKitReach whenSent)
    {
        double3 start = new(PlanetRadius + height, 0, 0);
        Slug bomb = new(start, Vec.Zero, null, 1, start, Vec.Zero) { Munition = kit, Ground = new Ball() };

        whenSent = default;
        bool sent = sendAfter < 0.0;

        for (int i = 0; i < 60_000 && bomb.State == RoundState.Flying; i++)
        {
            if (!sent && bomb.Age >= sendAfter)
            {
                whenSent = Reach(kit, bomb.PositionEcl, bomb.VelocityEcl);
                bomb.Retarget(Aimpoint.AtPoint(aim));
                sent = true;
            }

            // Exactly what SampleTarget hands a round aimed at a fixed point.
            TargetState? target = bomb.Aimpoint.Kind == AimpointKind.None
                                      ? null
                                      : bomb.Aimpoint.ToTargetState();

            bomb.Update(Dt, target, GravityAt(bomb.PositionEcl), Vec.Zero, start, kit);
        }

        return bomb.PositionEcl;
    }

    private static double3 Offset(double3 ballistic, double metres)
        => Vec.Unit(ballistic + new double3(0, metres, 0)) * PlanetRadius;

    /// <summary>A store sent somewhere inside the region gets there.</summary>
    [Fact]
    public void AStoreSentSomewhereInsideTheRegionArrives()
    {
        MunitionProfile kit = Kit();
        double3 ballistic = Drop(kit, 5000.0, -1.0, Vec.Zero, out _);

        double3 aim = Offset(ballistic, 600.0);
        double3 landed = Drop(kit, 5000.0, 5.0, aim, out TailKitReach reach);

        Out.WriteLine($"sent 5 s in at 600 m: region {reach.RadiusMetres:F0} m, "
                      + $"landed {Vec.Len(landed - aim):F1} m from it");

        Assert.True(reach.Covers(aim), $"600 m should be inside a {reach.RadiusMetres:F0} m region");
        Assert.True(Vec.Len(landed - aim) < 25.0,
                    $"it should arrive, missed by {Vec.Len(landed - aim):F1} m");
    }

    /// <summary>The control: with nothing sent it lands where it was thrown.</summary>
    [Fact]
    public void WithNothingSentItLandsWhereItWasThrown()
    {
        MunitionProfile kit = Kit();
        double3 ballistic = Drop(kit, 5000.0, -1.0, Vec.Zero, out _);
        double3 aim = Offset(ballistic, 600.0);

        Assert.True(Vec.Len(Drop(kit, 5000.0, -1.0, Vec.Zero, out _) - aim) > 550.0,
                    "an unsent store should still be about 600 m from the aim");
    }

    /// <summary>
    /// Sent too late it falls short, and the region says so before it happens. This is the case
    /// that reads as the weapon being broken, which is why the region is drawn at all.
    /// </summary>
    [Fact]
    public void SentTooLateItFallsShortAndTheRegionSaidSo()
    {
        MunitionProfile kit = Kit();
        double3 ballistic = Drop(kit, 5000.0, -1.0, Vec.Zero, out _);
        double3 aim = Offset(ballistic, 600.0);

        double3 landed = Drop(kit, 5000.0, 30.0, aim, out TailKitReach reach);
        double missed = Vec.Len(landed - aim);

        Out.WriteLine($"sent 30 s in at 600 m: {reach.SecondsToGo:F1} s left, region "
                      + $"{reach.RadiusMetres:F0} m, landed {missed:F1} m from it");

        Assert.False(reach.Covers(aim), "600 m should be outside the region this late");
        Assert.True(reach.ShortfallFrom(aim) > 0.0, "and the shortfall should be reported");
        Assert.True(missed > 25.0, $"so it should fall short, and it missed by {missed:F1} m");
    }

    /// <summary>
    /// A place outside the region is still taken: the store steers at it and lands nearer than it
    /// would have. Reporting rather than refusing is the whole rule in <c>WeaponSystem.Designate</c>.
    /// </summary>
    [Fact]
    public void APlaceOutsideTheRegionIsStillTakenAndStillHelps()
    {
        MunitionProfile kit = Kit();
        double3 ballistic = Drop(kit, 5000.0, -1.0, Vec.Zero, out _);

        // Well past anything the kit can reach from five kilometres.
        double3 aim = Offset(ballistic, 6000.0);

        double3 landed = Drop(kit, 5000.0, 2.0, aim, out TailKitReach reach);
        double steered = Vec.Len(landed - aim);
        double unsteered = Vec.Len(ballistic - aim);

        Out.WriteLine($"6 km out: region {reach.RadiusMetres:F0} m, steered to {steered:F0} m "
                      + $"against {unsteered:F0} m unsteered");

        Assert.False(reach.Covers(aim));
        Assert.True(steered < unsteered - 500.0,
                    $"steering at it should close the gap: {steered:F0} m against {unsteered:F0} m");
    }
}
