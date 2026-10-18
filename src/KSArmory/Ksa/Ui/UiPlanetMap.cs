using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// This installation's own map window: a planet to put the warheads on, with the list of them
/// beside it.
///
/// <para>Its picture is <see cref="PlanetMap"/> — the mod's own exporter rather than the one
/// KSA's ground-track window owns, so there is no chrome, no menu and no flags belonging to
/// somebody else, and it follows this panel's craft rather than whichever one is being flown.</para>
///
/// <para>The pan and zoom are KSA's <see cref="GenericImGuiViewport"/>, which is public and
/// constructible. Calling it ourselves is the point: it hands back the crop as an out-parameter,
/// so nothing here has to work out what the engine is showing — the fault that arithmetic caused
/// twice cannot happen to a window that owns its own viewport.</para>
/// </summary>
internal sealed partial class Ui
{
    /// <summary>The map's own picture, for the hook that renders it inside the engine's pass.</summary>
    public PlanetMap PlanetMap => _planetMap;

    private readonly PlanetMap _planetMap = new();
    private readonly GenericImGuiViewport _planetView = new();

    // 2:1 is the shape of an equirectangular world; anything else letterboxes it.
    private const float MapAspect = 0.5f;

    private const float RowsBelow = 132f;

    private void DrawPlanetMapWindow()
    {
        if (_icbms?.For(Focused) is not { } computer || !computer.Config.MapOpen)
        {
            // Nothing is looking at it, so give the textures back rather than render a planet
            // every frame for a window that is shut.
            _planetMap.Close();
            return;
        }

        ImGui.SetNextWindowSize(new float2(760f, 560f), ImGuiCond.FirstUseEver);

        bool open = computer.Config.MapOpen;

        if (ImGui.Begin($"{KsaWorld.DisplayName(computer.Craft)} targets###KSArmoryPlanetMap",
                        ref open))
        {
            DrawPlanetMapContents(computer);
        }

        ImGui.End();
        computer.Config.MapOpen = open;
    }

    private void DrawPlanetMapContents(IcbmComputer computer)
    {
        if (!_planetMap.TryOpen())
        {
            ImGui.TextColored(Red, "no map");
            ImGui.TextDisabled($"  {_planetMap.Trouble}");

            if (!PlanetMapHook.Installed && PlanetMapHook.Trouble.Length > 0)
                ImGui.TextDisabled($"  {PlanetMapHook.Trouble}");

            return;
        }

        // Flat, not the real terminator: half a planet in darkness is no use for picking a place.
        _planetMap.SetLit(false);

        DrawPlanetMapHeader(computer);

        float2 avail = ImGui.GetContentRegionAvail();
        float2 size = new(avail.X, Math.Max(80f, Math.Min(avail.Y - RowsBelow,
                                                          avail.X * MapAspect)));

        // Ours, so it may be called outright -- it consumes the drag and the wheel notch, which is
        // exactly why nothing may call it twice in a frame.
        _planetView.CreateDraggableZoomableViewport(size, out float2 min, out float2 max);
        _planetMap.SetCrop(min, max);

        float2 at = ImGui.GetCursorScreenPos();

        if (_planetMap.TryTexture(out ImTextureRef texture)) ImGui.Image(texture, in size);
        else ImGui.Dummy(size);

        GroundTrackMarks.Draw(computer, at, size, min, max, _config.DrawGroundTrackCalibration);
        GroundTrackDesignator.Update(computer, at, size, min, max);

        DrawPlanetMapTargets(computer);
    }

    private void DrawPlanetMapHeader(IcbmComputer computer)
    {
        ImGui.TextDisabled(computer.Program.Phase.ToString());
        ImGui.SameLine();

        if (computer.Config.Armed) ImGui.TextColored(Red, "ARMED");
        else ImGui.TextDisabled("safe");

        ImGui.SameLine();
        bool picking = computer.Config.DesignateByClicking;
        if (ImGui.Checkbox("Click to aim", ref picking)) computer.Config.DesignateByClicking = picking;
        Tip("Click bare ground to put a target there, and click a target again to put another "
            + "warhead on it -- the number beside the mark is how many clicks it has had. Shift "
            + "takes one back, and shift on a target with none removes it. A warhead comes from "
            + "the lead, which is where an unplaced one rides anyway. A refused click says why "
            + "beside the pointer before it is made.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Spread")) computer.BalanceTargets();
        Tip("Share every warhead out evenly over the targets chosen. The click-by-click way is to "
            + "click a mark to put one more on it; this is the whole-handful way.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Whole world")) _planetView.ResetView();
        Tip("Back to the whole planet. Drag to pan, wheel to zoom, middle-click to reset.");

        ImGui.Separator();
    }

    private void DrawPlanetMapTargets(IcbmComputer computer)
    {
        var entries = computer.Targets;

        if (entries.Count == 0)
        {
            ImGui.TextDisabled("no target");
            return;
        }

        int aboard = computer.WarheadsAboard;
        int assigned = 0;
        for (int i = 0; i < entries.Count; i++) assigned += entries[i].Warheads;

        // The ledger, because an unassigned warhead is not idle -- it rides to the lead. Without
        // this line a half-assigned set reads as broken when it is doing exactly what it says.
        ImGui.TextDisabled($"{assigned} of {aboard} warhead(s) placed");

        if (assigned < aboard)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"- {aboard - assigned} rides to the lead");
        }

        ImGui.TextDisabled($"{entries.Count} target(s), {computer.Reach.Say()}");

        for (int i = 0; i < entries.Count; i++)
        {
            bool lead = i == computer.LeadTarget;

            ImGui.PushID(i);

            if (lead) ImGui.TextColored(Amber, $"{entries[i].Site.Coordinates}");
            else ImGui.TextDisabled($"{entries[i].Site.Coordinates}");

            ImGui.SameLine();
            ImGui.TextDisabled($"x{entries[i].Warheads}");

            if (TargetEdit.MayRemove(i, entries.Count, computer.LeadTarget))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("drop")) computer.RemoveTarget(i);
            }

            ImGui.PopID();
        }
    }
}
