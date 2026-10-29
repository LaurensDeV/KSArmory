using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The panes that describe one weapons system. Every method reads <c>_system</c> and
/// <c>_policy</c>, which <see cref="Ui.Focus"/> sets before any of this runs.
/// </summary>
internal sealed partial class Ui
{
    private void DrawComponents(KSA.Vehicle craft)
    {
        KsaWorld.SurveyParts(craft, _surveyed);
        WeaponInventory inv = WeaponSurvey.Survey(_surveyed, Catalogue.Components);

        // IsInstallation, not IsWeaponSystem: one director and no armament still counts.
        if (!inv.IsInstallation)
        {
            ImGui.TextColored(Grey, "  Nothing this mod recognises.");
            Tip("A craft becomes an installation by carrying a part from Catalogue.Components, "
                + "unless every part on it only rides on something else, as a lone rail or pod does.");
            return;
        }

        // One section per kind of part, holding every role it plays and every station of it.
        _kinds.Clear();
        foreach (FoundComponent c in inv.Components)
        {
            if (!_kinds.Contains(c.Profile)) _kinds.Add(c.Profile);
        }

        foreach (ComponentProfile kind in _kinds)
        {
            int stations = 0;
            foreach (FoundComponent c in inv.Components)
            {
                if (ReferenceEquals(c.Profile, kind) && c.Role == kind.Role) stations++;
            }

            ImGui.SeparatorText(stations > 1 ? $"{kind.DisplayName}  x{stations}" : kind.DisplayName);
            ImGui.PushID(kind.PartId);

            // A part with a single row needs no fold: it would repeat the heading.
            int rows = 0;
            foreach (FoundComponent c in inv.Components)
            {
                if (ReferenceEquals(c.Profile, kind)) rows++;
            }

            // Rows print the selected weapon's numbers, so another weapon says so once and draws
            // none; a director it carries still gets its row.
            bool otherWeapon = _crewed && kind.Role == WeaponRole.Launcher && !IsSelectedWeapon(kind);
            if (otherWeapon) NotSelected();

            // Off the enum: a hand-written list silently omits a role added later.
            foreach (WeaponRole role in Enum.GetValues<WeaponRole>())
            {
                // Numbered among their own kind: a director's set is a sensor role too.
                bool ownCount = IsSearchPart(kind);
                int of = ownCount ? stations : inv.CountOf(role);
                int nth = 0;
                bool drawn = false;

                for (int i = 0; i < inv.Components.Count; i++)
                {
                    FoundComponent c = inv.Components[i];
                    if (c.Role != role || (ownCount && !ReferenceEquals(c.Profile, kind))) continue;

                    if (ReferenceEquals(c.Profile, kind) && !(drawn && StacksStations(kind, role))
                        && !(otherWeapon && StacksStations(kind, role)))
                    {
                        // ImGui keys a widget on its label within the id scope; two directors
                        // would share tick boxes.
                        ImGui.PushID(i);
                        DrawComponentRow(craft, c, kind, role, nth, stations, of, alone: rows == 1);
                        ImGui.PopID();
                        drawn = true;
                    }

                    nth++;
                }
            }

            ImGui.PopID();
        }

        if (CarriesSearchSet(craft)) DrawPictureSummary(craft);
    }

    private readonly List<ComponentProfile> _kinds = [];

    private static bool StacksStations(WeaponRole role)
        => role is not (WeaponRole.Camera or WeaponRole.Guidance or WeaponRole.Countermeasures);

    private static bool StacksStations(ComponentProfile kind, WeaponRole role)
        => StacksStations(role) && !IsSearchPart(kind);

    private static bool IsSearchPart(ComponentProfile kind) => Catalogue.SearchSetForPart(kind.PartId) is not null;

    private bool CarriesSearchSet(KSA.Vehicle? craft)
    {
        _searchSets.On(craft, _searchScratch);
        return _searchScratch.Count > 0;
    }

    private const int SearchRowsShown = 6;

    // Paired by position: the sets are keyed on parts and the survey lists positions.
    private void DrawSearchSetComponent(KSA.Vehicle craft, FoundComponent c)
    {
        _searchSets.On(craft, _searchScratch);

        SearchSet? set = null;
        double nearest = double.MaxValue;
        foreach (SearchSet s in _searchScratch)
        {
            double d;
            try { d = Vec.Len2(s.Part.PositionVehicleAsmb - c.PositionVehicleAsmb); }
            catch { continue; }

            if (d < nearest)
            {
                nearest = d;
                set = s;
            }
        }

        if (set is null)
        {
            ImGui.TextColored(Amber, "not crewed - no set resolved for this part");
            return;
        }

        List<Track> tracks = set.Radar.Tracks;
        if (tracks.Count == 0)
        {
            ImGui.TextDisabled(set.Radar.TooFaint > 0
                                   ? $"holding nothing - {set.Radar.TooFaint} too faint to see"
                                   : "holding nothing");
            Tip("It sees heat: a craft with its engines lit. One coasting, parked or slower than "
                + $"{set.Sensor.MinTargetSpeed:F0} m/s is not held.");
        }
        else
        {
            ImGui.TextColored(Green, $"holding {tracks.Count}");
            for (int i = 0; i < Math.Min(tracks.Count, SearchRowsShown); i++)
            {
                Track t = tracks[i];
                string line = $"  {t.Contact.DisplayName}  {Distance.Say(t.Range)}  {t.Contact.HeatKwPerSr:F0} kW/sr";
                if (t.IsThreat) ImGui.TextColored(Amber, line + "  threat");
                else ImGui.Text(line);
            }

            if (tracks.Count > SearchRowsShown) ImGui.TextDisabled($"  and {tracks.Count - SearchRowsShown} more");
        }

        ImGui.TextDisabled($"{LooksToward(craft, set.Boresight)}, head {(set.Radar.Watched is { } w ? $"on {w.Contact.DisplayName}" : "sweeping")}");
        Tip($"Passive: it gives nothing away, so there is nothing to switch off. It sees its whole "
            + $"{set.Sensor.ConeDeg:F0} deg cone wherever the head points; the head only shows where it is looking.");
    }

    private static string LooksToward(KSA.Vehicle craft, double3 boresightEcl)
    {
        if (KsaWorld.ParentBody(craft) is not { } body
            || ScopeFrame(body, KsaWorld.PositionEcl(craft)) is not { } frame)
        {
            return "looks somewhere";
        }

        double3 local = frame.ToLocalDirection(boresightEcl);
        // Rounded before printing, or a level boresight reads "-0 deg up".
        double up = Math.Round(double.RadiansToDegrees(Math.Asin(Math.Clamp(local.Z, -1.0, 1.0)))) + 0.0;
        return TerrainMap.HeadingDeg(local, still: 1e-3) is { } heading
                   ? $"looks {Math.Round(heading) % 360:F0} deg, {up:F0} deg up"
                   : up > 0 ? "looks straight up" : "looks straight down";
    }

    private void DrawPictureSummary(KSA.Vehicle craft)
    {
        ImGui.SeparatorText("Picture");

        IReadOnlyList<Track> merged = _picture.TracksFor(craft);
        if (merged.Count == 0)
        {
            ImGui.TextDisabled("nothing held");
        }
        else
        {
            string best = _picture.BestFor(craft) switch
            {
                KSA.Vehicle v => KsaWorld.DisplayName(v),
                null => "none",
                _ => "a round",
            };
            ImGui.Text($"{merged.Count} contact{(merged.Count == 1 ? "" : "s")}, best: {best}");
        }

        Tip("Every set aboard that searches for itself, merged, each contact once. A director with "
            + "Track on, and a beam weapon on auto-engage, take its best contact when nothing more "
            + "specific tells them where to look.");
    }

    private void DrawComponentRow(KSA.Vehicle craft, FoundComponent c, ComponentProfile kind, WeaponRole role,
                                  int nth, int stations, int of, bool alone)
    {
        string label = !StacksStations(kind, role) && of > 1 ? $"{c.DisplayName}  {nth + 1} of {of}" : c.DisplayName;
        bool folded = !(alone && label == kind.DisplayName);

        // Open by default: the controls are the reason to be on this tab.
        if (folded && !ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen)) return;

        if (stations > 1 && role == WeaponRole.Launcher) DrawStationsLoaded(craft, kind);

        // Only the last branch reads `_system`, which Focus leaves unassigned on an unarmed craft.
        if (role == WeaponRole.Camera)
        {
            DrawCameraComponent(nth);
        }
        else if (role == WeaponRole.Guidance)
        {
            DrawGuidanceComponent(craft, nth);
        }
        else if (role == WeaponRole.Countermeasures)
        {
            DrawDispenserComponent(craft, nth);
        }
        else if (role == WeaponRole.Sensor && IsSearchPart(kind))
        {
            DrawSearchSetComponent(craft, c);
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
                case WeaponRole.Launcher: DrawLauncherComponent(kind); break;
                case WeaponRole.Gun: DrawGunComponent(kind); break;
                case WeaponRole.Sensor: DrawSensorComponent(kind); break;
            }
        }

        if (folded) ImGui.TreePop();
    }

    // The nth component is the nth dispenser, paired by part order as the cameras are. The controls
    // are the craft's and drawn once: a press reaches every dispenser of that load.
    private void DrawDispenserComponent(KSA.Vehicle craft, int nth)
    {
        _countermeasures.On(craft, _dispenserScratch);
        if (nth >= _dispenserScratch.Count)
        {
            ImGui.TextColored(Amber, "not fitted - no dispenser resolved for this part");
            return;
        }

        Dispenser d = _dispenserScratch[nth].Dispenser;
        string what = d.Kind == DecoyKind.Flare ? "flares" : "chaff";
        ImGui.TextColored(d.Remaining > 0 ? Green : Grey, $"{d.Remaining}/{d.Profile.Count} {what}");

        if (nth == 0) DrawCountermeasureControls(craft);
    }

    private void DrawCountermeasureControls(KSA.Vehicle craft)
    {
        (int flares, int flareCap) = _countermeasures.Load(craft, DecoyKind.Flare);
        (int chaff, int chaffCap) = _countermeasures.Load(craft, DecoyKind.Chaff);

        ImGui.Separator();
        ImGui.Text($"Aboard: {flares}/{flareCap} flares, {chaff}/{chaffCap} chaff");

        ImGui.BeginDisabled(flares == 0);
        if (ImGui.Button("Flares")) _countermeasures.Dispense(craft, DecoyKind.Flare);
        ImGui.EndDisabled();
        Tip("A salvo of two flares, a quarter-second apart, from every flare dispenser aboard at once, so a "
            + "symmetric pair throws either side of the craft. A flare takes a heat seeker looking at it, and "
            + "does nothing to a radar one.");
        ImGui.SameLine();

        ImGui.BeginDisabled(chaff == 0);
        if (ImGui.Button("Chaff")) _countermeasures.Dispense(craft, DecoyKind.Chaff);
        ImGui.EndDisabled();
        Tip("A salvo of two chaff cartridges from every chaff dispenser aboard. Chaff stops dead in the air, so a Doppler seeker only sees it "
            + "while you fly square to the missile.");
        ImGui.SameLine();

        ImGui.BeginDisabled(flares == 0 && chaff == 0);
        if (ImGui.Button("Both"))
        {
            _countermeasures.Dispense(craft, DecoyKind.Flare);
            _countermeasures.Dispense(craft, DecoyKind.Chaff);
        }
        ImGui.EndDisabled();
        Tip("A salvo of each, for a missile whose seeker you cannot place.");

        bool auto = _countermeasures.AutoOn(craft);
        if (ImGui.Checkbox("Auto-dispense", ref auto)) _countermeasures.SetAuto(craft, auto);
        Tip($"Throws on its own when a missile aimed at this craft closes inside {AutoDispense.WarningRangeMetres / 1000.0:F0} km: "
            + "flares for a heat seeker, chaff for a radar one, both for anything else, at most every "
            + $"{AutoDispense.RetriggerSeconds:F0} s.");

        if (_countermeasures.Busy(craft)) ImGui.TextDisabled("dispensing");
        else if (flares == 0 && chaff == 0) ImGui.TextColored(Amber, "empty");
    }

    private void DrawGuidanceComponent(KSA.Vehicle craft, int nth)
    {
        if (nth > 0)
        {
            ImGui.TextDisabled("redundant - the first one flies this craft");
            return;
        }

        if (_icbms.For(craft) is not { } computer)
        {
            ImGui.TextDisabled("no computer crewed on this craft");
            return;
        }

        IcbmPhase phase = computer.Command.Phase;
        ImGui.TextColored(PhaseColour(phase), computer.Config.Armed ? $"armed - {phase}" : "not armed");
        ImGui.TextDisabled("  target, arming and settings on the Ballistic tab");
    }

    // Every station of a kind is one weapon.
    private bool IsSelectedWeapon(ComponentProfile kind) => _system.Profile.PartId == kind.PartId;

    private void DrawStationsLoaded(KSA.Vehicle craft, ComponentProfile kind)
    {
        _roster.AllOn(craft, _stationRows);

        int stations = 0;
        int loaded = 0;
        foreach (WeaponSystems.Entry e in _stationRows)
        {
            if (e.Weapon.Profile.PartId != kind.PartId) continue;

            stations++;
            if (e.Weapon.Ammo > 0) loaded++;
        }

        ImGui.Text($"{stations} stations, {loaded} loaded");
    }

    private readonly List<WeaponSystems.Entry> _stationRows = [];

    private void ForEachStation(Action<WeaponSystem> act)
    {
        if (_roster.For(Focused) is not { } selected) { act(_system); return; }

        _roster.StationsOf(selected, _stationRows);
        foreach (WeaponSystems.Entry e in _stationRows) act(e.Weapon);
    }

    private void NotSelected()
    {
        ImGui.TextColored(Amber, "not the selected weapon");
        ImGui.TextDisabled("  it has its own magazine and auto-engage - pick it in Weapons");
    }

    private void DrawLauncherComponent(ComponentProfile kind)
    {
        if (!IsSelectedWeapon(kind)) return;

        if (_system.Launcher is null)
        {
            ImGui.TextColored(Red, "not resolved");
            ImGui.TextDisabled("  the part is fitted but its subparts were not found");
            return;
        }

        DrawArmamentTally(ArmamentKind.Tubes);
        DrawArmamentTally(ArmamentKind.Beam);

        if (_system.ReloadRemaining > 0.0)
        {
            ImGui.Text($"Reloading: {_system.ReloadRemaining:F1}s");
            ImGui.ProgressBar(
                (float)(1.0 - _system.ReloadRemaining / Math.Max(0.001f, _profile.ReloadSeconds)));
        }

        DrawTurretLine();
        DrawTurretControls();

        if (ImGui.Button("Reload")) ForEachStation(b => b.Reload());
        ImGui.SameLine();
        if (ImGui.Button("Safe all")) ForEachStation(b => b.SafeAll());

        // A beam throws no rounds to ride.
        if (_fit.FirstOf(ArmamentKind.Tubes) is null && _fit.FirstOf(ArmamentKind.Belt) is null) return;

        ImGui.Checkbox("Chase this launcher's rounds", ref _policy.ChaseRounds);
        Tip("Rides the camera behind a round this launcher fires, and hands the view back on its own: "
            + "after the burst, or about two seconds after the round has nothing left to arrive at. "
            + "Right-drag looks around the round and the wheel moves in or out; let go and the view "
            + "eases back behind it.");

        // A guided round goes where it is steered, so a ballistic pipper over one would lie.
        if (_fit.Drops)
        {
            ImGui.Checkbox("Bomb sight", ref _policy.DrawBombSight);
            Tip("Marks where a store released now would land, flown rather than solved.");
        }
    }

    private void DrawGunComponent(ComponentProfile kind)
    {
        if (!IsSelectedWeapon(kind)) return;

        DrawArmamentTally(ArmamentKind.Belt);
        ImGui.TextDisabled(_system.GunsAreLaid ? "  laid" : "  not laid");
    }

    // The set's numbers are on the Tuning tab: they belong to the shared profile.
    private void DrawSensorComponent(ComponentProfile kind)
    {
        if (!IsSelectedWeapon(kind))
        {
            ImGui.TextDisabled("its own set; not the one fire control reads");
            return;
        }

        DrawRadarState();

        // `_policy`: whether this set turns is the installation's; its rpm is the profile's.
        if (_fit.SweepsASearchArray)
        {
            ImGui.Checkbox("Stop the search array", ref _policy.SearchRadarStopped);
        }

        if (_system.Sensor.Emits)
        {
            ImGui.Checkbox("Radar silent", ref _policy.RadarSilent);
            Tip("On: the set stops transmitting, so an anti-radiation round has nothing to home on, "
                + "and the set sees nothing either. Off: it transmits, and an anti-radiation round "
                + "can home on it.");
            if (_policy.RadarSilent)
                ImGui.TextDisabled("  not transmitting: nothing to home on, and nothing seen either");
        }

        if (ScopeTab(_sensor) is not { } tab) return;

        // A local: TakeScope flips the flag the pop reads.
        bool scopeTinted = _policy.ScopeOpen;
        if (scopeTinted) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.42f, 0.30f, 1f));
        if (ImGui.Button("Scope")) TakeScope(_policy);
        if (scopeTinted) ImGui.PopStyleColor();
        Tip($"Opens or closes a scope of what this set holds, by bearing and range. The {tab} tab "
            + "has the same scope above the full track list.");
    }

    // Auto-engage and FIRE belong to the installation, so they are in DrawSystemHeader, not here.
    private void DrawFireControlComponent()
    {
        // Hidden, not disabled: a greyed control still claims the system has it.
        if (_fit.AutoEngages)
        {
            // The installation's own, so not on Tuning, where every control is shared.
            if (_fit.SalvoCapacity > 1)
                ImGui.SliderInt("Rounds per target", ref _policy.RoundsPerTarget,
                                1, _fit.SalvoCapacity);

            ImGui.Checkbox("Never target the vehicle I'm flying", ref _policy.ProtectControlledVehicle);
            Tip("Stops this weapon aiming at the craft you fly. It is not armour: a burst or a stray round near it, "
                + "this weapon's own included, still damages it.");
        }

        if (_fit.Aims)
        {
            ImGui.Checkbox("Aim with the mouse", ref _policy.MouseAim);
            Tip("The launcher follows the cursor instead of what the radar is holding. Auto-engage "
                + "still decides when to fire, and the drives still have to settle first. An optical "
                + "head has its own Mouse aim, on its director's row.");
        }

        ImGui.Checkbox("Fire at the mouse", ref _policy.MouseFire);
        Tip("Click the ground to send a round there. No target and no lock are needed: the ring "
            + "shows where, and turns red when the weapon is empty or not laid, the point is out of "
            + "reach, or the round could not be guided there.");

        // Alone below a rule: it discards the stored settings.
        ImGui.Separator();
        if (ImGui.Button("Reset settings") && _system.Platform is { } craft)
        {
            SettingsStore.Forget(KsaWorld.DisplayName(craft));
            new SystemSettings().ApplyTo(_policy);
            _roster.WriteNow();
            Log.Info($"settings reset for {KsaWorld.DisplayName(craft)}");
        }
        Tip("Back to defaults, and forgotten from the settings file. This resets the whole "
            + "installation, not this component.");
    }

    private void DrawArmamentTally(ArmamentKind kind)
    {
        if (_fit.FirstOf(kind) is not { } arm) return;

        (int remaining, bool firing) = LiveState(_system, arm);

        if (firing) ImGui.TextColored(Red, arm.Describe(remaining, firing));
        else ImGui.Text(arm.Describe(remaining, firing));

        ImGui.SameLine();
        ImGui.Checkbox($"live##{kind}", ref Armament.EnabledIn(_policy, kind));
    }

    // By position: KsaWorld.SurveyParts and OpticParts.FindAll both walk the part span in index
    // order, so the nth camera component is the nth head.
    private void DrawCameraComponent(int nth)
    {
        if (nth >= _headScratch.Count)
        {
            ImGui.TextColored(Amber, "not crewed - no head resolved for this part");
            return;
        }

        DrawOpticView(_headScratch[nth]);
    }

    // Above the tab bar: every fire-control gate returns quietly, and the hold line is all that
    // tells them apart. CLAUDE.md, "Adding a weapon system", on the header strip.
    private void DrawSystemHeader()
    {
        if (_system.Platform is null)
        {
            ImGui.TextColored(Grey, "No platform - take control of a vehicle.");
            ImGui.Separator();
            return;
        }

        // The picker beside FIRE: several weapons share one trigger.
        WeaponFit fit = _fit;
        if (DrawWeaponPicker(Focused)) ImGui.SameLine();

        // Through the group: firing the selected station reaches the same rail every time.
        if (ImGui.Button("FIRE")) FireSelectedGroup();
        // The cases WeaponSystem.FireAtLock branches on, in its order.
        Tip(_system.TriggerArmament == ArmamentKind.Belt
                ? "Fire one burst now, wherever the guns are pointing."
            : _system.TriggerArmament == ArmamentKind.Beam
                ? "Hold the beam on for one dwell, at the craft or place you shift-clicked or else at the "
                  + "set's lock. With nothing to aim at it lases wherever the director is looking."
            : !_system.Munition.Powered
                ? "Release one store now. A guided store steers onto whatever is designated; an "
                  + "unguided one, or a guided one with nothing designated, simply falls."
                : "Fire one round now, at the craft you shift-clicked or else at the radar's lock.");

        // The station the trigger would reach, not the one selected, as FIRE goes through the group.
        ImGui.SameLine();
        DrawHoldLine(_system, _policy.AutoEngage);

        if (_system.Rounds.Count > 0)
        {
            ImGui.SameLine();
            ImGui.Text($"   In flight: {_system.Rounds.Count}");
        }

        // Absent, not disabled, on a rack of stores: nothing it carries engages on its own.
        bool anotherLine = false;
        if (fit.AutoEngages)
        {
            ImGui.Checkbox("Auto engage", ref _policy.AutoEngage);
            Tip("On: it picks whatever its sensors and IFF allow and fires at it by itself. "
                + "Off: it fires only when you press FIRE.");
            anotherLine = true;
        }

        _roster.AllOn(Focused, _weaponScratch);
        if (_weaponScratch.Count > 0)
        {
            if (anotherLine) ImGui.SameLine(0f, ImGui.GetFrameHeight());

            // A local: the button toggles the flag that guards the pop.
            bool weaponsTinted = _weaponsOpen;
            if (weaponsTinted) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.42f, 0.30f, 1f));
            if (ImGui.Button("Weapons")) _weaponsOpen = !_weaponsOpen;
            if (weaponsTinted) ImGui.PopStyleColor();
        }

        DrawClockWarning();
        ImGui.Separator();
    }

    // Only when the clock is the problem: fire control runs on simulated time, so heavy warp
    // explains a silent system.
    private void DrawClockWarning()
    {
        if (KsaWorld.IsPaused) return;

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

    private void DrawRadarState()
    {
        // Reported apart: auto-engage fires at the lock, never at a designation.
        bool designated = _system.Designation.Kind != AimpointKind.None;
        if (designated) ImGui.Text($"designated: {_system.DesignationName}");

        // A store is released onto a designation, never onto a lock.
        if (_fit.Drops)
        {
            if (!designated) ImGui.TextColored(Grey, "nothing designated - shift-click the ground");
            return;
        }

        if (_system.Radar.Locked is not { } locked)
        {
            ImGui.TextColored(Grey, "nothing locked");
            return;
        }

        bool solution = _system.Radar.HasFiringSolution;

        ImGui.TextColored(solution ? Red : Amber, solution ? "LOCKED" : "acquiring...");
        ImGui.SameLine();
        ImGui.Text($"{locked.Contact.DisplayName}   {locked.Range / 1000.0:F2} km, "
                   + $"closing {locked.ClosingSpeed:F0} m/s");
        ImGui.TextDisabled($"  CPA {locked.ClosestApproach:F0} m in "
                           + $"{locked.TimeToClosestApproach:F1}s");
    }

    // `_policy`, so off the Tuning tab: two mounts can disagree about how each is driven.
    private void DrawTurretControls()
    {
        if (_system.Launcher is null || !_fit.Aims) return;

        ImGui.Checkbox("Track with turret", ref _policy.TurretTracking);

        bool byHand = ImGui.TreeNode("Drive it by hand");
        Tip("Neither spin nor manual aim needs a target.");
        if (!byHand) return;

        if (_fit.Traverses) ImGui.Checkbox("Spin continuously", ref _policy.TurretSpin);
        ImGui.Checkbox("Manual aim", ref _policy.TurretManual);

        if (_fit.Traverses)
        {
            ImGui.SliderFloat("Bearing (deg)", ref _policy.TurretManualBearingDeg, -180f, 180f);
        }

        if (_fit.Elevates)
        {
            ImGui.SliderFloat("Elevation (deg)", ref _policy.TurretManualElevationDeg, 0f, 82f);
            Tip("Applies to spin as well as manual aim.");
        }

        ImGui.TreePop();
    }

    private void DrawTurretLine()
    {
        if (_system.Launcher is null) return;

        // Not "subpart not found", which would read as a fault.
        if (!_fit.Aims) return;

        if (_fit.Traverses && _system.TurretPart is null)
        {
            ImGui.TextColored(Amber, "Turret: subpart not found (fixed forward)");
            return;
        }

        if (_system.AnyDriveRefused)
        {
            string frozen = string.Join(", ",
                Enum.GetValues<DriveChannel>()
                    .Where(c => !_system.DriveWorks(c))
                    .Select(c => c.ToString().ToLowerInvariant()));
            ImGui.TextColored(Red, $"Drive: engine refused the transform write ({frozen})");
            if (!_system.DriveWorks(DriveChannel.Turret) || !_system.DriveWorks(DriveChannel.Pods))
            {
                ImGui.TextColored(Red, "Holding fire: the tubes cannot be laid");
                return;
            }
        }

        double bearing = float.RadiansToDegrees((float)_system.Turret.BearingRad);
        if (bearing < 0.0) bearing += 360.0;
        double elevation = float.RadiansToDegrees((float)_system.Turret.ElevationRad);
        string aim = $"Turret: {bearing:F0} deg, elev {elevation:F0} deg";

        if (!_policy.TurretTracking)
        {
            ImGui.TextColored(Grey, $"{aim} (tracking off)");
        }
        else if (_system.IsLaid)
        {
            ImGui.TextColored(Green, $"{aim} - laid");
        }
        else if (_system.Turret.OnTarget)
        {
            ImGui.TextColored(Amber, $"{aim} - settling");
        }
        else
        {
            double error = Math.Abs(float.RadiansToDegrees((float)_system.Turret.ErrorRad));
            double elevError = Math.Abs(float.RadiansToDegrees((float)_system.Turret.ElevationErrorRad));
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

        DrawRadarState();
        ImGui.Separator();

        if (_system.Radar.Tracks.Count == 0)
        {
            ImGui.TextDisabled("scope clear");
        }

        // Or an empty scope with craft in the world reads as a broken radar.
        if (_system.Radar.MaskedByTerrain > 0)
        {
            ImGui.TextDisabled($"  {_system.Radar.MaskedByTerrain} behind the horizon");
        }

        if (_system.Radar.MaskedByBurst > 0)
        {
            ImGui.TextDisabled($"  {_system.Radar.MaskedByBurst} behind a fireball's ionised air");
        }

        if (_system.Radar.LostToChaff > 0)
        {
            ImGui.TextDisabled($"  {_system.Radar.LostToChaff} lost to chaff in the notch, reacquiring");
        }

        if (_system.Radar.IgnoredWreckage > 0)
        {
            ImGui.TextDisabled($"  {_system.Radar.IgnoredWreckage} piece(s) of wreckage, not engaged");
        }

        for (int i = 0; i < _system.Radar.Tracks.Count; i++)
        {
            Track t = _system.Radar.Tracks[i];
            bool isLock = ReferenceEquals(t, _system.Radar.Locked);

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
                _system.Radar.ManualDesignation = t.Contact.Handle;
            }
        }

        if (_system.Radar.ManualDesignation is not null && ImGui.Button("Clear designation"))
        {
            _system.Radar.ManualDesignation = null;
        }

    }
}
