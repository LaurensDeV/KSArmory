using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Clicking KSA's ground-track map to put the warheads somewhere. Decided through
/// <see cref="TargetEdit.ClickDoes"/> and <see cref="ReachDisplay.Verdict"/>, as
/// <see cref="SiteDesignator"/> is, so a click on the map and one on the ground refuse alike.
///
/// <para>The gesture is a release near its press, because the map is the invisible button KSA
/// pans with. It needs no arming: <c>DesignateByClicking</c> gates only the world designator,
/// where a click already means other things.</para>
/// </summary>
internal static class GroundTrackDesignator
{
    private static readonly ImColor8 Refused = new(185, 185, 195, 235);
    private static readonly ImColor8 Takes = new(255, 170, 60, 235);

    // How far a press may travel and still be a click rather than a pan.
    private const float SlopPx = 4f;

    private static float2 _pressed;
    private static bool _pressing;

    public static void Update(IcbmComputer computer, float2 at, float2 size,
                              float2 min, float2 max)
    {
        float2 mouse = ImGui.GetMousePos();
        bool over = MapViewport.TryPlace(at, size, min, max, mouse, out double lon, out double lat);

        // On a mark the gesture is weighting, which the reach does not bound: putting another
        // warhead on a place already chosen is accounting, not a divert.
        int mark = over ? GroundTrackMarks.Nearest(computer, at, size, min, max, mouse) : -1;

        if (over) Say(computer, mouse, lon, lat, mark);

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false))
        {
            _pressing = over;
            _pressed = mouse;
        }

        // Right-click takes a target away whatever it holds, in one gesture.
        if (over && mark >= 0 && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            Drop(computer, mark);
            return;
        }

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left)) return;

        bool wasClick = _pressing && over
                        && Math.Abs(mouse.X - _pressed.X) <= SlopPx
                        && Math.Abs(mouse.Y - _pressed.Y) <= SlopPx;

        _pressing = false;
        if (!wasClick) return;

        if (mark >= 0) Weigh(computer, mark, ImGui.GetIO().KeyShift ? -1 : 1);
        else Take(computer, lon, lat);
    }

    // The lead cannot be removed -- the arc, the correction and the trim are all solved against
    // it, so replacing it is a new shot rather than an edit. With one target it IS the lead, and
    // then the only honest answer is to clear the set, which is what designating nothing does.
    private static void Drop(IcbmComputer computer, int index)
    {
        if (computer.MayRemoveTarget(index))
        {
            computer.RemoveTarget(index);
            Log.Debug($"ground-track map: target {index + 1} removed, "
                      + $"{computer.Targets.Count} left");
            return;
        }

        if (computer.Targets.Count == 1)
        {
            computer.Designate(AimSite.None);
            Log.Debug("ground-track map: the last target was the lead, so the set is cleared");
            return;
        }

        Log.Debug($"ground-track map: target {index + 1} is the lead and stays; "
                  + "clear the set to start over");
    }

    // A click on a mark moves one warhead onto it; shift takes one back, and the click that would
    // take the last one off a target removes the target -- which is the only way a map can say
    // "not here after all" without a second gesture to learn.
    private static void Weigh(IcbmComputer computer, int index, int delta)
    {
        int had = index < computer.Targets.Count ? computer.Targets[index].Warheads : 0;

        if (delta < 0 && had == 0)
        {
            if (!computer.MayRemoveTarget(index))
            {
                Log.Debug($"ground-track map: target {index + 1} is the lead and stays");
                return;
            }

            computer.RemoveTarget(index);
            Log.Debug($"ground-track map: target {index + 1} removed, "
                      + $"{computer.Targets.Count} left");
            return;
        }

        if (!computer.ShiftTargetWarheads(index, delta))
        {
            Log.Debug($"ground-track map: {delta:+0;-0} on target {index + 1} refused, "
                      + $"{computer.Targets.Count} target(s) over {computer.WarheadsAboard}");
            return;
        }

        Log.Debug($"ground-track map: target {index + 1} now has "
                  + $"{computer.Targets[index].Warheads} warhead(s)");
    }

    private static void Take(IcbmComputer computer, double lon, double lat)
    {
        if (computer.Parent is not { } body) return;

        AimSite site = new(body.Id, lat, lon, "");
        if (!site.IsSet) return;

        ReachVerdict verdict = Verdict(computer, site);

        // Logged although the cursor line already says it: it tells a refusal from a click that
        // never reached here.
        if (!ReachDisplay.Takes(verdict))
        {
            Log.Debug($"ground-track map: click on {site.Coordinates} refused, {verdict}");
            return;
        }

        if (verdict == ReachVerdict.Adds)
        {
            if (!computer.AddTarget(site))
            {
                Log.Debug($"ground-track map: click on {site.Coordinates} not added");
                return;
            }

            // A target with nothing on it is not a stop, so placing one and leaving it empty is a
            // click that looks like it worked and changed nothing about the shot.
            computer.ShiftTargetWarheads(computer.Targets.Count - 1, 1);

            Log.Debug($"ground-track map: click on {site.Coordinates} added, "
                      + $"{computer.Targets.Count} target(s)");
            return;
        }

        computer.Designate(site);
        Log.Debug($"ground-track map: click designated {site.Coordinates}");
    }

    private static ReachVerdict Verdict(IcbmComputer computer, AimSite site)
    {
        TargetClick click = TargetEdit.ClickDoes(computer.Targets.Count, computer.Program.Phase,
                                                 computer.Reach.HasRegion);

        // Not a number where the reach cannot price the place, which the verdict reads as unknown.
        // Zeroes would read as the middle of the region and take the click.
        if (!computer.TryReachOffsets(site, out double along, out double cross))
        {
            along = double.NaN;
            cross = double.NaN;
        }

        // The map is of this craft's own parent and the site is built from it, so there is no
        // other body a click here could name.
        return computer.Reach.Verdict(click, onTheBody: true, along, cross);
    }

    // Beside the cursor rather than on the panel, so a refusal is read before the click.
    private static void Say(IcbmComputer computer, float2 mouse, double lon, double lat, int mark)
    {
        ImDrawListPtr list = ImGui.GetWindowDrawList();
        float2 spot = new(mouse.X + 14f, mouse.Y + 6f);

        if (mark >= 0 && mark < computer.Targets.Count)
        {
            int had = computer.Targets[mark].Warheads;
            bool back = ImGui.GetIO().KeyShift;

            string what = back
                ? had == 0 ? "right-click to remove" : $"take one back ({had - 1})"
                : $"one more here ({had + 1})   right-click removes";

            list.AddText(spot, Takes, what);
            list.AddText(new float2(spot.X, spot.Y + 14f), Refused,
                         computer.Targets[mark].Site.Coordinates);
            return;
        }

        if (computer.Parent is not { } body) return;

        AimSite site = new(body.Id, lat, lon, "");
        if (!site.IsSet) return;

        ReachVerdict verdict = Verdict(computer, site);
        string says = ReachDisplay.CursorSays(verdict, computer.Targets.Count);

        list.AddText(spot, ReachDisplay.Takes(verdict) ? Takes : Refused,
                     says.Length > 0 ? says : site.Coordinates);

        if (says.Length > 0)
            list.AddText(new float2(spot.X, spot.Y + 14f), Refused, site.Coordinates);
    }
}
