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
        if (!KsaWorld.TryCursorGroundPoint(out _, out double latitude, out double longitude,
                                           out string body))
        {
            return;
        }

        computer.Designate(new AimSite(body, latitude, longitude, ""));
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
        // and a designation there is one the computer will refuse to fly. Better said here, while
        // the cursor is still over it, than as a line of red text after the click.
        bool reachable = computer.Parent is { } parent && parent.Id == body;

        double3 up = computer.Parent is { } centre
                         ? Vec.Unit(groundEcl - centre.GetPositionEcl())
                         : KsaWorld.LocalUp(anchor);

        double range = Vec.Len(groundEcl - KsaWorld.PositionEcl(anchor));

        KsaWorld.DrawCircleEcl(groundEcl, up, Math.Max(MarkerMin, range * MarkerScale),
                               reachable ? MarkerColour : RefusedColour);
    }
}
