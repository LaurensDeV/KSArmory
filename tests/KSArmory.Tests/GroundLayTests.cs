using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The 5"/54 laid onto a place on flat ground by the flown solve, and flown there as a real
/// <see cref="Slug"/> until it comes down.
///
/// <para>The line of sight is the harness's own evidence: laid along it, a shell from a mount a few
/// metres up is in the ground long before it reaches anything kilometres away. And a world that turns
/// is the second: flown against a frame that coasts, the shell falls under a gravity the ground does
/// not feel and lands long.</para>
/// </summary>
public class GroundLayTests(ITestOutputHelper output)
{
    private const double Frame = 1.0 / 60.0;
    private const double SurfaceGravity = 9.80665;
    private const double Radius = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    // The mount at the origin, a few metres above the ground, with local up along +Z.
    private const double MountHeight = 4.0;
    private static readonly double3 Centre = new(0, 0, -(Radius + MountHeight));

    private static MunitionProfile Shell => Arsenal.Shell5In54;

    // The same shell with no air to fly through and no fuse, for the world that turns.
    private static MunitionProfile Airless() => new()
    {
        Name = "GroundLayAirless",
        DisplayName = "airless five-inch",
        LaunchSpeed = Shell.LaunchSpeed,
        DragK = 0f,
        NeutralDensityRatio = 0f,
        MaxFlightSeconds = 40f,
        FuseRadius = 0f,
        FuseArmSeconds = 0f,
        ChargeKg = Shell.ChargeKg,
    };

    private static double3 GravityAt(double3 p)
    {
        double3 toCentre = Centre - p;
        double r = Vec.Len(toCentre);
        return toCentre * (SurfaceGravity * Radius * Radius / (r * r * r));
    }

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p - Centre) - Radius) / ScaleHeight);

    // The ground a distance along the surface from under the mount.
    private static double3 Ground(double km)
    {
        double angle = km * 1000.0 / Radius;
        return Centre + (new double3(Math.Sin(angle), 0, Math.Cos(angle)) * Radius);
    }

    private static double SurfaceKm(double3 p)
    {
        double3 fromCentre = p - Centre;
        return Math.Atan2(fromCentre.X, fromCentre.Z) * Radius / 1000.0;
    }

    // Fires a shell along a direction with its proximity fuse facing nothing, and flies it a frame at a
    // time until it crosses the ground. Where and when it came down, or null if it never did.
    private static (double3 Landed, double Seconds)? FlyToGround(double3 direction, double3 launchVelocity = default,
                                                                 MunitionProfile? munition = null)
    {
        MunitionProfile shell = munition ?? Shell;
        var slug = new Slug(Vec.Zero, launchVelocity + (Vec.Unit(direction) * shell.LaunchSpeed), null, -1,
                            Vec.Zero, Vec.Zero)
        {
            Munition = shell,
            GravityAt = (p, _) => GravityAt(p),
            AirDensityAt = (p, _) => shell.DragK > 0f ? DensityAt(p) : 0.0,
        };

        var nowhere = new TargetState(new double3(0, 0, 1.0e9), Vec.Zero, 0.0);
        double3 before = slug.PositionEcl;

        for (double t = 0.0; slug.State == RoundState.Flying && t < 40.0; t += Frame)
        {
            slug.Update(Frame, nowhere, GravityAt(slug.PositionEcl), Vec.Zero, Vec.Zero, shell,
                        shell.DragK > 0f ? DensityAt(slug.PositionEcl) : 0.0);

            double3 after = slug.PositionEcl;
            double above = Vec.Len(after - Centre) - Radius;
            if (above <= 0.0)
            {
                double was = Vec.Len(before - Centre) - Radius;
                double f = was / (was - above);
                return (before + ((after - before) * f), t + (Frame * f));
            }

            before = after;
        }

        return null;
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(8.0)]
    [InlineData(15.0)]
    public void AShellLaidOnTheFlownSolveLandsOnTheGround(double km)
    {
        double3 ground = Ground(km);

        Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, ground, Vec.Zero, Vec.Zero,
                                                null, Shell, GravityAt, DensityAt, Vec.Zero,
                                                out double3 lay, out double flight));
        Assert.True(flight < Shell.MaxFlightSeconds, $"{flight:F1} s is past the shell's {Shell.MaxFlightSeconds} s");

        var landed = FlyToGround(lay);
        Assert.True(landed.HasValue, "the shell never came down");

        double miss = Vec.Len(landed!.Value.Landed - ground);
        double elevation = double.RadiansToDegrees(Math.Asin(Vec.Unit(lay).Z));
        output.WriteLine($"{km,4:F1} km: laid {elevation:F2} deg, {flight:F1} s, landed {miss:F1} m from the point");

        Assert.True(miss < Shell.LethalRadius, $"landed {miss:F1} m from the point");
    }

    [Fact]
    public void LaidAlongTheLineOfSightTheShellLandsFarShort()
    {
        var landed = FlyToGround(Ground(8.0));

        Assert.True(landed.HasValue, "the shell never came down");
        Assert.True(SurfaceKm(landed!.Value.Landed) < 2.0,
                    $"came down {SurfaceKm(landed.Value.Landed):F2} km out, which the line of sight should not reach");
    }

    /// <summary>
    /// On a world that turns, the ground under the mount is accelerating, and a lay that lets its frame
    /// coast puts the shell under a gravity the ground does not feel. Spun fast here, half a metre a second
    /// squared at the mount, so a coasting frame misses by hundreds of metres; flown inertially, with the
    /// ground turning under the shell.
    /// </summary>
    [Fact]
    public void OnATurningWorldTheShellLandsWhereTheGroundHasGone()
    {
        double3 spin = new(0, Math.Sqrt(0.5 / Radius), 0);
        double3 VelocityOf(double3 p) => Vec.Cross(spin, p - Centre);
        double3 AccelerationOf(double3 p) => Vec.Cross(spin, Vec.Cross(spin, p - Centre));

        double3 ground = Ground(8.0);
        MunitionProfile shell = Airless();

        bool Lay(double3 frameAcceleration, double3 targetAcceleration, out double3 lay)
            => BallisticLead.TrySolveFlown(Vec.Zero, VelocityOf(Vec.Zero), VelocityOf(Vec.Zero), frameAcceleration,
                                           ground, VelocityOf(ground), targetAcceleration, null, shell,
                                           GravityAt, _ => 0.0, Vec.Zero, out lay, out _);

        double MissFrom(double3 lay)
        {
            var landed = FlyToGround(lay, VelocityOf(Vec.Zero), shell);
            Assert.True(landed.HasValue, "the shell never came down");

            // Where the ground has turned to by then, about the spin axis through the centre.
            double a = spin.Y * landed!.Value.Seconds;
            double3 r = ground - Centre;
            double3 moved = Centre + new double3((r.X * Math.Cos(a)) + (r.Z * Math.Sin(a)), r.Y,
                                                 (-r.X * Math.Sin(a)) + (r.Z * Math.Cos(a)));
            return Vec.Len(landed.Value.Landed - moved);
        }

        Assert.True(Lay(AccelerationOf(Vec.Zero), AccelerationOf(ground), out double3 turning));
        Assert.True(Lay(Vec.Zero, Vec.Zero, out double3 coasting));

        double turned = MissFrom(turning);
        double coasted = MissFrom(coasting);
        output.WriteLine($"turning frame {turned:F1} m, coasting frame {coasted:F1} m");

        Assert.True(turned < 10.0, $"landed {turned:F1} m from where the ground had gone");
        Assert.True(coasted > 100.0, $"a coasting frame landed only {coasted:F1} m off, so this proves nothing");
    }

    /// <summary>
    /// An acceleration shared by the ground, the target and the gravity field is the frame's, and cannot
    /// reach the lay: it cancels out of everything the round and the target feel against the ground.
    /// </summary>
    [Fact]
    public void AnAccelerationSharedByTheGroundTheTargetAndGravityDoesNotMoveTheLay()
    {
        double3 ground = Ground(8.0);
        double3 common = new(0.3, -0.2, 0.4);

        Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, ground, Vec.Zero, Vec.Zero,
                                                null, Shell, GravityAt, DensityAt, Vec.Zero,
                                                out double3 still, out double stillFlight));
        Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, common, ground, Vec.Zero, common,
                                                null, Shell, p => GravityAt(p) + common, DensityAt, Vec.Zero,
                                                out double3 shared, out double sharedFlight));

        Assert.True(Vec.AngleBetween(still, shared) < 1e-6, $"{Vec.AngleBetween(still, shared)} rad apart");
        Assert.Equal(stillFlight, sharedFlight, 3);
    }
}
