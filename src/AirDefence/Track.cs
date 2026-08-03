using Brutal.Numerics;
using KSA;

namespace AirDefence;

/// <summary>
/// One radar contact. Rebuilt from live vehicle state every frame; the only thing that
/// persists between frames is how long we have held it, which gates weapons release.
/// </summary>
internal sealed class Track
{
    public required Vehicle Vehicle { get; init; }

    /// <summary>Target position in the ecliptic frame (m).</summary>
    public double3 PositionEcl;

    /// <summary>Target velocity in the ecliptic frame (m/s).</summary>
    public double3 VelocityEcl;

    /// <summary>Slant range from the battery (m).</summary>
    public double Range;

    /// <summary>Speed relative to the battery (m/s). Positive is closing.</summary>
    public double ClosingSpeed;

    /// <summary>How near this target will pass the battery if nobody manoeuvres (m).</summary>
    public double ClosestApproach;

    /// <summary>Seconds until <see cref="ClosestApproach"/> is reached.</summary>
    public double TimeToClosestApproach;

    /// <summary>Continuous seconds this contact has been held.</summary>
    public double HeldSeconds;

    /// <summary>True once the contact satisfies the threat criteria and may be fired on.</summary>
    public bool IsThreat;

    /// <summary>Rounds currently in the air against this target.</summary>
    public int RoundsAssigned;

    /// <summary>
    /// Lower sorts first. Ranks by how soon the target reaches its closest approach,
    /// so the most immediate threat is serviced first.
    /// </summary>
    public double Priority => IsThreat ? TimeToClosestApproach : double.MaxValue;
}
