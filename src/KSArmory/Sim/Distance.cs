namespace KSArmory;

/// <summary>
/// A distance as a reader wants it, which is not one unit across the range this shot has run.
///
/// <para><b>Every aim readout printed kilometres to one decimal</b>, chosen when a miss was
/// kilometres. The shot now lands at ten metres, where that has no resolution at all — anything
/// under 50 m prints as <c>0.0 km</c>. On 2026-09-07-1824 the log read <c>bias 0.0 km</c> on every
/// flight while the correction was closing 4 km down to 20 m, so the one number needed to say what
/// the correction converges to was the one number the instrument could not express.</para>
///
/// <para>The lesson is the same one the 250 m improvement band taught an hour earlier
/// (<c>docs/ACCURACY-PLAN.md</c> 3bx): <b>a constant sized for the shot as it was becomes blind as
/// the shot improves</b>, and a fixed unit is that in the readout rather than in the logic.</para>
/// </summary>
internal static class Distance
{
    /// <summary>Below this a distance is said in metres, above it in kilometres.</summary>
    public const double KilometreFrom = 1_000.0;

    /// <summary>
    /// One distance, in the unit that shows it.
    ///
    /// <para>Absent rather than zero for a value that is not a number: a readout is being used to
    /// tell a converged loop from a broken one, and printing an unreadable state as <c>0.0 m</c>
    /// is the failure this whole class exists to stop.</para>
    /// </summary>
    public static string Say(double metres)
    {
        if (!double.IsFinite(metres)) return "unknown";

        return Math.Abs(metres) < KilometreFrom ? $"{metres:F1} m" : $"{metres / 1000.0:F2} km";
    }
}
