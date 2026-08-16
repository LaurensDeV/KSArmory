using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where the ground is under a round.
///
/// <para>Answered as a <b>centre and a surface radius</b> rather than as an altitude, and that is
/// the point of the shape: a round samples it once a frame and then knows its own height above the
/// ground at every sub-step for the cost of a subtraction. An altitude would have to be re-read
/// per sub-step to mean anything, and a terrain sample is the expensive call here.</para>
///
/// <para>The approximation it buys is that the surface under the round is treated as a sphere of
/// that radius for the frame. Over the few metres of ground track a falling round covers in one
/// frame that is exact except across a cliff edge, and a cliff is where the engine's own height
/// query is discontinuous anyway.</para>
///
/// <para>Unlike <see cref="IHullTest"/> this takes an absolute position, and it is entitled to:
/// terrain is a property of the world rather than of a separation between two things, so there is
/// no pair of epochs to mismatch and nothing for the ecliptic carrier to leak through.</para>
/// </summary>
internal interface IGroundTest
{
    /// <param name="positionEcl">Where the round is now.</param>
    /// <param name="centreEcl">Centre of the body beneath it.</param>
    /// <param name="surfaceRadius">Distance from that centre to the ground under the round.</param>
    bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius);
}

/// <summary>The surface a falling round actually meets, which over sea is the sea.</summary>
internal static class GroundSurface
{
    /// <summary>
    /// The higher of the terrain and the waterline, both as heights above the body's mean radius.
    ///
    /// <para>A height field answers with terrain and nothing else, so under an ocean it reports the
    /// <em>seabed</em>. A round fused on contact then falls straight through the waterline and
    /// bursts on the bottom — which is a detonation nobody sees, and reads in play as a warhead
    /// that simply failed.</para>
    ///
    /// <para>Over land the terrain is above the waterline and wins, so nothing about dry ground
    /// changes. Bodies with no sea pass <paramref name="hasSea"/> false and are untouched.</para>
    /// </summary>
    public static double Height(double terrainHeight, double seaLevel, bool hasSea)
    {
        if (!double.IsFinite(terrainHeight)) return terrainHeight;
        if (!hasSea || !double.IsFinite(seaLevel)) return terrainHeight;

        return seaLevel > terrainHeight ? seaLevel : terrainHeight;
    }
}
