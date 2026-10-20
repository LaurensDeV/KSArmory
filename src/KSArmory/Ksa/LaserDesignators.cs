namespace KSArmory;

/// <summary>
/// Every laser spot in the world this frame, by code -- what a laser-guided weapon will read, from
/// any craft whose designator carries its code.
/// </summary>
internal static class LaserDesignators
{
    private static readonly List<LaserSpot> _spots = [];
    private static readonly List<LaserSpot> _lastStep = [];

    public static IReadOnlyList<LaserSpot> Active => _spots;

    /// <summary>
    /// Every spot as the last step stated it. What a spot tracker searches, so a head reads every spot
    /// at one instant rather than only those of the heads that ran before it this step.
    /// </summary>
    public static IReadOnlyList<LaserSpot> LastStep => _lastStep;

    /// <summary>Keeps last frame's spots for the trackers, then forgets them; each lasing head states its own again.</summary>
    public static void BeginFrame()
    {
        _lastStep.Clear();
        _lastStep.AddRange(_spots);
        _spots.Clear();
    }

    public static void Add(LaserSpot spot) => _spots.Add(spot);
}
