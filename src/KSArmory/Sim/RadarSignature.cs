namespace KSArmory;

/// <summary>
/// How large a contact looks to a radar, and how far that lets the set see it.
///
/// <para>The first thing in this mod that makes detection depend on what a target <em>is</em>
/// rather than only on where it is going. It is also the substrate chaff needs: a cloud that
/// returns more than the aircraft is only meaningful once there is something to return more
/// <em>than</em>.</para>
/// </summary>
public static class RadarSignature
{
    /// <summary>
    /// A cross-section from a contact's size (m²), as the disc a sphere of that radius presents.
    ///
    /// <para>Crude on purpose, and only meaningful as a ratio. A craft's <c>MeanRadius</c> is the
    /// half-diagonal of its bounding box — a number built for orbital clearance, standing well
    /// clear of the skin — so this overstates every contact by roughly the same factor, and the
    /// factor divides out against the reference. What survives is the ordering, which is the part
    /// worth having: a missile is a far smaller target than the aircraft that launched it.</para>
    /// </summary>
    public static double CrossSectionFor(double meanRadius)
        => double.IsFinite(meanRadius) && meanRadius > 0.0 ? Math.PI * meanRadius * meanRadius : 0.0;

    /// <summary>
    /// How far the set detects a contact of this cross-section, given the range it manages against
    /// <paramref name="referenceCrossSection"/>.
    ///
    /// <para>The **fourth** root, which is the whole reason this is a function rather than a
    /// multiplication. Received power falls as the fourth power of range, so detection range goes
    /// as the fourth root of cross-section: a target a hundredth the size is seen at a third of the
    /// range, not a hundredth of it. Scaling range linearly with size makes small targets
    /// effectively invisible and is the mistake this exists to prevent.</para>
    ///
    /// <para>Returns <paramref name="referenceRange"/> unchanged whenever either cross-section is
    /// unusable, so a set that has not been given a reference behaves exactly as it did before
    /// there was one.</para>
    /// </summary>
    public static double DetectionRange(double referenceRange, double crossSection,
                                        double referenceCrossSection)
    {
        if (!double.IsFinite(referenceRange) || referenceRange <= 0.0) return 0.0;
        if (!double.IsFinite(crossSection) || crossSection <= 0.0) return referenceRange;
        if (!double.IsFinite(referenceCrossSection) || referenceCrossSection <= 0.0) return referenceRange;

        double scaled = referenceRange * Math.Pow(crossSection / referenceCrossSection, 0.25);

        return double.IsFinite(scaled) ? scaled : referenceRange;
    }
}
