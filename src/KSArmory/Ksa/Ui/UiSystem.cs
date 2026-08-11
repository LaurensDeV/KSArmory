using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The panes that describe one weapons system: what it is made of, what it can see, and what its
/// drives and weapons are doing.
///
/// <para>Every method here reads <c>_battery</c> and <c>_policy</c>, which are <b>not</b> fixed —
/// <see cref="Ui.Focus"/> points them at whichever system is being drawn, and the shell calls it
/// before any of this runs. A pane added here that is reached another way will quietly describe
/// the wrong installation.</para>
/// </summary>
internal sealed partial class Ui
{
    // What this system is made of, read off the craft rather than from a table -- which is the
    // whole point of surveying, and why it belongs to a system rather than to the mod's menu.
    private void DrawComponents(KSA.Vehicle craft)
    {
        KsaWorld.SurveyParts(craft, _surveyed);
        WeaponInventory inv = WeaponSurvey.Survey(_surveyed, Arsenal.Components);

        ImGui.TextDisabled($"{_surveyed.Count} part(s) on the craft");

        // IsInstallation, not IsWeaponSystem: a craft carrying one director and no armament is
        // something this mod recognises, and it is the case this pane most needs to describe.
        if (!inv.IsInstallation)
        {
            ImGui.TextColored(Grey, "  Nothing this mod recognises.");
            ImGui.TextDisabled("  A craft becomes an installation by carrying a part from");
            ImGui.TextDisabled("  Arsenal.Components.");
            return;
        }

        _heads.On(craft, _headScratch);

        // One group per role, read off the enum rather than listed here. A hand-written list
        // silently omits a role added later, which reads as the survey not finding one.
        foreach (WeaponRole role in Enum.GetValues<WeaponRole>())
        {
            int n = inv.CountOf(role);
            if (n == 0) continue;

            ImGui.SeparatorText(GroupName(role));

            int nth = 0;
            for (int i = 0; i < inv.Components.Count; i++)
            {
                FoundComponent c = inv.Components[i];
                if (c.Role != role) continue;

                // Per component, not per label. Several of one kind draw identical controls, and
                // ImGui keys a widget on its label within the current id scope -- so without this
                // the second director's tick boxes are the first's.
                ImGui.PushID(i);
                DrawComponentRow(c, role, nth, n);
                ImGui.PopID();

                nth++;
            }
        }
    }

    // One component: where it sits, and whatever it is that the panel can drive.
    private void DrawComponentRow(FoundComponent c, WeaponRole role, int nth, int of)
    {
        string label = of > 1 ? $"{c.DisplayName}  {nth + 1} of {of}" : c.DisplayName;

        if (!ImGui.TreeNode(label)) return;

        double3 at = c.PositionVehicleAsmb;
        ImGui.TextDisabled($"at ({at.X:F2}, {at.Y:F2}, {at.Z:F2}) m");

        // A director is its own instrument and needs no weapons system. Everything else here
        // describes one, and reads `_battery` -- which Focus leaves unassigned on a craft that
        // carries no armament. A craft with one director and a provided sensor row reaches this
        // with nothing crewed, so the guard is on the path rather than in each handler.
        if (role == WeaponRole.Camera)
        {
            DrawCameraComponent(nth);
        }
        else if (!_crewed)
        {
            ImGui.TextDisabled("no weapons system on this craft");
        }
        else
        {
            switch (role)
            {
                case WeaponRole.FireControl: DrawFireControlComponent(); break;
                case WeaponRole.Launcher: DrawLauncherComponent(c); break;
                case WeaponRole.Gun: DrawGunComponent(c); break;
                case WeaponRole.Sensor: DrawSensorComponent(c); break;
            }
        }

        ImGui.TreePop();
    }

    // Whether a row is the one fire control is actually running.
    //
    // Every launcher part registers as a Launcher and *provides* its gun and sensor rows, so a
    // craft carrying two of them lists two of each while WeaponSystems crews one -- LauncherOrdinal
    // is a const 0. Matching the crewed profile by name rather than counting rows is what keeps a
    // second Pantsir's cannon from reporting the first one's belt.
    private bool IsCrewed(FoundComponent c)
        => string.Equals(c.DisplayName, _profile.DisplayName, StringComparison.Ordinal);

    // Said once per row that is fitted and not run, rather than left to be inferred from a row
    // full of blanks. Without it the panel shows three loaded rails and fires one.
    private void NotRun()
    {
        ImGui.TextColored(Amber, "fitted, not run");
        ImGui.TextDisabled("  one weapons system per craft: another part of this kind is crewed");
    }

    // The launcher: what it holds, how it is laid, and the switches that belong to it.
    private void DrawLauncherComponent(FoundComponent c)
    {
        if (!IsCrewed(c)) { NotRun(); return; }

        if (_battery.Launcher is null)
        {
            ImGui.TextColored(Red, "not resolved");
            ImGui.TextDisabled("  the part is fitted but its subparts were not found");
            return;
        }

        DrawArmamentTally(ArmamentKind.Tubes);

        if (_battery.ReloadRemaining > 0.0)
        {
            ImGui.Text($"Reloading: {_battery.ReloadRemaining:F1}s");
            ImGui.ProgressBar(
                (float)(1.0 - _battery.ReloadRemaining / Math.Max(0.001f, _profile.ReloadSeconds)));
        }

        DrawTurretLine();

        if (ImGui.Button("Reload")) _battery.Reload();
        ImGui.SameLine();
        if (ImGui.Button("Safe all")) _battery.SafeAll();

        // A view control, so it sits with the weapon whose rounds it would ride.
        ImGui.Checkbox("Chase this launcher's rounds", ref _policy.ChaseRounds);
        ImGui.TextDisabled("  rides the camera behind a round it fires; the view comes back after");

        // Only where it answers the right question. A guided round goes where it is steered, so a
        // ballistic pipper over one is a ring in the wrong place with nothing to say so.
        if (_fit.Drops)
        {
            ImGui.Checkbox("Bomb sight", ref _policy.DrawBombSight);
            ImGui.TextDisabled("  where a store released now would land, flown rather than solved");
        }
    }

    // The cannon: its belt, and whether it is live.
    private void DrawGunComponent(FoundComponent c)
    {
        if (!IsCrewedProvider(c)) { NotRun(); return; }

        DrawArmamentTally(ArmamentKind.Belt);
        ImGui.TextDisabled(_battery.GunsAreLaid ? "  laid" : "  not laid");
    }

    // The set: what it is holding right now. Its numbers are on the Tuning tab, because they
    // belong to the profile and every system running that loadout shares them.
    private void DrawSensorComponent(FoundComponent c)
    {
        if (!IsCrewedProvider(c))
        {
            ImGui.TextDisabled("its own set; not the one fire control reads");
            return;
        }

        DrawRadarState();
    }

    // A provided row belongs to whichever part declared it, and only the crewed part's provided
    // rows describe the running system.
    //
    // Both sides resolve through the registry, and they have to: a provided row is declared as a
    // profile's DisplayName, so anything else compared against it is a second name for one thing.
    // The gun arm read Armament.Label instead -- which is the belt's *heading*, "Cannon", against a
    // row called "2A38M 30 mm cannon". Those never matched, so every Pantsir reported a working
    // cannon as "fitted, not run" on a craft with one launcher and nothing else going on. Fire
    // control never consulted this, so the gun fired throughout and only the panel lied.
    private bool IsCrewedProvider(FoundComponent c)
    {
        if (string.Equals(c.DisplayName, Arsenal.SensorNamed(_profile.Sensor).DisplayName,
                          StringComparison.Ordinal))
        {
            return true;
        }

        return _fit.Describes(ArmamentKind.Belt, c.DisplayName);
    }

    // Everything about releasing a weapon, on the part that decides it.
    private void DrawFireControlComponent()
    {
        ImGui.Checkbox("Master arm", ref _policy.Armed);
        ImGui.SameLine();
        ImGui.Checkbox("Auto engage", ref _policy.AutoEngage);

        if (ImGui.Button("FIRE")) _battery.FireAtLock();
        ImGui.SameLine();
        if (ImGui.Button("Reset settings") && _battery.Platform is { } craft)
        {
            SettingsStore.Forget(KsaWorld.DisplayName(craft));
            new SystemSettings().ApplyTo(_policy);
            _batteries.WriteNow();
            Log.Info($"settings reset for {KsaWorld.DisplayName(craft)}");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Back to defaults, and forgotten from the settings file.\n"
                             + "This resets the whole installation, not this component.");
        }

        ImGui.Checkbox("Never target the vehicle I'm flying", ref _policy.ProtectControlledVehicle);

        ImGui.Checkbox("Aim with the mouse", ref _policy.MouseAim);
        if (_policy.MouseAim)
        {
            ImGui.TextDisabled("  The launcher and the optical head follow the cursor. Auto-engage");
            ImGui.TextDisabled("  still decides when to fire; the drives still have to settle first.");
        }

        ImGui.Checkbox("Fire at the mouse", ref _policy.MouseFire);
        if (_policy.MouseFire)
        {
            ImGui.TextDisabled("  Click the ground to send a round there. No target and no lock");
            ImGui.TextDisabled("  needed - the ring shows where, and turns red when it would refuse.");
            if (!_policy.Armed) ImGui.TextColored(Amber, "  Master arm is off, so clicks do nothing.");
        }
    }

    // One armament's reading, or nothing when the system carries none of that kind.
    private void DrawArmamentTally(ArmamentKind kind)
    {
        if (_fit.FirstOf(kind) is not { } arm) return;

        (int remaining, bool firing) = LiveState(_battery, arm);

        if (firing) ImGui.TextColored(Red, arm.Describe(remaining, firing));
        else ImGui.Text(arm.Describe(remaining, firing));

        // Beside its own reading rather than with the master arm. Whether this weapon is live is
        // a fact about this weapon; what fire control decides is whether anything may shoot.
        ImGui.SameLine();
        ImGui.Checkbox($"live##{kind}", ref Armament.EnabledIn(_policy, kind));
    }

    // The nth director's own controls, under the nth camera row.
    //
    // Bound by position: KsaWorld.SurveyParts and OpticParts.FindAll both walk the craft's part
    // span in index order, so the nth camera component is the nth head. A survey that ever
    // stopped agreeing would silently pair a row with the wrong instrument, so a shortfall is
    // reported rather than assumed away.
    private void DrawCameraComponent(int nth)
    {
        if (nth >= _headScratch.Count)
        {
            ImGui.TextColored(Amber, "not crewed - no head resolved for this part");
            return;
        }

        DrawOpticView(_headScratch[nth]);
    }

    // The two facts that belong to the installation rather than to any part of it, plus a word
    // about the clock when it is stopping the thing working.
    //
    // Above the tab bar rather than inside a tab. Every gate in fire control returns quietly, so
    // an unarmed system, one with no lock, one still settling and one whose drives the engine
    // refused all look identical from outside: this line is the only thing that separates them,
    // and it is no use on a tab nobody is looking at.
    //
    // Not inside the fire-control component row either, for the same reason. A row folds, and a
    // headline behind a disclosure triangle is worse than one behind a tab.
    private void DrawSystemHeader()
    {
        if (_battery.Platform is null)
        {
            ImGui.TextColored(Grey, "No platform - take control of a vehicle.");
            ImGui.Separator();
            return;
        }

        if (_battery.Hold is { } why) ImGui.TextColored(Amber, $"Holding fire: {why}");
        else ImGui.TextColored(Green, "Clear to fire");

        if (_battery.Rounds.Count > 0)
        {
            ImGui.SameLine();
            ImGui.Text($"   In flight: {_battery.Rounds.Count}");
        }

        DrawClockWarning();
        ImGui.Separator();
    }

    // Only when the clock is a problem. Fire control runs on simulated time, so a paused or
    // heavily warped world explains a silent system -- and unsaid, that is indistinguishable from
    // tracking being broken. The ordinary cases say nothing: the game has its own speed readout,
    // and repeating it here is a line that is always present and never news.
    private void DrawClockWarning()
    {
        if (KsaWorld.IsPaused)
        {
            ImGui.TextDisabled("Paused - stopped with the world");
            return;
        }

        if (_warp.Yielded)
        {
            ImGui.TextColored(Red, "Warp not held - something else is driving the speed control");
            ImGui.TextDisabled("  Rounds in flight will lag the world and miss.");
            return;
        }

        if (_warp.Holding)
        {
            ImGui.TextColored(Amber,
                $"Warp held at {KsaWorld.SimulationSpeed:0.#}x - {_warp.HeldSpeed:F0}x returns "
                + "when the rounds land");
            return;
        }

        if (KsaWorld.SimulationSpeed > MaxTrackableWarp && !_config.LimitWarpInFlight)
        {
            ImGui.TextColored(Amber,
                $"Warp {KsaWorld.SimulationSpeed:F0}x - too fast to guide; rounds will lag");
        }
    }

    // What the set is holding. Two lines rather than four: the name, the range and the closing
    // speed are one fact about one contact, and CPA is the separate one -- it is what decides
    // engageability, so it earns its own line and nothing else does.
    private void DrawRadarState()
    {
        if (_battery.Radar.Locked is not { } locked)
        {
            ImGui.TextColored(Grey, "Radar: no threat");
            return;
        }

        bool solution = _battery.Radar.HasFiringSolution;

        ImGui.TextColored(solution ? Red : Amber, solution ? "LOCKED" : "acquiring...");
        ImGui.SameLine();
        ImGui.Text($"{locked.Contact.DisplayName}   {locked.Range / 1000.0:F2} km, "
                   + $"closing {locked.ClosingSpeed:F0} m/s");
        ImGui.TextDisabled($"  CPA {locked.ClosestApproach:F0} m in "
                           + $"{locked.TimeToClosestApproach:F1}s");
    }

    // Which of the game's camera views the optical head drives. KSA opens the views; a mod can
    // only borrow one, so this is a picker rather than a switch.
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
            ImGui.SliderFloat("Director bearing (deg)", ref policy.ManualBearingDeg, -180f, 180f);
            ImGui.SliderFloat("Director elevation (deg)", ref policy.ManualElevationDeg,
                              entry.Head.Profile.MinElevationDeg, entry.Head.Profile.MaxElevationDeg);
        }

        if (policy.Viewport >= 0) DrawSightLine(entry.Head, policy, main);

        // The chosen window has gone, so stop writing to something that is no longer shown. The
        // main view is exempt: it is never in the collected list, and it cannot be closed.
        if (policy.Viewport >= 0 && policy.Viewport != main && !_viewports.Contains(policy.Viewport))
        {
            policy.Viewport = -1;
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

    private void DrawTurretLine()
    {
        if (_battery.Launcher is null) return;

        // A launcher with nothing to lay is not a launcher whose drives are missing, and saying
        // "subpart not found" at one of them reads as a fault on a system that is working.
        if (!_fit.Aims)
        {
            ImGui.TextDisabled("Mount: fixed - it shoots where the craft points");
            return;
        }

        if (_fit.Traverses && _battery.TurretPart is null)
        {
            ImGui.TextColored(Amber, "Turret: subpart not found (fixed forward)");
            return;
        }

        if (_battery.AnyDriveRefused)
        {
            string frozen = string.Join(", ",
                Enum.GetValues<DriveChannel>()
                    .Where(c => !_battery.DriveWorks(c))
                    .Select(c => c.ToString().ToLowerInvariant()));
            ImGui.TextColored(Red, $"Drive: engine refused the transform write ({frozen})");
            if (!_battery.DriveWorks(DriveChannel.Turret) || !_battery.DriveWorks(DriveChannel.Pods))
            {
                ImGui.TextColored(Red, "Holding fire: the tubes cannot be laid");
                return;
            }
        }

        double bearing = float.RadiansToDegrees((float)_battery.Turret.BearingRad);
        if (bearing < 0.0) bearing += 360.0;
        double elevation = float.RadiansToDegrees((float)_battery.Turret.ElevationRad);
        string aim = $"Turret: {bearing:F0} deg, elev {elevation:F0} deg";

        if (!_policy.TurretTracking)
        {
            ImGui.TextColored(Grey, $"{aim} (tracking off)");
        }
        else if (_battery.IsLaid)
        {
            ImGui.TextColored(Green, $"{aim} - laid");
        }
        else if (_battery.Turret.OnTarget)
        {
            ImGui.TextColored(Amber, $"{aim} - settling");
        }
        else
        {
            double error = Math.Abs(float.RadiansToDegrees((float)_battery.Turret.ErrorRad));
            double elevError = Math.Abs(float.RadiansToDegrees((float)_battery.Turret.ElevationErrorRad));
            ImGui.TextColored(Amber, $"{aim} - slewing ({Math.Max(error, elevError):F0} deg to go)");
        }
    }

    private void DrawTrackList()
    {
        if (!_fit.Searches)
        {
            ImGui.TextDisabled("No sensor: nothing to hold a track, and nothing to designate.");
            return;
        }

        if (_battery.Radar.Tracks.Count == 0)
        {
            ImGui.TextDisabled("scope clear");
        }

        // An empty scope with craft in the world reads as a broken radar. Saying how many the
        // planet is hiding is the difference between that and a working one with nothing in view.
        if (_battery.Radar.MaskedByTerrain > 0)
        {
            ImGui.TextDisabled($"  {_battery.Radar.MaskedByTerrain} behind the horizon");
        }

        for (int i = 0; i < _battery.Radar.Tracks.Count; i++)
        {
            Track t = _battery.Radar.Tracks[i];
            bool isLock = ReferenceEquals(t, _battery.Radar.Locked);

            float4 colour = isLock ? Red : AllegianceColour(t.Allegiance);
            string mark = t.Allegiance == Allegiance.Friendly ? "F"
                : t.Allegiance == Allegiance.Neutral ? "N"
                : t.Allegiance == Allegiance.Hostile ? "H" : "?";

            ImGui.TextColored(colour,
                $"{(isLock ? ">" : " ")}[{mark}] {t.Contact.DisplayName}  " +
                $"{t.Range / 1000.0:F2} km  cpa {t.ClosestApproach:F0} m  " +
                $"{(t.RoundsAssigned > 0 ? $"[{t.RoundsAssigned} away]" : "")}");

            ImGui.SameLine();
            if (ImGui.Button($"designate##{i}"))
            {
                _battery.Radar.ManualDesignation = t.Contact.Handle;
            }
        }

        if (_battery.Radar.ManualDesignation is not null && ImGui.Button("Clear designation"))
        {
            _battery.Radar.ManualDesignation = null;
        }

    }
}
