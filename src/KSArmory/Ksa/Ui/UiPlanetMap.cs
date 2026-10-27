using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// This installation's own map window: <see cref="PlanetMap"/>'s picture, following this panel's
/// craft rather than the one flown, with the target list beside it.
/// <para>The pan and zoom are KSA's <see cref="GenericImGuiViewport"/>, called directly: it hands
/// back the crop, so nothing here has to work out what the engine is showing.</para>
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

    // A window dragged shut still draws a frame on the way down.
    private const float LeastPx = 80f;

    private void DrawPlanetMapWindow()
    {
        if (_icbms?.For(Focused) is not { } computer || !computer.Config.MapOpen)
        {
            // Shut: give the textures back rather than render a planet every frame.
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

        // Open means the list is being edited, which is what prices the divert reach; without
        // a reach a click can only replace the set.
        computer.NoteBeingEdited();

        DrawPlanetMapHeader(computer);

        // Fitted, not filled: filling the width squashes the picture.
        float2 avail = ImGui.GetContentRegionAvail();
        float2 size = MapViewport.Fit(new float2(avail.X, avail.Y - RowsBelow), MapAspect,
                                      LeastPx, out float inset);

        if (inset > 0f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + inset);

        // Here, because the pan is in image pixels; before the viewport call, so this frame shows it.
        if (_fitWanted)
        {
            _fitWanted = false;
            KsaWorld.SetMapView(_planetView, _fitZoom,
                                new float2(_fitTo.X * size.X * _fitZoom,
                                           _fitTo.Y * size.Y * _fitZoom));
        }

        // Consumes the drag and the wheel notch, so never twice in a frame.
        _planetView.CreateDraggableZoomableViewport(size, out float2 min, out float2 max);
        _planetMap.SetCrop(min, max);

        float2 at = ImGui.GetCursorScreenPos();

        if (_planetMap.TryTexture(out ImTextureRef texture)) ImGui.Image(texture, in size);
        else ImGui.Dummy(size);

        GroundTrackMarks.Draw(computer, at, size, min, max, _config.DrawGroundTrackCalibration);
        GroundTrackDesignator.Update(computer, at, size, min, max);

        // Fitted once, on the click that creates the set: the reach is kilometres across on a map
        // forty thousand wide. The view is the operator's after that.
        if (computer.Targets.Count == 1 && _hadTargets == 0) FitToReach(computer);

        _hadTargets = computer.Targets.Count;

        DrawPlanetMapTargets(computer);
    }

    private void DrawPlanetMapHeader(IcbmComputer computer)
    {
        ImGui.TextDisabled(computer.Program.Phase.ToString());
        ImGui.SameLine();

        if (computer.Config.Armed) ImGui.TextColored(Red, "ARMED");
        else ImGui.TextDisabled("safe");

        ImGui.SameLine();
        if (ImGui.SmallButton("Clear")) computer.Designate(AimSite.None);
        Tip("Forget every target and start the shot over. The lead cannot be removed on its own "
            + "- the arc, the correction and the trim are solved against it - so with one target "
            + "left this is the only way to take it off.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Spread")) computer.BalanceTargets();
        Tip("Share every warhead out evenly over the targets chosen. The click-by-click way is to "
            + "click a mark to put one more on it; this is the whole-handful way.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Fit reach")) FitToReach(computer);
        Tip("Zoom to the ground this bus can still divert to. A post-boost bus moves a landing by "
            + "kilometres, not hundreds of them, so at whole-planet zoom that circle is a fraction "
            + "of a pixel - which is why a second target has to go near the first.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Whole world")) _planetView.ResetView();
        Tip("Back to the whole planet. Drag to pan, wheel to zoom, middle-click to reset.");

        // Always: the circle is invisible at most zooms.
        if (computer.Reach.HasRegion)
        {
            ImGui.TextDisabled($"  another target must land within "
                               + $"{Distance.Say(computer.Reach.SemiMinorMetres)} of this one");
        }
        else
        {
            ImGui.TextDisabled($"  {computer.Reach.Say()}");
        }

        ImGui.Separator();
    }

    // The crop is one over the zoom, so a ring wants ShareOfView of the view over its diameter.
    private void FitToReach(IcbmComputer computer)
    {
        if (computer.Parent is not { } body) return;
        if (computer.Targets.Count == 0) return;
        if (!computer.Reach.HasRegion) return;

        AimSite lead = computer.Targets[computer.LeadTarget].Site;
        if (!lead.IsSet) return;

        double degrees = GroundCircle.HeightDeg(computer.Reach.SemiMajorMetres, body.MeanRadius);
        if (!(degrees > 0.0)) return;

        float zoom = (float)Math.Clamp(ShareOfView * 90.0 / degrees, 1.0, 5000.0);
        float2 uv = MapViewport.ToUv(lead.LongitudeDeg, lead.LatitudeDeg);
        float half = 0.5f / zoom;

        _fitTo = new float2(-(uv.X - half), -(uv.Y - half));
        _fitZoom = zoom;
        _fitWanted = true;
    }

    private const double ShareOfView = 0.25;

    private float2 _fitTo;
    private float _fitZoom = 1f;
    private bool _fitWanted;
    private int _hadTargets;

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

        // An unassigned warhead is not idle: it rides to the lead.
        ImGui.TextDisabled($"{assigned} of {aboard} warhead(s) placed");

        if (assigned < aboard)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"- {aboard - assigned} rides to the lead");
        }

        ImGui.TextDisabled($"{entries.Count} target(s), {computer.Reach.Say()}");
        ImGui.TextDisabled("  click to place, click again for another warhead, "
                           + "shift-click to take one back, right-click to remove");

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
            else if (entries.Count == 1)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("drop")) computer.Designate(AimSite.None);
                Tip("The last target is the lead, so taking it off is clearing the set.");
            }

            ImGui.PopID();
        }
    }
}
