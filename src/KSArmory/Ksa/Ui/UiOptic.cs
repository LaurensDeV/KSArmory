using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The rows describing an optical director: what it is looking at, what it is looking through, and
/// which contacts it will watch.
///
/// <para>Split from <see cref="Ui"/>'s weapons-system panes because a director is not one. It is a
/// part in its own right, crewed per head rather than per weapons system, and nothing here reads
/// <c>_battery</c> or <c>_policy</c> — a craft carrying one director and no armament has every row
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

        DrawGimbalState(entry.Head);

        // Only windows the player can actually see. KSA keeps offscreen viewports of its own, and
        // offering those means picking a view that shows nothing, which is indistinguishable from
        // the feature being broken.
        KsaWorld.CollectUsableViewports(_viewports);

        int main = KsaWorld.MainViewportIndex;

        ImGui.Text("Director view:");
        ImGui.SameLine();

        if (ImGui.RadioButton("off", policy.Viewport < 0)) policy.Viewport = -1;

        // The main view first, because it is the one that works. It is offered whatever else is
        // open and needs nothing opening, so the head is usable on a bare game.
        ImGui.SameLine();
        if (ImGui.RadioButton("main view", policy.Viewport == main)) TakeMainView(policy, main);

        foreach (int index in _viewports)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(KsaWorld.DescribeViewport(index), policy.Viewport == index))
            {
                policy.Viewport = index;
            }
        }

        if (policy.Viewport == main)
        {
            // Named explicitly because neither reflex works. Driving the view puts it in Fixed
            // mode, and FixedController reads no input at all, so the mouse is inert; Shift+C
            // routes through Viewport.NextCameraMode, whose switch has no Fixed case and returns
            // false. The View menu sets a mode outright, which is why it is the one that works.
            ImGui.TextDisabled("  borrowed while selected. KSA's View > Orbit Camera takes it");
            ImGui.TextDisabled("  back and switches this off - the mouse and Shift+C will not");
        }
        else if (policy.Viewport >= 0)
        {
            ImGui.TextDisabled("  no sky or terrain detail here - KSA renders secondary views");
            ImGui.TextDisabled("  without the atmosphere pass. See docs/BLOCKED-ON-KSA.md");
        }

        // A button rather than a tick box: it opens a window, and a checkmark reads as "this
        // setting is on" while the window arrives somewhere else unannounced. Tinted while open,
        // which is what a tick box was being asked to say.
        // The local matters: TakeMap flips MapOpen, so reading it again after the button pops a
        // style that was never pushed. Same defect as the Weapons button above.
        bool mapTinted = policy.MapOpen;
        if (mapTinted) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.42f, 0.30f, 1f));
        if (ImGui.Button("Map")) TakeMap(policy);
        if (mapTinted) ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.TextDisabled("the ground under this head, with what it can see on it");

        ImGui.Checkbox("Track with the director", ref policy.Tracking);
        ImGui.SameLine();
        ImGui.Checkbox("Aim by hand", ref policy.Manual);
        ImGui.SameLine();
        ImGui.Checkbox("Mouse aim", ref policy.MouseAim);

        if (policy.MouseAim)
        {
            ImGui.TextDisabled("  the head follows the cursor, ahead of tracking and of the sliders");

            // Only meaningful on the main view: the rest area exists because a head driving its
            // own picture chases a cursor its own turning keeps off centre, and pointing at a site
            // from another view has no such loop.
            if (policy.Viewport == KsaWorld.MainViewportIndex)
            {
                ImGui.SliderFloat("Rest area (px)", ref policy.MouseDeadZonePx, 0f, 200f);
                ImGui.TextDisabled("  inside the ring the head holds; outside it follows");
            }
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
            ImGui.SliderFloat(rollNod ? "Sight nod off boresight (deg)" : "Director elevation (deg)",
                              ref policy.ManualElevationDeg, second.Min, second.Max);

            if (rollNod)
            {
                ImGui.TextDisabled("  roll turns the whole nose; nod tilts the ball within it");
            }
        }

        if (policy.Viewport >= 0) DrawSightLine(entry.Head, policy, main);

        // The chosen window has gone, so stop writing to something that is no longer shown. The
        // main view is exempt: it is never in the collected list, and it cannot be closed.
        if (policy.Viewport >= 0 && policy.Viewport != main && !_viewports.Contains(policy.Viewport))
        {
            policy.Viewport = -1;
        }
    }

    // Which mechanism this head is, and where it has got to in that mechanism's own terms.
    //
    // Worth a line because a head at its stop and a head with nothing to look at are the same
    // picture from outside -- the same reason the header strip says why fire is being held. The
    // two gimbals are described in different words on purpose: a pod has no elevation and a mast
    // head has no roll, and quoting either in the other's terms is a number nobody can act on.
    private static void DrawGimbalState(OpticalHead head)
    {
        OpticProfile profile = head.Profile;
        MountFrame mount = head.Mount;
        double3 aim = head.AimWhenDrawn;

        if (profile.Gimbal != GimbalKind.RollNod)
        {
            double elevation = double.RadiansToDegrees(OpticGeometry.ElevationRad(mount, aim));

            ImGui.TextDisabled($"  elevating head: {elevation:F0} deg over the mounting face "
                               + $"({profile.MinElevationDeg:F0} to {profile.MaxElevationDeg:F0})");
            return;
        }

        double off = double.RadiansToDegrees(OpticGeometry.OffBoresightRad(mount, aim));
        double roll = double.RadiansToDegrees(OpticGeometry.RollAngleRad(mount, aim));

        ImGui.TextDisabled($"  roll-nod gimbal: nose rolled {roll:F0} deg, sight nodded {off:F0} deg "
                           + $"off the centreline (stop {profile.MaxOffBoresightDeg:F0})");

        if (off >= profile.MaxOffBoresightDeg - 0.5)
        {
            ImGui.TextColored(Amber, "  at the nod stop: the gimbal will go no further");
        }
        else if (off <= profile.KeyholeDeg + 0.5)
        {
            ImGui.TextColored(Amber, "  in the keyhole: dead ahead has no roll angle at all");
        }
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

    private void DrawSightLine(OpticalHead head, OpticConfig policy, int main)
    {
        ImGui.Text("Magnification:");

        foreach (float detent in SightZoom.Detents)
        {
            ImGui.SameLine();
            bool selected = Math.Abs(policy.Magnification - detent) < 1e-3f;
            if (ImGui.RadioButton($"x{detent:0.#}##zoom", selected)) policy.Magnification = detent;
        }

        // Only on the main view. A secondary viewport's camera is positioned outright rather than
        // driven through the borrowed-view path, so nothing writes its field of view.
        if (policy.Viewport != main)
        {
            ImGui.TextDisabled("  the main view only - nothing sets a secondary view's zoom");
        }

        ImGui.Checkbox("Sight symbology", ref policy.Symbology);
        ImGui.SameLine();
        ImGui.Checkbox("Level the horizon", ref policy.StabiliseHorizon);

        ImGui.TextDisabled(policy.StabiliseHorizon
            ? "  held against the site's vertical; near straight up or down it carries"
            : "  rigid with the head - it rolls with the craft, and sideways stays sideways");

        ImGui.Separator();
        DrawDirectorIff(head, policy);
    }

    // Who this director will look at. Its own, not the weapon's: a head finds its own targets
    // through its own sensor, and a craft can carry one with no armament at all.
    //
    // The team is picked off the session roster rather than typed. A second free-text box would
    // share _ownTeamEntry with the weapon's, so typing in one would show in the other; and the
    // roster is the list of names that exist, which is what a picker wants anyway.
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
            ImGui.SameLine();
            if (ImGui.Button("Release")) head.ClearDesignation();

            ImGui.TextDisabled("  designated by hand; it beats whatever the set would have picked");
            return;
        }

        ImGui.TextDisabled(head.LockedTrack is { } track
                           ? $"Watching: {track.Contact.DisplayName} (its own pick)"
                           : "Watching: nothing on scope");

        ImGui.TextDisabled("  shift-click the world to point it at something");
    }

    private void DrawDirectorIff(OpticalHead head, OpticConfig policy)
    {
        IffPolicy iff = policy.Iff;

        DrawWhatItWatches(head);

        ImGui.Checkbox("Never look at the vehicle I'm flying",
                       ref policy.ProtectControlledVehicle);

        // Only when there is something to pick. With no teams the whole node held two lines of
        // prose and no control, which is a fold that opens onto nothing -- and one of those lines
        // explained an implementation decision to somebody who never asked.
        if (_config.TeamNames.Count == 0)
        {
            ImGui.TextDisabled("no teams declared; add one under Teams and IFF to sort contacts");
            return;
        }

        if (!ImGui.TreeNode("Who it may watch")) return;

        ImGui.Text($"Own team: {iff.OwnTeam ?? "(none)"}");

        for (int i = 0; i < _config.TeamNames.Count; i++)
        {
            string team = _config.TeamNames[i];

            // PushID rather than a ## suffix: several directors can be drawn in one window once
            // the panel lists them, and a label is only unique within its own id scope.
            ImGui.PushID(i);

            bool own = string.Equals(team, iff.OwnTeam, StringComparison.OrdinalIgnoreCase);
            // Through `policy` rather than the local, so the write says which object it lands on.
            if (ImGui.RadioButton(team, own)) policy.Iff.OwnTeam = own ? null : team;

            if (!own)
            {
                bool allied = iff.AlliedTeams.Contains(team);
                bool neutral = iff.NeutralTeams.Contains(team);

                ImGui.SameLine();
                if (ImGui.Checkbox("allied", ref allied))
                {
                    Toggle(iff.AlliedTeams, team, allied);
                    if (allied) iff.NeutralTeams.Remove(team);
                }

                ImGui.SameLine();
                if (ImGui.Checkbox("neutral", ref neutral))
                {
                    Toggle(iff.NeutralTeams, team, neutral);
                    if (neutral) iff.AlliedTeams.Remove(team);
                }
            }

            ImGui.PopID();
        }

        // The same three switches the weapon has, worded for an instrument: a director watches
        // rather than engages, so "engage neutrals" would describe something it cannot do.
        bool unknown = iff.EngageUnknown;
        if (ImGui.Checkbox("Watch unknown contacts", ref unknown)) iff.EngageUnknown = unknown;

        bool neutrals = iff.EngageNeutral;
        if (ImGui.Checkbox("Watch neutrals", ref neutrals)) iff.EngageNeutral = neutrals;

        bool friendly = iff.ProtectFriendly;
        if (ImGui.Checkbox("Never watch friendlies", ref friendly)) iff.ProtectFriendly = friendly;

        ImGui.TreePop();
    }
}
