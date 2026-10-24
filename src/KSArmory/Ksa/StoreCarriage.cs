using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The hardware a store hangs by, shown when it means something: a missile's own rail or ejector only
/// while the missile is attached to something, and the empty Weapon Mount only while nothing is on its
/// station -- the store brings the same carriage, and mates at the same origin.
///
/// <para>The mod's one pass in the vehicle editor as well as in flight. A subpart's scale is honoured by
/// the editor's drawing, picking and snapping, and is not saved, so a load shows each part as its XML
/// declares it until the first frame decides.</para>
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
            else if (Catalogue.LauncherForPart(id)?.CarriageMarker is { Length: > 0 } carriage)
            {
                Show(part, carriage, part.TreeParent is not null, inEditor);
            }
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
