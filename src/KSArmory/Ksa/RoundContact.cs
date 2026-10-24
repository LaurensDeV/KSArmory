using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A round in flight, seen by somebody else's sensor.
///
/// <para>The round is held by reference, so whether it is still flying is always current.</para>
///
/// <para><b>Its kinematics are a snapshot, and must be.</b> A round is advanced by its own launcher's
/// update, so a live reference reads as start-of-frame or end-of-frame depending on roster order,
/// which is a dictionary's. Sampled once before any system steps, every system sees one instant.</para>
/// </summary>
/// <param name="round">The round itself, which is also its <see cref="IContact.Handle"/>.</param>
/// <param name="positionEcl">
/// Where it will be at the <em>end</em> of this frame's step, which is the phase KSA hands vehicle
/// state over at and the phase <see cref="TargetState"/> is defined at. See
/// <c>KSArmoryMod.CollectAirborne</c>, which is the only thing that may build one of these.
/// </param>
/// <param name="velocityEcl">Its velocity at that instant, carrying the frame's ~29.8 km/s.</param>
/// <param name="firedBy">The name of the craft that launched it, for its display name.</param>
/// <param name="declaredTeam">
/// The side the shooter's flag puts it on, or null for no team. Carried rather than looked up: a
/// round is in no craft roster, and a loose system has no craft left to look up.
/// </param>
/// <param name="anchor">
/// The craft the round's drawn offset is measured from. A round's <c>OffsetFromPlatform</c> is a
/// separation from its launcher, so drawing it needs that launcher — see
/// <c>docs/FRAMES-AND-EPOCHS.md</c>.
/// </param>
/// <param name="accelerationEcl">Its pull and its drag at that instant, gravity included.</param>
internal sealed class RoundContact(IProjectile round, string? firedBy, string? declaredTeam,
                                   KSA.Vehicle? anchor, double3 positionEcl, double3 velocityEcl,
                                   double3 accelerationEcl) : IContact
{
    public IProjectile Round { get; } = round;

    public object Handle => Round;

    public string DisplayName => $"{firedBy ?? "unknown"} {RoundLabel.For(Round.Tube)}";

    public string TeamKey => firedBy ?? string.Empty;

    /// <summary>Its shooter's, so a round is on the side that fired it.</summary>
    public string? DeclaredTeam => declaredTeam;

    /// <summary>The craft that fired it, so its own sensors can disregard it.</summary>
    public KSA.Vehicle? LaunchedFrom => anchor;

    /// <summary>A missile's body, not its lethal radius: a blast is measured against it and a sight sizes its bracket from it.</summary>
    public double MeanRadius => 1.5;

    public double HeatKwPerSr => Signature.HeatOfRound(Round.MotorBurning);

    /// <summary>Gone the moment it stops flying, whether it hit, burst or timed out.</summary>
    public bool IsAlive => Round.State == RoundState.Flying;

    public double3 PositionEcl => positionEcl;

    public double3 VelocityEcl => velocityEcl;

    // Coasting only: a motor still burning would be held for the whole of a shell's flight when it
    // stops in seconds, and the radar's history of the velocity takes over while it burns.
    public double3 AccelerationEcl => accelerationEcl;

    public bool IsDebris => false;

    /// <summary>What makes a sensor hold it to a closing geometry before calling it a threat.</summary>
    public bool IsMunition => true;

    public DragShape? DragShape => null;

    /// <summary>
    /// The launcher's drawn position plus the round's offset, never the simulated one, which differs by
    /// metres on a landed craft and by a frame of ecliptic motion besides.
    /// </summary>
    public bool TryDrawEgo(out double3 posEgo)
    {
        posEgo = Vec.Zero;

        if (anchor is null || !KsaWorld.TryVehicleEgo(anchor, out double3 anchorEgo)) return false;

        posEgo = anchorEgo + Round.OffsetFromPlatform;

        return true;
    }
}
