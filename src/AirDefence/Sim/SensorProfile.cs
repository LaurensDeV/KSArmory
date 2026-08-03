namespace AirDefence;

/// <summary>
/// What a sensor can see and what it considers worth shooting at.
///
/// Separate from <see cref="MunitionProfile"/> because the two vary independently: a longer
/// -ranged set on the same launcher, or the same set feeding a different round. Separate from
/// <see cref="Config"/> because it belongs to a weapon system, not to the player's preferences.
/// </summary>
public sealed class SensorProfile
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Maximum detection range (m).</summary>
    public float Range = 20000f;

    /// <summary>
    /// Half-angle of the search cone about the boresight (degrees). Measured off local "up",
    /// so 90 is a full hemisphere down to the horizon — the right default for a site that has
    /// to cover the whole sky. Narrow it to model a directional radar.
    /// </summary>
    public float ConeDeg = 90f;

    /// <summary>
    /// A track counts as a threat if its closest point of approach to the battery falls inside
    /// this radius (m). This is what makes "passing by" targets engageable rather than only
    /// head-on ones.
    /// </summary>
    public float ThreatRadius = 5000f;

    /// <summary>Only threats whose CPA lands within this many seconds are engaged.</summary>
    public float ThreatHorizonSeconds = 40f;

    /// <summary>Continuous seconds a track must be held before weapons release.</summary>
    public float LockSeconds = 0.8f;

    /// <summary>Tracks below this relative speed are ignored (m/s), e.g. docked craft.</summary>
    public float MinTargetSpeed = 15f;

    public float ConeHalfAngleRad => float.DegreesToRadians(ConeDeg);
}
