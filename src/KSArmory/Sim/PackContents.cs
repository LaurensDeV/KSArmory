namespace KSArmory;

/// <summary>
/// What reading one pack produced: what it may register, and what it may not.
///
/// <para>Both halves always. A pack whose every definition was refused still answers, because
/// "nothing was accepted, here is why" and "nothing was offered" are different states and only
/// one of them is a bug the author can fix.</para>
/// </summary>
public sealed class PackContents
{
    /// <summary>The pack, as it named itself. Nothing looks this up; it is supplied.</summary>
    public required string Source { get; init; }

    public IReadOnlyList<MunitionProfile> Munitions { get; init; } = [];
    public IReadOnlyList<SensorProfile> Sensors { get; init; } = [];
    public IReadOnlyList<LauncherProfile> Launchers { get; init; } = [];
    public IReadOnlyList<OpticProfile> Optics { get; init; } = [];

    /// <summary>
    /// One per launcher, minted rather than written. A launcher absent from the components
    /// registry loads, resolves its tubes, matches a part Id and is then invisible to the panel,
    /// so the pairing is made here instead of being a rule an author has to know.
    /// </summary>
    public IReadOnlyList<ComponentProfile> Components { get; init; } = [];

    public IReadOnlyList<PackFault> Faults { get; init; } = [];

    /// <summary>How many definitions came through.</summary>
    public int Accepted => Munitions.Count + Sensors.Count + Launchers.Count + Optics.Count;
}
