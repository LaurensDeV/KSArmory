using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where the ground is under a round.
///
/// <para>Answered as a <b>centre and a surface radius</b> rather than as an altitude, and that is
/// the point of the shape: one sample makes the round's height above the ground a subtraction at
/// every sub-step that follows. A terrain query is the expensive call here, so how often it is
/// worth paying is the round's own decision — <see cref="MunitionProfile.SamplesGroundPerSubStep"/>
/// is where a round says it wants one per sub-step instead of one per frame.</para>
///
/// <para>The approximation a held sample buys is that the surface is a sphere of that radius for as
/// long as it is held. Across the few metres a bomb covers in a frame that is exact but for a cliff
/// edge, where the engine's own height query is discontinuous anyway; across the hundreds of metres
/// a re-entering warhead covers it is a real term, and <c>docs/KINETIC-FLOOR.md</c> prices it.</para>
///
/// <para>Unlike <see cref="IHullTest"/> this takes an absolute position, and it is entitled to: it
/// answers with a <em>centre</em> as well as a radius, so the height above ground is a difference
/// taken against the same body sample the aim point, the flown prediction and the gravity term are
/// all measured from. That is why the sample is <b>not</b> back-dated the way the air-density lookup
/// is — moving it alone would translate the surface relative to every one of them.</para>
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
