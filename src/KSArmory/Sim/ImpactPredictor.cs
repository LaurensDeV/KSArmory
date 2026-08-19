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

    /// <summary>
    /// How far under the surface the reported impact may sit.
    ///
    /// <para>Not a tolerance on the answer — a bias in it. The search accepts the first sample
    /// <em>below</em> the ground, so it always reports a point past the crossing, and always
    /// downrange: at 7 km/s on a shallow arc, stopping ten metres deep is tens of metres long. It
    /// is the entire floor under the miss distance once guidance is closing to centimetres a
    /// second, and it flatters nothing — it makes every shot look worse than it is.</para>
    /// </summary>
    public const double CrossingToleranceMetres = 0.25;

    // Where the bisection gives up, for an arc arriving too steeply to resolve.
    private const double MinRefineSeconds = 1e-6;

    /// <summary>
    /// The step used once there is air worth integrating.
    ///
    /// <para>The coarse step is chosen for a vacuum arc, where the acceleration barely changes
    /// across it. Entry is the opposite: the round sheds most of its speed in a few tens of
    /// seconds, and a step sized for the coast integrates straight through that.</para>
    /// </summary>
    public const double AtmosphericStepSeconds = 0.25;

    // Below this the drag term cannot move the answer, and paying for a fine step through the whole
    // coast to discover that is the one cost this has to avoid.
    private const double NoticeableDensity = 1e-7;

    /// <summary>
    /// The air, and what it does to the particular round being predicted.
    ///
    /// <para>Without it the prediction is a vacuum arc. That is right for the bus, which cuts off
    /// above the atmosphere, and wrong for the warheads it releases — and on a shallow arrival the
    /// difference is tens of kilometres, always short. <see cref="Medium.Drag"/> is the same call
    /// the round itself makes, deliberately: a prediction that models drag its own way is a second
    /// flight model to keep in step with the first.</para>
    /// </summary>
    /// <param name="DensityRatioAt">Air density at a point on the arc, relative to sea level.</param>
    /// <param name="Munition">The round whose <c>DragK</c> applies — the warhead, not the bus.</param>
    internal readonly record struct Drag(Func<double3, double> DensityRatioAt, MunitionProfile Munition);

    // How far a closed orbit's lowest point has to clear the mean sphere before it is called safe
    // without flying it. Terrain stands above that sphere, so the clearance has to be more than any
    // mountain rather than merely positive.
    private const double NeverComesDownMargin = 12_000.0;

    /// <param name="stepSeconds">The coarse step. Refined automatically at the crossing.</param>
    /// <param name="terrainRadiusAt">
    /// Surface radius under a <em>body-fixed</em> point, i.e. one with the planet's rotation already
    /// taken back out. Null flies against the mean sphere, which is the honest answer when no
    /// height field is reachable rather than a silent flat-Earth assumption.
    /// </param>
    /// <param name="pathCci">Optional, filled with the trajectory for drawing.</param>
    /// <param name="drag">The air. Null flies in vacuum, which is right only above the atmosphere.</param>
    public static bool TryPredict(BallisticBody body, double3 positionCci, double3 velocityCci,
                                  double stepSeconds, double maxSeconds, out Impact impact,
                                  Func<double3, double>? terrainRadiusAt = null,
                                  List<double3>? pathCci = null,
                                  Drag? drag = null)
    {
        impact = default;
        pathCci?.Clear();

        if (!body.IsUsable) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;
        if (!(stepSeconds > 0.0) || !(maxSeconds > 0.0)) return false;

        // A closed orbit whose lowest point clears the ground never arrives, and asking the conic
        // costs nothing. Flying it instead means integrating the whole horizon — ten thousand steps,
        // several times a second — to reach the same conclusion about a vehicle in a stable orbit,
        // which is exactly what a computer holding for a burn window is sitting in.
        double periapsis = Kepler.PeriapsisRadius(body.Mu, positionCci, velocityCci);
        if (double.IsFinite(periapsis) && periapsis > body.SurfaceRadius + NeverComesDownMargin)
        {
            return false;
        }

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
            if (DensityAt(body, r, drag) > NoticeableDensity) h = Math.Min(h, AtmosphericStepSeconds);

            Step(body, r, v, h, drag, out double3 rNext, out double3 vNext);
            double tNext = t + h;

            if (!Vec.IsFinite(rNext) || !Vec.IsFinite(vNext)) return false;

            bool below = rNext.Length() <= SurfaceUnder(body, rNext, tNext, terrainRadiusAt);

            if (below && everAboveGround)
            {
                // Walk the step down rather than interpolating across it: near the ground the arc
                // is steep and fast, and a linear crossing on a 10 s step is kilometres out. Each
                // halving retries from the same state, so this is a bisection on the arrival time
                // and it stops on how deep the answer is rather than on how small the step got —
                // which is the thing that actually matters and is scale-free across bodies.
                double depth = SurfaceUnder(body, rNext, tNext, terrainRadiusAt) - rNext.Length();

                if (depth > CrossingToleranceMetres && h > MinRefineSeconds)
                {
                    h *= 0.5;
                    continue;
                }

                impact = new Impact(rNext, body.UncarryCci(rNext, tNext), vNext, tNext);
                pathCci?.Add(rNext);
                return true;
            }

            if (below)
            {
                // Under the ground and not climbing out of it: there is no arc here to find, and
                // saying so now matters. Waiting for one costs the full horizon of integration —
                // ten thousand steps, several times a second, to answer a question about a vehicle
                // that is sitting on its pad. A launch site below the mean sphere is the ordinary
                // way into this, not an edge case.
                if (!everAboveGround && Vec.Dot(rNext, vNext) <= 0.0) return false;
            }
            else
            {
                everAboveGround = true;
            }

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

        // No terrain reaches up here, so the answer cannot change the only question asked of it -
        // whether the point is underground. The lookup is the expensive part of a prediction and
        // most of an arc is nowhere near the ground, so skipping it is what lets the remaining
        // samples be the accurate ones.
        if (pointCci.Length() > body.SurfaceRadius + NeverComesDownMargin) return body.SurfaceRadius;

        double radius = terrainRadiusAt(body.UncarryCci(pointCci, seconds));
        return double.IsFinite(radius) && radius > 0.0 ? radius : body.SurfaceRadius;
    }

    private static double DensityAt(BallisticBody body, double3 pointCci, Drag? drag)
    {
        if (drag is not { } air) return 0.0;

        double density = air.DensityRatioAt(pointCci);
        return double.IsFinite(density) && density > 0.0 ? density : 0.0;
    }

    // Airspeed is measured against the air, which turns with the body - the same frame a round's
    // own drag is measured in, and worth several hundred metres a second at the equator.
    private static double3 Accel(BallisticBody body, double3 r, double3 v, Drag? drag)
    {
        double3 accel = body.GravityCci(r);

        double density = DensityAt(body, r, drag);
        if (density <= 0.0 || drag is not { } air) return accel;

        return accel - Medium.Drag(v - body.GroundVelocityCci(r), air.Munition, density);
    }

    // Classical fourth-order Runge-Kutta.
    private static void Step(BallisticBody body, double3 r, double3 v, double h, Drag? drag,
                             out double3 rNext, out double3 vNext)
    {
        double3 k1v = Accel(body, r, v, drag);
        double3 k1r = v;

        double3 k2v = Accel(body, r + k1r * (h * 0.5), v + k1v * (h * 0.5), drag);
        double3 k2r = v + k1v * (h * 0.5);

        double3 k3v = Accel(body, r + k2r * (h * 0.5), v + k2v * (h * 0.5), drag);
        double3 k3r = v + k2v * (h * 0.5);

        double3 k4v = Accel(body, r + k3r * h, v + k3v * h, drag);
        double3 k4r = v + k3v * h;

        rNext = r + (k1r + k2r * 2.0 + k3r * 2.0 + k4r) * (h / 6.0);
        vNext = v + (k1v + k2v * 2.0 + k3v * 2.0 + k4v) * (h / 6.0);
    }
}
