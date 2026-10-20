using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a search set's head looks, in its part's frame: at the contact its set holds, kept inside
/// the head's travel, or else sweeping its field. <b>Drawn only</b> — the set sees its whole cone
/// whichever way the head points.
/// </summary>
public static class SearchHead
{
    /// <summary>The command, unit length and inside the travel.</summary>
    /// <param name="toContactPartFrame">From the head's pivot to what the set holds, or null.</param>
    /// <param name="scanSeconds">The set's own clock, phase included.</param>
    public static double3 Command(SearchSetProfile profile, double3? toContactPartFrame, double scanSeconds)
    {
        if (toContactPartFrame is { } toContact && Vec.Len2(toContact) > 1e-12)
        {
            return ClampToTravel(toContact, profile.HeadTravelDeg);
        }

        return ClampToTravel(Scan(profile, scanSeconds), profile.HeadTravelDeg);
    }

    /// <summary>
    /// A side-to-side sweep about the part's +Y with a nod at twice its rate, so the head traces a
    /// figure of eight rather than a line.
    /// </summary>
    public static double3 Scan(SearchSetProfile profile, double seconds)
    {
        double period = Math.Max(0.5, profile.ScanPeriodSeconds);
        double phase = 2.0 * Math.PI * seconds / period;

        double az = double.DegreesToRadians(profile.ScanHalfWidthDeg) * Math.Sin(phase);
        double el = double.DegreesToRadians(profile.ScanNodDeg) * Math.Sin(2.0 * phase);

        return new double3(Math.Sin(el), Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az));
    }

    /// <summary>A direction pulled onto the cone of this half-angle about +Y if it lies outside it.</summary>
    public static double3 ClampToTravel(double3 aim, double travelDeg)
    {
        double3 dir = Vec.Unit(aim);
        if (!Vec.IsFinite(dir) || Vec.Len2(dir) < 0.5) return OpticGeometry.RestDirection;

        double limit = double.DegreesToRadians(Math.Clamp(travelDeg, 0.0, 179.0));
        double off = Vec.AngleBetween(OpticGeometry.RestDirection, dir);
        if (off <= limit) return dir;

        // Keep the bearing round +Y; straight behind has none, so any will do.
        double3 across = Vec.RejectFrom(dir, OpticGeometry.RestDirection);
        across = Vec.Len2(across) < 1e-12 ? Vec.AnyPerpendicular(OpticGeometry.RestDirection) : Vec.Unit(across);

        return Vec.Unit(OpticGeometry.RestDirection * Math.Cos(limit) + across * Math.Sin(limit));
    }

    /// <summary>
    /// Where in its sweep a set starts, so several on one hull do not sweep in step: a share of the
    /// period taken from a seed the caller derives from the part.
    /// </summary>
    public static double PhaseSeconds(SearchSetProfile profile, int seed)
    {
        double share = (uint)(seed * 2654435761u) / (double)uint.MaxValue;
        return share * Math.Max(0.5, profile.ScanPeriodSeconds);
    }
}
