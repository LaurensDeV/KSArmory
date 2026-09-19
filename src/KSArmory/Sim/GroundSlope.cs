using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How steep the ground is around a point, and what a round arriving on it pays for that.
///
/// <para>The terrain adds no bias — the aim point's placement and the round's stopping surface are
/// an exact round trip through one field — but it <em>multiplies</em>. A residual miss `d` lands on
/// ground `slope·d` higher or lower, which the arrival angle turns back into `slope·d / tan γ` of
/// further miss, so the loop's gain is <c>slope / tan γ</c>. **At gain one there is no fixed point
/// at all**: where the round stops is decided by the ground rather than by the shot, and re-aiming
/// walks away from the answer. <c>docs/KINETIC-FLOOR.md</c> section 5.</para>
///
/// <para>This exists because that regime turned out not to be hypothetical. Measured over 1,273
/// warheads at the flown site — 26.485S, 68.148W, in the Andes — the local slope runs to a median
/// of 0.152 and a 90th percentile of 1.092, which at the flown 32° arrival puts a quarter of
/// rockets past gain one, where their groups blow up from about 7 mm to 20–28 mm
/// (<c>docs/ACCURACY-PLAN.md</c> 3ej). **A site has to be judged before a night is spent on it**,
/// and until now the only instrument for that was flying the night.</para>
/// </summary>
public static class GroundSlope
{
    /// <summary>Past this the ground decides where the round stops, and re-aiming does not converge.</summary>
    public const double GainWithNoFixedPoint = 1.0;

    /// <summary>What the ground around a point is, and what it costs a round coming in at an angle.</summary>
    /// <param name="Median">
    /// The magnitude of the ground's gradient — the slope of the plane through the samples, which is
    /// the same number whichever way that plane is tilted.
    ///
    /// <para><b>Not the middle of a ring of spokes</b>, which is the obvious implementation and is
    /// wrong: a spoke across the contour of a tilted plane reads zero, so the median of a ring
    /// <em>halves</em> a planar slope — 0.150 for a plane tilted 0.300 — and the ground at a
    /// warhead's own scale is a tilted plane (<c>docs/KINETIC-FLOOR.md</c> section 4). That would
    /// understate every gain by about two, and the gain is the whole question.</para>
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
    /// <para><b>The radius is the question, not a detail.</b> A height field answers differently at
    /// different scales — on Earth the base grid is 1.5–3.1 km, the detail textures 7–20 m a texel,
    /// and below about a third of a metre the engine's `float3` direction packing makes the surface
    /// a staircase (<c>docs/KSA-TERRAIN.md</c>). So ask at the scale the miss actually spans, and
    /// ask at more than one if that scale is not known.</para>
    ///
    /// <para>The median rather than the mean, because a single spoke crossing a gully is not what
    /// the ground there is, and the mean lets it decide.</para>
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

        // And a ring besides, for what a gradient cannot say: whether the ground is merely tilted
        // or actually rough. A plane reads the same on both; a gully reads far worse on this one.
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
