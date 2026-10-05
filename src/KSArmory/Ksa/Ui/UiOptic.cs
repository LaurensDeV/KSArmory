using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The rows describing an optical director: what it is looking at, what it is looking through, and
/// which contacts it will watch.
///
/// <para>Split from <see cref="Ui"/>'s weapons-system panes because a director is not one. It is a
/// part in its own right, crewed per head rather than per weapons system, and nothing here reads
/// <c>_system</c> or <c>_policy</c> — a craft carrying one director and no armament has every row
/// below and none of those.</para>
/// </summary>
internal sealed partial class Ui
{
    // Which of the game's camera views an optical director drives, and how far its optics are
    // wound in. The head is a part in its own right, so this reads the head fitted to the craft
    // being shown rather than anything belonging to the weapons system.
    private void DrawOpticView(OpticalHeads.Entry entry)
    {
        OpticConfig policy = entry.Policy;

        // Declared and unresolved is a fault worth saying out loud. A head that is fitted and
        // cannot be found looks exactly like one that is not fitted, and both then show nothing.
        if (entry.Head.OpticPart is null)
        {
            ImGui.TextColored(Amber, "Optical director: head subpart not found");
            return;
        }

        if (entry.Head.Profile.RollMarker is not null && entry.Head.RollPart is null)
        {
            ImGui.TextColored(Amber, "Optical director: roll gimbal subpart not found");
        }


        int main = KsaWorld.MainViewportIndex;
        bool onWindow = policy.Viewport >= 0 && policy.Viewport != main;

        // Buttons, all three: each opens or takes something, and a radio or a tick box reads as a
        // setting while a window arrives somewhere else.
        bool spare = KsaWorld.SpareCameraWindows > 0;
        if (!onWindow && !spare) ImGui.BeginDisabled();
        if (Toggle("Camera", onWindow))
        {
            if (onWindow) policy.Viewport = -1;
            else if (KsaWorld.TryOpenCameraWindow(out int opened)) TakeWindow(policy, opened);
        }
        if (!onWindow && !spare) ImGui.EndDisabled();
        Tip(spare || onWindow
                ? "This director's own camera window, with zoom, sensor mode and tracking on the picture. "
                  + "Drag it out of the game onto another monitor. It shows terrain and sky but not KSA's "
                  + "clouds, ocean or lights, and its ground shadows move with the main view."
                : "All four of KSA's camera windows are in use; close one to open another.");

        ImGui.SameLine();
        if (Toggle("Main view", policy.Viewport == main))
        {
            if (policy.Viewport == main) policy.Viewport = -1;
            else TakeMainView(policy, main);
        }
        Tip("Look through this director with the view you fly from.");

        ImGui.SameLine();
        if (Toggle("Map", policy.MapOpen)) TakeMap(policy);
        Tip("A map of the ground under this head, with what it can see on it.");

        if (policy.Viewport == main)
        {
            // Neither reflex works: driving the view puts it in Fixed mode, which reads no input,
            // and Shift+C's switch has no Fixed case. The View menu sets a mode outright.
            ImGui.TextDisabled("  KSA's View > Orbit Camera takes the view back");
        }

        DrawWhatItWatches(entry.Head);
        if (entry.Head.HasLaser) DrawLaser(entry.Head, policy, onWindow);

        ImGui.Text("Aim:");
        ImGui.SameLine();
        ImGui.Checkbox("Track", ref policy.Tracking);
        Tip("Slew onto what the director is holding.");
        ImGui.SameLine();
        ImGui.Checkbox("Mouse", ref policy.MouseAim);
        Tip("Follow the cursor, ahead of tracking and of hand aim.");
        ImGui.SameLine();
        ImGui.Checkbox("Hand", ref policy.Manual);
        Tip("Point it with sliders.");

        // The rest area exists because a head driving its own picture chases a cursor its own
        // turning keeps off centre; from another view there is no such loop.
        if (policy.MouseAim && policy.Viewport == main)
        {
            ImGui.SliderFloat("Rest area (px)", ref policy.MouseDeadZonePx, 0f, 200f);
            Tip("Inside the ring the head holds; outside it, the head follows the cursor.");
        }

        if (policy.Manual)
        {
            // Named and bounded by the head's own gimbal. A pod has no bearing and no elevation,
            // and driving it in those terms cross-couples the two: changing the elevation moves
            // the roll the shell is sent to as well, so the nose turns when only the ball should
            // tilt. It also names directions past the nod stop, which the travel clamp then moves
            // -- leaving the sliders reading one thing and the ball pointing at another.
            bool rollNod = entry.Head.Profile.Gimbal == GimbalKind.RollNod;
            var (first, second) = OpticGeometry.ManualRanges(entry.Head.Profile);

            ImGui.SliderFloat(rollNod ? "Nose roll (deg)" : "Director bearing (deg)",
                              ref policy.ManualBearingDeg, first.Min, first.Max);
            if (rollNod) Tip("Turns the whole nose. The nod then tilts the ball within it.");
            ImGui.SliderFloat(rollNod ? "Sight nod off boresight (deg)" : "Director elevation (deg)",
                              ref policy.ManualElevationDeg, second.Min, second.Max);
            if (rollNod) Tip("Tilts the ball within the nose, which the roll turns.");
        }

        // The camera window carries its own zoom; the main view has nowhere else to put one.
        if (policy.Viewport == main) DrawMagnification(entry.Head, policy);

        DrawOpticSettings(entry.Head, policy);

        // The chosen window has gone, so stop writing to something that is no longer shown.
        if (onWindow && !KsaWorld.IsUsableCameraWindow(policy.Viewport)) policy.Viewport = -1;
    }

    // The laser's switch and what it is doing, in words. On a camera window the switch is on the
    // picture, so here it is reported rather than duplicated. The code is under Settings: it
    // matters only to a weapon homing on it, and the default does.
    private void DrawLaser(OpticalHead head, OpticConfig policy, bool onWindow)
    {
        if (onWindow)
        {
            if (!policy.Lasing)
            {
                ImGui.TextDisabled("Laser: off - LASER on the camera window fires it");
                return;
            }

            ImGui.Text("Laser:");
        }
        else
        {
            if (Toggle(policy.Lasing ? "Stop laser" : "Fire laser", policy.Lasing)) policy.Lasing = !policy.Lasing;
            Tip("Measures the distance to whatever the crosshair is on, and marks it for laser-guided "
                + "weapons. It will not fire through your own craft.");
            if (!policy.Lasing) return;
        }

        ImGui.SameLine();
        if (head.LaserInhibited) ImGui.TextColored(Amber, "blocked by your own craft");
        else if (head.Spot is { } spot) ImGui.Text($"{Distance.Say(spot.RangeMetres)} to {head.SpotOn}");
        else ImGui.TextDisabled($"nothing within {Distance.Say(head.Profile.LaserRangeMetres)}");
    }

    private static void DrawLaserCode(OpticConfig policy)
    {
        ImGui.SetNextItemWidth(70f);
        int code = policy.LaserCode;
        if (ImGui.InputInt("Laser code##laser", ref code, 0, 0) && LaserCode.IsValid(code)) policy.LaserCode = code;
        Tip("A laser-guided weapon homes on the spot carrying its own code, from any craft. Four digits, "
            + "1111 to 1788: the first 1, the second 1-7, the last two 1-8.");
    }

    // A button that shows whether what it opens is open.
    private static bool Toggle(string label, bool on)
    {
        if (on) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.42f, 0.30f, 1f));
        bool pressed = ImGui.Button(label);
        if (on) ImGui.PopStyleColor();
        return pressed;
    }

    // Magnification and symbology. Detents rather than a slider: a real sight has optical stops,
    // and a factor arrived at by dragging is one nobody can return to.
    // There is one main view, so one head at a time may be pointed at it. Secondary viewports
    // need no exclusion -- each is its own window and two heads can fill two of them.
    private void TakeMainView(OpticConfig policy, int main)
    {
        foreach (OpticalHeads.Entry other in _headScratch)
        {
            if (!ReferenceEquals(other.Policy, policy) && other.Policy.Viewport == main)
            {
                other.Policy.Viewport = -1;
            }
        }

        policy.Viewport = main;
    }

    // One head per camera window, from any craft: two writing one camera fight every frame.
    private void TakeWindow(OpticConfig policy, int index)
    {
        foreach (OpticalHeads.Entry other in _heads.All)
        {
            if (!ReferenceEquals(other.Policy, policy) && other.Policy.Viewport == index)
            {
                other.Policy.Viewport = -1;
            }
        }

        policy.Viewport = index;
    }

    private static void DrawMagnification(OpticalHead head, OpticConfig policy)
    {
        ImGui.Text("Zoom:");

        Span<float> stops = stackalloc float[SightZoom.Detents.Length + 1];
        foreach (float detent in stops[..SightZoom.StopsFor(head.Profile.MaxMagnification, stops)])
        {
            ImGui.SameLine();
            bool selected = Math.Abs(policy.Magnification - detent) < 1e-3f;
            if (ImGui.RadioButton($"x{detent:0.#}##zoom", selected)) policy.Magnification = detent;
        }
    }

    // What nobody needs on the way past, folded.
    private void DrawOpticSettings(OpticalHead head, OpticConfig policy)
    {
        if (!ImGui.TreeNode("Settings")) return;

        ImGui.Checkbox("Sight symbology", ref policy.Symbology);
        ImGui.SameLine();
        ImGui.Checkbox("Level the horizon", ref policy.StabiliseHorizon);
        Tip("On: the picture is held against the site's vertical, and near straight up or down the "
            + "roll is carried from frame to frame. Off: the picture is rigid with the head, so it "
            + "rolls with the craft, and sideways stays sideways.");

        if (head.HasLaser) DrawLaserCode(policy);

        // The head's own; who it may watch is its team's, in the Teams window.
        ImGui.Checkbox("Never look at the vehicle I'm flying", ref policy.ProtectControlledVehicle);
        ImGui.TreePop();
    }

    // What the head is following right now, and the only way to stop it.
    //
    // A status, kept apart from the team picker below, which is a policy: one says what the head is
    // doing and the other says what it is allowed to do, and a heading that sounds like the first
    // over controls that are the second is worse than either alone.
    //
    // The Release button is the whole exit. Nothing else clears a designation -- a craft that dies
    // takes its own with it, and ground never dies, so without this a shift-click on a hillside is
    // followed for the rest of the session.
    private void DrawWhatItWatches(OpticalHead head)
    {
        if (head.Designation.Kind != AimpointKind.None)
        {
            ImGui.Text($"Watching: {head.DesignationName}");
            Tip("Designated by hand, which beats whatever the head's own sensor would have picked.");
            ImGui.SameLine();
            if (ImGui.Button("Release")) head.ClearDesignation();
            return;
        }

        ImGui.TextDisabled(head.LockedTrack is { } track
                           ? $"Watching: {track.Contact.DisplayName}"
                           : "Watching: nothing - shift-click the world to point it");
    }
}
