using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A body's height field, asked along directions from its centre — the seam a sensor looks over
/// the real skyline through.
///
/// <para>Separate from <see cref="IGroundTest"/>, which answers for one point and resolves the
/// body each time it is asked. A line of sight samples one body tens of times in a row, so the
/// body is settled once and only the height varies.</para>
/// </summary>
public interface ITerrainHeights
{
    /// <summary>
    /// Terrain height above the mean sphere along a direction from the body's centre (m).
    ///
    /// <para>False when the field cannot be read. A caller must treat that as <em>no claim</em>
    /// rather than as flat ground: reading zero from an unreadable field puts a sensor's whole
    /// horizon at the mean sphere, which is a planet-sized change nothing announces.</para>
    /// </summary>
    bool TryHeight(double3 dirFromCentre, out double metres);
}
