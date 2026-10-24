using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Something a sensor can hold, not necessarily a craft: this mod's rounds are not KSA vehicles and
/// still have to be seen and shot at. The kinematics a track reasons on are <see cref="TrackState"/>'s.
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
    /// A craft's name, or a round's shooter's. Not an allegiance: <see cref="DeclaredTeam"/> alone is,
    /// because a name says what a craft is called and not whose it is.
    /// </summary>
    string TeamKey { get; }

    /// <summary>
    /// The side the panel's flag put this contact on, or a round's shooter's; null is on no team.
    /// </summary>
    string? DeclaredTeam { get; }

    /// <summary>Radius, which the blast and the reticle both size from.</summary>
    double MeanRadius { get; }

    /// <summary>How hot it looks to an infrared set, in kW/sr (<see cref="Signature"/>).</summary>
    double HeatKwPerSr { get; }

    bool IsAlive { get; }

    double3 PositionEcl { get; }

    double3 VelocityEcl { get; }

    /// <summary>Everything accelerating it, gravity included. Zero where that is not known.</summary>
    double3 AccelerationEcl { get; }

    /// <summary>
    /// Wreckage a failure shed. Tracked, and never engaged: a burst that breaks a craft leaves pieces
    /// still flying and still closing, and shooting them spends shells on something already dead.
    /// </summary>
    bool IsDebris { get; }

    /// <summary>
    /// A round in flight rather than a craft. Tracked either way; what it changes is that a round
    /// has to be <em>closing</em> to be a threat, because it cannot turn round and come back.
    /// </summary>
    bool IsMunition { get; }

    /// <summary>Its drag box and attitude as the engine flies it, or null where there is none to read.</summary>
    DragShape? DragShape { get; }

    /// <summary>
    /// Where its middle is drawn, which is not where it is simulated — see
    /// <c>docs/FRAMES-AND-EPOCHS.md</c> — and for a craft not its centre of mass, which a bracket
    /// would sit below. False when it cannot be placed, which the overlay reads as "draw nothing".
    /// </summary>
    bool TryDrawEgo(out double3 posEgo);

    /// <summary>
    /// The craft that put this in the air, or null. Only so a set can ignore its own platform's
    /// salvo; not a side, since two launchers on one team still see each other's rounds.
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

    public string? DeclaredTeam => KsaWorld.TeamRoster.For(Vehicle);

    /// <summary>Nothing launched a craft. A sensor skips its own platform by reference instead.</summary>
    public Vehicle? LaunchedFrom => null;

    public double MeanRadius => KsaWorld.MeanRadius(Vehicle);

    public double HeatKwPerSr => KsaWorld.HeatOf(Vehicle);

    public bool IsAlive => KsaWorld.IsAlive(Vehicle);

    public double3 PositionEcl => KsaWorld.PositionEcl(Vehicle);

    public double3 VelocityEcl => KsaWorld.VelocityEcl(Vehicle);

    public double3 AccelerationEcl => KsaWorld.AccelerationEcl(Vehicle);

    public bool IsDebris => KsaWorld.IsDebris(Vehicle);

    /// <summary>A craft, which may turn round however it is going now.</summary>
    public bool IsMunition => false;

    public DragShape? DragShape => KsaWorld.DragShapeOf(Vehicle);

    public bool TryDrawEgo(out double3 posEgo) => KsaWorld.TryVehicleCentreEgo(Vehicle, out posEgo);
}
