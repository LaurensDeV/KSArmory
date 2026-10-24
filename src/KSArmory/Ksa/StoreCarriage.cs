using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The hardware a store hangs by, shown when it means something: a missile's own rail or ejector only
/// while the missile is attached to something, and the empty Ejector Rack only while nothing is on its
/// station -- the store brings the same carriage, and mates at the same origin. A rocket pod on the rack's
/// floor station brings none, so the rack stays and swaps its 14-inch hooks for the pod's 30-inch pair.
///
/// <para>The mod's one pass in the vehicle editor as well as in flight. A subpart's scale is honoured by
/// the editor's drawing, picking and snapping, and is not saved, so a load shows each part as its XML
/// declares it until the first frame decides.</para>
/// </summary>
internal static class StoreCarriage
{
    public const string WeaponsRailId = "KSArmory_Prefab_WeaponsRail";
    private const string StationConnector = "_wrConnectorStore";
    private const string PodConnector = "_wrConnectorPod";
    private const string RackBody = "WeaponsRail_CarriageBody";
    private const string RackArms = "WeaponsRail_CarriageArm";
    private const string StoreHooks = "WeaponsRail_CarriageHook";
    private const string PodHooks = "WeaponsRail_PodHook";

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
                bool carrying = false, podded = false;
                foreach (Part.Connector connector in part.Connectors)
                {
                    if (connector.Connection is null) continue;
                    if (connector.Id == StationConnector) carrying = true;
                    else if (connector.Id == PodConnector) podded = true;
                }

                bool changed = Show(part, RackBody, !carrying, inEditor);
                changed |= Show(part, RackArms, !carrying, inEditor);
                changed |= Show(part, StoreHooks, !carrying && !podded, inEditor);
                changed |= Show(part, PodHooks, !carrying && podded, inEditor);
                if (changed)
                {
                    string state = carrying ? "hidden under the store on it" : podded ? "carrying a pod" : "shown, empty";
                    Log.Info($"ejector rack {state}" + (inEditor ? " (editor)" : ""));
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
