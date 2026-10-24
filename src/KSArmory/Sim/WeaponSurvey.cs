using Brutal.Numerics;

namespace KSArmory;

/// <summary>What a part is, to a weapons system.</summary>
public enum WeaponRole
{
    /// <summary>Decides what the craft shoots at. Every launcher provides one as a built-in.</summary>
    FireControl,

    /// <summary>Throws rounds. Every registered launcher part carries it, tubes or not.</summary>
    Launcher,

    /// <summary>Finds targets. Feeds the threat model.</summary>
    Sensor,

    /// <summary>
    /// Looks at one thing already known and shows it to the operator, where a <see cref="Sensor"/>
    /// detects and produces tracks. A craft can carry either without the other.
    /// </summary>
    Camera,

    /// <summary>Throws unguided rounds on a belt.</summary>
    Gun,

    /// <summary>
    /// Flies the airframe it is on: what earns a craft a ballistic computer.
    ///
    /// <para>Not <see cref="FireControl"/>: a Pantsir has fire control and nothing to fly.
    /// docs/GUIDANCE-SECTION.md.</para>
    /// </summary>
    Guidance,

    /// <summary>
    /// Throws flares and chaff. Its own role rather than a <see cref="Launcher"/>: nothing it throws is
    /// aimed, tracked or fused, and a craft carrying only a dispenser is not a weapons system.
    /// </summary>
    Countermeasures,
}

/// <summary>
/// One part this mod recognises, and what it contributes to a craft.
///
/// <para>Matched by part Id: KSA cannot register a custom module without patching the engine, so
/// a part cannot say what it is (docs/BLOCKED-ON-KSA.md).</para>
/// </summary>
public sealed class ComponentProfile
{
    public required string PartId { get; init; }
    public required WeaponRole Role { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// Roles this one part carries as subparts rather than as parts of their own.
    ///
    /// <para>The survey walks parts, so a Pantsir's radar, director and cannon, which are
    /// <c>SubPart</c>s, are found only through this.</para>
    /// </summary>
    public IReadOnlyList<BuiltInComponent> Provides { get; init; } = [];
}

/// <summary>A role a part carries within itself: what it is and what to call it.</summary>
public readonly record struct BuiltInComponent(WeaponRole Role, string DisplayName);

/// <summary>
/// One part found on a craft, with where it sits.
///
/// <para>The position and rotation come from the craft itself rather than from a table.</para>
/// </summary>
/// <param name="Rides">
/// The part's connectors carry <c>ToSurface</c> or <c>FromSurface</c>, which the editor's own root
/// test rejects, so it cannot be a craft on its own. False by default: an unasked part is a platform.
/// </param>
public readonly record struct SurveyedPart(
    string PartId,
    double3 PositionVehicleAsmb,
    doubleQuat Asmb2VehicleAsmb,
    bool Rides = false);

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
/// <para>Ordering is the part tree's, so "launcher 2" means the same part between frames until a
/// split renumbers it; a crewed system then follows its part by reference (<c>WeaponSystem.HeldPart</c>).</para>
/// </summary>
public sealed class WeaponInventory
{
    public required IReadOnlyList<FoundComponent> Components { get; init; }

    /// <summary>
    /// Whether there is anything on this craft for a store to ride on.
    ///
    /// <para>False only when <em>every</em> part rides: a pod or rail a breakup left as a craft of its
    /// own, which the editor would refuse to root. A hull with one director on it still counts.</para>
    /// </summary>
    public bool HasPlatform { get; init; }

    public int CountOf(WeaponRole role)
    {
        int n = 0;
        for (int i = 0; i < Components.Count; i++)
        {
            if (Components[i].Role == role) n++;
        }
        return n;
    }

    public bool IsWeaponSystem => HasPlatform
                                  && (CountOf(WeaponRole.Launcher) > 0
                                      || CountOf(WeaponRole.Gun) > 0
                                      || CountOf(WeaponRole.FireControl) > 0);

    /// <summary>
    /// Whether something aboard flies the craft, which is what crews a ballistic computer on it.
    /// </summary>
    public bool HasGuidance => HasPlatform && CountOf(WeaponRole.Guidance) > 0;

    /// <summary>
    /// Whether this mod recognises anything on the craft at all, weapon or not.
    ///
    /// <para>What the panel lists, as opposed to what it crews: a craft with only a director is one,
    /// and crewing it as a weapons system would give it a system with no launcher.</para>
    /// </summary>
    public bool IsInstallation => Components.Count > 0 && HasPlatform;

    public static readonly WeaponInventory Empty = new() { Components = [], HasPlatform = false };
}

/// <summary>
/// Walks a craft's parts and reports which of them this mod recognises.
///
/// <para>No KSA types: the caller flattens the part tree into <see cref="SurveyedPart"/>.</para>
/// </summary>
public static class WeaponSurvey
{
    /// <param name="parts">Every part on the craft, in tree order.</param>
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

            // Built-ins are subparts, placed at the part's own position.
            for (int b = 0; b < profile.Provides.Count; b++)
            {
                BuiltInComponent built = profile.Provides[b];
                found.Add(new FoundComponent(profile, built.Role, built.DisplayName,
                                             part.PositionVehicleAsmb, part.Asmb2VehicleAsmb));
            }
        }

        bool hasPlatform = false;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Rides) continue;

            hasPlatform = true;
            break;
        }

        return new WeaponInventory { Components = found, HasPlatform = hasPlatform };
    }

    /// <summary>
    /// The component a part Id names, or null.
    ///
    /// <para>Exact match, unlike <c>LauncherPart.FindSubPart</c>'s substring: across a whole craft a
    /// substring would match whatever a player's part happened to be called.</para>
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
