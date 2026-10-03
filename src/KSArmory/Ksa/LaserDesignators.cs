namespace KSArmory;

/// <summary>
/// Every laser spot in the world this frame, by code -- what a laser-guided weapon will read, from
/// any craft whose designator carries its code.
/// </summary>
internal static class LaserDesignators
{
    private static readonly List<LaserSpot> _spots = [];

    public static IReadOnlyList<LaserSpot> Active => _spots;

    /// <summary>Forgets last frame's spots; each lasing head states its own again.</summary>
    public static void BeginFrame() => _spots.Clear();

    public static void Add(LaserSpot spot) => _spots.Add(spot);
}
