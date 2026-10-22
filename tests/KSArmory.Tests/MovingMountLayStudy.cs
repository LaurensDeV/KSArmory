using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

public class MovingMountLayStudy(ITestOutputHelper output)
{
    private const double SurfaceGravity = 9.80665;
    private const double Radius = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;
    private static readonly double3 Centre = new(0, 0, -(Radius + 4.0));

    private static double3 GravityAt(double3 p)
    {
        double3 toCentre = Centre - p;
        double r = Vec.Len(toCentre);
        return toCentre * (SurfaceGravity * Radius * Radius / (r * r * r));
    }

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p - Centre) - Radius) / ScaleHeight);

    private static double3 Ground(double km, double sideKm = 0.0)
    {
        double angle = km * 1000.0 / Radius;
        double side = sideKm * 1000.0 / Radius;
        return Centre + (Vec.Unit(new double3(Math.Sin(angle), Math.Sin(side), Math.Cos(angle))) * Radius);
    }

    private (int shortFrames, int flips, double reachMin, double reachMax) Fly(bool mountMoves, bool targetMoves, double km)
    {
        double latitude = double.DegreesToRadians(28.6);
        double3 spin = new double3(0, Math.Cos(latitude), Math.Sin(latitude)) * 7.2921e-5;
        double3 VelocityOf(double3 p) => Vec.Cross(spin, p - Centre);
        double3 AccelerationOf(double3 p) => Vec.Cross(spin, Vec.Cross(spin, p - Centre));

        Random noise = new(11);
        double3 hint = Vec.Zero;
        double reach = 1.0, lo = 1.0, hi = 0.0;
        int shortFrames = 0, flips = 0;
        bool wasShort = false;
        const double dt = 1.0 / 60.0;

        for (int frame = 0; frame < 600; frame++)
        {
            double t = frame * dt;
            double heave = 2.0 * Math.PI / 8.0;
            double3 muzzle = mountMoves ? new double3(13.0 * t, 0, 2.0 + (3.0 * Math.Sin(heave * t))) : new double3(0, 0, 2.0);
            double3 mountVelocity = VelocityOf(muzzle) + (mountMoves ? new double3(13.0, 0, 3.0 * heave * Math.Cos(heave * t)) : Vec.Zero);

            double3 place = Ground(km, targetMoves ? 0.005 * t : 0.0);
            double3 targetVelocity = VelocityOf(place);
            double3 targetAcceleration = AccelerationOf(place);
            if (targetMoves)
            {
                targetVelocity += new double3(0, 5.0 + (noise.NextDouble() - 0.5), (noise.NextDouble() - 0.5) * 0.5);
                targetAcceleration += new double3((noise.NextDouble() - 0.5) * 4.0, (noise.NextDouble() - 0.5) * 4.0,
                                                  (noise.NextDouble() - 0.5) * 4.0);
            }

            BallisticLead.TrySolveFlownOrReach(muzzle, mountVelocity, VelocityOf(muzzle), AccelerationOf(muzzle), Vec.Zero,
                                               place, targetVelocity, targetAcceleration, null, BuiltIns.Shell5In54,
                                               GravityAt, DensityAt, hint, reach, out double3 lay, out _, out reach,
                                               null, VelocityOf);
            bool isShort = reach < 1.0;
            if (isShort) shortFrames++;
            if (frame > 0 && isShort != wasShort) flips++;
            wasShort = isShort;
            if (isShort) { lo = Math.Min(lo, reach); hi = Math.Max(hi, reach); }
            hint = lay - muzzle;
        }

        return (shortFrames, flips, lo, hi);
    }

    [Trait("kind", "study")]
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Study(bool mountMoves, bool targetMoves)
    {
        foreach (double km in new[] { 10.0, 20.0 })
        {
            var r = Fly(mountMoves, targetMoves, km);
            output.WriteLine($"mount {(mountMoves ? "moving" : "still")}, target {(targetMoves ? "rolling" : "still")}, {km} km: "
                             + $"{r.shortFrames} of 600 frames laid short, {r.flips} flips"
                             + (r.shortFrames > 0 ? $", reach {r.reachMin:P1}-{r.reachMax:P1}" : ""));
        }
    }
}
