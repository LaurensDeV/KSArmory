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
}
