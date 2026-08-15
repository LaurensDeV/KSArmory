using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The operator's panel: master arm, radar and guidance tuning, the track list with
/// manual designation, and a rolling event log.
/// </summary>
internal sealed partial class Ui(Config config, WeaponSystems roster, OpticalHeads heads, WarpPolicy warp, WatchCamera watch, CraftMover mover, BurstTool bursts)
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
    private readonly WeaponSystems _batteries = roster;
    private readonly OpticalHeads _heads = heads;
    private readonly WarpPolicy _warp = warp;

    // The system the panes read. Not fixed, and not set here: Focus points them at whichever
    // system is being drawn and this file calls it before anything else runs. The panes live in
    // UiSystem.cs and UiTuning.cs and simply use them, so a pane reached by any other path will
    // quietly describe the wrong installation.
    private WeaponSystem _battery = null!;
    private SystemConfig _policy = null!;

    // Whether the two above are safe to read this frame. A craft can be worth a window without
    // being a weapons system -- one director and no armament is the case -- and everything under
    // Debug and every pane reads a battery.
    private bool _crewed;
    private readonly WatchCamera _watch = watch;
    private readonly CraftMover _mover = mover;
    private readonly BurstTool _bursts = bursts;
    private readonly List<int> _viewports = [];
    private readonly List<SurveyedPart> _surveyed = [];
    private readonly List<OpticalHeads.Entry> _headScratch = [];
    private readonly List<WeaponSystems.Entry> _weaponScratch = [];
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
        new("KSArmory settings", DrawSettingsPane, PaneGroup.Session),

        // Its own button beside settings, rather than a collapsed header inside that window. Sim
        // speed, the target spawner and the log are all reached *while* an engagement is running,
        // and a fold two windows deep is not a place to keep those.
        new("Debug tools", DrawDebugPane, PaneGroup.Session),

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
    /// <para>A top-level <c>KSArmory</c> rather than a shared <c>Mods</c>, deliberately. MrJeranimo's
    /// <b>ModMenu</b> owns that name: it transpiles <c>Program.DrawMenuBar</c> and splices in its
    /// own <c>BeginMenu("Mods")</c>. Two of those in one bar merge only if ImGui's menu-merging
    /// covers it, and being wrong means two menus side by side on the machines of exactly the
    /// people who have both mods. A name of this mod's own cannot collide, needs no dependency,
    /// and leaves registering with ModMenu as something to add later rather than undo.</para>
    ///
    /// <para>Called from <b>before</b> KSA's GUI pass, not after. From an after-GUI hook
    /// <c>BeginMainMenuBar</c> returns false and nothing appears, because the bar has already been
    /// ended for the frame. Opening it first instead means KSA's own menus append to this one.</para>
    /// </summary>
    public void DrawMenuBarEntry()
    {
        // ModMenu draws this mod's entry when it is installed -- see DrawModMenu. Drawing a
        // second one here would list KSArmory twice in the same bar.
        if (ModMenuPresence.Installed) return;

        try
        {
            if (!ImGui.BeginMainMenuBar()) return;

            if (ImGui.BeginMenu("KSArmory"))
            {
                DrawMenuContents();
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

    // What sits under the menu, wherever the menu came from: this mod's own bar, or ModMenu's.
    private void DrawMenuContents()
    {
        bool visible = Visible;
        if (ImGui.MenuItem("Panel", default, ref visible, true)) Visible = visible;
    }

    /// <summary>
    /// Called by <b>ModMenu</b>, if the player has it, to fill this mod's entry in its shared
    /// menu. Found by reflection on the attribute's name — see <see cref="ModMenuEntryAttribute"/>
    /// for why that means no dependency.
    ///
    /// <para>Static because ModMenu resolves an instance only for a couple of hardcoded method
    /// names; a static one it can always call. <see cref="Current"/> is set when the panel is
    /// built, and the null check is what happens if ModMenu scans before that.</para>
    /// </summary>
    // Said once, not per frame: this is called from inside a menu build, so a failure repeats for
    // as long as the menu is open.
    private static bool _warnedModMenu;

    [ModMenuEntry("KSArmory")]
    public static void DrawModMenu()
    {
        // The identical call through this mod's own bar is wrapped; this one was not, so anything
        // thrown here went into ModMenu's menu build instead of the log -- leaving an entry that
        // does nothing and no evidence anywhere of why.
        try
        {
            Current?.DrawMenuContents();
        }
        catch (Exception e)
        {
            if (_warnedModMenu) return;
            _warnedModMenu = true;
            Log.Warn($"ModMenu entry failed, so its Panel item will not work: {e.Message}. "
                     + "Reopen from the floating KSArmory button instead.");
        }
    }

    /// <summary>The panel ModMenu should drive. There is one.</summary>
    internal static Ui? Current { get; private set; }

    public void Draw()
    {
        // Set here rather than in a constructor: this is a primary-constructor class, and ModMenu
        // may scan for the attribute before anything has drawn.
        Current = this;

        RefreshSystems();
        _batteries.Sync(_systems);


        // Dropped when the craft has nothing left to manage -- a battery *or* a director. Testing
        // the battery alone clears the selection on the frame after a camera-only craft is picked,
        // so the window opens and shuts again before it is ever drawn.
        if (_managed is not null
            && _batteries.For(_managed) is null && _heads.FirstOn(_managed) is null)
        {
            _managed = null;
        }
        Focused = _managed ?? _batteries.Default();

        // Two different questions, and collapsing them into one is a null dereference. The manage
        // window has something to show for a battery *or* a director, so it asks the second; the
        // panes all read `_battery`, which `Focus` leaves unassigned when it answers false, so they
        // must ask the first.
        _crewed = Focus(Focused);

        bool anyCrewed = _crewed || _heads.FirstOn(Focused) is not null;

        if (!Visible)
        {
            // Closing the panel must not strand the operator with no way back, and the button is
            // the only route this mod controls. Both others are somebody else's: appending to
            // KSA's bar is ImGui behaviour rather than a supported hook, and ModMenu's entry is
            // another mod's menu reached by transpiling a private method.
            //
            // So it is drawn whenever it is wanted, and no longer suppressed on the grounds that
            // ModMenu is installed and will provide. The recovery cannot be conditional on a
            // third party working, because there is no way back from being wrong: the setting
            // that would switch this on lives *inside* the panel that is shut.
            bool reopenButton = _config.FloatingPanelButton;

            if (reopenButton
                && ImGui.Begin("KSArmory##reopen",
                               ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                if (ImGui.Button("KSArmory")) Visible = true;
            }
            if (reopenButton) ImGui.End();
            if (anyCrewed) DrawManageWindow();
            DrawPanes();
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
        if (anyCrewed) DrawManageWindow();

        // Outside the crewed gate: these are the session's windows, and the settings one has to
        // open on a world with nothing in it. Each body checks for itself what it needs.
        DrawPanes();

        // Not gated on a crewed system: the thing being reported may be that there isn't one.
        DrawReportWindow();

        // After the panes, which is what fills the head list this reads.
        DrawMapWindow();

        // Not gated on the panel being open: the switcher is for use while flying, and having to
        // open the panel to reach it would put it back where it just came from.
        DrawWeaponsWindow();
    }

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
            if (inv.IsInstallation) _systems.Add((craft, inv));
        }
    }

    // The list the panel opens on: what exists, not what happens to be under the camera.
    //
    // A table, one row per system: a heading, a status line and a row of buttons each stops
    // being readable at two craft.
    private void DrawSystemList()
    {
        RefreshSystems();

        if (_systems.Count == 0)
        {
            ImGui.TextColored(Grey, "Nothing of this mod's is fitted to anything.");
            ImGui.TextDisabled("Fit a launcher from Weapons, or an EO director from Sensors.");
            ImGui.TextDisabled("A craft with only a director is listed too - it is not a weapon,");
            ImGui.TextDisabled("but it has a camera worth pointing.");
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
            WeaponSystems.Entry? entry = _batteries.For(craft);

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
                // That row's own load, never the focused system's. The panes read whichever
                // system is focused and a row is not it, so borrowing the focused launcher here
                // reports one installation's magazine against another's name.
                ImGui.TextColored(e.Policy.Armed ? Red : Grey,
                                  $"{(e.Policy.Armed ? "ARMED" : "safe")}  {Tally(e.Battery)}");
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

    // Inline, and small: three short buttons fit a table row where a full label does not.
    // Moving the battery is a decision about one system, so it lives in that system's window.
    private void DrawSystemRowButtons(KSA.Vehicle craft)
    {
        // Point the camera at it and label it for a few seconds, without moving or commandeering
        // anything. One shot rather than a toggle: both halves end on their own, so there is
        // nothing left to switch off and no state for the button to get out of step with.
        // Worded, not a glyph: ImGui's default font carries basic Latin only, so a crosshair
        // renders as a box, and an ASCII stand-in for one has to be hovered to be understood.
        // "Look at" reads beside "Go to" and "Manage" as the third thing that can be done to a
        // row, which is what it is.
        // Nothing to turn towards when the view is already on it. Shown as inert rather than
        // hidden, so the row keeps its shape and the button does not move under the pointer.
        if (KsaWorld.MainViewFollows(craft))
        {
            ImGui.TextDisabled("Look at");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Already looking at it.");
        }
        else
        {
            if (ImGui.SmallButton("Look at"))
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
        // A craft can carry a director and no armament at all, and every tab but Components reads
        // a battery. Rather than refusing to open -- which leaves the operator with a listed craft
        // and no way into its camera -- the window opens with what that craft actually has.
        bool armed = Focus(craft);
        if (!armed && _heads.FirstOn(craft) is null)
        {
            _managed = null;
            return;
        }

        bool open = true;

        // The title is the craft's identity, so the pane below it does not repeat either half.
        // ###id keeps the window in place while the visible part changes.
        string flying = ReferenceEquals(craft, KsaWorld.ControlledVehicle) ? "" : " - not flying";

        if (ImGui.Begin($"{KsaWorld.DisplayName(craft)}{flying}###KSArmorySystem", ref open))
        {
            // Above the tabs, so it is on screen whichever one is open. What it carries is the
            // answer to why the system is or is not shooting, which is the question most often
            // asked while looking at some other tab.
            if (armed) DrawSystemHeader();

            if (ImGui.BeginTabBar("##systemtabs"))
            {
                // First, and the only one a craft always has: what it is made of. Every other tab
                // is about a weapons system, which a craft carrying one director does not have.
                if (ImGui.BeginTabItem("Components")) { DrawComponents(craft); ImGui.EndTabItem(); }

                if (armed)
                {
                    // "Radar" rather than "Tracks": the tab carries the lock and the scope state
                    // as well as the list, which is the whole of what the set is doing.
                    if (ImGui.BeginTabItem("Radar")) { DrawTrackList(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem("Tuning")) { DrawTuning(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem("Teams and IFF")) { DrawIff(); ImGui.EndTabItem(); }
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();

        if (!open) _managed = null;
    }

    // What one system is holding, in a table cell: every armament it carries, however many that is.
    private static string Tally(WeaponSystem battery)
    {
        WeaponFit fit = WeaponFit.Of(battery.Profile, battery.Sensor);
        return string.Join("  ", fit.Armaments.Select(a => a.Tally(LiveState(battery, a).Remaining)));
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

    private void DrawPaneToggles()
    {
        // One button. Everything session-wide lives in the window behind it, so the main panel is
        // the list of systems and nothing else -- which is the only thing on it that changes as
        // the world does.
        DrawPaneGroup(null, PaneGroup.Session);
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

    // The weapon system of whichever battery the panel is showing. Tuning edits the shared
    // Arsenal instance, so it reaches every battery running that system.
    private LauncherProfile _profile => _battery.Profile;
    private MunitionProfile _munition => _battery.Munition;
    private SensorProfile _sensor => _battery.Sensor;

    // What the focused system is fitted with, which is what decides which controls exist. Read
    // fresh every time: the profiles it is derived from are the same instances the tuning sliders
    // edit, so anything held across frames answers for the load the system started with.
    private WeaponFit _fit => WeaponFit.Of(_battery.Profile, _battery.Sensor);

    // The battery's own counters, paired with the armament they belong to. The one place that
    // names an armament kind: a battery exposes a counter per weapon rather than a lookup, so
    // something has to bridge the description to them.
    private static (int Remaining, bool Firing) LiveState(WeaponSystem battery, Armament arm)
        => arm.Kind == ArmamentKind.Belt
            ? (battery.GunAmmo, battery.GunsFiring)
            : (battery.Ammo, false);

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
