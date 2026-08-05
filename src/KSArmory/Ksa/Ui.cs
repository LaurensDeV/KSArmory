using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The operator's panel: master arm, radar and guidance tuning, the track list with
/// manual designation, and a rolling event log.
/// </summary>
internal sealed class Ui(Config config, BatteryRoster roster, WarpPolicy warp, WatchCamera watch, CraftMover mover, BurstTool bursts)
{
    private static readonly float4 Green = new(0.4f, 1.0f, 0.45f, 1f);
    private static readonly float4 Red = new(1.0f, 0.35f, 0.3f, 1f);
    private static readonly float4 Amber = new(1.0f, 0.78f, 0.25f, 1f);
    private static readonly float4 Grey = new(0.65f, 0.65f, 0.7f, 1f);

    // Warp above which a frame carries more simulated time than the interceptor can integrate, so
    // the battery stands rounds down. Indicative only: the real limit is per frame, so a lower
    // frame rate reaches it sooner. Assumes 60 fps.
    private const double MaxTrackableWarp = Interceptor.MaxFaithfulStep * 60.0;

    private readonly Config _config = config;
    private readonly BatteryRoster _batteries = roster;
    private readonly WarpPolicy _warp = warp;

    // The system every pane below reads. Every one of them was written against a single battery,
    // and pointing this at whichever system is being shown is what lets them all stay that way.
    private DefenceBattery _battery = null!;
    private BatteryConfig _policy = null!;
    private readonly WatchCamera _watch = watch;
    private readonly CraftMover _mover = mover;
    private readonly BurstTool _bursts = bursts;
    private readonly List<int> _viewports = [];
    private readonly List<(string Name, string Character)> _roster = [];
    private readonly List<(string What, string Id, bool Resolved)> _armedChain = [];
    private readonly List<SurveyedPart> _surveyed = [];
    private readonly List<KSA.Vehicle> _craftScratch = [];
    private KSA.Vehicle? _managed;
    private string _ownTeamEntry = string.Empty;
    private string _newTeamEntry = string.Empty;

    public bool Visible = true;

    /// <summary>The system the panel is pointed at, for the overlay to highlight.</summary>
    public KSA.Vehicle? Focused { get; private set; }

    // Points the panes at one system. Returns false when there is nothing crewed to point at,
    // which is the only state in which they must not be drawn at all.
    private bool Focus(KSA.Vehicle? craft)
    {
        if (_batteries.For(craft) is not { } entry) return false;

        _battery = entry.Battery;
        _policy = entry.Policy;
        return true;
    }

    // What a pane is about. Anything belonging to one installation is a tab in that system's own
    // window instead; Session is the rest, and Debug is for whoever is working on the mod rather
    // than playing with it.
    private enum PaneGroup { Session, Debug }

    // One pop-out window: what it is called, whether it is open, and what it draws. A class
    // rather than a struct so Open is shared with the button that toggles it.
    private sealed class Pane(string title, Action body, PaneGroup group)
    {
        public readonly string Title = title;
        public readonly Action Body = body;
        public readonly PaneGroup Group = group;

        public bool Open;
    }

    private Pane[]? _panes;

    // Built lazily because every Body is an instance method. Order is the order the buttons
    // appear in, which runs roughly from what an operator touches most to what they touch once.
    private Pane[] Panes => _panes ??=
    [
        new("Kittens", DrawKittenRoster, PaneGroup.Session),
        new("Test targets", DrawTestTargets, PaneGroup.Debug),
        new("Log", DrawLog, PaneGroup.Debug),
    ];

    public void Draw()
    {
        RefreshSystems();
        _batteries.Sync(_systems);

        if (_managed is not null && _batteries.For(_managed) is null) _managed = null;
        Focused = _managed ?? _batteries.Default();

        // Nothing crewed: the panes all read a battery, so there is nothing for them to show.
        bool anyCrewed = Focus(Focused);

        if (!Visible)
        {
            // Closing the panel must not strand the operator with no way back.
            if (ImGui.Begin("KSArmory##reopen", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                if (ImGui.Button("KSArmory")) Visible = true;
            }
            ImGui.End();
            if (anyCrewed) { DrawManageWindow(); DrawPanes(); }
            return;
        }

        // ###id so the version can ride in the title without the window losing its place
        // every time the mod is bumped.
        if (ImGui.Begin($"KSArmory {Build.Version}###KSArmory", ref Visible))
        {
            // Opens on what exists in the world rather than on whatever the camera is pointed
            // at. Everything below is about the *selected* system, and everything that is not
            // about one particular system is a pane.
            DrawSystemList();
            ImGui.Separator();
            DrawPaneToggles();
        }

        ImGui.End();

        // Outside the main window's Begin/End: each of these is its own top-level window, so
        // they must not be nested inside another one.
        if (anyCrewed)
        {
            DrawManageWindow();
            DrawPanes();
        }
    }

    // What the mod recognises on the craft being flown. Reports only -- nothing depends on this
    // yet. Proving discovery works against real user-built craft comes before anything is wired
    // to it, because a survey that quietly finds nothing looks exactly like a craft with no
    // weapons on it.
    // Every craft in the world this mod recognises as a weapons system, refreshed on a timer.
    //
    // Surveying is a walk of every part of every loaded vehicle, so it does not belong on a
    // per-frame path just to draw a list that changes when a craft is built or destroyed.
    private readonly List<(KSA.Vehicle Craft, WeaponInventory Inventory)> _systems = [];
    private int _systemsAge = RefreshSystemsEvery;
    private const int RefreshSystemsEvery = 60;

    /// <summary>The weapons systems last surveyed, for the on-screen markers.</summary>
    public IReadOnlyList<(KSA.Vehicle Craft, WeaponInventory Inventory)> Systems
    {
        get { RefreshSystems(); return _systems; }
    }

    private void RefreshSystems()
    {
        if (++_systemsAge < RefreshSystemsEvery) return;
        _systemsAge = 0;

        _systems.Clear();
        KsaWorld.CollectVehicles(_craftScratch);
        for (int i = 0; i < _craftScratch.Count; i++)
        {
            KSA.Vehicle craft = _craftScratch[i];
            KsaWorld.SurveyParts(craft, _surveyed);
            WeaponInventory inv = WeaponSurvey.Survey(_surveyed, Arsenal.Components);
            if (inv.IsWeaponSystem) _systems.Add((craft, inv));
        }
    }

    // The list the panel opens on: what exists, not what happens to be under the camera.
    //
    // A table, one row per system, because the alternative grew a heading, a status line and a
    // row of buttons each and stopped being readable at two craft.
    private void DrawSystemList()
    {
        RefreshSystems();

        if (_systems.Count == 0)
        {
            ImGui.TextColored(Grey, "No weapons systems.");
            ImGui.TextDisabled("A craft becomes one by carrying a part this mod recognises.");
            ImGui.TextDisabled("Recognised parts are listed under Components once it is one.");
            return;
        }

        ImGui.Text($"Weapons systems ({_systems.Count})");

        if (!ImGui.BeginTable("##systems", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##what", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##act", ImGuiTableColumnFlags.WidthFixed);

        for (int i = 0; i < _systems.Count; i++)
        {
            (KSA.Vehicle craft, WeaponInventory inv) = _systems[i];
            bool isFocused = ReferenceEquals(craft, Focused);
            BatteryRoster.Entry? entry = _batteries.For(craft);

            ImGui.PushID(i);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (isFocused) ImGui.TextColored(Green, KsaWorld.DisplayName(craft));
            else ImGui.Text(KsaWorld.DisplayName(craft));

            // Every system runs its own battery, so every row reports its own state rather than
            // one row's state and a list of names.
            ImGui.TableNextColumn();
            if (entry is { } e)
            {
                ImGui.TextColored(e.Policy.Armed ? Red : Grey,
                                  $"{(e.Policy.Armed ? "ARMED" : "safe")}  {e.Battery.Ammo}/{_profile.TubeCount}");
            }
            else
            {
                ImGui.TextDisabled(Describe(inv));
            }

            ImGui.TableNextColumn();
            DrawSystemRowButtons(craft);

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    // Inline, and small: three short buttons fit a row where "Run the battery here" did not.
    // Moving the battery is a decision about one system, so it lives in that system's window.
    private void DrawSystemRowButtons(KSA.Vehicle craft)
    {
        // Point the camera at it and label it for a few seconds, without moving or commandeering
        // anything. One shot rather than a toggle: both halves end on their own, so there is
        // nothing left to switch off and no state for the button to get out of step with.
        // ASCII on purpose: ImGui's default font carries basic Latin only, so a crosshair glyph
        // would render as a box.
        if (ImGui.SmallButton("(+)"))
        {
            Markers.Show(craft);
            _watch.Watch(craft);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Turn the view towards it and label it for a few seconds.\n"
                             + "Move the camera yourself at any point and it lets go.");
        }

        ImGui.SameLine();
        bool flyingIt = ReferenceEquals(craft, KsaWorld.ControlledVehicle);
        if (!flyingIt && ImGui.SmallButton("Go to")) KsaWorld.GoTo(craft);
        if (flyingIt) ImGui.TextDisabled("here");

        ImGui.SameLine();
        if (ImGui.SmallButton("Manage")) _managed = craft;
    }

    // One system's own window: everything that belongs to that installation rather than to the
    // session, as tabs. Separate from the main panel so the list stays a list.
    private void DrawManageWindow()
    {
        if (_managed is not { } craft || !KsaWorld.IsAlive(craft))
        {
            _managed = null;
            return;
        }

        // ###id keeps one window across a change of craft, so it holds its size and place
        // instead of opening afresh every time a different system is managed.
        // Point the panes at *this* window's craft. Focus was worked out at the top of the frame
        // from last frame's selection, so the window that opens on the click that selected it
        // would otherwise show -- and edit -- the previously focused battery for one frame.
        if (!Focus(craft))
        {
            _managed = null;
            return;
        }

        bool open = true;
        if (ImGui.Begin($"{KsaWorld.DisplayName(craft)}###KSArmorySystem", ref open))
        {
            if (ImGui.BeginTabBar("##systemtabs"))
            {
                if (ImGui.BeginTabItem("Status")) { DrawSystemPane(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Tracks")) { DrawTrackList(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Tuning")) { DrawTuning(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Teams and IFF")) { DrawIff(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Components")) { DrawComponents(craft); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();

        if (!open) _managed = null;
    }

    private static string Describe(WeaponInventory inv)
    {
        List<string> parts = [];
        foreach (WeaponRole role in Enum.GetValues<WeaponRole>())
        {
            int n = inv.CountOf(role);
            if (n > 0) parts.Add(n == 1 ? role.ToString() : $"{n} {role}");
        }
        return string.Join(", ", parts);
    }

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

    // Plural, and spaced: the enum names are identifiers and read as such on screen.
    private static string GroupName(WeaponRole role) => role switch
    {
        WeaponRole.FireControl => "Fire control",
        WeaponRole.Launcher => "Launchers",
        WeaponRole.Sensor => "Sensors",
        WeaponRole.Camera => "Cameras",
        WeaponRole.Gun => "Guns",
        _ => role.ToString(),
    };

    // The selected system's own controls: where it is, what it is holding, and its master arm.
    // One battery runs at a time -- the profiles are per system, but the fire control, radar and
    // drives are a single instance that mounts to one craft. Until that is widened, selecting a
    // system the battery is not on can show what it is and offer to move the battery there.

    private void DrawSystemPane()
    {
        DrawStatus();
        ImGui.Separator();
        DrawWeapons();
    }

    private void DrawPaneToggles()
    {
        DrawPaneGroup("Session", PaneGroup.Session);

        // Collapsed, and last: these answer questions about the mod, not about the engagement.
        if (ImGui.TreeNode("Debug"))
        {
            // The overlay is diagnostic drawing, so its master switch belongs here rather than
            // only under Display -- which is where someone goes to tune it, not to find it.
            ImGui.Checkbox("Draw debug lines", ref _config.DrawOverlays);
            ImGui.TextDisabled("  search cone, tracks, round tracers, drive facing");
            if (_config.DrawOverlays)
            {
                ImGui.TextDisabled("  Display has the individual switches");
            }

            ImGui.Separator();

            DrawBurstTool();
            ImGui.Separator();

            // Inline rather than a pane of its own. It is one tick box and a line of state, and
            // a window holding that is a window to open, move and close for nothing.
            DrawCraftMover();
            ImGui.Separator();

            DrawPaneGroup(null, PaneGroup.Debug);
            ImGui.TreePop();
        }
    }

    private void DrawPaneGroup(string? heading, PaneGroup group)
    {
        if (heading is not null) ImGui.TextDisabled(heading);

        int shown = 0;
        for (int i = 0; i < Panes.Length; i++)
        {
            Pane pane = Panes[i];
            if (pane.Group != group) continue;

            if (shown++ % 2 == 1) ImGui.SameLine();

            // A button, never a tick box. A checkmark reads as "this setting is on", so a window
            // arriving instead is unannounced and the tick says nothing about where it went.
            // Opening a window is an action; tick boxes are for state.
            if (pane.Open) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.45f, 0.25f, 1f));
            if (ImGui.Button(pane.Open ? $"{pane.Title} (open)" : pane.Title)) pane.Open = !pane.Open;
            if (pane.Open) ImGui.PopStyleColor();
        }
    }

    private void DrawPanes()
    {
        for (int i = 0; i < Panes.Length; i++)
        {
            Pane pane = Panes[i];
            if (!pane.Open) continue;

            // Title doubles as the ImGui id, so each pane keeps its own size and position
            // across sessions the way any other window does.
            if (ImGui.Begin(pane.Title, ref pane.Open)) pane.Body();
            ImGui.End();
        }
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

        if (KsaWorld.CharacterOf(_battery.Platform) is { } character)
        {
            bool armed = character == KsaWorld.ArmedCharacterId;
            ImGui.TextColored(armed ? Green : Amber, $"  kitten wearing '{character}'");
            if (!armed) ImGui.TextDisabled("  Arm it, then EVA again - the body is fixed at EVA.");
        }

        if (_battery.Launcher is not null)
        {
            ImGui.TextColored(Green, $"Launcher: {_config.Launcher.DisplayName} fitted");
        }
        else if (_config.RequireLauncherPart)
        {
            ImGui.TextColored(Red, "Launcher: none fitted");
            ImGui.TextDisabled($"  Add the {_config.Launcher.DisplayName} in the editor,");
            ImGui.TextDisabled("  or untick 'Require launcher part' below.");
        }
        else
        {
            ImGui.TextColored(Amber, "Launcher: none (part requirement off)");
        }

        if (_policy.Armed) ImGui.TextColored(Red, "MASTER ARM: ARMED");
        else ImGui.TextColored(Green, "MASTER ARM: SAFE");

        ImGui.SameLine();
        ImGui.Text($"   Rounds: {_battery.Ammo}/{_profile.TubeCount}");

        if (_profile.HasCannon)
        {
            ImGui.SameLine();
            if (_battery.GunsFiring) ImGui.TextColored(Red, $"   Cannon: {_battery.GunAmmo} FIRING");
            else ImGui.Text($"   Cannon: {_battery.GunAmmo}");
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

    // Where the turret is pointing, and whether it is still swinging. Also the place the engine's
    // verdict on the transform write surfaces: if KSA refuses it, the drive gives up for the
    // session and this is where that gets said, rather than the turret just silently never moving.
    // The weapon system the panel is tuning. See Config.Select.
    private LauncherProfile _profile => _config.Launcher;
    private MunitionProfile _munition => _config.Munition;

    // Speeds worth a button. KSA's own roller stops at 0.1x; these go two decades below.
    private static readonly (string Label, double Speed)[] SlowMotionSpeeds =
    [
        ("0.01x", 0.01), ("0.05x", 0.05), ("0.1x", 0.1), ("0.25x", 0.25), ("1x", 1.0),
    ];

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
        if (_battery.OpticPart is null) return;

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

        if (_battery.TurretPart is null)
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
        ImGui.SameLine();
        ImGui.Checkbox("Missiles", ref _policy.MissilesEnabled);
        if (_profile.HasCannon)
        {
            ImGui.SameLine();
            ImGui.Checkbox("Cannon", ref _policy.GunsEnabled);
        }

        DrawOpticView();

        if (ImGui.Button("FIRE")) _battery.FireAtLock();
        ImGui.SameLine();
        if (ImGui.Button("Reload")) _battery.Reload();
        ImGui.SameLine();
        if (ImGui.Button("Safe all")) _battery.SafeAll();

        ImGui.Checkbox("Never target the vehicle I'm flying", ref _policy.ProtectControlledVehicle);

        ImGui.Checkbox("Aim with the mouse", ref _policy.MouseAim);
        if (_policy.MouseAim)
        {
            ImGui.TextDisabled("  The launcher and the optical head follow the cursor. Auto-engage");
            ImGui.TextDisabled("  still decides when to fire; the drives still have to settle first.");
        }

        ImGui.Checkbox("Hold timewarp down while rounds fly", ref _config.LimitWarpInFlight);
        ImGui.TextDisabled($"  Above ~{MaxTrackableWarp:F0}x a round cannot be simulated. Held only");
        ImGui.TextDisabled("  while something is in the air, and given back after.");
        if (!_config.LimitWarpInFlight)
        {
            ImGui.TextColored(Amber, "  Off: rounds under warp will lag the world and miss.");
        }
    }

    // Spawns a drone on a timed pass, so the system can be tested without building and flying a
    // second craft by hand.
    // The kitten roster, and which character each one wears.
    //
    // Doubles as the only check that this mod's character registered: ModLibrary.AllCharacters is
    // internal, so a roster entry naming it is the one public evidence it loaded. If "arm" leaves
    // the entry reading something else, the XML did not take.
    private void DrawKittenRoster()
    {
        KsaWorld.CollectRoster(_roster);
        if (_roster.Count == 0)
        {
            ImGui.TextDisabled("  No roster yet.");
            return;
        }

        int armed = 0;
        for (int i = 0; i < _roster.Count; i++)
        {
            if (_roster[i].Character == KsaWorld.ArmedCharacterId) armed++;
        }

        // Say it up front rather than letting every Arm fail silently. An asset file missing
        // from mod.toml is never reported, so this is the only place it surfaces.
        // Every link in this chain fails silently, so every link is asked about separately.
        KsaWorld.CollectArmedChain(_armedChain);
        bool available = true;
        for (int i = 0; i < _armedChain.Count; i++)
        {
            (string what, string id, bool ok) = _armedChain[i];
            if (ok)
            {
                ImGui.TextColored(Green, $"  {what}: {id}");
            }
            else
            {
                ImGui.TextColored(Red, $"  {what}: {id} did NOT resolve");
                available = false;
            }
        }

        if (!available)
        {
            ImGui.TextDisabled("  The first red line is where it breaks. A declaration that does");
            ImGui.TextDisabled("  not resolve is skipped in silence by KSA, not reported.");
            return;
        }

        ImGui.TextDisabled($"  {armed} of {_roster.Count} carry the shoulder cannon.");
        ImGui.TextDisabled("  Arming changes the *next* kitten built from that entry - EVA again.");

        for (int i = 0; i < _roster.Count; i++)
        {
            (string name, string character) = _roster[i];
            bool isArmed = character == KsaWorld.ArmedCharacterId;

            ImGui.PushID(i);
            if (isArmed) ImGui.TextColored(Green, $"  {name}");
            else if (ImGui.Button($"Arm##{i}")) KsaWorld.SetRosterCharacter(name, KsaWorld.ArmedCharacterId);
            if (!isArmed)
            {
                ImGui.SameLine();
                ImGui.Text($"{name}");
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"({character})");
            ImGui.PopID();
        }

    }

    private void DrawBurstTool()
    {
        ImGui.Checkbox("Explosions on click", ref _config.BurstTool);

        if (_config.BurstTool)
        {
            ImGui.TextDisabled("  click the ground to set one off there");
            ImGui.SliderFloat("Size", ref _config.BurstScale, 0.25f, 8f);
            ImGui.Checkbox("Fireball (off: airburst)", ref _config.BurstFireball);
        }

        // Straight overhead, for when the pointer is not the question -- it needs no aim and no
        // ground under it, so it still answers "does the effect work at all".
        if (ImGui.Button("Burst overhead")) FireTestBurst(Detonation.Fireball);
        ImGui.SameLine();
        ImGui.TextDisabled("100 m over the system shown");

        if (!Detonation.ParticlesEnabled)
        {
            ImGui.TextColored(Red, "KSA's Particles graphics setting is OFF");
            ImGui.TextDisabled("  nothing will draw until it is turned back on");
        }
    }

    // A burst overhead, where it cannot be missed.
    private void FireTestBurst(string emitterId)
    {
        if (_battery.Platform is not { } platform)
        {
            Log.Info("no platform to burst over");
            return;
        }

        double3 at = KsaWorld.PositionEcl(platform) + KsaWorld.LocalUp(platform) * 100.0;
        Log.Info($"test burst: {emitterId} 100 m over {KsaWorld.DisplayName(platform)}");
        Detonation.Show(emitterId, at, platform);
    }

    private void DrawCraftMover()
    {
        ImGui.Checkbox("Move craft with the mouse", ref _config.MoveCraftWithMouse);

        if (!_config.MoveCraftWithMouse)
        {
            ImGui.TextDisabled("  click a craft to lift it, click the ground to set it down");
            return;
        }

        if (_mover.Held is { } held)
        {
            ImGui.TextColored(Amber, $"  holding {KsaWorld.DisplayName(held)} - click the ground");
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel")) _mover.Release();
        }
        else if (_mover.Hovered is { } over)
        {
            ImGui.TextColored(Green, $"  click to pick up {KsaWorld.DisplayName(over)}");
        }
        else
        {
            ImGui.TextDisabled("  point at a craft; it rings when the click would take it");
        }
    }

    private void DrawTestTargets()
    {
        if (_battery.Platform is null)
        {
            ImGui.TextDisabled("no platform");
            return;
        }

        ImGui.SliderFloat("Time to pass (s)", ref _spawnSeconds, 10f, 180f);
        ImGui.SliderFloat("Speed (m/s)", ref _spawnSpeed, 50f, 2000f);
        ImGui.SliderFloat("Miss distance (m)", ref _spawnMiss, 0f, 5000f);

        // Spawning beyond radar range is the easiest way to see nothing happen at all.
        float spawnRange = _spawnSpeed * _spawnSeconds;
        if (spawnRange > _config.Sensor.Range)
        {
            ImGui.TextColored(Amber,
                $"spawns {spawnRange / 1000f:F1} km out - beyond {_config.Sensor.Range / 1000f:F1} km radar range");
            ImGui.TextDisabled("  it will be invisible until it closes to range");
        }
        else
        {
            ImGui.TextDisabled($"spawns {spawnRange / 1000f:F1} km out, inside radar range");
        }

        // Which craft to fly as the drone. A stock vessel reads as an actual intruder;
        // cloning the launcher means air-defence sites attacking air-defence sites.
        ImGui.SliderInt("Drone type", ref _spawnCraft, 0, TestTarget.StockCraft.Length);
        string craft = _spawnCraft < TestTarget.StockCraft.Length
            ? TestTarget.StockCraft[_spawnCraft]
            : "clone of this craft";
        ImGui.TextDisabled($"  {craft}");

        string? craftName = _spawnCraft < TestTarget.StockCraft.Length
            ? TestTarget.StockCraft[_spawnCraft]
            : null;

        if (ImGui.Button("Overhead"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.Overhead,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }
        ImGui.SameLine();
        if (ImGui.Button("Head-on"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.HeadOn,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }
        ImGui.SameLine();
        if (ImGui.Button("Passing by"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.PassingBy,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }

        ImGui.TextDisabled("Arm before they arrive.");
        ImGui.TextDisabled("Head-on dives steepest and holds its speed best in atmosphere.");

        ImGui.Separator();

        // Writes the battery's whole world view to KSArmory.log, including why each nearby
        // vehicle was or was not tracked. Far more useful than staring at an empty screen.
        if (ImGui.Checkbox("Verbose log", ref _config.VerboseLog))
        {
            Log.Threshold = _config.VerboseLog ? Log.Level.Debug : Log.Level.Info;
            Log.Info(_config.VerboseLog ? "verbose logging on" : "verbose logging off");
        }
        ImGui.TextDisabled("  developer detail; off in release builds");

        if (ImGui.Button("Write diagnostic dump"))
        {
            Diagnostics.Dump(_battery, _config, _policy);
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("Keep dumping", ref _config.DiagnosticDump))
        {
            Diagnostics.ResetTimer();
        }
        ImGui.TextDisabled("  -> Logs/KSArmory.log");

    }

    // 30 s at 300 m/s spawns 9 km out, comfortably inside the default 20 km radar range,
    // and keeps the ballistic arc shallow enough to stay well clear of terrain.
    private float _spawnSeconds = 30f;
    private float _spawnSpeed = 300f;
    private float _spawnMiss = 1500f;
    private int _spawnCraft;   // index into TestTarget.StockCraft; past the end = clone

    private void DrawTrackList()
    {
        if (_battery.Radar.Tracks.Count == 0)
        {
            ImGui.TextDisabled("scope clear");
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

    // Teams and IFF. The whole subsystem shipped tested and unreachable: nothing outside
    // Config.cs ever wrote Config.Iff or Config.TeamNames, so every session anyone has played
    // ran with no teams declared and every contact Unknown.
    private void DrawIff()
    {
        ImGui.TextDisabled("KSA has no team field. A craft joins a team when the team's name");
        ImGui.TextDisabled("appears anywhere in its name - so \"Red\" also matches \"Redstone\".");
        ImGui.TextDisabled("Longest match wins. Name teams distinctly.");
        ImGui.Separator();

        if (TextField("Own team", ref _ownTeamEntry))
        {
            _policy.Iff.OwnTeam = string.IsNullOrWhiteSpace(_ownTeamEntry) ? null : _ownTeamEntry.Trim();
            Remember(_policy.Iff.OwnTeam);
        }

        ImGui.SameLine();
        ImGui.TextDisabled(_policy.Iff.OwnTeam is null ? "(none - everything is Unknown)" : "");

        if (TextField("Add team", ref _newTeamEntry) && !string.IsNullOrWhiteSpace(_newTeamEntry))
        {
            Remember(_newTeamEntry.Trim());
            _newTeamEntry = string.Empty;
        }

        if (_config.TeamNames.Count == 0)
        {
            ImGui.TextDisabled("  no teams declared; every contact classifies as Unknown");
        }

        for (int i = _config.TeamNames.Count - 1; i >= 0; i--)
        {
            string team = _config.TeamNames[i];
            bool own = string.Equals(team, _policy.Iff.OwnTeam, StringComparison.OrdinalIgnoreCase);

            bool allied = _policy.Iff.AlliedTeams.Contains(team);
            bool neutral = _policy.Iff.NeutralTeams.Contains(team);

            ImGui.TextColored(AllegianceColour(_policy.Iff.Classify(team)), $"  {team}");
            ImGui.SameLine();

            if (own)
            {
                ImGui.TextDisabled("own team");
            }
            else
            {
                if (ImGui.Checkbox($"allied##a{i}", ref allied))
                {
                    Toggle(_policy.Iff.AlliedTeams, team, allied);
                    if (allied) _policy.Iff.NeutralTeams.Remove(team);
                }
                ImGui.SameLine();
                if (ImGui.Checkbox($"neutral##n{i}", ref neutral))
                {
                    Toggle(_policy.Iff.NeutralTeams, team, neutral);
                    if (neutral) _policy.Iff.AlliedTeams.Remove(team);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button($"remove##t{i}"))
            {
                _config.TeamNames.RemoveAt(i);
                _policy.Iff.AlliedTeams.Remove(team);
                _policy.Iff.NeutralTeams.Remove(team);
            }
        }

        ImGui.Separator();

        bool engageUnknown = _policy.Iff.EngageUnknown;
        if (ImGui.Checkbox("Engage unknown contacts", ref engageUnknown)) _policy.Iff.EngageUnknown = engageUnknown;

        bool engageNeutral = _policy.Iff.EngageNeutral;
        if (ImGui.Checkbox("Engage neutrals", ref engageNeutral)) _policy.Iff.EngageNeutral = engageNeutral;

        bool protectFriendly = _policy.Iff.ProtectFriendly;
        if (ImGui.Checkbox("Never engage friendlies", ref protectFriendly)) _policy.Iff.ProtectFriendly = protectFriendly;

    }

    private static void Toggle(HashSet<string> set, string team, bool wanted)
    {
        if (wanted) set.Add(team);
        else set.Remove(team);
    }

    private void Remember(string? team)
    {
        if (string.IsNullOrWhiteSpace(team)) return;
        if (!_config.TeamNames.Contains(team, StringComparer.OrdinalIgnoreCase))
        {
            _config.TeamNames.Add(team);
        }
    }

    private static float4 AllegianceColour(Allegiance a) => a switch
    {
        Allegiance.Friendly => Green,
        Allegiance.Hostile => Red,
        Allegiance.Neutral => Grey,
        _ => Amber,
    };

    // ImGui.InputText wants a fixed byte buffer, so each field owns one and the string is
    // marshalled either side of the call.
    private static bool TextField(string label, ref string value)
    {
        Span<byte> buffer = stackalloc byte[64];
        int written = System.Text.Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
        buffer[Math.Min(written, buffer.Length - 1)] = 0;

        if (!ImGui.InputText(label, buffer, ImGuiInputTextFlags.EnterReturnsTrue, null, default))
        {
            return false;
        }

        int end = buffer.IndexOf((byte)0);
        value = System.Text.Encoding.UTF8.GetString(buffer[..(end < 0 ? buffer.Length : end)]);
        return true;
    }

    private void DrawTuning()
    {
        if (!ImGui.TreeNode("Radar")) { DrawGuidanceNode(); return; }

        ImGui.SliderFloat("Range (m)", ref _config.Sensor.Range, 500f, 40000f);
        ImGui.SliderFloat("Cone half-angle (deg)", ref _config.Sensor.ConeDeg, 5f, 180f);
        ImGui.SliderFloat("Threat radius (m)", ref _config.Sensor.ThreatRadius, 100f, 10000f);
        ImGui.SliderFloat("Threat horizon (s)", ref _config.Sensor.ThreatHorizonSeconds, 5f, 120f);
        ImGui.SliderFloat("Lock time (s)", ref _config.Sensor.LockSeconds, 0f, 5f);
        ImGui.SliderFloat("Min target speed (m/s)", ref _config.Sensor.MinTargetSpeed, 0f, 200f);
        ImGui.TreePop();

        if (ImGui.TreeNode("Turret"))
        {
            ImGui.Checkbox("Track with turret", ref _policy.TurretTracking);
            ImGui.SliderFloat("Traverse rate (deg/s)", ref _profile.SlewRateDeg, 5f, 180f);
            ImGui.SliderFloat("Elevation rate (deg/s)", ref _profile.ElevationRateDeg, 5f, 120f);
            ImGui.SliderFloat("Settle before firing (s)", ref _profile.SettleSeconds, 0f, 2f);
            ImGui.Checkbox("Eject along the tube", ref _profile.LaunchAlongTube);
            ImGui.TextDisabled("  off: slew to the target on launch, plus loft");

            ImGui.Separator();
            ImGui.TextDisabled("Drive it by hand - neither needs a target:");
            ImGui.Checkbox("Spin continuously", ref _policy.TurretSpin);
            ImGui.Checkbox("Manual aim", ref _policy.TurretManual);
            ImGui.SliderFloat("Bearing (deg)", ref _policy.TurretManualBearingDeg, -180f, 180f);
            ImGui.SliderFloat("Elevation (deg)", ref _policy.TurretManualElevationDeg, 0f, 82f);
            ImGui.TextDisabled("  Elevation applies to spin as well as manual aim.");

            ImGui.Separator();
            ImGui.SliderFloat("Search array (rpm)", ref _profile.SearchRadarRpm, 0f, 60f);
            ImGui.Checkbox("Stop the search array", ref _policy.SearchRadarStopped);
            ImGui.TreePop();
        }

        DrawGuidanceNode();
    }

    private void DrawGuidanceNode()
    {
        if (ImGui.TreeNode("Guidance"))
        {
            ImGui.SliderFloat("Nav constant N", ref _munition.NavConstant, 1f, 8f);
            ImGui.SliderFloat("Max lateral (g)", ref _munition.MaxLateralG, 5f, 80f);
            ImGui.SliderFloat("Seeker FOV (deg)", ref _munition.SeekerFovDeg, 10f, 90f);
            ImGui.SliderFloat("Gravity compensation", ref _munition.GravityCompensation, 0f, 1.5f);
            ImGui.SliderFloat("Boost accel (m/s2)", ref _munition.BoostAccel, 0f, 800f);
            ImGui.SliderFloat("Boost time (s)", ref _munition.BoostSeconds, 0f, 10f);
            ImGui.SliderFloat("Launch speed (m/s)", ref _munition.LaunchSpeed, 5f, 300f);
            ImGui.SliderFloat("Max flight time (s)", ref _munition.MaxFlightSeconds, 3f, 90f);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Warhead"))
        {
            ImGui.SliderFloat("Fuse radius (m)", ref _munition.FuseRadius, 2f, 200f);
            ImGui.SliderFloat("Fuse arm delay (s)", ref _munition.FuseArmSeconds, 0f, 5f);
            ImGui.SliderFloat("Lethal radius (m)", ref _munition.LethalRadius, 2f, 300f);
            ImGui.SliderFloat("Blast radius (m)", ref _munition.BlastRadius, 5f, 600f);
            ImGui.SliderInt("Rounds per target", ref _policy.RoundsPerTarget, 1, _profile.TubeCount);
            ImGui.SliderFloat("Salvo spacing (s)", ref _profile.SalvoSpacing, 0.05f, 3f);
            ImGui.SliderFloat("Reload time (s)", ref _profile.ReloadSeconds, 0f, 60f);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Display"))
        {
            ImGui.Checkbox("World overlay", ref _config.DrawOverlays);
            ImGui.TextDisabled("  everything drawn in the world around a system");

            if (_config.DrawOverlays)
            {
                ImGui.Checkbox("Only the system shown in the panel",
                               ref _config.DrawOverlayForFocusedOnly);
                ImGui.TextDisabled("  off: every crewed system draws its own");
            }

            ImGui.Separator();
            ImGui.Checkbox("Warhead effects", ref _config.DrawExplosions);
            ImGui.TextDisabled("  the fireball, not a debug line -- kept when those are off");

            ImGui.Checkbox("Weapons-system markers", ref _config.DrawSystemMarkers);
            ImGui.TextDisabled("  brackets over every system; (+) in the list pins a label");
            ImGui.Checkbox("Radar volume", ref _config.DrawRadarVolume);
            ImGui.Checkbox("Drive facing line", ref _config.DrawTurretFacing);
            ImGui.TextDisabled("  where the drives think they point, not where they are told to");
            ImGui.SliderFloat("Cone draw length (m)", ref _config.ConeDisplayMetres, 200f, 20000f);
            ImGui.TextDisabled("  cosmetic only; detection range is set under Radar");
            ImGui.Checkbox("Tracks", ref _config.DrawTracks);
            ImGui.Checkbox("Track marker spheres", ref _config.DrawTrackMarkers);
            ImGui.TextDisabled("  large ball on each contact; scales with range");
            ImGui.Checkbox("Predicted pass point", ref _config.DrawClosestApproach);
            ImGui.TextDisabled("  where a threat will pass if it holds course");
            ImGui.Checkbox("Rounds", ref _config.DrawMissiles);
            ImGui.Checkbox("Round tracer spheres", ref _config.DrawRoundMarkers);
            // Bodies and tracers are placed by entirely separate paths, so toggling this while
            // watching a round in flight says which of the two is misbehaving.
            ImGui.Checkbox("Round bodies (off = tracers only)", ref _config.UseRoundBodies);
            ImGui.Checkbox("Tube markers (debug)", ref _config.DrawTubeMarkers);
            ImGui.TextDisabled(_battery.RoundBodyCount > 0 && _battery.RoundBodiesWork
                ? "  rounds have real bodies; the tracer hides them up close"
                : "  no round bodies available - tracers are all there is");
            ImGui.TreePop();
        }
    }

    private void DrawLog()
    {
        var events = _battery.Events;
        for (int i = events.Count - 1; i >= 0; i--)
        {
            ImGui.TextDisabled($"[{events[i].AtSeconds:F1}] {events[i].Message}");
        }

    }
}
