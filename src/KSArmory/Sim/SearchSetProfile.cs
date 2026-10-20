using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A part that is a search set and nothing else — an IRST bolted to a hull. Its contacts go into
/// the craft's picture for the directors and the beam to take.
/// </summary>
public sealed class SearchSetProfile
{
    public required string PartId { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>The <see cref="SensorProfile.Name"/> it searches with.</summary>
    public required string Sensor { get; init; }

    /// <summary>The subpart that turns, found by its Id containing this; null for a set with no head.</summary>
    public string? HeadMarker { get; init; }

    /// <summary>What the head turns about, in the part's frame; its mesh is centred here.</summary>
    public double3 HeadPivot { get; init; }

    /// <summary>How far off the part's +Y the head may look (deg).</summary>
    public float HeadTravelDeg = 60f;

    public float HeadSlewDegPerSecond = 90f;

    /// <summary>The sweep with nothing held: how far either side (deg), its period, and its nod.</summary>
    public float ScanHalfWidthDeg = 45f;

    public float ScanPeriodSeconds = 6f;

    public float ScanNodDeg = 8f;
}
