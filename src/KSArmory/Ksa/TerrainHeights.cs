using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One body's height field, off the engine's own height map.
///
/// <para><c>accurate: false</c> unless asked, unlike <see cref="GroundTest"/>. A sensor asks tens of
/// times per contact per scan and is deciding whether a ridge is in the way rather than where exactly
/// its crest is, which is the engine's own terrain solver's choice too. The pointer asks once a frame
/// where it actually is, and wants the exact surface.</para>
/// </summary>
internal sealed class TerrainHeights(Celestial body, bool accurate = false) : ITerrainHeights
{
    private readonly Celestial _body = body;
    private readonly bool _accurate = accurate;

    public bool TryHeight(double3 dirFromCentre, out double metres)
    {
        metres = 0.0;

        try
        {
            metres = _body.GetTerrainHeightFromDirCce(dirFromCentre, accurate: _accurate);

            return double.IsFinite(metres);
        }
        catch
        {
            return false;
        }
    }
}
