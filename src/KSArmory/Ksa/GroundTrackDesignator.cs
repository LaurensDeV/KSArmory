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

        if (over) Say(computer, mouse, lon, lat);

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

        Take(computer, lon, lat);
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
            bool took = computer.AddTarget(site);
            Log.Debug($"ground-track map: click on {site.Coordinates} "
                      + (took ? $"added, {computer.Targets.Count} target(s)" : "not added"));
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
    private static void Say(IcbmComputer computer, float2 mouse, double lon, double lat)
    {
        if (computer.Parent is not { } body) return;

        AimSite site = new(body.Id, lat, lon, "");
        if (!site.IsSet) return;

        ReachVerdict verdict = Verdict(computer, site);
        string says = ReachDisplay.CursorSays(verdict);

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        float2 where = new(mouse.X + 14f, mouse.Y + 6f);

        draw.AddText(where, ReachDisplay.Takes(verdict) ? Takes : Refused,
                     says.Length > 0 ? says : site.Coordinates);

        if (says.Length > 0)
            draw.AddText(new float2(where.X, where.Y + 14f), Refused, site.Coordinates);
    }
}
