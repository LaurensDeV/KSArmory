using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The 5"/54 on a world the size of Mars, in its thin air and under its weak pull: where a shell stays up
/// longer than its clock, and where the straight line to a place far out runs kilometres under the ground
/// on the way there.
/// </summary>
public class ReachAlongTheGroundTests(ITestOutputHelper output)
{
    private const double Frame = 1.0 / 60.0;
    private const double Radius = 3_389_500.0;
    private const double SurfaceGravity = 3.72;
    private const double ScaleHeight = 11_000.0;
    private const double AirRatio = 0.02 / 1.225;

    // The mount at the origin, a few metres above the ground, with local up along +Z.
    private const double MountHeight = 4.0;
    private static readonly double3 Centre = new(0, 0, -(Radius + MountHeight));

    private static MunitionProfile Shell => Arsenal.Shell5In54;

    private static double3 GravityAt(double3 p)
    {
        double3 toCentre = Centre - p;
        double r = Vec.Len(toCentre);
        return toCentre * (SurfaceGravity * Radius * Radius / (r * r * r));
    }

    private static double DensityAt(double3 p)
        => AirRatio * Math.Exp(-Math.Max(0.0, Vec.Len(p - Centre) - Radius) / ScaleHeight);

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

    // Fires a shell along a direction and flies it a frame at a time until it crosses the ground, as the gun
    // fires it: second order, and on a path that can only land, so no clock ends it first.
    private static (double3 Landed, double Seconds)? FlyToGround(double3 direction)
    {
        var slug = new Slug(Vec.Zero, Vec.Unit(direction) * Shell.LaunchSpeed, null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            SecondOrder = true,
            GravityAt = (p, _) => GravityAt(p),
            AirDensityAt = (p, _) => DensityAt(p),
            ApproachAt = (_, _) => Approach.Landing,
        };

        var nowhere = new TargetState(new double3(0, 0, 1.0e9), Vec.Zero, 0.0);
        double3 before = slug.PositionEcl;

        for (double t = 0.0; slug.State == RoundState.Flying && t < 1_000.0; t += Frame)
        {
            slug.Update(Frame, nowhere, GravityAt(slug.PositionEcl), Vec.Zero, Vec.Zero, Shell, DensityAt(slug.PositionEcl));

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

    // The farthest any elevation throws the shell, and how long that flight takes.
    private static double LongestReachKm(out double seconds)
    {
        double best = 0.0;
        seconds = 0.0;

        for (double deg = 30.0; deg <= 54.0; deg += 2.0)
        {
            double rad = double.DegreesToRadians(deg);
            if (FlyToGround(new double3(Math.Cos(rad), 0, Math.Sin(rad))) is not { } flown) continue;

            double km = SurfaceKm(flown.Landed);
            if (km <= best) continue;

            best = km;
            seconds = flown.Seconds;
        }

        return best;
    }

    /// <summary>
    /// A shell that stays up longer than its clock is still laid to land. The clock is a fallback for a path
    /// nobody can judge, and the flown lead searched only within it, which from Mars ended the gun's reach at
    /// 90 km.
    /// </summary>
    [Fact]
    public void AShellThatStaysUpLongerThanItsClockIsStillLaidToLand()
    {
        double best = LongestReachKm(out double bestSeconds);
        Assert.True(bestSeconds > Shell.MaxFlightSeconds,
                    $"the longest reach, {best:F1} km, took {bestSeconds:F0} s, inside the clock, so this proves nothing");

        double3 place = Ground(0.9 * best);
        Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, place, Vec.Zero, Vec.Zero,
                                                null, Shell, GravityAt, DensityAt, Vec.Zero, out double3 lay, out double flight),
                    $"no lay onto {0.9 * best:F1} km, where {best:F1} km is reachable");

        var landed = FlyToGround(lay);
        Assert.True(landed.HasValue, "the shell never came down");

        double miss = Vec.Len(landed!.Value.Landed - place);
        output.WriteLine($"longest reach {best:F1} km in {bestSeconds:F0} s; laid onto {0.9 * best:F1} km, {flight:F0} s, "
                         + $"landed {miss:F1} m from it");

        Assert.True(miss < Shell.LethalRadius, $"landed {miss:F1} m from the place");
    }
}
