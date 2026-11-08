namespace KSArmory;

/// <summary>
/// What an armour plate is made of, as the penetration law reads it: its density, Lambert's coefficient for
/// it, and its resistance to a rod — read from a definition file, so a pack can add one.
/// </summary>
public sealed class ArmourMaterial
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    public float DensityKgPerM3 { get; init; }

    /// <summary>
    /// The coefficient <c>a</c> in Lambert's ballistic limit, with lengths in cm, mass in g and speed in m/s:
    /// 4,000 for rolled homogeneous armour, 1,750 for aluminium (BRL-MR-3960, p. 17).
    /// </summary>
    public float LambertCoefficient { get; init; }

    /// <summary>The target's resistance to a penetrating rod, Tate's H, in pascals: what decides a glance.</summary>
    public float ResistancePascals { get; init; }
}
