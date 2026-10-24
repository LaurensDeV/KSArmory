using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which falling stores a new designation reaches.
///
/// <para>The store released last takes every mark until its weapon releases again, but only inside its
/// fins' reach: a mark outside aims the next store, and steered there this one would land in between.</para>
/// </summary>
internal static class StoreRetarget
{
    public static bool Takes(bool releasedSince) => !releasedSince;

    /// <summary>
    /// Whether the fins can still bring the store onto a place. A region that could not be flown makes
    /// no claim, so the place is taken rather than refused on a guess.
    /// </summary>
    public static bool Reaches(in TailKitReach reach, double3 aimEcl)
        => !reach.Known || reach.Covers(aimEcl);
}
