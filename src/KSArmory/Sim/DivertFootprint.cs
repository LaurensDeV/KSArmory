using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How far one bus can move its own landing, as an ellipse on the ground.
///
/// <para>Every later target is a place the bus can divert to with the propellant it has left, and
/// <see cref="ReleaseFocus.FlownSensitivity"/> already flies the columns that say how far a metre a
/// second of release velocity moves the landing — once per salvo, for the separation kicks. This is
/// those same columns read as a region rather than as a correction, so the display costs no flying.</para>
///
/// <para><b>The long axis belongs to the arrival angle, not to the range.</b> It runs about
/// <c>450 · cot γ</c> metres per m/s at the release epoch, so it is 25:1 at 9°, 3:1 at 20° and 2:1 at
/// 33°. A typed shape would be wrong at every geometry but one.</para>
///
/// <para><b>Measured against the arrival the flight pins, it is smaller and rounder than that.</b> These
/// columns leave the arrival <em>clock</em> free, which is the minimum-norm solve
/// <see cref="ReleaseFocus"/> does; <c>IcbmProgram</c> latches the arrival and solves every later arc to
/// it, and that third condition costs 2.56x along the track at 6,179 km and 11.94x at 12,902, while
/// costing exactly nothing across it. So <see cref="SemiMajorMetresPerMetrePerSecond"/> is the free-clock
/// best case unless a release loop re-commits the arrival per target.
/// <c>docs/MIRV-TARGETS.md</c>.</para>
/// </summary>
/// <param name="OrientationRad">
/// Which way the long axis points, from the frame's downrange. Nearly always zero — the major axis sits
/// within a tenth of a degree of the ground track at every geometry measured — and computed anyway,
/// because "nearly always" is how an assumption gets shipped.
/// </param>
/// <param name="FromTheRealState">
/// Whether the columns came from the bus's own state or from a projected cutoff. A footprint drawn from
/// the pad is an estimate whose long axis was out by 1.04x, 0.51x and 0.59x at three flown geometries,
/// because <c>cot γ</c> belongs to the arc actually flown and the pad does not know it yet. The short
/// axis is exact from anywhere, being <c>0.96 · t_go</c> on a time the player already set.
/// </param>
internal readonly record struct DivertFootprint(double SemiMajorMetresPerMetrePerSecond,
                                                double SemiMinorMetresPerMetrePerSecond,
                                                double OrientationRad,
                                                ArrivalFrame Frame,
                                                double FlightSeconds,
                                                bool FromTheRealState)
{
    /// <summary>
    /// Read the ellipse off the columns a salvo has already flown.
    /// </summary>
    /// <remarks>
    /// The arrival frame is taken against the ground-relative velocity, as <c>ReleaseFocus.TryCancel</c>
    /// does: the semi-axes are the same either way, because both frames span one horizontal plane, but
    /// the orientation and the arrival angle are not — inertially they read 5.5° and a degree out.
    /// </remarks>
    public static bool TryFrom(BallisticBody body, ReleaseFocus.FlownSensitivity flown, bool fromTheRealState,
                               out DivertFootprint footprint)
    {
        footprint = default;

        double3 overGround = flown.ArrivalVelocityCci - body.GroundVelocityCci(flown.ArrivedCci);
        if (!ArrivalFrame.TryAt(flown.ArrivedCci, overGround, out ArrivalFrame frame)) return false;

        // The 2x3 map from release velocity to ground displacement, as its two rows.
        double3[] columns = [flown.VelocityX, flown.VelocityY, flown.VelocityZ];
        double a0 = Vec.Dot(columns[0], frame.Downrange), a1 = Vec.Dot(columns[1], frame.Downrange),
               a2 = Vec.Dot(columns[2], frame.Downrange);
        double c0 = Vec.Dot(columns[0], frame.Cross), c1 = Vec.Dot(columns[1], frame.Cross),
               c2 = Vec.Dot(columns[2], frame.Cross);

        // The singular values of that map are the square roots of the eigenvalues of its row Gram,
        // which is 2x2 and closed form -- no sweep, and nothing to converge.
        double m11 = (a0 * a0) + (a1 * a1) + (a2 * a2);
        double m12 = (a0 * c0) + (a1 * c1) + (a2 * c2);
        double m22 = (c0 * c0) + (c1 * c1) + (c2 * c2);

        double trace = m11 + m22;
        double gap = Math.Sqrt(Math.Max(0.0, ((trace * trace) / 4.0) - ((m11 * m22) - (m12 * m12))));

        double major = Math.Sqrt(Math.Max(0.0, (trace / 2.0) + gap));
        double minor = Math.Sqrt(Math.Max(0.0, (trace / 2.0) - gap));

        if (!(major > 0.0) || !double.IsFinite(major) || !double.IsFinite(minor)) return false;

        footprint = new DivertFootprint(major, minor, 0.5 * Math.Atan2(2.0 * m12, m11 - m22),
                                        frame, flown.FlightSeconds, fromTheRealState);
        return true;
    }

    /// <summary>What a wanted ground displacement costs, in metres a second of divert.</summary>
    /// <remarks>
    /// The inverse of the ellipse: a displacement resolved onto the axes, each divided by that axis's
    /// reach. This is what a cursor shows as "3 targets, 12 m/s left".
    /// </remarks>
    public double CostMetresPerSecond(double alongMetres, double crossMetres)
    {
        double cos = Math.Cos(OrientationRad), sin = Math.Sin(OrientationRad);

        double onMajor = (alongMetres * cos) + (crossMetres * sin);
        double onMinor = (crossMetres * cos) - (alongMetres * sin);

        double major = onMajor / SemiMajorMetresPerMetrePerSecond;
        double minor = SemiMinorMetresPerMetrePerSecond > 0.0 ? onMinor / SemiMinorMetresPerMetrePerSecond
                                                              : (onMinor == 0.0 ? 0.0 : double.PositiveInfinity);

        return Math.Sqrt((major * major) + (minor * minor));
    }

    /// <summary>Whether a displacement is inside what the stated budget can pay for.</summary>
    public bool Reaches(double alongMetres, double crossMetres, double budgetMetresPerSecond)
        => CostMetresPerSecond(alongMetres, crossMetres) <= budgetMetresPerSecond;
}
