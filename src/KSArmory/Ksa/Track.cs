namespace KSArmory;

/// <summary>
/// One radar contact: what was seen, plus the kinematics <see cref="TrackState"/> holds.
///
/// <para>Rebuilt from live state every frame; the only thing that persists between frames is how
/// long we have held it, which gates weapons release.</para>
///
/// <para>The split is what lets ranking and salvo allocation be tested headlessly — the contact
/// is the only part that needs the game. Everything the fire-control logic actually reasons about
/// is on the base class, which is why widening a contact beyond a craft cost that logic nothing.
/// </para>
/// </summary>
internal sealed class Track : TrackState
{
    public required IContact Contact { get; init; }
}
