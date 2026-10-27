using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How steep the ground is around a point, and what a round arriving on it pays for that.
///
/// <para>The terrain adds no bias but <em>multiplies</em>: a residual miss <c>d</c> lands on ground
/// <c>slope·d</c> higher or lower, so the loop's gain is <c>slope / tan γ</c>, and <b>at gain one there
/// is no fixed point at all</b> (<c>docs/KINETIC-FLOOR.md</c> section 5). Over 1,273 warheads in the
/// Andes a quarter were past it (<c>docs/ACCURACY-PLAN.md</c> 3ej); this judges a site without flying.</para>
/// </summary>
public static class GroundSlope
{
    /// <summary>Past this the ground decides where the round stops, and re-aiming does not converge.</summary>
    public const double GainWithNoFixedPoint = 1.0;

    /// <summary>What the ground around a point is, and what it costs a round coming in at an angle.</summary>
    /// <param name="Median">
    /// The magnitude of the ground's gradient, whichever way it is tilted. <b>Not the median of a ring
    /// of spokes</b>, despite the name: that halves a planar slope, 0.150 for a plane tilted 0.300
    /// (<c>docs/KINETIC-FLOOR.md</c> section 4).
    /// </param>
    /// <param name="Worst">The steepest spoke — roughness, which the gradient alone cannot show.</param>
    /// <param name="Gain">`Median / tan γ`, the loop gain a residual miss is multiplied by each pass.</param>
    /// <param name="Amplification">
    /// `1 / (1 - Gain)`, the total a miss ends up at — or infinity at or past gain one, which is a
    /// real answer rather than a failure: there is no fixed point to converge on.
    /// </param>
    public readonly record struct Reading(double Median, double Worst, double Gain, double Amplification)
    {
        /// <summary>Whether re-aiming on this ground converges at all.</summary>
        public bool HasAFixedPoint => Gain < GainWithNoFixedPoint;

        /// <summary>One line for a log, in the terms an operator can act on.</summary>
        public string Describe()
            => HasAFixedPoint
                   ? $"slope {Median:F3} (worst {Worst:F3}), gain {Gain:F2}, so a miss ends up "
                     + $"{Amplification:F2}x what it would be on the flat"
                   : $"slope {Median:F3} (worst {Worst:F3}), gain {Gain:F2} -- PAST ONE, so the ground "
                     + "decides where a round stops and re-aiming does not converge";
    }

    /// <summary>
    /// The ground around <paramref name="frame"/>'s anchor, sampled on a ring of
    /// <paramref name="spokes"/> directions at <paramref name="metres"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The radius is the question, not a detail</b>: a height field answers differently at each
    /// scale (<c>docs/KSA-TERRAIN.md</c>), so ask at the scale the miss spans.</para>
    /// </remarks>
    public static bool TryAround(MapFrame frame, Func<double3, double> radiusAt, double metres,
                                 int spokes, double arrivalRadians, out Reading reading)
    {
        reading = default;

        if (radiusAt is null || !(metres > 0.0) || spokes < 2) return false;
        if (!(arrivalRadians > 0.0) || arrivalRadians >= Math.PI / 2.0) return false;

        double here = radiusAt(frame.DirectionAt(0.0, 0.0));
        if (!double.IsFinite(here) || here <= 0.0) return false;

        // The gradient, centred so a constant tilt cancels out of it exactly.
        double east = radiusAt(frame.DirectionAt(+metres, 0.0));
        double west = radiusAt(frame.DirectionAt(-metres, 0.0));
        double north = radiusAt(frame.DirectionAt(0.0, +metres));
        double south = radiusAt(frame.DirectionAt(0.0, -metres));

        if (!double.IsFinite(east) || !double.IsFinite(west)
            || !double.IsFinite(north) || !double.IsFinite(south))
        {
            return false;
        }

        double dx = (east - west) / (2.0 * metres);
        double dy = (north - south) / (2.0 * metres);
        double slope = Math.Sqrt(dx * dx + dy * dy);

        // The ring says what a gradient cannot: whether the ground is tilted or rough.
        double worst = 0.0;
        for (int i = 0; i < spokes; i++)
        {
            double a = 2.0 * Math.PI * i / spokes;
            double there = radiusAt(frame.DirectionAt(Math.Cos(a) * metres, Math.Sin(a) * metres));
            if (!double.IsFinite(there) || there <= 0.0) continue;

            worst = Math.Max(worst, Math.Abs(there - here) / metres);
        }

        double gain = slope / Math.Tan(arrivalRadians);

        reading = new Reading(slope, worst, gain,
                              gain < GainWithNoFixedPoint ? 1.0 / (1.0 - gain) : double.PositiveInfinity);
        return true;
    }
}
