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

        if (!inv.IsWeaponSystem)
        {
            ImGui.TextColored(Grey, "  Nothing this mod recognises.");
            ImGui.TextDisabled("  A craft becomes a weapons system by carrying a part from");
            ImGui.TextDisabled("  Arsenal.Components. Only the launcher is registered so far.");
            return;
        }

        // One group per role, read off the enum rather than listed here. A hand-written list
        // silently omits a role added later, which reads as the survey not finding one.
        foreach (WeaponRole role in Enum.GetValues<WeaponRole>())
        {
            int n = inv.CountOf(role);
            if (n == 0) continue;

            if (!ImGui.TreeNode($"{GroupName(role)} ({n})")) continue;

            for (int i = 0; i < inv.Components.Count; i++)
            {
                FoundComponent c = inv.Components[i];
                if (c.Role != role) continue;

                double3 at = c.PositionVehicleAsmb;
                ImGui.Text(c.DisplayName);
                ImGui.TextDisabled($"  at ({at.X:F2}, {at.Y:F2}, {at.Z:F2}) m");
            }

            ImGui.TreePop();
        }
    }

    private void DrawSystemPane()
    {
        DrawStatus();
        ImGui.Separator();
        DrawWeapons();
    }

    private void DrawStatus()
    {
        if (_battery.Platform is null)
        {
            ImGui.TextColored(Grey, "No platform - take control of a vehicle.");
            return;
        }

        string platform = KsaWorld.DisplayName(_battery.Platform);
        bool flyingIt = ReferenceEquals(_battery.Platform, KsaWorld.ControlledVehicle);
        ImGui.Text($"Platform: {platform}");
        if (!flyingIt) ImGui.TextDisabled("  (you are flying something else; the battery stays here)");

        if (_battery.Launcher is not null)
        {
            ImGui.TextColored(Green, $"Launcher: {_profile.DisplayName} fitted");
        }
        else if (_config.RequireLauncherPart)
        {
            ImGui.TextColored(Red, "Launcher: none fitted");
            ImGui.TextDisabled($"  Add the {_profile.DisplayName} in the editor,");
            ImGui.TextDisabled("  or untick 'Require launcher part' below.");
        }
        else
        {
            ImGui.TextColored(Amber, "Launcher: none (part requirement off)");
        }

        if (_policy.Armed) ImGui.TextColored(Red, "MASTER ARM: ARMED");
        else ImGui.TextColored(Green, "MASTER ARM: SAFE");

        // One reading per armament the system is fitted with. A launcher with no tubes and one
        // with no cannon both describe themselves here without this knowing which it is, so
        // "Rounds: 0/0" against a weapon that never had a magazine cannot arise.
        IReadOnlyList<Armament> readings = _fit.Armaments;
        for (int i = 0; i < readings.Count; i++)
        {
            Armament arm = readings[i];
            (int remaining, bool firing) = LiveState(_battery, arm);

            ImGui.SameLine();
            if (firing) ImGui.TextColored(Red, $"   {arm.Describe(remaining, firing)}");
            else ImGui.Text($"   {arm.Describe(remaining, firing)}");
        }

        if (_battery.ReloadRemaining > 0.0)
        {
            ImGui.Text($"Reloading: {_battery.ReloadRemaining:F1}s");
            ImGui.ProgressBar(
                (float)(1.0 - _battery.ReloadRemaining / Math.Max(0.001f, _profile.ReloadSeconds)));
        }

        // The battery runs on simulated time, so a paused or heavily warped game is not a fault
        // but it does explain a silent battery. Saying so beats the report this came from,
        // which was "tracking is completely messed up".
        if (KsaWorld.IsPaused)
        {
            ImGui.Text("Paused - the battery is stopped with the world");
        }
        else if (_warp.Holding)
        {
            ImGui.TextColored(Amber,
                $"Warp held at {KsaWorld.SimulationSpeed:0.#}x - {_warp.HeldSpeed:F0}x returns "
                + "when the rounds land");
        }
        else if (_warp.Yielded)
        {
            ImGui.TextColored(Red, "Warp not held - something else is driving the speed control");
            ImGui.TextDisabled("  Rounds in flight will lag the world and miss.");
        }
        else if (KsaWorld.SimulationSpeed > 1.0)
        {
            double warp = KsaWorld.SimulationSpeed;
            ImGui.Text(warp > MaxTrackableWarp && !_config.LimitWarpInFlight
                ? $"Warp {warp:F0}x - too fast to guide; rounds will lag the world"
                : $"Warp {warp:F0}x");
        }

        DrawSlowMotion();

        // Should stay at zero. If it does not, the render rate is outrunning the simulation
        // clock and that is worth knowing, because it explains stuttering round bodies.
        if (_battery.FramesWithoutSimStep > 0)
        {
            ImGui.TextColored(Amber, $"Frames with no sim step: {_battery.FramesWithoutSimStep}");
        }

        DrawTurretLine();

        var locked = _battery.Radar.Locked;
        if (locked is null)
        {
            ImGui.TextColored(Grey, "Radar: no threat");
        }
        else
        {
            float4 colour = _battery.Radar.HasFiringSolution ? Red : Amber;
            ImGui.TextColored(colour, _battery.Radar.HasFiringSolution ? "LOCKED" : "acquiring...");
            ImGui.Text($"  {KsaWorld.DisplayName(locked.Vehicle)}");
            ImGui.Text($"  range {locked.Range / 1000.0:F2} km   closing {locked.ClosingSpeed:F0} m/s");
            ImGui.Text($"  CPA {locked.ClosestApproach:F0} m in {locked.TimeToClosestApproach:F1}s");
        }

        ImGui.Text($"In flight: {_battery.Rounds.Count}");

        // The most-asked question about this mod, answered where it is asked. Every gate in fire
        // control returns quietly, so an unarmed battery, one with no lock and one whose drives
        // are still settling all look identical from outside.
        if (_battery.Hold is { } why) ImGui.TextColored(Amber, $"Holding fire: {why}");
        else ImGui.TextColored(Green, "Clear to fire");
    }

    // Slow motion, well below what the game's speed control reaches. An engagement is over in a
    // couple of seconds of real time and the interesting part — the round leaving the tube, the
    // endgame turn, the fuse — happens far faster than it can be watched. Nothing in KSA stops the
    // simulation running at a hundredth of real time; its roller is simply built in tenths.
    private void DrawSlowMotion()
    {
        ImGui.Text($"Sim speed: {KsaWorld.SimulationSpeed:0.###}x");

        foreach ((string label, double speed) in SlowMotionSpeeds)
        {
            ImGui.SameLine();
            if (ImGui.Button(label)) KsaWorld.SetSimulationSpeed(speed);
        }
    }

    // Which of the game's camera views the optical head drives. KSA opens the views; a mod can
    // only borrow one, so this is a picker rather than a switch.
    private void DrawOpticView()
    {
        if (!_fit.HasOpticalHead) return;

        // Declared and unresolved is a fault worth saying out loud. Testing the subpart alone
        // cannot tell it apart from a system that never had a head, and both then show nothing.
        if (_battery.OpticPart is null)
        {
            ImGui.TextColored(Amber, "Optical head: subpart not found");
            return;
        }

        // Only windows the player can actually see. KSA keeps offscreen viewports of its own -
        // the thumbnail renderer is one - and offering those means picking a view that shows
        // nothing, which is indistinguishable from the feature being broken.
        KsaWorld.CollectUsableViewports(_viewports);

        ImGui.Text("Optical head view:");
        ImGui.SameLine();

        if (_viewports.Count == 0)
        {
            ImGui.TextDisabled("open a camera window in KSA (View menu), then pick it here");
            _policy.OpticViewport = -1;
            return;
        }

        if (ImGui.RadioButton("off", _policy.OpticViewport < 0)) _policy.OpticViewport = -1;

        foreach (int index in _viewports)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(KsaWorld.DescribeViewport(index), _policy.OpticViewport == index))
            {
                _policy.OpticViewport = index;
            }
        }

        if (_policy.OpticViewport >= 0)
        {
            ImGui.TextDisabled("  no sky or terrain detail here - KSA renders secondary views");
            ImGui.TextDisabled("  without the atmosphere pass. See docs/BLOCKED-ON-KSA.md");
        }

        // A window closed under us, so stop writing to something that is no longer shown.
        if (_policy.OpticViewport >= 0 && !_viewports.Contains(_policy.OpticViewport))
        {
            _policy.OpticViewport = -1;
        }
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

    private void DrawWeapons()
    {
        ImGui.Checkbox("Master arm", ref _policy.Armed);
        ImGui.SameLine();
        ImGui.Checkbox("Auto engage", ref _policy.AutoEngage);

        // One switch per armament fitted. A switch for a weapon the system does not carry reads as
        // one that is turned off rather than as one that is not there.
        IReadOnlyList<Armament> switches = _fit.Armaments;
        for (int i = 0; i < switches.Count; i++)
        {
            Armament arm = switches[i];
            ImGui.SameLine();
            ImGui.Checkbox(arm.Label, ref Armament.EnabledIn(_policy, arm.Kind));
        }

        // A view control, so it sits with the other thing that decides what you are looking at.
        ImGui.Checkbox("Chase this system's rounds", ref _policy.ChaseRounds);
        ImGui.TextDisabled("  rides the camera behind a round it fires; the view comes back after");

        DrawOpticView();

        if (ImGui.Button("FIRE")) _battery.FireAtLock();
        ImGui.SameLine();
        if (ImGui.Button("Reload")) _battery.Reload();
        ImGui.SameLine();
        if (ImGui.Button("Safe all")) _battery.SafeAll();

        ImGui.SameLine();
        if (ImGui.Button("Reset settings") && _battery.Platform is { } craft)
        {
            SettingsStore.Forget(KsaWorld.DisplayName(craft));
            new BatterySettings().ApplyTo(_policy);
            _batteries.WriteNow();
            Log.Info($"settings reset for {KsaWorld.DisplayName(craft)}");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Back to defaults, and forgotten from the settings file.\n"
                             + "Settings persist across restarts now, so this is the way back.");
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

        ImGui.Checkbox("Hold timewarp down while rounds fly", ref _config.LimitWarpInFlight);
        ImGui.TextDisabled($"  Above ~{MaxTrackableWarp:F0}x a round cannot be simulated. Held only");
        ImGui.TextDisabled("  while something is in the air, and given back after.");
        if (!_config.LimitWarpInFlight)
        {
            ImGui.TextColored(Amber, "  Off: rounds under warp will lag the world and miss.");
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
                $"{(isLock ? ">" : " ")}[{mark}] {KsaWorld.DisplayName(t.Vehicle)}  " +
                $"{t.Range / 1000.0:F2} km  cpa {t.ClosestApproach:F0} m  " +
                $"{(t.RoundsAssigned > 0 ? $"[{t.RoundsAssigned} away]" : "")}");

            ImGui.SameLine();
            if (ImGui.Button($"designate##{i}"))
            {
                _battery.Radar.ManualDesignation = t.Vehicle;
            }
        }

        if (_battery.Radar.ManualDesignation is not null && ImGui.Button("Clear designation"))
        {
            _battery.Radar.ManualDesignation = null;
        }

    }
}
