using Brutal.Numerics;
using KSArmory.Sim;

namespace KSArmory;

/// <summary>
/// A round in flight, seen by somebody else's sensor.
///
/// <para>This is what makes an incoming missile a thing that can be shot at. It is the same round
/// object its own launcher is integrating — held by reference, not copied — so its position and
/// velocity are always this frame's, and a sensor sees exactly what the simulation says.</para>
/// </summary>
/// <param name="round">The round itself, which is also its <see cref="IContact.Handle"/>.</param>
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
internal sealed class RoundContact(IProjectile round, string? firedBy, KSA.Vehicle? anchor) : IContact
{
    public IProjectile Round { get; } = round;

    public object Handle => Round;

    /// <summary>
    /// Named for the launcher and the tube, which is how the log already identifies a round. The
    /// cannon use negative tube numbers, so a shell reads as a shell.
    /// </summary>
    public string DisplayName => Round.Tube < 0
                                 ? $"{firedBy ?? "unknown"} shell"
                                 : $"{firedBy ?? "unknown"} round {Round.Tube}";

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

    public double3 PositionEcl => Round.PositionEcl;

    public double3 VelocityEcl => Round.VelocityEcl;

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
