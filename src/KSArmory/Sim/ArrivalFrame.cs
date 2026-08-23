using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The axes an arrival is worth measuring in: local up under the impact, the ground track the round
/// comes in along, and the one square to both.
///
/// <para>Every term in a ballistic error budget lands somewhere in these three and they are not
/// interchangeable. A displacement along <see cref="Up"/> is multiplied by <c>cot γ</c> before it
/// reaches the ground — eight at a 7° arrival — while the same displacement across the track is
/// carried through at one to one. Reporting a drift as a length says nothing about what it costs;
/// resolving it here says everything. <c>docs/ARRIVAL-ANGLE.md</c> is why that ratio is the whole
/// lever.</para>
///
/// <para><b>A vertical arrival has no track</b>, and this refuses rather than picking a
/// perpendicular. Same shape as <c>TerrainMap.MapFrame.TryAt</c> at the poles and
/// <see cref="Vec.PerpendicularTo"/> everywhere else in this mod: an axis nobody can name is not one
/// to invent, because the invented one flips as the geometry creeps past it.</para>
/// </summary>
internal readonly record struct ArrivalFrame(double3 Up, double3 Downrange, double3 Cross)
{
    /// <summary>
    /// How square to vertical an arrival has to be before the track means anything, as the sine of
    /// the angle off the local vertical.
    ///
    /// <para>A thousandth is about 0.06°, which on the shallowest arc this mod flies is far below
    /// anything a measurement resolves. It is a guard against a divide, not a policy.</para>
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

    /// <summary>
    /// A vector in the body's inertial frame, as its components up, downrange and across.
    ///
    /// <para>Returned in that order as one <c>double3</c> because they are read together — the point
    /// of resolving is the comparison between them, and three separate outputs invite a caller to
    /// take one and forget which of the others it was large against.</para>
    /// </summary>
    public double3 Resolve(double3 vectorCci)
        => new(Vec.Dot(vectorCci, Up), Vec.Dot(vectorCci, Downrange), Vec.Dot(vectorCci, Cross));

    /// <summary>How far off the local vertical the arrival came in, in degrees below the horizontal.</summary>
    public double BelowHorizontalDegrees(double3 impactVelocityCci)
    {
        double speed = Vec.Len(impactVelocityCci);
        if (speed <= 0.0) return double.NaN;

        double sine = -Vec.Dot(impactVelocityCci, Up) / speed;
        return Math.Asin(Math.Clamp(sine, -1.0, 1.0)) * 180.0 / Math.PI;
    }
}
