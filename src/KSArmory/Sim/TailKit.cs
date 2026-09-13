using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Steering a falling store onto a place on the ground: where it would land if nothing steered
/// it, and the push square to its airflow that moves that point onto the designation.
/// </summary>
internal static class TailKit
{
    /// <param name="toAim">The aim point, from the round.</param>
    /// <param name="aimVelocityMinusRound">The aim point's velocity less the round's.</param>
    /// <param name="localVelocity">The round's velocity through the air, which the fins push square to.</param>
    /// <param name="dragAccel">What the air is doing to the round now, carried over the rest of the fall.</param>
    public static double3 Command(double3 toAim, double3 aimVelocityMinusRound, double3 localVelocity,
                                  double3 gravity, double3 dragAccel, MunitionProfile munition)
    {
        double g = Vec.Len(gravity);
        if (!(g > 1e-6) || !Vec.IsFinite(toAim) || !Vec.IsFinite(aimVelocityMinusRound)) return Vec.Zero;

        double3 up = gravity / -g;
        double3 p = -toAim;
        double3 u = -aimVelocityMinusRound;
        double3 a0 = gravity + (Vec.IsFinite(dragAccel) ? dragAccel : Vec.Zero);

        double h = Vec.Dot(p, up);
        if (h <= 0.0) return Vec.Zero;

        double vz = Vec.Dot(u, up);
        double az = Vec.Dot(a0, up);
        double disc = (vz * vz) - (2.0 * az * h);
        if (disc < 0.0) return Vec.Zero;

        double denom = -vz + Math.Sqrt(disc);
        if (denom <= 1e-9) return Vec.Zero;

        double t = 2.0 * h / denom;
        double3 miss = p + (u * t) + (a0 * (0.5 * t * t));
        double3 arriving = u + (a0 * t);
        double arrivingUp = Vec.Dot(arriving, up);
        if (arrivingUp >= -1e-6) return Vec.Zero;

        double3 wanted = miss * (-munition.NavConstant / (t * t));

        double3 along = Vec.Unit(localVelocity);
        double3 e1 = Vec.AnyPerpendicular(along.Equals(Vec.Zero) ? up : along);
        double3 e2 = Vec.Unit(Vec.Cross(along.Equals(Vec.Zero) ? up : along, e1));

        double3 m1 = Moves(e1);
        double3 m2 = Moves(e2);

        double a11 = Vec.Dot(m1, m1);
        double a12 = Vec.Dot(m1, m2);
        double a22 = Vec.Dot(m2, m2);
        double reg = (1e-6 * (a11 + a22)) + 1e-12;
        a11 += reg;
        a22 += reg;

        double det = (a11 * a22) - (a12 * a12);
        if (!(det > 0.0)) return Vec.Zero;

        double b1 = Vec.Dot(m1, wanted);
        double b2 = Vec.Dot(m2, wanted);
        double x1 = ((b1 * a22) - (b2 * a12)) / det;
        double x2 = ((a11 * b2) - (a12 * b1)) / det;

        double3 command = (e1 * x1) + (e2 * x2);
        return Vec.IsFinite(command) ? Vec.ClampLength(command, munition.MaxLateralAccel) : Vec.Zero;

        // Where a push moves the landing: sideways by itself, and along the arrival for however
        // much it lengthens or shortens the fall.
        double3 Moves(double3 push) => push - (arriving * (Vec.Dot(push, up) / arrivingUp));
    }
}
