using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which laser spot a director's spot tracker locks onto: one carrying its own code, made by somebody
/// else, within its set's range and where its head can look — the nearest of those.
///
/// <para>The spots are the ones stated last step, carried over this step by the velocity of what each
/// lies on, so every director reads every spot at one instant whatever order the heads update in.
/// Carried rather than held: the velocity includes the planet's ~29.8 km/s, and a held point is left
/// that far behind per second.</para>
/// </summary>
public static class SpotSearch
{
    /// <param name="canSee">Whether the head can look at a point and nothing is in the way: its travel
    /// and the ground, which only the game can answer.</param>
    public static bool TryChoose(IReadOnlyList<LaserSpot> spots, int code, object? ownDesignator,
                                 double3 eyeEcl, double maxRangeMetres, double dt, Func<double3, bool> canSee,
                                 out double3 spotEcl)
    {
        spotEcl = Vec.Zero;
        double best = double.PositiveInfinity;
        double step = double.IsFinite(dt) ? Math.Max(dt, 0.0) : 0.0;

        foreach (LaserSpot spot in spots)
        {
            if (spot.Code != code || ReferenceEquals(spot.Designator, ownDesignator)) continue;

            double3 at = spot.PositionEcl + (spot.VelocityEcl * step);
            if (!Vec.IsFinite(at)) continue;

            double range = Vec.Len(at - eyeEcl);
            if (!(range <= maxRangeMetres) || range >= best || !canSee(at)) continue;

            best = range;
            spotEcl = at;
        }

        return double.IsFinite(best);
    }
}
