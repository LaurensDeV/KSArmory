using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where the vehicle would come down if the engines stopped now — flown, not solved.
///
/// <para>The same choice <see cref="BombSight"/> makes, for the same reason and at a thousand times
/// the range: the arc is stepped rather than evaluated, so it can be flown against the real height
/// field rather than against the mean sphere the conic maths assumes. A shot aimed at a 5 km
/// plateau and predicted against sea level is long by the distance the arc covers falling those
/// 5 km, which at re-entry speeds is kilometres.</para>
///
/// <para><see cref="BallisticArc"/> answers what to aim for; this answers where the aim is
/// actually going. Keeping them separate is what lets the second contradict the first, which is
/// the whole value of having it — a guidance loop that predicts with the same maths it steers by
/// can only ever agree with itself.</para>
/// </summary>
internal static class ImpactPredictor
{
    /// <summary>Where it lands and what it is doing when it gets there.</summary>
    internal readonly record struct Impact(
        double3 PointCci,
        double3 GroundFixedPointCci,
        double3 VelocityCci,
        double Seconds);

    /// <summary>The point of stopping. A shot on a hyperbolic escape never comes down at all.</summary>
    public const double DefaultMaxSeconds = 6.0 * 3600.0;

    /// <param name="stepSeconds">The coarse step. Refined automatically at the crossing.</param>
    /// <param name="terrainRadiusAt">
    /// Surface radius under a <em>body-fixed</em> point, i.e. one with the planet's rotation already
    /// taken back out. Null flies against the mean sphere, which is the honest answer when no
    /// height field is reachable rather than a silent flat-Earth assumption.
    /// </param>
    /// <param name="pathCci">Optional, filled with the trajectory for drawing.</param>
    public static bool TryPredict(BallisticBody body, double3 positionCci, double3 velocityCci,
                                  double stepSeconds, double maxSeconds, out Impact impact,
                                  Func<double3, double>? terrainRadiusAt = null,
                                  List<double3>? pathCci = null)
    {
        impact = default;
        pathCci?.Clear();

        if (!body.IsUsable) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;
        if (!(stepSeconds > 0.0) || !(maxSeconds > 0.0)) return false;

        double3 r = positionCci;
        double3 v = velocityCci;
        double t = 0.0;
        double h = stepSeconds;

        pathCci?.Add(r);

        // Starting below the surface is a launch from inside the terrain sample, not an impact.
        // Climbing out of it is normal on the pad, so the first crossing is only believed once the
        // vehicle has been above ground at least once.
        bool everAboveGround = r.Length() > SurfaceUnder(body, r, t, terrainRadiusAt);

        while (t < maxSeconds)
        {
            Step(body, r, v, h, out double3 rNext, out double3 vNext);
            double tNext = t + h;

            if (!Vec.IsFinite(rNext) || !Vec.IsFinite(vNext)) return false;

            bool below = rNext.Length() <= SurfaceUnder(body, rNext, tNext, terrainRadiusAt);

            if (below && everAboveGround)
            {
                // Walk the step down rather than interpolating across it: near the ground the arc
                // is steep and fast, and a linear crossing on a 10 s step is kilometres out.
                if (h > 0.01)
                {
                    h *= 0.5;
                    continue;
                }

                impact = new Impact(rNext, body.UncarryCci(rNext, tNext), vNext, tNext);
                pathCci?.Add(rNext);
                return true;
            }

            if (!below) everAboveGround = true;

            r = rNext;
            v = vNext;
            t = tNext;
            pathCci?.Add(r);

            if (pathCci is { Count: > 4096 }) pathCci.RemoveAt(pathCci.Count - 1);
        }

        return false;
    }

    private static double SurfaceUnder(BallisticBody body, double3 pointCci, double seconds,
                                       Func<double3, double>? terrainRadiusAt)
    {
        if (terrainRadiusAt is null) return body.SurfaceRadius;
        double radius = terrainRadiusAt(body.UncarryCci(pointCci, seconds));
        return double.IsFinite(radius) && radius > 0.0 ? radius : body.SurfaceRadius;
    }

    // Classical fourth-order Runge-Kutta. Gravity is the only force: above the atmosphere that is
    // the whole of it, and the part of the fall where it is not is flown by the round itself once
    // the warheads are away.
    private static void Step(BallisticBody body, double3 r, double3 v, double h,
                             out double3 rNext, out double3 vNext)
    {
        double3 k1v = body.GravityCci(r);
        double3 k1r = v;

        double3 k2v = body.GravityCci(r + k1r * (h * 0.5));
        double3 k2r = v + k1v * (h * 0.5);

        double3 k3v = body.GravityCci(r + k2r * (h * 0.5));
        double3 k3r = v + k2v * (h * 0.5);

        double3 k4v = body.GravityCci(r + k3r * h);
        double3 k4r = v + k3v * h;

        rNext = r + (k1r + k2r * 2.0 + k3r * 2.0 + k4r) * (h / 6.0);
        vNext = v + (k1v + k2v * 2.0 + k3v * 2.0 + k4v) * (h / 6.0);
    }
}
