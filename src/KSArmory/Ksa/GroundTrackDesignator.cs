using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Clicking KSA's ground-track map to put the warheads somewhere.
///
/// <para>The same decision as <see cref="SiteDesignator"/>, which takes a click on the world:
/// <see cref="TargetEdit.ClickDoes"/> says whether a click designates or adds, and
/// <see cref="ReachDisplay.Verdict"/> refuses one the bus could not divert to. Sharing those
/// rather than restating them is what makes a click on the map and a click on the ground behave
/// identically, including the refusals.</para>
///
/// <para><b>A release, not a press.</b> The map is an invisible button KSA drags and zooms with,
/// so a press is the start of a pan as often as it is a click. The gesture is a release near where
/// the press landed; anything further is the operator having moved the map, and designating there
/// would put a warhead wherever the drag happened to end.</para>
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
        // The same switch the world designator obeys, so the two cannot disagree about whether
        // clicking is armed -- and so there is one control for it rather than two.
        if (!computer.Config.DesignateByClicking) return;

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

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left)) return;

        bool wasClick = _pressing && over
                        && Math.Abs(mouse.X - _pressed.X) <= SlopPx
                        && Math.Abs(mouse.Y - _pressed.Y) <= SlopPx;

        _pressing = false;
        if (!wasClick) return;

        if (mark >= 0) Weigh(computer, mark, ImGui.GetIO().KeyShift ? -1 : 1);
        else Take(computer, lon, lat);
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

        // Said at Debug either way. The screen is already answering -- the cursor's line says why
        // a click will be refused before it is made -- but "I clicked the map and nothing
        // happened" is the report this feature will actually get, and a verdict in the log is the
        // difference between a refusal and a gesture that never reached here at all.
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

    // Beside the cursor rather than on the panel, for the reason SiteDesignator says it there: a
    // refusal an operator has to look away to read is one they find out about by clicking and
    // watching nothing happen.
    private static void Say(IcbmComputer computer, float2 mouse, double lon, double lat, int mark)
    {
        ImDrawListPtr list = ImGui.GetWindowDrawList();
        float2 spot = new(mouse.X + 14f, mouse.Y + 6f);

        if (mark >= 0 && mark < computer.Targets.Count)
        {
            int had = computer.Targets[mark].Warheads;
            bool back = ImGui.GetIO().KeyShift;

            string what = back
                ? had == 0 ? computer.MayRemoveTarget(mark) ? "remove" : "the lead stays"
                           : $"take one back ({had - 1})"
                : $"one more here ({had + 1})";

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
