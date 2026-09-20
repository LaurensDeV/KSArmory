using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Point at the world and put the warheads there — the ballistic computer's aim point, picked off
/// the ground rather than typed in.
///
/// <para>A tool that is armed and then used, not a button that is pressed. That distinction is the
/// whole reason this exists: a button samples the cursor at the moment it is clicked, and at that
/// moment the cursor is over the button — so it designates whatever happens to lie behind the
/// panel, silently and plausibly. A place on a map cannot be named by a control that is in the
/// way of it.</para>
///
/// <para>Per computer rather than per session, like every other tool that acts on one
/// installation: two missiles in the same world are aimed by different people at different
/// things.</para>
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

        // Nothing under the cursor is a click on the sky, and there is no sensible place to put a
        // warhead there. Silent on purpose: the marker below is already saying there is nowhere to
        // aim, which is a better answer than a line in a log.
        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out double latitude,
                                           out double longitude, out string body))
        {
            return;
        }

        AimSite site = new(body, latitude, longitude, "");

        ReachVerdict verdict = Verdict(computer, groundEcl, body);

        // Silent, for the reason the sky case above is: the cursor ring has been saying why for as
        // long as it has been over this spot, which beats a line in a log the player is not reading.
        if (!ReachDisplay.Takes(verdict)) return;

        // Past cutoff the booster has already flown to target 1, so a click is another place for the
        // bus rather than a new shot -- designating there resets the flight it is half-way through.
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
        TargetClick click = TargetEdit.ClickDoes(computer.Targets.Count, computer.Program.Phase);
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

        // Greyed on another world, because a ballistic arc is a two-body problem about one planet
        // and a designation there is one the computer will refuse to fly -- and greyed outside the
        // bus's divert reach, which is the same kind of refusal one phase later. Better said here,
        // while the cursor is still over it, than as a line of red text after the click.
        ReachVerdict verdict = Verdict(computer, groundEcl, body);

        double3 up = computer.Parent is { } centre
                         ? Vec.Unit(groundEcl - centre.GetPositionEcl())
                         : KsaWorld.LocalUp(anchor);

        double range = Vec.Len(groundEcl - KsaWorld.PositionEcl(anchor));

        KsaWorld.DrawCircleEcl(groundEcl, up, Math.Max(MarkerMin, range * MarkerScale),
                               ReachDisplay.Takes(verdict) ? MarkerColour : RefusedColour);

        Say(ReachDisplay.CursorSays(verdict));
    }

    // Beside the cursor rather than on the panel: a refusal an operator has to look away to read is
    // one they find out about by clicking and watching nothing happen. On the foreground list, so
    // it is over the scene without a window of its own to swallow the click underneath it.
    private static void Say(string what)
    {
        if (what.Length == 0) return;

        float2 at = ImGui.GetIO().MousePos;

        ImGui.GetForegroundDrawList().AddText(new float2(at.X + 16f, at.Y + 6f), Refused, what);
    }
}
