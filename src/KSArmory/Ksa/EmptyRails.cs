using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The empty missile rail, which hides its own rail while a missile part sits on its station: the
/// missile brings the rail it belongs on, and mates at the same origin, so its rail takes this one's place.
///
/// <para>The mod's one pass in the vehicle editor as well as in flight. A subpart's scale is honoured by
/// the editor's drawing, picking and snapping, and is not saved, so a load shows the rail until the first
/// frame decides.</para>
/// </summary>
internal static class EmptyRails
{
    public const string PartId = "KSArmory_Prefab_MissileRail";
    private const string StationConnector = "_mrConnectorStore";
    private const string BeamMarker = "MissileRail_Beam";

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
            Log.Warn($"empty rails: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void Visit(ReadOnlySpan<Part> parts, bool inEditor)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            Part part = parts[i];
            if (part?.Template?.Id != PartId) continue;

            bool carrying = false;
            foreach (Part.Connector connector in part.Connectors)
            {
                if (connector.Id == StationConnector && connector.Connection is not null) carrying = true;
            }

            double3 want = carrying ? Hidden : Shown;
            bool changed = false;
            foreach (Part sub in part.SubParts)
            {
                if (sub.Id is { } id && id.Contains(BeamMarker, StringComparison.Ordinal) && !sub.Scale.Equals(want))
                {
                    sub.Scale = want;
                    changed = true;
                }
            }

            if (!changed) continue;

            // The editor caches a part's bounds and only recomputes them on request.
            if (inEditor) part.UpdateBounds();
            Log.Info($"missile rail {(carrying ? "hidden under the missile on it" : "shown, empty")}"
                     + (inEditor ? " (editor)" : ""));
        }
    }
}
