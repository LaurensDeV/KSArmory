using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The operator's panel: master arm, radar and guidance tuning, the track list with
/// manual designation, and a rolling event log.
/// </summary>
internal sealed partial class Ui(Config config, WeaponSystems roster, OpticalHeads heads, IcbmComputers icbms, WarpPolicy warp, WatchCamera watch, CraftMover mover, BurstTool bursts)
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

        // This pass walks the world too, and it is a different hook from the simulation step --
        // one can run without the other, so neither may inherit the other's census.
        KsaWorld.InvalidateCensus();

        RefreshSystems();
        _batteries.Sync(_systems);
        _icbms.Sync(_systems, _batteries.Handovers);


        // Follow a weapon that a decoupler carried onto another craft, before the test below
        // notices the old one has nothing left and shuts the window - which would happen in the
        // middle of a deployment, on the frame the operator most wants to be watching.
        for (int i = 0; i < _batteries.Handovers.Count; i++)
        {
            if (ReferenceEquals(_managed, _batteries.Handovers[i].From))
            {
                _managed = _batteries.Handovers[i].To;
            }
        }

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

        // Filled here rather than by whichever tab happens to be in front: every window below
        // reads it, and `BeginTabItem` bodies run only for the selected tab, so filling it from
        // Components leaves the map drawing the heads of whatever craft was shown when that tab
        // was last open.
        _heads.On(_managed, _headScratch);

        if (Visible)
        {
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
        }
        else if (_config.FloatingPanelButton)
        {
            // Closing the panel must not strand the operator with no way back, and the button is
            // the only route this mod controls. Both others are somebody else's: appending to
            // KSA's bar is ImGui behaviour rather than a supported hook, and ModMenu's entry is
            // another mod's menu reached by transpiling a private method.
            //
            // Drawn whenever it is wanted rather than being suppressed where ModMenu would
            // provide a route: the recovery cannot be conditional on a third party working,
            // because there is no way back from being wrong -- the setting that would switch this
            // on lives *inside* the panel that is shut.
            if (ImGui.Begin("KSArmory##reopen",
                            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                if (ImGui.Button("KSArmory")) Visible = true;
            }

            ImGui.End();
        }

        // Each of these is its own top-level window, so none may be nested inside the panel's
        // Begin/End -- and none is gated on the panel being open. Every one carries its own open
        // flag and returns on it, so closing the panel is not a request to shut the map, the
        // scope, a report half-written, or the switcher the trigger is pointed at. The manage
        // window is the only one gated at all, on there being something to manage.
        if (anyCrewed) DrawManageWindow();
        DrawPanes();
        DrawReportWindow();
        DrawMapWindow();
        DrawScopeWindow();
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
        IReadOnlyList<KSA.Vehicle> world = KsaWorld.Vehicles;
        for (int i = 0; i < world.Count; i++)
        {
            KSA.Vehicle craft = world[i];
            KsaWorld.SurveyParts(craft, _surveyed);
            WeaponInventory inv = WeaponSurvey.Survey(_surveyed, Catalogue.Components);
            if (inv.IsInstallation) _systems.Add((craft, inv));
        }
    }

    // The list the panel opens on: what exists, not what happens to be under the camera.
    //
    // Grouped by the team each installation fights for, one row each, after the vessel switchers
    // players already know: the name flies it, and what is wanted mid-fight -- guard, chase, team
    // -- is one click on the same row. Everything else is behind its "..." window.
    private void DrawSystemList()
    {
        RefreshSystems();

        if (_systems.Count == 0)
        {
            ImGui.TextColored(Grey, "Nothing of this mod's is fitted to anything.");
            ImGui.TextDisabled("Fit a launcher from Weapons, or an EO director from Sensors.");
            Tip("A craft with only a director is listed too. It is not a weapon, but it has a "
                + "camera worth pointing.");
            return;
        }

        if (!ImGui.BeginTable("##switcher", 5, ImGuiTableFlags.SizingStretchProp)) return;

        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##guard", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##chase", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##team", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##more", ImGuiTableColumnFlags.WidthFixed);

        IReadOnlyList<string> teams = _config.TeamNames;

        // Each declared team in its declared order, then the rest: on no team, or on one that is
        // no longer declared, which would otherwise be listed nowhere.
        for (int group = 0; group <= teams.Count; group++)
        {
            bool headed = false;

            for (int i = 0; i < _systems.Count; i++)
            {
                (KSA.Vehicle craft, WeaponInventory inv) = _systems[i];
                if (GroupOf(TeamOf(craft), teams) != group) continue;

                if (!headed)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (group < teams.Count) ImGui.TextColored(TeamColour(group), teams[group]);
                    else ImGui.TextColored(Grey, "No team");
                    headed = true;
                }

                ImGui.PushID(i);
                ImGui.TableNextRow();
                DrawSwitcherRow(craft, inv);
                ImGui.PopID();
            }
        }

        ImGui.EndTable();
    }

    // One installation's row. Each switch acts on everything of its kind on the craft -- two
    // rails are one craft to guard -- where its own window acts on one system at a time.
    private void DrawSwitcherRow(KSA.Vehicle craft, WeaponInventory inv)
    {
        _rowSystems.Clear();
        foreach (WeaponSystems.Entry e in _batteries.All)
        {
            if (ReferenceEquals(e.Craft, craft)) _rowSystems.Add(e);
        }

        _heads.On(craft, _rowHeads);

        DrawSwitcherName(craft, inv);

        ImGui.TableNextColumn();
        if (_rowSystems.Count > 0) DrawGuardButton();

        ImGui.TableNextColumn();
        if (_rowSystems.Count > 0) DrawChaseButton();

        ImGui.TableNextColumn();
        DrawTeamButton(craft);

        ImGui.TableNextColumn();
        if (ImGui.Button("...")) _managed = craft;
        Tip("Everything else about it, in its own window.");
    }

    // Clicking a name flies it, as in every switcher of this kind; looking at it without taking
    // the seat is the right button.
    private void DrawSwitcherName(KSA.Vehicle craft, WeaponInventory inv)
    {
        ImGui.TableNextColumn();

        bool flying = ReferenceEquals(craft, KsaWorld.ControlledVehicle);
        float width = ImGui.GetContentRegionAvail().X;

        if (flying) ImGui.PushStyleColor(ImGuiCol.Button, FlyingButton);
        if (ImGui.Button($"{KsaWorld.DisplayName(craft)}##name", new float2?(new float2(width, 0f)))
            && !flying)
        {
            KsaWorld.GoTo(craft);
        }
        if (flying) ImGui.PopStyleColor();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Markers.Show(craft);
            _watch.Watch(craft);
        }

        string status = _batteries.For(craft) is { } e
            ? $"{(e.Policy.Armed ? "ARMED" : "safe")}  {Tally(e.Battery)}{Speed(e.Battery)}"
            : Describe(inv);

        Tip(status + "\n\n" + (flying ? "You are flying it." : "Click to fly it.")
            + " Right-click to look at it and label it without taking the seat.");
    }

    private void DrawGuardButton()
    {
        Guard guard = Guard.Safe;
        for (int i = 0; i < _rowSystems.Count; i++)
        {
            WeaponSystems.Entry e = _rowSystems[i];
            Guard one = GuardState.Of(e.Policy.Armed, e.Policy.AutoEngage, CanAutoEngage(e));
            guard = i == 0 ? one : GuardState.Combine(guard, one);
        }

        ImColor8 ink = guard switch
        {
            Guard.Guarding => GuardInk,
            Guard.Armed => ArmedInk,
            _ => OffInk,
        };

        if (IconButton("##guard", Icon.Shield, ink, lit: guard != Guard.Safe))
        {
            bool on = GuardState.TurnsOn(guard);
            foreach (WeaponSystems.Entry e in _rowSystems)
            {
                e.Policy.Armed = on;
                if (CanAutoEngage(e)) e.Policy.AutoEngage = on;
            }
        }

        Tip(guard switch
        {
            Guard.Guarding => "Guarding: armed, and engaging whatever its sensors and IFF allow. "
                              + "Click to make every weapon on it safe.",
            Guard.Armed => "Armed, but not standing guard: some or all of its weapons fire only when "
                           + "told. Click to guard.",
            _ => "Safe. Click to guard: master arm and auto-engage together, on every weapon on it.",
        });
    }

    private static bool CanAutoEngage(WeaponSystems.Entry e)
        => WeaponFit.Of(e.Battery.Profile, e.Battery.Sensor).AutoEngages;

    private void DrawChaseButton()
    {
        bool chasing = false;
        foreach (WeaponSystems.Entry e in _rowSystems) chasing |= e.Policy.ChaseRounds;

        if (IconButton("##chase", Icon.Camera, chasing ? ChaseInk : OffInk, lit: chasing))
        {
            foreach (WeaponSystems.Entry e in _rowSystems) e.Policy.ChaseRounds = !chasing;
        }

        Tip((chasing ? "Chasing. " : "Not chasing. ")
            + "Rides the camera behind a round this craft fires, while it is the craft you are "
            + "flying or the one whose window is open, and hands the view back on its own: after "
            + "the burst, or about two seconds after the round has nothing left to arrive at.");
    }

    private void DrawTeamButton(KSA.Vehicle craft)
    {
        IReadOnlyList<string> teams = _config.TeamNames;
        string? team = TeamOf(craft);
        int group = GroupOf(team, teams);

        ImColor8 ink = group < teams.Count ? Ink(TeamColour(group)) : OffInk;

        if (IconButton("##team", Icon.Flag, ink, lit: false) && teams.Count > 0)
        {
            string? next = Teams.Next(team, teams);

            foreach (WeaponSystems.Entry e in _rowSystems) e.Policy.Iff.OwnTeam = next;
            foreach (OpticalHeads.Entry h in _rowHeads) h.Policy.Iff.OwnTeam = next;

            // The Teams and IFF tab edits a text buffer of its own, which would go on showing the
            // team this replaced.
            if (ReferenceEquals(craft, Focused)) _ownTeamEntry = next ?? string.Empty;
        }

        Tip(teams.Count == 0
            ? "No teams declared yet. Add them on the Teams and IFF tab of any craft's window."
            : $"On {team ?? "no team"}: the side it fights for, which its IFF sorts every contact "
              + "against. Click for the next declared team.");
    }

    // The side an installation fights for: its selected weapon's, or its director's when it
    // carries no weapon. What the flag shows and what the list groups by.
    private string? TeamOf(KSA.Vehicle craft)
    {
        if (_batteries.For(craft) is { } entry) return entry.Policy.Iff.OwnTeam;

        _heads.On(craft, _teamHeads);
        return _teamHeads.Count > 0 ? _teamHeads[0].Policy.Iff.OwnTeam : null;
    }

    private static int GroupOf(string? team, IReadOnlyList<string> teams)
    {
        if (team is null) return teams.Count;

        for (int i = 0; i < teams.Count; i++)
        {
            if (string.Equals(teams[i], team, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return teams.Count;
    }

    private readonly List<WeaponSystems.Entry> _rowSystems = [];
    private readonly List<OpticalHeads.Entry> _rowHeads = [];
    private readonly List<OpticalHeads.Entry> _teamHeads = [];

    private static readonly float4[] TeamColours =
    [
        new(1.00f, 0.42f, 0.36f, 1f),
        new(0.42f, 0.62f, 1.00f, 1f),
        new(0.45f, 0.88f, 0.45f, 1f),
        new(1.00f, 0.78f, 0.30f, 1f),
        new(0.76f, 0.52f, 1.00f, 1f),
        new(0.36f, 0.86f, 0.90f, 1f),
    ];

    private static float4 TeamColour(int group) => TeamColours[group % TeamColours.Length];

    private static ImColor8 Ink(float4 c)
        => new((byte)(c.X * 255f), (byte)(c.Y * 255f), (byte)(c.Z * 255f), (byte)(c.W * 255f));

    private static readonly float4 LitButton = new(0.22f, 0.36f, 0.26f, 1f);
    private static readonly float4 FlyingButton = new(0.20f, 0.32f, 0.48f, 1f);
    private static readonly ImColor8 GuardInk = new(100, 240, 120, 255);
    private static readonly ImColor8 ArmedInk = new(255, 200, 70, 255);
    private static readonly ImColor8 ChaseInk = new(130, 190, 255, 255);
    private static readonly ImColor8 OffInk = new(140, 140, 150, 255);
    private static readonly ImColor8 Lens = new(24, 26, 32, 255);

    private enum Icon { Shield, Camera, Flag }

    // A square button the height of a text one, with a symbol drawn on it. Drawn rather than
    // typed, because KSA's fonts carry basic Latin only and a symbol would render as a box.
    private static bool IconButton(string id, Icon icon, ImColor8 ink, bool lit)
    {
        float side = ImGui.GetFrameHeight();

        if (lit) ImGui.PushStyleColor(ImGuiCol.Button, LitButton);
        bool clicked = ImGui.Button(id, new float2?(new float2(side, side)));
        if (lit) ImGui.PopStyleColor();

        DrawIcon(ImGui.GetWindowDrawList(), icon, ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ink);
        return clicked;
    }

    private static void DrawIcon(ImDrawListPtr draw, Icon icon, float2 min, float2 max, ImColor8 ink)
    {
        float w = max.X - min.X;
        float h = max.Y - min.Y;
        float2 P(float u, float v) => new(min.X + (u * w), min.Y + (v * h));

        switch (icon)
        {
            case Icon.Shield:
                draw.AddRectFilled(P(0.25f, 0.18f), P(0.75f, 0.52f), ink);
                draw.AddTriangleFilled(P(0.25f, 0.52f), P(0.75f, 0.52f), P(0.5f, 0.86f), ink);
                break;

            case Icon.Camera:
                draw.AddRectFilled(P(0.18f, 0.34f), P(0.82f, 0.78f), ink);
                draw.AddRectFilled(P(0.36f, 0.24f), P(0.56f, 0.34f), ink);
                draw.AddCircleFilled(P(0.5f, 0.56f), 0.13f * h, Lens);
                break;

            case Icon.Flag:
                draw.AddLine(P(0.3f, 0.16f), P(0.3f, 0.86f), ink, 1.5f);
                draw.AddTriangleFilled(P(0.3f, 0.18f), P(0.78f, 0.34f), P(0.3f, 0.5f), ink);
                break;
        }
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

                // Second, and not gated on `armed`: the computer flies the whole vehicle, so it is
                // there whether or not the weapon it is delivering has a radar to speak of.
                if (_icbms.For(craft) is { } computer && ImGui.BeginTabItem("Ballistic"))
                {
                    DrawIcbm(computer);
                    ImGui.EndTabItem();
                }

                if (armed)
                {
                    // "Radar" rather than "Tracks": the tab carries the lock and the scope state
                    // as well as the list, which is the whole of what the set is doing.
                    //
                    // Only for a set that presents a picture. A seeker head cues the shooter with a
                    // growl and a reticle and a designation set has no array at all, so a scope of
                    // tracks for either shows a search that never happened. An anti-radiation
                    // seeker is the exception and gets its own name, because a list of who is
                    // radiating is not a search picture.
                    string? scope = _sensor.Scope switch
                    {
                        ScopePresentation.Search => "Radar",
                        ScopePresentation.Emitters => "Emitters",
                        _ => null,
                    };

                    if (scope is not null && ImGui.BeginTabItem(scope))
                    {
                        DrawScope();
                        DrawTrackList();
                        ImGui.EndTabItem();
                    }
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
    // Airspeed against the ground under it, the way a round's is measured -- in the ecliptic every
    // craft on the planet reads 29.8 km/s and none of them are going anywhere. Blank below walking
    // pace, because a row for a launcher parked on its pad does not need a zero in it.
    private static string Speed(IWeaponSystemView battery)
    {
        if (battery.Platform is not { } craft || !KsaWorld.IsAlive(craft)) return "";

        try
        {
            double3 at = KsaWorld.PositionEcl(craft);
            double3 through = KsaWorld.VelocityEcl(craft) - KsaWorld.GroundVelocityAt(craft, at);
            double speed = Vec.Len(through);

            return double.IsFinite(speed) && speed >= 1.0 ? $"   {speed:N0} m/s" : "";
        }
        catch
        {
            return "";
        }
    }

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

    // What the control just drawn does, on hover. Explanations live here and a line under a
    // control is for state the operator has to act on -- see CLAUDE.md on the panel.
    //
    // Wrapped, because a bare tooltip never is and a long one runs off the screen.
    private static void Tip(string text)
    {
        if (!ImGui.BeginItemTooltip()) return;

        ImGui.PushTextWrapPos(ImGui.GetFontSize() * TipWidthEms);
        ImGui.TextWrapped(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    // The width Dear ImGui's own help markers wrap at.
    private const float TipWidthEms = 35f;

    // A grey (?) carrying an explanation that belongs to a section rather than to one control.
    private static void Help(string text)
    {
        ImGui.TextDisabled("(?)");
        Tip(text);
    }

}
