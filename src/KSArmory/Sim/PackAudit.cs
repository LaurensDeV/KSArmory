namespace KSArmory;

/// <summary>
/// Asks whether what registered can actually be found in the world.
///
/// <para>The half of validation that cannot run when a definition is read: a pack registers before
/// KSA has loaded a single asset bundle, so at that point no part exists to check against. Run
/// once the catalogue is shut and everything is loaded.</para>
///
/// <para><b>It reports and never unregisters.</b> A launcher naming a part nothing declares is
/// already harmless — no craft can carry a part that does not exist, so the profile is simply
/// never matched. What it is not is <em>visible</em>, and that is the whole complaint: the part is
/// missing from the editor, the weapon is missing from the panel, and both look exactly like a mod
/// that was never installed.</para>
/// </summary>
public static class PackAudit
{
    /// <summary>
    /// Every registered launcher and head, against the parts that actually loaded.
    /// </summary>
    /// <param name="sourceOf">Which pack registered a part Id, for attributing the fault.</param>
    public static IReadOnlyList<PackFault> Run(
        IReadOnlyList<LauncherProfile> launchers,
        IReadOnlyList<OpticProfile> optics,
        IPartCatalogue parts,
        Func<string, string> sourceOf)
    {
        List<PackFault> faults = [];

        foreach (LauncherProfile launcher in launchers)
        {
            if (Missing(launcher.PartId, "Launcher", parts, sourceOf, faults)) continue;

            IReadOnlyList<string> subParts = parts.SubPartIdsOf(launcher.PartId);
            Marker(launcher.PartId, "Launcher", "TurretMarker", launcher.TurretMarker, subParts, sourceOf, faults);
            Marker(launcher.PartId, "Launcher", "PodsMarker", launcher.PodsMarker, subParts, sourceOf, faults);
            Marker(launcher.PartId, "Launcher", "GunsMarker", launcher.GunsMarker, subParts, sourceOf, faults);
            Marker(launcher.PartId, "Launcher", "RadarMarker", launcher.RadarMarker, subParts, sourceOf, faults);
            Marker(launcher.PartId, "Launcher", "OpticBaseMarker", launcher.OpticBaseMarker, subParts, sourceOf, faults);
        }

        foreach (OpticProfile head in optics)
        {
            if (Missing(head.PartId, "Optic", parts, sourceOf, faults)) continue;

            IReadOnlyList<string> subParts = parts.SubPartIdsOf(head.PartId);
            Marker(head.PartId, "Optic", "BaseMarker", head.BaseMarker, subParts, sourceOf, faults);
            Marker(head.PartId, "Optic", "HeadMarker", head.HeadMarker, subParts, sourceOf, faults);
            Marker(head.PartId, "Optic", "RollMarker", head.RollMarker, subParts, sourceOf, faults);
        }

        return faults;
    }

    private static bool Missing(string partId, string kind, IPartCatalogue parts,
                                Func<string, string> sourceOf, List<PackFault> faults)
    {
        if (parts.Declares(partId)) return false;

        faults.Add(new PackFault(sourceOf(partId), kind, partId,
                                 "no part with this Id was declared, so nothing can ever carry it"));
        return true;
    }

    // One marker against the part's subparts, by the rule the game resolves it with.
    //
    // Two hits is a fault and not a nicety. Resolution is a case-insensitive substring and takes
    // the first match, so a marker matching both Pods and PodsCover silently drives whichever the
    // part happens to list first -- and moving a subpart in the XML then changes which assembly
    // articulates.
    private static void Marker(string partId, string kind, string field, string? marker,
                               IReadOnlyList<string> subParts,
                               Func<string, string> sourceOf, List<PackFault> faults)
    {
        if (string.IsNullOrEmpty(marker)) return;

        int hits = 0;
        for (int i = 0; i < subParts.Count; i++)
        {
            if (subParts[i].Contains(marker, StringComparison.OrdinalIgnoreCase)) hits++;
        }

        if (hits == 1) return;

        faults.Add(new PackFault(
            sourceOf(partId), kind, partId,
            hits == 0
                ? $"{field}=\"{marker}\" matches no subpart of this part"
                : $"{field}=\"{marker}\" matches {hits} subparts, and resolution takes the first"));
    }
}
