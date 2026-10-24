using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Everything about a radar contact except <em>what</em> it is.
///
/// <para><c>Ksa/Track.cs</c> derives from this and adds the contact, which cannot cross into <c>Sim/</c>.</para>
/// </summary>
internal class TrackState
{
    // Auto-properties, not fields: the test build has none of the Ksa/ code that assigns them, and as
    // fields each would raise CS0649 there.

    /// <summary>Target position in the ecliptic frame (m).</summary>
    public double3 PositionEcl { get; set; }

    /// <summary>Target velocity in the ecliptic frame (m/s).</summary>
    public double3 VelocityEcl { get; set; }

    /// <summary>
    /// What the contact is doing to its velocity, gravity included. Zero where it is not known.
    /// </summary>
    public double3 AccelerationEcl { get; set; }

    /// <summary>
    /// Its drag box and attitude, so a lead can fly its drag area as its airflow turns. Null for a
    /// contact that has none to read, which is then flown on the coefficient it was measured with.
    /// </summary>
    public DragShape? DragShape { get; set; }

    /// <summary>Slant range from the system (m).</summary>
    public double Range { get; set; }

    /// <summary>Speed relative to the system (m/s). Positive is closing.</summary>
    public double ClosingSpeed { get; set; }

    /// <summary>How near this target will pass the system if nobody manoeuvres (m).</summary>
    public double ClosestApproach { get; set; }

    /// <summary>Seconds until <see cref="ClosestApproach"/> is reached.</summary>
    public double TimeToClosestApproach { get; set; }

    /// <summary>Continuous seconds this contact has been held.</summary>
    public double HeldSeconds { get; set; }

    /// <summary>True once the contact satisfies the threat criteria; whether it may be fired on is IFF's.</summary>
    public bool IsThreat { get; set; }

    public string? Team { get; set; }

    /// <summary>Where this contact stands relative to the system. See <see cref="IffPolicy"/>.</summary>
    public Allegiance Allegiance { get; set; }

    /// <summary>Rounds currently in the air against this target.</summary>
    public int RoundsAssigned { get; set; }

    /// <summary>
    /// Lower sorts first: time to closest approach. Non-threats sort last rather than being removed,
    /// because the panel still lists them.
    /// </summary>
    public double Priority => IsThreat ? TimeToClosestApproach : double.MaxValue;
}
