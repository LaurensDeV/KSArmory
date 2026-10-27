using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The rows describing an optical director. Nothing here reads <c>_system</c> or <c>_policy</c>:
/// a craft carrying one director and no armament has every row below and neither of those.
/// </summary>
internal sealed partial class Ui
{
    private void DrawOpticView(OpticalHeads.Entry entry)
    {
        OpticConfig policy = entry.Policy;

        // Said outright: a head fitted and not found otherwise looks like one not fitted.
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

        DrawWhatItWatches(entry.Head, policy);
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
        ImGui.SameLine();
        ImGui.Checkbox("Spot", ref policy.SpotSearch);
        Tip($"Search for a spot somebody else is lasing on code {policy.LaserCode} and lock onto it, "
            + "within range and where the head can look. The code is under Settings.");

        // The rest area exists because a head driving its own picture chases a cursor its own
        // turning keeps off centre; from another view there is no such loop.
        if (policy.MouseAim && policy.Viewport == main)
        {
            ImGui.SliderFloat("Rest area (px)", ref policy.MouseDeadZonePx, 0f, 200f);
            Tip("Inside the ring the head holds; outside it, the head follows the cursor.");
        }

        if (policy.Manual)
        {
            // In the gimbal's own axes: bearing and elevation on a pod cross-couple roll and nod,
            // and name directions past the nod stop that the travel clamp then moves.
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

        if (onWindow && !KsaWorld.IsUsableCameraWindow(policy.Viewport)) policy.Viewport = -1;
    }

    // On a camera window the switch is on the picture, so here it is only reported.
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
            bool beam = head.Profile.Beam is not null;
            if (Toggle(policy.Lasing ? "Stop laser" : "Fire laser", policy.Lasing)) policy.Lasing = !policy.Lasing;
            Tip(beam
                ? "The designator: ranges whatever the crosshair is on and marks it for laser-guided weapons. "
                  + "The megawatt beam is a weapon of its own -- select it in the weapons panel and fire it "
                  + "with FIRE, or let auto-engage use it. Neither fires through your own craft."
                : "Measures the distance to whatever the crosshair is on, and marks it for laser-guided "
                  + "weapons. It will not fire through your own craft.");
            if (beam) DrawChemicals(head);
            if (!policy.Lasing && !head.BeamOn) return;
        }

        ImGui.SameLine();
        if (head.LaserInhibited) ImGui.TextColored(Amber, "blocked by your own craft");
        else if (head.Burning is { } part)
            ImGui.TextColored(Amber, $"burning {part.Id} on {head.SpotOn}: {PartHealth.World.Of(part):P0} left");
        else if (head.Spot is { } spot) ImGui.Text($"{Distance.Say(spot.RangeMetres)} to {head.SpotOn}");
        else if (head.InCloud) ImGui.TextColored(Amber, "stopped by cloud");
        else ImGui.TextDisabled($"nothing within {Distance.Say(head.Profile.LaserRangeMetres)}");
    }

    private static void DrawChemicals(OpticalHead head)
    {
        ImGui.SameLine();
        double left = head.ChemicalSecondsLeft;
        if (left > 0.0) ImGui.TextDisabled($"{left:F0} s of laser chemicals left");
        else ImGui.TextColored(Amber, "laser chemicals spent");
    }

    private static void DrawLaserCode(OpticConfig policy)
    {
        ImGui.SetNextItemWidth(70f);
        int code = policy.LaserCode;
        if (ImGui.InputInt("Laser code##laser", ref code, 0, 0) && LaserCode.IsValid(code)) policy.LaserCode = code;
        Tip("A laser-guided weapon homes on the spot carrying its own code, from any craft. Four digits, "
            + "1111 to 1788: the first 1, the second 1-7, the last two 1-8.");
    }

    private static bool Toggle(string label, bool on)
    {
        if (on) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.42f, 0.30f, 1f));
        bool pressed = ImGui.Button(label);
        if (on) ImGui.PopStyleColor();
        return pressed;
    }

    // One main view, so one head at a time; camera windows are excluded per window in TakeWindow.
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

    // Detents rather than a slider: a factor arrived at by dragging is one nobody can return to.
    private static void DrawMagnification(OpticalHead head, OpticConfig policy)
    {
        ImGui.Text("Zoom:");

        Span<float> stops = stackalloc float[SightZoom.Detents.Length + 1];
        int i = 0;
        foreach (float detent in stops[..SightZoom.StopsFor(head.Profile.MaxMagnification, stops)])
        {
            // Six to a line, the next lines indented under the first: the YAL-1's eleven stops
            // run off the panel in one.
            if (i > 0 && i % 6 == 0) ImGui.Dummy(new float2(ImGui.CalcTextSize("Zoom:").X, 0f));
            ImGui.SameLine();
            i++;

            bool selected = Math.Abs(policy.Magnification - detent) < 1e-3f;
            if (ImGui.RadioButton($"x{detent:0.#}##zoom", selected)) policy.Magnification = detent;
        }
    }

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

    // Release is the only thing that clears a designation on the ground, which never dies.
    private void DrawWhatItWatches(OpticalHead head, OpticConfig policy)
    {
        if (head.Designation.Kind != AimpointKind.None)
        {
            ImGui.Text($"Watching: {head.DesignationName}");
            Tip("Designated by hand, which beats whatever the head's own sensor would have picked.");
            ImGui.SameLine();
            if (ImGui.Button("Release")) head.ClearDesignation();
            return;
        }

        if (head.FoundSpot is not null)
        {
            ImGui.Text($"Watching: the laser spot on code {policy.LaserCode}");
            return;
        }

        if (head.LockedTrack is { } track)
        {
            ImGui.TextDisabled($"Watching: {track.Contact.DisplayName}{CuedBy(head.CuedBy)}");
            return;
        }

        string? pictured = head.Pictured switch
        {
            KSA.Vehicle v => KsaWorld.DisplayName(v),
            null => null,
            _ => "a round",
        };

        ImGui.TextDisabled(pictured is not null && !policy.Tracking ? $"Watching: nothing - the craft's picture holds {pictured}; Track follows it"
                           : pictured is not null ? $"Watching: nothing - {pictured} is beyond this head's reach"
                           : policy.SpotSearch ? $"Watching: nothing - no spot on code {policy.LaserCode} in view"
                           : "Watching: nothing - shift-click the world to point it");
    }

    private static string CuedBy(CueSource source) => source switch
    {
        CueSource.Launcher => " - cued by its launcher",
        CueSource.Picture => " - from the craft's picture",
        _ => "",
    };
}
