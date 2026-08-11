using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Something a sensor can hold: what it is called, how big it is, whether it still exists, and
/// where it is.
///
/// <para><b>Not necessarily a craft.</b> Tying a contact to a <c>Vehicle</c> limits a sensor to
/// the things KSA simulates, and this mod's own rounds are not among them — so a missile in
/// flight could not be seen, tracked or shot at, including by the launcher that fired it.
/// Everything a track is asked for is one of these five questions, and none of them needs the
/// contact to be a craft.</para>
///
/// <para>The kinematics stay on <see cref="TrackState"/> in <c>Sim/</c>, which does not know what
/// a contact is and does not need to. This is only the identity half.</para>
/// </summary>
internal interface IContact
{
    /// <summary>
    /// What this contact <em>is</em>, for comparing two sightings of it and for keying dwell.
    /// Reference identity: two contacts are the same when their handles are the same object.
    /// </summary>
    object Handle { get; }

    string DisplayName { get; }

    /// <summary>
    /// The name the team roster is matched against, which is not always what the contact is
    /// called. A round's is its <em>shooter's</em> craft name, so it inherits that side's
    /// allegiance without anything having to know a round from a craft.
    /// </summary>
    string TeamKey { get; }

    /// <summary>Radius, which the blast and the reticle both size from.</summary>
    double MeanRadius { get; }

    /// <summary>False once it is gone, so fire control can drop it.</summary>
    bool IsAlive { get; }

    double3 PositionEcl { get; }

    double3 VelocityEcl { get; }

    /// <summary>
    /// Where it is drawn, which is not where it is simulated — see <c>docs/FRAMES-AND-EPOCHS.md</c>.
    /// False when it cannot be placed, which the overlay reads as "draw nothing".
    /// </summary>
    bool TryDrawEgo(out double3 posEgo);

    /// <summary>
    /// The craft that put this in the air, or null for something nobody launched.
    ///
    /// <para>Only so a sensor can disregard its <em>own</em> platform's rounds. IFF cannot do it:
    /// allegiance decides what may be <em>engaged</em>, and a friendly contact is still tracked —
    /// correctly, because a sight pointed at a friendly craft is doing its job. What is never
    /// useful is a set watching its own salvo leave, from metres away, at the top of its
    /// priority.</para>
    ///
    /// <para>Deliberately not "whose side it is on". Two launchers on one team should still see
    /// each other's rounds, and seeing an incoming missile is the whole reason a round is a
    /// contact at all.</para>
    /// </summary>
    Vehicle? LaunchedFrom { get; }
}

/// <summary>A craft, seen by a sensor.</summary>
internal sealed class VehicleContact(Vehicle vehicle) : IContact
{
    public Vehicle Vehicle { get; } = vehicle;

    public object Handle => Vehicle;

    public string DisplayName => KsaWorld.DisplayName(Vehicle);

    public string TeamKey => DisplayName;

    /// <summary>Nothing launched a craft. A sensor skips its own platform by reference instead.</summary>
    public Vehicle? LaunchedFrom => null;

    public double MeanRadius => KsaWorld.MeanRadius(Vehicle);

    public bool IsAlive => KsaWorld.IsAlive(Vehicle);

    public double3 PositionEcl => KsaWorld.PositionEcl(Vehicle);

    public double3 VelocityEcl => KsaWorld.VelocityEcl(Vehicle);

    public bool TryDrawEgo(out double3 posEgo) => KsaWorld.TryVehicleEgo(Vehicle, out posEgo);
}
