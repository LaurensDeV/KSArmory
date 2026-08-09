using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Everything about a radar contact except <em>what</em> it is.
///
/// <para>Split out of <c>Track</c> so that ranking and salvo allocation can be tested without
/// the game. The identity of a contact is a <c>KSA.Vehicle</c> and cannot cross into
/// <c>Sim/</c>; its kinematics are ordinary arithmetic and belong here. <c>Ksa/Track.cs</c>
/// derives from this and adds the vehicle.</para>
/// </summary>
internal class TrackState
{
    // Auto-properties rather than fields on purpose. The test project links Sim/** and nothing
    // else, so the Ksa/ code that populates these is not in that compilation; as fields they
    // each raise CS0649 "never assigned" there, which is six warnings of pure noise.

    /// <summary>Target position in the ecliptic frame (m).</summary>
    public double3 PositionEcl { get; set; }

    /// <summary>Target velocity in the ecliptic frame (m/s).</summary>
    public double3 VelocityEcl { get; set; }

    /// <summary>Slant range from the battery (m).</summary>
    public double Range { get; set; }

    /// <summary>Speed relative to the battery (m/s). Positive is closing.</summary>
    public double ClosingSpeed { get; set; }

    /// <summary>How near this target will pass the battery if nobody manoeuvres (m).</summary>
    public double ClosestApproach { get; set; }

    /// <summary>Seconds until <see cref="ClosestApproach"/> is reached.</summary>
    public double TimeToClosestApproach { get; set; }

    /// <summary>Continuous seconds this contact has been held.</summary>
    public double HeldSeconds { get; set; }

    /// <summary>True once the contact satisfies the threat criteria and may be fired on.</summary>
    public bool IsThreat { get; set; }

    /// <summary>Team name this contact was assigned, if any.</summary>
    public string? Team { get; set; }

    /// <summary>Where this contact stands relative to the battery. See <see cref="IffPolicy"/>.</summary>
    public Allegiance Allegiance { get; set; }

    /// <summary>Rounds currently in the air against this target.</summary>
    public int RoundsAssigned { get; set; }

    /// <summary>
    /// Lower sorts first. Ranks by how soon the target reaches its closest approach, so the most
    /// immediate threat is serviced first. Non-threats sort to the end rather than being
    /// removed, because the panel still lists them.
    /// </summary>
    public double Priority => IsThreat ? TimeToClosestApproach : double.MaxValue;
}
