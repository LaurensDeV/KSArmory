using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The operator's panel: master arm, radar and guidance tuning, the track list with
/// manual designation, and a rolling event log.
/// </summary>
internal sealed partial class Ui(Config config, BatteryRoster roster, WarpPolicy warp, WatchCamera watch, CraftMover mover, BurstTool bursts)
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

    // The system the panes read. Not fixed, and not set here: Focus points them at whichever
    // system is being drawn and this file calls it before anything else runs. The panes live in
    // UiSystem.cs and UiTuning.cs and simply use them, so a pane reached by any other path will
    // quietly describe the wrong installation.
    private DefenceBattery _battery = null!;
    private BatteryConfig _policy = null!;
    private readonly WatchCamera _watch = watch;
    private readonly CraftMover _mover = mover;
    private readonly BurstTool _bursts = bursts;
    private readonly List<int> _viewports = [];
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
    // window; Debug is for whoever is working on the mod rather than playing with it.
    private enum PaneGroup { Debug }

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
        new("Test targets", DrawTestTargets, PaneGroup.Debug),
        new("Log", DrawLog, PaneGroup.Debug),
    ];

    /// <summary>
    /// Adds <c>Mods → KSArmory</c> to the game's own menu bar.
    ///
    /// <para>KSA draws that bar inline in <c>Program</c> with hardcoded menus and offers no
    /// extension point, and StarMap's attributes are lifecycle hooks with nothing menu-shaped
    /// among them. What makes this work anyway is that ImGui's main menu bar is immediate-mode
    /// and persistent: reopening it later in the same frame appends rather than starting a
    /// second bar.</para>
    ///
    /// <para>"Mods" rather than "KSArmory" as the top-level menu, so several mods doing this
    /// share one menu instead of each adding their own — ImGui merges menus by label.</para>
    ///
    /// <para>Called from <b>before</b> KSA's GUI pass, not after. Measured: from an after-GUI hook
    /// <c>BeginMainMenuBar</c> returns false and nothing appears, because the bar has already been
    /// ended for the frame. Opening it first instead means KSA's own menus append to ours.</para>
    /// </summary>
    public void DrawMenuBarEntry()
    {
        try
        {
            if (!ImGui.BeginMainMenuBar()) return;

            if (ImGui.BeginMenu("Mods"))
            {
                bool visible = Visible;
                if (ImGui.MenuItem("KSArmory", default, ref visible, true)) Visible = visible;

                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }
        catch (Exception e)
        {
            // Never take the panel down with it. A menu that does not appear is a cosmetic loss;
            // an exception here happens inside KSA's own GUI pass.
            if (_warnedMenuBar) return;

            _warnedMenuBar = true;
            Log.Warn($"menu bar entry failed, use the floating button: {e.Message}");
        }
    }

    private bool _warnedMenuBar;

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
            // Closing the panel must not strand the operator with no way back. Kept even though
            // Mods -> KSArmory does the same job: appending to KSA's menu bar depends on ImGui
            // behaviour that has not been proved on anyone else's machine, and a mod with no way
            // to reopen its own panel is unusable rather than merely untidy.
            if (_config.FloatingPanelButton
                && ImGui.Begin("KSArmory##reopen",
                               ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                if (ImGui.Button("KSArmory")) Visible = true;
            }
            if (_config.FloatingPanelButton) ImGui.End();
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
            DrawReportFooter();
        }

        ImGui.End();

        // Outside the main window's Begin/End: each of these is its own top-level window, so
        // they must not be nested inside another one.
        if (anyCrewed)
        {
            DrawManageWindow();
            DrawPanes();
        }

        // Not gated on a crewed system: the thing being reported may be that there isn't one.
        DrawReportWindow();
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
        // Nothing to turn towards when the view is already on it. Shown as inert rather than
        // hidden, so the row keeps its shape and the button does not move under the pointer.
        if (KsaWorld.MainViewFollows(craft))
        {
            ImGui.TextDisabled("(+)");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Already looking at it.");
        }
        else
        {
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

    private void DrawPaneToggles()
    {
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

            DrawLogging();
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
            // Read once: the button toggles pane.Open, so testing it again after would pop a
            // colour that was never pushed, or leak one that was.
            bool open = pane.Open;

            if (open) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.45f, 0.25f, 1f));
            if (ImGui.Button(open ? $"{pane.Title} (open)" : pane.Title)) pane.Open = !open;
            if (open) ImGui.PopStyleColor();
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

    // Where the turret is pointing, and whether it is still swinging. Also the place the engine's
    // verdict on the transform write surfaces: if KSA refuses it, the drive gives up for the
    // session and this is where that gets said, rather than the turret just silently never moving.
    // The weapon system of whichever battery the panel is showing. Tuning edits the shared
    // Arsenal instance, so it reaches every battery running that system.
    private LauncherProfile _profile => _battery.Profile;
    private MunitionProfile _munition => _battery.Munition;
    private SensorProfile _sensor => _battery.Sensor;

    // Speeds worth a button. KSA's own roller stops at 0.1x; these go two decades below.
    private static readonly (string Label, double Speed)[] SlowMotionSpeeds =
    [
        ("0.01x", 0.01), ("0.05x", 0.05), ("0.1x", 0.1), ("0.25x", 0.25), ("1x", 1.0),
    ];

    // 30 s at 300 m/s spawns 9 km out, comfortably inside the default 20 km radar range,
    // and keeps the ballistic arc shallow enough to stay well clear of terrain.
    private float _spawnSeconds = 30f;
    private float _spawnSpeed = 300f;
    private float _spawnMiss = 1500f;
    private int _spawnCraft;   // index into TestTarget.StockCraft; past the end = clone

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

}
