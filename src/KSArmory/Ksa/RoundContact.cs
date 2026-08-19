using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A round in flight, seen by somebody else's sensor.
///
/// <para>This is what makes an incoming missile a thing that can be shot at. The round itself is
/// held by reference, so what it <em>is</em> — its name, its side, whether it is still flying —
/// is always current.</para>
///
/// <para><b>Its kinematics are a snapshot, and must be.</b> Every system in the world is handed
/// one of these list before any of them steps, but a round's position is advanced by its
/// <em>own</em> launcher's update — so a live reference reads as start-of-frame or end-of-frame
/// depending on which system asks first, and roster order is a dictionary's. Against a 1.8 km/s
/// closing round that is a whole frame of relative motion, metres of it across the shell's fuse
/// radius, deciding a hit. Sampled once, every system sees one instant.</para>
/// </summary>
/// <param name="round">The round itself, which is also its <see cref="IContact.Handle"/>.</param>
/// <param name="positionEcl">
/// Where it will be at the <em>end</em> of this frame's step, which is the phase KSA hands vehicle
/// state over at and the phase <see cref="TargetState"/> is defined at. See
/// <c>KSArmoryMod.CollectAirborne</c>, which is the only thing that may build one of these.
/// </param>
/// <param name="velocityEcl">Its velocity at that instant, carrying the frame's ~29.8 km/s.</param>
/// <param name="firedBy">
/// The system that launched it. Carried so a round inherits its shooter's allegiance: a launcher's
/// own missiles must read as friendly to everything on its side, or a battery engages its own
/// salvo the moment it clears the tubes.
/// </param>
/// <param name="anchor">
/// The craft the round's drawn offset is measured from. A round's <c>OffsetFromPlatform</c> is a
/// separation from its launcher, so drawing it needs that launcher — see
/// <c>docs/FRAMES-AND-EPOCHS.md</c>.
/// </param>
internal sealed class RoundContact(IProjectile round, string? firedBy, KSA.Vehicle? anchor,
                                   double3 positionEcl, double3 velocityEcl) : IContact
{
    public IProjectile Round { get; } = round;

    public object Handle => Round;

    /// <summary>
    /// Named for the launcher and the tube, which is how the log already identifies a round.
    /// </summary>
    public string DisplayName => $"{firedBy ?? "unknown"} {RoundLabel.For(Round.Tube)}";

    /// <summary>Its shooter's craft, so a round is on the side that fired it.</summary>
    public string TeamKey => firedBy ?? string.Empty;

    /// <summary>The craft that fired it, so its own sensors can disregard it.</summary>
    public KSA.Vehicle? LaunchedFrom => anchor;

    /// <summary>
    /// A missile's body, not its warhead. Small: it is what a blast is measured against and what a
    /// sight sizes its bracket from, and using a lethal radius here would make a round look like
    /// the volume it threatens rather than the object it is.
    /// </summary>
    public double MeanRadius => 1.5;

    /// <summary>Gone the moment it stops flying, whether it hit, burst or timed out.</summary>
    public bool IsAlive => Round.State == RoundState.Flying;

    public double3 PositionEcl => positionEcl;

    public double3 VelocityEcl => velocityEcl;

    /// <summary>
    /// The drawn position, which is the launcher's drawn position plus the round's own flight —
    /// never the simulated one. The two differ by metres on a landed craft and by a frame of
    /// ecliptic motion besides.
    /// </summary>
    public bool TryDrawEgo(out double3 posEgo)
    {
        posEgo = Vec.Zero;

        if (anchor is null || !KsaWorld.TryVehicleEgo(anchor, out double3 anchorEgo)) return false;

        posEgo = anchorEgo + Round.OffsetFromPlatform;

        return true;
    }
}
