using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which falling stores a new designation reaches.
///
/// <para>The store released last can be steered by every mark after it, until its weapon releases
/// again -- and only onto a place its fins can still reach. That reach is what tells the two uses of
/// a click apart: inside it, the falling store is being corrected; outside it, the next one is being
/// aimed, and the falling store keeps its target.</para>
///
/// <para>And only onto a place its fins can still reach. Steered at one they cannot, it lands
/// somewhere in between and hits nothing, where it would have hit what it was already sent at.</para>
/// </summary>
internal static class StoreRetarget
{
    public static bool Takes(bool releasedSince) => !releasedSince;

    /// <summary>
    /// Whether the fins can still bring the store onto a place. A region that could not be flown
    /// -- a store still climbing, or nothing readable where it is -- makes no claim, so the place
    /// is taken rather than refused on a guess.
    /// </summary>
    public static bool Reaches(in TailKitReach reach, double3 aimEcl)
        => !reach.Known || reach.Covers(aimEcl);
}
