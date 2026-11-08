namespace KSArmory;

/// <summary>
/// A part that is an armour plate: which material, and how thick. A plate is not a weapon and makes no
/// component row, so a craft carrying only plates is not a weapons system.
/// </summary>
public sealed class ArmourProfile
{
    public required string PartId { get; init; }

    /// <summary>The <see cref="ArmourMaterial.Name"/> it is made of.</summary>
    public required string Material { get; init; }

    public float ThicknessMm { get; init; }
}
