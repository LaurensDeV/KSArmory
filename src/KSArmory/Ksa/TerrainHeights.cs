using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One body's height field, off the engine's own height map.
///
/// <para><c>accurate: false</c> throughout, unlike <see cref="GroundTest"/>. That one resolves
/// where a single bomb lands and can afford the exact query; this one is asked tens of times per
/// contact per scan, and a sensor is deciding whether a ridge is in the way rather than where
/// exactly its crest is. The engine's own terrain solver makes the same choice for the same
/// reason.</para>
/// </summary>
internal sealed class TerrainHeights(Celestial body) : ITerrainHeights
{
    private readonly Celestial _body = body;

    public bool TryHeight(double3 dirFromCentre, out double metres)
    {
        metres = 0.0;

        try
        {
            metres = _body.GetTerrainHeightFromDirCce(dirFromCentre, accurate: false);

            return double.IsFinite(metres);
        }
        catch
        {
            return false;
        }
    }
}
