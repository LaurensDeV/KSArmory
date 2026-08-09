using Brutal.Numerics;

namespace KSArmory;

/// <summary>What a part is, to a weapons system.</summary>
public enum WeaponRole
{
    /// <summary>Decides what the craft shoots at. One per craft.</summary>
    FireControl,

    /// <summary>Throws guided rounds. Carries its own tubes.</summary>
    Launcher,

    /// <summary>Finds targets. Feeds the threat model.</summary>
    Sensor,

    /// <summary>
    /// Looks at one thing and shows it to the operator.
    ///
    /// <para>Separate from <see cref="Sensor"/> because it does a different job: a sensor
    /// *detects*, producing tracks the threat model ranks, while a camera *observes* something
    /// already known and drives one of the game's viewports. A craft can carry either without the
    /// other — a site with no camera still shoots, and a camera without a radar has nothing to
    /// look at but is still worth pointing by hand.</para>
    ///
    /// <para>They also differ in what they cost: a sensor is a per-frame scan over every loaded
    /// vehicle, a camera is a transform write and a borrowed viewport.</para>
    /// </summary>
    Camera,

    /// <summary>Throws unguided rounds on a belt.</summary>
    Gun,
}

/// <summary>
/// One part this mod recognises, and what it contributes to a craft.
///
/// <para>Matched by part Id, because that is the only lever available: KSA cannot register a
/// custom module without patching the engine, so a part cannot *say* what it is — see
/// docs/BLOCKED-ON-KSA.md. A registry keyed on Id is the same mechanism
/// <see cref="LauncherProfile"/> already uses, one level finer.</para>
/// </summary>
public sealed class ComponentProfile
{
    public required string PartId { get; init; }
    public required WeaponRole Role { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// Roles this one part carries as subparts rather than as parts of their own.
    ///
    /// <para>A prefab like the Pantsir is a single <c>Part</c> whose radar, optical head and
    /// cannon are <c>SubPart</c>s of it. The survey walks parts, so without this it reports a
    /// launcher and nothing else — which reads as a system that has no camera rather than one
    /// whose camera is built in.</para>
    /// </summary>
    public IReadOnlyList<BuiltInComponent> Provides { get; init; } = [];
}

/// <summary>A role a part carries within itself: what it is and what to call it.</summary>
public readonly record struct BuiltInComponent(WeaponRole Role, string DisplayName);

/// <summary>
/// One part found on a craft, with where it sits.
///
/// <para>The position and rotation come from the craft itself rather than from a table. That is
/// the whole point of surveying: a prefab launcher has to have its tube geometry generated into
/// <c>Arsenal.cs</c> and cross-checked by <c>validate-parts.py</c>, because nothing at run time
/// connects the model to the code. A part the player placed knows where it is.</para>
/// </summary>
public readonly record struct SurveyedPart(
    string PartId,
    double3 PositionVehicleAsmb,
    doubleQuat Asmb2VehicleAsmb);

/// <summary>A component found on a craft: what it is, and where.</summary>
public readonly record struct FoundComponent(
    ComponentProfile Profile,
    WeaponRole Role,
    string DisplayName,
    double3 PositionVehicleAsmb,
    doubleQuat Asmb2VehicleAsmb);

/// <summary>
/// Everything this mod recognises on one craft.
///
/// <para>Ordering is the part tree's, which is the order the craft was assembled in. That is
/// stable for a given craft and is what makes "launcher 2" mean the same thing between frames —
/// the same reason the battery keys on a part ordinal rather than a <c>Part</c> reference, which
/// KSA rebuilds during staging and docking.</para>
/// </summary>
public sealed class WeaponInventory
{
    public required IReadOnlyList<FoundComponent> Components { get; init; }

    public int CountOf(WeaponRole role)
    {
        int n = 0;
        for (int i = 0; i < Components.Count; i++)
        {
            if (Components[i].Role == role) n++;
        }
        return n;
    }

    /// <summary>Whether this craft is a weapons system at all.</summary>
    /// <remarks>
    /// Anything recognised counts. The intended gate is an explicit fire-control part: it gives a
    /// craft's settings an owner, and it stops a piece of debris that happens to carry a launcher
    /// from becoming a battery of its own. No such part exists, so gating on it would find nothing.
    /// </remarks>
    public bool IsWeaponSystem => Components.Count > 0;

    public static readonly WeaponInventory Empty = new() { Components = [] };
}

/// <summary>
/// Walks a craft's parts and reports which of them this mod recognises.
///
/// <para>No KSA types: the caller flattens the part tree into <see cref="SurveyedPart"/> and
/// applies the answer. That is what makes the matching and grouping testable, and the alternative
/// is discovering a mis-assembled craft in flight.</para>
/// </summary>
public static class WeaponSurvey
{
    /// <summary>
    /// Groups the recognised parts of one craft.
    /// </summary>
    /// <param name="parts">Every part on the craft, in tree order.</param>
    /// <param name="registry">The components this mod knows about.</param>
    public static WeaponInventory Survey(IReadOnlyList<SurveyedPart> parts,
                                         IReadOnlyList<ComponentProfile> registry)
    {
        if (parts.Count == 0 || registry.Count == 0) return WeaponInventory.Empty;

        List<FoundComponent> found = [];
        for (int i = 0; i < parts.Count; i++)
        {
            SurveyedPart part = parts[i];
            if (Match(part.PartId, registry) is not { } profile) continue;

            found.Add(new FoundComponent(profile, profile.Role, profile.DisplayName,
                                         part.PositionVehicleAsmb, part.Asmb2VehicleAsmb));

            // Built-ins sit at the part's own position: they are subparts of it, and where each
            // one is within the assembly is the model's business rather than the survey's.
            for (int b = 0; b < profile.Provides.Count; b++)
            {
                BuiltInComponent built = profile.Provides[b];
                found.Add(new FoundComponent(profile, built.Role, built.DisplayName,
                                             part.PositionVehicleAsmb, part.Asmb2VehicleAsmb));
            }
        }

        return new WeaponInventory { Components = found };
    }

    /// <summary>
    /// The component a part Id names, or null.
    ///
    /// <para>Exact match, not a substring. <c>LauncherPart.FindSubPart</c> matches on *containing*
    /// a marker and takes the first hit, which is right for a marker naming one subpart inside one
    /// part and wrong here: across a whole craft, "Launcher" would match anything a player's part
    /// happened to be called.</para>
    /// </summary>
    public static ComponentProfile? Match(string partId, IReadOnlyList<ComponentProfile> registry)
    {
        for (int i = 0; i < registry.Count; i++)
        {
            if (registry[i].PartId == partId) return registry[i];
        }
        return null;
    }
}
