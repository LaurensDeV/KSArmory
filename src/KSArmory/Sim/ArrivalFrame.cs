using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The axes an arrival is worth measuring in: local up under the impact, the ground track the round
/// comes in along, and the one square to both.
///
/// <para>A displacement along <see cref="Up"/> reaches the ground multiplied by <c>cot γ</c>, eight at a
/// 7° arrival, where one across the track goes through one to one; <c>docs/ARRIVAL-ANGLE.md</c>.</para>
///
/// <para><b>A vertical arrival has no track</b>, and this refuses rather than picking a perpendicular
/// that would flip as the geometry creeps past it, as <c>TerrainMap.MapFrame.TryAt</c> does at the poles.</para>
/// </summary>
internal readonly record struct ArrivalFrame(double3 Up, double3 Downrange, double3 Cross)
{
    /// <summary>
    /// The least sine of the angle off the local vertical for which the track means anything: about
    /// 0.06°, a guard against a divide rather than a policy.
    /// </summary>
    public const double LeastHorizontal = 1e-3;

    /// <param name="impactPointCci">Where the round comes down, from the body's centre.</param>
    /// <param name="impactVelocityCci">What it is doing when it gets there.</param>
    public static bool TryAt(double3 impactPointCci, double3 impactVelocityCci, out ArrivalFrame frame)
    {
        frame = default;

        if (!Vec.IsFinite(impactPointCci) || !Vec.IsFinite(impactVelocityCci)) return false;
        if (Vec.Len(impactPointCci) <= 0.0) return false;

        double3 up = Vec.Unit(impactPointCci);

        double speed = Vec.Len(impactVelocityCci);
        if (speed <= 0.0) return false;

        double3 horizontal = impactVelocityCci - up * Vec.Dot(impactVelocityCci, up);
        if (Vec.Len(horizontal) / speed < LeastHorizontal) return false;

        double3 along = Vec.Unit(horizontal);

        frame = new ArrivalFrame(up, along, Vec.Cross(up, along));
        return true;
    }

    /// <summary>A vector in the body's inertial frame, as its components (up, downrange, across).</summary>
    public double3 Resolve(double3 vectorCci)
        => new(Vec.Dot(vectorCci, Up), Vec.Dot(vectorCci, Downrange), Vec.Dot(vectorCci, Cross));

    /// <summary>The arrival's angle below the local horizontal, in degrees.</summary>
    public double BelowHorizontalDegrees(double3 impactVelocityCci)
    {
        double speed = Vec.Len(impactVelocityCci);
        if (speed <= 0.0) return double.NaN;

        double sine = -Vec.Dot(impactVelocityCci, Up) / speed;
        return Math.Asin(Math.Clamp(sine, -1.0, 1.0)) * 180.0 / Math.PI;
    }
}
