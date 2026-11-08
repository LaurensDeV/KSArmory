using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The hardware a store hangs by, shown when it means something: a missile's or a pod's own rail or
/// ejector only while it is attached to something, and the empty Weapon Mount only while nothing is on its
/// station -- the store brings the same carriage, and mates at the same origin.
///
/// <para>The mod's one pass in the vehicle editor as well as in flight. A subpart's scale is honoured by
/// the editor's drawing, picking and snapping, and is not saved, so a load shows each part as its template
/// has it, the carriage parked (<see cref="ParkOnTemplates"/>), until the first frame decides.</para>
/// </summary>
internal static class StoreCarriage
{
    public const string WeaponsRailId = "KSArmory_Prefab_WeaponsRail";
    private const string StationConnector = "_wrConnectorStore";
    private const string RailCarriageMarker = "WeaponsRail_Carriage";

    private static readonly double3 Shown = new(1.0, 1.0, 1.0);
    private static readonly double3 Hidden = new(1e-3, 1e-3, 1e-3);

    private static bool _warned;

    public static void Update()
    {
        try
        {
            if (Program.Editor is { } editor)
            {
                Visit(editor.EditingSpace.AllParts, inEditor: true);
                foreach (PartTree loose in editor.UnattachedPartTrees) Visit(loose.Parts, inEditor: true);
            }
            else if (KsaWorld.InFlightScene)
            {
                foreach (Vehicle vehicle in KsaWorld.Vehicles)
                {
                    if (KsaWorld.IsAlive(vehicle)) Visit(vehicle.Parts.Parts, inEditor: false);
                }
            }
        }
        catch (Exception e)
        {
            if (_warned) return;
            _warned = true;
            Log.Warn($"store carriage: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void Visit(ReadOnlySpan<Part> parts, bool inEditor)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            Part part = parts[i];
            if (part?.Template?.Id is not { } id) continue;

            if (id == WeaponsRailId)
            {
                bool carrying = false;
                foreach (Part.Connector connector in part.Connectors)
                {
                    if (connector.Id == StationConnector && connector.Connection is not null) carrying = true;
                }

                if (Show(part, RailCarriageMarker, !carrying, inEditor))
                {
                    Log.Info($"weapon mount {(carrying ? "hidden under the store on it" : "shown, empty")}"
                             + (inEditor ? " (editor)" : ""));
                }
            }
            else if ((Catalogue.LauncherForPart(id)?.CarriageMarker ?? Catalogue.OpticForPart(id)?.CarriageMarker)
                     is { Length: > 0 } carriage)
            {
                Show(part, carriage, part.TreeParent is not null, inEditor);
            }
        }
    }

    /// <summary>
    /// Parks every store's carriage on its part template. The XML declares it at full size so a tool that
    /// reads the part files draws it; the editor's thumbnails and a part's first frame are built from the
    /// template, so they show the store alone, as a carriage declared parked does.
    /// </summary>
    public static void ParkOnTemplates()
    {
        int parked = 0;
        foreach (LauncherProfile launcher in Catalogue.Launchers) parked += Park(launcher.PartId, launcher.CarriageMarker);
        foreach (OpticProfile optic in Catalogue.Optics) parked += Park(optic.PartId, optic.CarriageMarker);
        if (parked > 0) Log.Info($"store carriage: {parked} carriage subpart(s) parked on their templates");
    }

    private static int Park(string partId, string? marker)
    {
        if (string.IsNullOrEmpty(marker)) return 0;
        try
        {
            if (ModLibrary.Get<PartTemplate>(partId) is not { } template) return 0;
            int parked = 0;
            foreach (TransformDataReference<SubPartTemplate> sub in template.SubPartInstances)
            {
                if (!sub.Key.Contains(marker, StringComparison.Ordinal)) continue;
                (sub.Transform ??= new TransformReference()).ScaleValue = Hidden;
                parked++;
            }

            return parked;
        }
        catch
        {
            // A part the library cannot answer for is drawn as its XML says, and Update still decides.
            return 0;
        }
    }

    // Whether anything changed: writes only then, and only then recomputes the bounds the editor caches.
    private static bool Show(Part part, string marker, bool shown, bool inEditor)
    {
        double3 want = shown ? Shown : Hidden;
        bool changed = false;
        foreach (Part sub in part.SubParts)
        {
            if (sub.Id is { } id && id.Contains(marker, StringComparison.Ordinal) && !sub.Scale.Equals(want))
            {
                sub.Scale = want;
                changed = true;
            }
        }

        if (changed && inEditor) part.UpdateBounds();
        return changed;
    }
}
