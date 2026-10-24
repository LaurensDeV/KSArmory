namespace KSArmory.Tests;

/// <summary>
/// Two metres a second off the tube, as the specimen for tests that are about what an ejection
/// kick <em>does</em> rather than about what the shipped round carries.
///
/// <para>The Mk 21 throws a quarter of this. The term it drives is exactly linear — <b>3,979 m of
/// impact per m/s</b>, constant to 0.3% from 0.1 to 2 — so a test measured against the shipped kick
/// shrinks with it and can stop exercising what it was written for. Same shape as
/// <see cref="CantedRing"/>: the phenomenon is still reachable by a weapon pack, so it keeps a specimen.</para>
///
/// <para>A test asserting something about the <em>shipped</em> round should read the profile
/// instead, and change when the profile does — that is the difference, and it is why these are not
/// one constant.</para>
/// </summary>
internal static class LoudKick
{
    public const double MetresPerSecond = 2.0;
}
