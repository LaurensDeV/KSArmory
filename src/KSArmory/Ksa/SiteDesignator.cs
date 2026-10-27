using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Point at the world and put the warheads there — the ballistic computer's aim point, picked off
/// the ground rather than typed in.
///
/// <para>A tool that is armed and then used, not a button: a button samples the cursor while it is
/// over the button, and so designates whatever lies behind the panel.</para>
///
/// <para>Per computer rather than per session: two missiles in one world aim at different things.</para>
/// </summary>
internal sealed class SiteDesignator
{
    private static readonly float4 MarkerColour = new(1.0f, 0.45f, 0.35f, 0.9f);
    private static readonly float4 RefusedColour = new(0.55f, 0.55f, 0.6f, 0.7f);

    private static readonly ImColor8 Refused = new(185, 185, 195, 235);

    // How big the ring on the ground is drawn, as a fraction of how far away it is. A fixed radius
    // is a dot from orbit and swallows the screen from the pad.
    private const double MarkerScale = 0.02;

    private const double MarkerMin = 500.0;

    /// <summary>Takes the click, if the tool is armed and the click was on the world.</summary>
    public void Update(IcbmComputer computer)
    {
        if (!computer.Config.DesignateByClicking) return;

        // A click on the panel is not a click on the world behind it.
        if (ImGui.GetIO().WantCaptureMouse) return;
        // Shift is the lock gesture, so a shift-click is not a click on the world.
        if (ImGui.GetIO().KeyShift) return;

        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)) return;

        // Silent on purpose: the cursor marker already says there is nowhere to aim.
        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out double latitude,
                                           out double longitude, out string body))
        {
            return;
        }

        AimSite site = new(body, latitude, longitude, "");

        ReachVerdict verdict = Verdict(computer, groundEcl, body);

        // Silent: the cursor ring already says why.
        if (!ReachDisplay.Takes(verdict)) return;

        // With a place already named and a reach that can refuse the click, it is another target
        // rather than a new shot -- and past cutoff designating would reset a flight half-way
        // through its coast.
        if (verdict == ReachVerdict.Adds)
        {
            computer.AddTarget(site);
            return;
        }

        computer.Designate(site);
    }

    // What a click on this spot would do: the list and the phase decide whether it designates or
    // adds, and only an add is bounded by what the bus can still divert to.
    private static ReachVerdict Verdict(IcbmComputer computer, double3 groundEcl, string body)
    {
        TargetClick click = TargetEdit.ClickDoes(computer.Targets.Count, computer.Program.Phase,
                                                 computer.Reach.HasRegion);
        bool onTheBody = computer.Parent is { } parent && parent.Id == body;

        // Not a number where the world would not resolve the point, which is what the verdict reads
        // as unknown. Zeroes would read as the middle of the region and take the click.
        if (!computer.TryReachOffsets(groundEcl, out double along, out double cross))
        {
            along = double.NaN;
            cross = double.NaN;
        }

        return computer.Reach.Verdict(click, onTheBody, along, cross);
    }

    /// <summary>Rings where the next click would aim, so the tool can be pointed before it is used.</summary>
    public void Draw(IcbmComputer computer)
    {
        if (!computer.Config.DesignateByClicking) return;
        if (ImGui.GetIO().WantCaptureMouse) return;
        if (KsaWorld.ControlledVehicle is not { } anchor) return;
        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out string body)) return;
        if (!KsaWorld.BeginDraw(anchor, KsaWorld.PositionEcl(anchor))) return;

        // Greyed where the click would be refused -- another world, or outside the bus's divert
        // reach -- so the refusal shows before the click rather than after it.
        ReachVerdict verdict = Verdict(computer, groundEcl, body);

        double range = Vec.Len(groundEcl - KsaWorld.PositionEcl(anchor));
        float4 ink = ReachDisplay.Takes(verdict) ? MarkerColour : RefusedColour;

        // Painted, its alpha the brightness it keeps over dark ground.
        GroundRings.Add(groundEcl, Math.Max(MarkerMin, range * MarkerScale), 0.0,
                        new float4(ink.X, ink.Y, ink.Z, 0.05f));

        Say(ReachDisplay.CursorSays(verdict, computer.Targets.Count));
    }

    // Beside the cursor, on the foreground list: over the scene with no window of its own to swallow
    // the click underneath it.
    private static void Say(string what)
    {
        if (what.Length == 0) return;

        float2 at = ImGui.GetIO().MousePos;

        ImGui.GetForegroundDrawList().AddText(new float2(at.X + 16f, at.Y + 6f), Refused, what);
    }
}
