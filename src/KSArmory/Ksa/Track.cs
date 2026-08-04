using KSA;

namespace KSArmory;

/// <summary>
/// One radar contact: a vehicle, plus the kinematics <see cref="TrackState"/> holds.
///
/// <para>Rebuilt from live vehicle state every frame; the only thing that persists between
/// frames is how long we have held it, which gates weapons release.</para>
///
/// <para>The split is what lets ranking and salvo allocation be tested headlessly — the vehicle
/// reference is the only part of a contact that needs the game. Everything the fire-control
/// logic actually reasons about is on the base class.</para>
/// </summary>
internal sealed class Track : TrackState
{
    public required Vehicle Vehicle { get; init; }
}
