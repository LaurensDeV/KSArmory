namespace KSArmory;

/// <summary>
/// Rebuilt from live state every frame; only <see cref="TrackState.HeldSeconds"/> carries over.
/// The contact is the only game-facing part, so ranking and allocation on the base are testable.
/// </summary>
internal sealed class Track : TrackState
{
    public required IContact Contact { get; init; }
}
