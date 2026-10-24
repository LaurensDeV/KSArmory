using System;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How far this stack could still throw a warhead — the ring a map draws round the rocket.
///
/// <para><b>A ceiling, not a promise.</b> It prices the velocity still to be gained against the
/// propellant left and knows nothing of gravity and drag losses on the climb. A loss allowance would be
/// an unmeasured constant, and a floor built on a guess is worse than a ceiling that says what it is.</para>
///
/// <para>Priced with <see cref="BallisticArc.TryCheapest"/>, the same solver the computer flies,
/// so a target inside this ring is one the computer will at least attempt rather than refuse on
/// different arithmetic.</para>
/// </summary>
internal static class BoosterReach
{
    // Enough to put the answer inside a few kilometres on an Earth-sized body, which is finer
    // than a ring drawn on a planet map can show.
    private const int Steps = 18;

    // Half a turn is as far as a ballistic shot goes; past it the short way round is the other
    // side of the same arc.
    private const double MostRadians = Math.PI;

    // Below this there is no ring worth drawing, and the search has nothing to bracket.
    private const double LeastRadians = 1e-4;

    /// <summary>
    /// The furthest great-circle range affordable from here, or zero where nothing is.
    /// </summary>
    public static double CeilingMetres(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                       double deltaVMetresPerSecond, double minArrivalDeg)
    {
        if (!body.IsUsable) return 0.0;
        if (!(deltaVMetresPerSecond > 0.0) || !double.IsFinite(deltaVMetresPerSecond)) return 0.0;

        double3 up = Vec.Unit(fromCci);
        if (!Vec.IsFinite(up)) return 0.0;

        double3 along = Tangent(body, fromCci, fromVelocityCci, up);
        if (!Vec.IsFinite(along)) return 0.0;

        // Nothing to bracket if even the shortest shot is unaffordable, which is a stack with no
        // propellant rather than a geometry problem.
        if (!Affordable(body, fromCci, fromVelocityCci, up, along, LeastRadians,
                        deltaVMetresPerSecond, minArrivalDeg))
        {
            return 0.0;
        }

        if (Affordable(body, fromCci, fromVelocityCci, up, along, MostRadians,
                       deltaVMetresPerSecond, minArrivalDeg))
        {
            return MostRadians * body.SurfaceRadius;
        }

        double near = LeastRadians;
        double far = MostRadians;

        for (int i = 0; i < Steps; i++)
        {
            double mid = 0.5 * (near + far);

            if (Affordable(body, fromCci, fromVelocityCci, up, along, mid,
                           deltaVMetresPerSecond, minArrivalDeg))
            {
                near = mid;
            }
            else
            {
                far = mid;
            }
        }

        return near * body.SurfaceRadius;
    }

    // Downrange is whichever way the stack is already going, so a rocket under way is priced along
    // its own track rather than across it. Standing still there is no such direction, and any
    // bearing is as good as another on a sphere.
    private static double3 Tangent(BallisticBody body, double3 fromCci, double3 velocityCci,
                                   double3 up)
    {
        double3 moving = velocityCci - body.GroundVelocityCci(fromCci);
        double3 across = moving - (up * Vec.Dot(moving, up));

        if (Vec.Len(across) > 1.0) return Vec.Unit(across);

        double3 any = Math.Abs(up.Z) < 0.9 ? new double3(0, 0, 1) : new double3(1, 0, 0);
        return Vec.Unit(Vec.Cross(Vec.Cross(up, any), up));
    }

    private static bool Affordable(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                   double3 up, double3 along, double radians,
                                   double deltaV, double minArrivalDeg)
    {
        double3 aim = ((up * Math.Cos(radians)) + (along * Math.Sin(radians))) * body.SurfaceRadius;

        if (!BallisticArc.TryCheapest(body, fromCci, fromVelocityCci, aim,
                                      out BallisticArc.Solution solution,
                                      minArrivalDeg: minArrivalDeg))
        {
            return false;
        }

        double cost = Vec.Len(solution.VelocityToGain(fromVelocityCci));

        return double.IsFinite(cost) && cost <= deltaV;
    }
}
