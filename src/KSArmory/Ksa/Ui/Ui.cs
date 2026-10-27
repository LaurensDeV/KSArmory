using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The operator's panel: auto-engage, radar and guidance tuning, the track list with
/// manual designation, and a rolling event log.
/// </summary>
internal sealed partial class Ui(Config config, WeaponSystems roster, OpticalHeads heads, Countermeasures countermeasures, SearchSets searchSets, CraftPicture picture, IcbmComputers icbms, WarpPolicy warp, WatchCamera watch, CraftMover mover, BurstTool bursts, Func<WeaponSystem, StoreReach> reachFor)
{
    // The overlay's own answer: a fresh solve is three flown trajectories, and could disagree with the ring.
    private readonly Func<WeaponSystem, StoreReach> _reachFor = reachFor;

    private static readonly float4 Green = new(0.4f, 1.0f, 0.45f, 1f);
    private static readonly float4 Red = new(1.0f, 0.35f, 0.3f, 1f);
    private static readonly float4 Amber = new(1.0f, 0.78f, 0.25f, 1f);
    private static readonly float4 Grey = new(0.65f, 0.65f, 0.7f, 1f);

    // Indicative only, at an assumed 60 fps: the real limit is per frame step.
    private const double MaxTrackableWarp = Interceptor.MaxFaithfulStep * 60.0;

    private readonly Config _config = config;
    private readonly WeaponSystems _roster = roster;
    private readonly OpticalHeads _heads = heads;
    private readonly Countermeasures _countermeasures = countermeasures;
    private readonly List<Countermeasures.Entry> _dispenserScratch = [];
    private readonly SearchSets _searchSets = searchSets;
    private readonly CraftPicture _picture = picture;
    private readonly List<SearchSet> _searchScratch = [];
    private readonly WarpPolicy _warp = warp;

    // Set by Focus before any pane draws; a pane reached by another path describes the wrong system.
    private WeaponSystem _system = null!;
    private SystemConfig _policy = null!;

    // Whether the two above are safe to read: a craft with one director and no armament has a
    // window but no system.
    private bool _crewed;
    private readonly WatchCamera _watch = watch;
    private readonly CraftMover _mover = mover;
    private readonly BurstTool _bursts = bursts;
    private readonly List<SurveyedPart> _surveyed = [];
    private readonly List<OpticalHeads.Entry> _headScratch = [];
    private readonly List<WeaponSystems.Entry> _weaponScratch = [];
    private KSA.Vehicle? _managed;
    private string _newTeamEntry = string.Empty;
    private string _menuTeamEntry = string.Empty;

    public bool Visible = true;

    /// <summary>The system the panel is pointed at, for the overlay to highlight.</summary>
    public KSA.Vehicle? Focused { get; private set; }

    /// <summary>Points the panel at a craft, as its switcher row's "..." button does.</summary>
    public void Manage(KSA.Vehicle craft) => _managed = craft;

    // False when nothing is crewed, the one state in which the panes must not be drawn.
    private bool Focus(KSA.Vehicle? craft)
    {
        if (_roster.For(craft) is not { } entry) return false;

        _system = entry.Weapon;
        _policy = entry.Policy;
        return true;
    }

    private enum PaneGroup { Session, Debug }

    // A class so Open is shared with the button that toggles it.
    private sealed class Pane(string title, Action body, PaneGroup group)
    {
        public readonly string Title = title;
        public readonly Action Body = body;
        public readonly PaneGroup Group = group;

        public bool Open;
    }

    private Pane[]? _panes;

    // Lazy because every Body is an instance method. Listed in button order.
    private Pane[] Panes => _panes ??=
    [
        new("KSArmory settings", DrawSettingsPane, PaneGroup.Session),

        new("Teams", DrawTeamsWindow, PaneGroup.Session),

        // Its own button, not a fold in settings: it is reached during an engagement.
        new("Debug tools", DrawDebugPane, PaneGroup.Session),

        new("Test targets", DrawTestTargets, PaneGroup.Debug),
        new("Log", DrawLog, PaneGroup.Debug),
    ];

    /// <summary>
    /// Adds a top-level <c>KSArmory</c> menu to the game's menu bar, which has no extension point:
    /// ImGui's main menu bar appends when reopened in the same frame. Top-level rather than under
    /// <c>Mods</c>, which ModMenu owns and splices in by transpiling <c>Program.DrawMenuBar</c>.
    /// <para>Called <b>before</b> KSA's GUI pass: after it, <c>BeginMainMenuBar</c> returns false
    /// because the bar has been ended for the frame.</para>
    /// </summary>
    public void DrawMenuBarEntry()
    {
        // ModMenu draws the entry when installed (DrawModMenu); a second would list it twice.
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
            // An exception here is inside KSA's own GUI pass; a missing menu is cosmetic.
            if (_warnedMenuBar) return;

            _warnedMenuBar = true;
            Log.Warn($"menu bar entry failed, use the floating button: {e.Message}");
        }
    }

    private bool _warnedMenuBar;

    private void DrawMenuContents()
    {
        bool visible = Visible;
        if (ImGui.MenuItem("Panel", default, ref visible, true)) Visible = visible;

        if (BuiltInFamilies.Loaded.Count > 0 && ImGui.BeginMenu("Built-in weapons"))
        {
            // Kept open on a click, so the label saying a restart is owed is seen when it changes.
            ImGui.PushItemFlag(ImGuiItemFlags.AutoClosePopups, false);
            foreach (string family in BuiltInFamilies.Loaded)
            {
                bool on = !BuiltInFamilies.Wanted.Contains(family);
                string owed = on == BuiltInFamilies.OffAtStart.Contains(family) ? " (restart to apply)" : "";
                if (ImGui.MenuItem($"{FamilyLabel(family)}{owed}###{family}", default, ref on, true))
                {
                    BuiltInFamilies.Want(family, on);
                }
                Tip("Off, its parts still load on craft that carry them, as plain structure, and leave the "
                    + "editor's list; its rounds and sensors stay available to other packs. Takes effect "
                    + "when KSA next starts.");
            }

            ImGui.Separator();
            if (ImGui.MenuItem("All on")) BuiltInFamilies.WantAll(on: true);
            if (ImGui.MenuItem("All off")) BuiltInFamilies.WantAll(on: false);
            Tip("Every family off leaves weapon packs as the only weapons there are: how a pack that "
                + "replaces the whole arsenal, such as the example mod's copy, is flown on its own.");

            ImGui.PopItemFlag();
            ImGui.EndMenu();
        }
    }

    // "DirectedEnergy" -> "Directed energy".
    private static string FamilyLabel(string family)
    {
        System.Text.StringBuilder label = new();
        for (int i = 0; i < family.Length; i++)
        {
            char c = family[i];
            if (i > 0 && char.IsUpper(c)) label.Append(' ').Append(char.ToLowerInvariant(c));
            else label.Append(c);
        }

        return label.ToString();
    }

    // Said once: a failure inside a menu build repeats every frame the menu is open.
    private static bool _warnedModMenu;

    /// <summary>
    /// Called by ModMenu, found by the attribute's name. Static because ModMenu resolves an
    /// instance only for a few hardcoded method names; <see cref="Current"/> is null before the first draw.
    /// </summary>
    [ModMenuEntry("KSArmory")]
    public static void DrawModMenu()
    {
        // Unwrapped, anything thrown here lands in ModMenu's menu build instead of this log.
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

    /// <summary>The panel ModMenu should drive.</summary>
    internal static Ui? Current { get; private set; }

    public void Draw()
    {
        Current = this;

        // A different hook from the simulation step, and either can run without the other.
        KsaWorld.InvalidateCensus();

        RefreshSystems();
        _roster.Sync(_systems);
        _icbms.Sync(_systems, _roster.Handovers);


        // Before the test below, which would shut the window on the craft a decoupler emptied.
        for (int i = 0; i < _roster.Handovers.Count; i++)
        {
            if (ReferenceEquals(_managed, _roster.Handovers[i].From))
            {
                _managed = _roster.Handovers[i].To;
            }
        }

        // A system, a director or a search set: testing the system alone drops a camera-only craft.
        if (_managed is not null
            && _roster.For(_managed) is null && _heads.FirstOn(_managed) is null && !CarriesSearchSet(_managed))
        {
            _managed = null;
        }
        if (_weaponsCraft is not null && (!_weaponsOpen || !KsaWorld.IsAlive(_weaponsCraft)
                                          || _roster.For(_weaponsCraft) is null))
        {
            _weaponsCraft = null;
        }

        // The Weapons window's craft is the one operated: trigger, designator and sight follow it.
        Focused = _weaponsCraft ?? _managed ?? _roster.Default();

        // Two questions: the panes read `_system` and need the first; the manage window also opens
        // for a director, so it asks the second. Collapsing them is a null dereference.
        _crewed = Focus(Focused);

        bool anyCrewed = _crewed || _heads.FirstOn(Focused) is not null;

        // Here, not in a tab: BeginTabItem bodies run only for the selected tab, and every window
        // below reads it.
        _heads.On(_managed, _headScratch);

        if (Visible)
        {
            // ###id so a version bump does not move the window.
            if (ImGui.Begin($"KSArmory {Build.Version}###KSArmory", ref Visible))
            {
                DrawSystemList();
                ImGui.Separator();
                DrawReportFooter();
            }

            ImGui.End();
        }
        else if (_config.FloatingPanelButton)
        {
            // The only route back this mod controls, so it is not suppressed where ModMenu would
            // offer one: the setting to switch it on is inside the shut panel.
            if (ImGui.Begin("KSArmory##reopen",
                            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                if (ImGui.Button("KSArmory")) Visible = true;
            }

            ImGui.End();
        }

        // Top-level windows, outside the panel's Begin/End and not gated on it: each carries its
        // own open flag, so closing the panel shuts none of them.
        if (anyCrewed) DrawManageWindow();
        DrawPanes();
        DrawReportWindow();
        DrawMapWindow();
        DrawScopeWindow();
        DrawPlanetMapWindow();
        DrawWeaponsWindow();
    }

    // On a timer: a survey walks every part of every loaded vehicle.
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

    // Grouped by team, one row per installation.
    private void DrawSystemList()
    {
        RefreshSystems();

        if (_systems.Count == 0)
        {
            ImGui.TextColored(Grey, "No weapons or sensors on any craft.");
            return;
        }

        if (!ImGui.BeginTable("##switcher", 6, ImGuiTableFlags.SizingStretchProp)) return;

        // Each icon keeps its column, so a craft without one leaves a gap rather than shifting the rest.
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##weapons", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##guard", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##chase", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##team", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##more", ImGuiTableColumnFlags.WidthFixed);

        IReadOnlyList<string> teams = _config.TeamNames;

        // Declared teams in order, then the rest: no team, or one no longer declared.
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

    // Each switch acts on every system of its kind on the craft; its window acts on one.
    private void DrawSwitcherRow(KSA.Vehicle craft, WeaponInventory inv)
    {
        _rowSystems.Clear();
        foreach (WeaponSystems.Entry e in _roster.All)
        {
            if (ReferenceEquals(e.Craft, craft)) _rowSystems.Add(e);
        }

        _heads.On(craft, _rowHeads);

        DrawSwitcherName(craft, inv);

        ImGui.TableNextColumn();
        if (_rowSystems.Count > 0) DrawWeaponsButton(craft);

        ImGui.TableNextColumn();
        if (_rowSystems.Count > 0) DrawGuardButton();

        ImGui.TableNextColumn();
        if (_rowSystems.Count > 0) DrawChaseButton();

        ImGui.TableNextColumn();
        DrawTeamButton(craft);

        ImGui.TableNextColumn();
        bool open = ReferenceEquals(_managed, craft);
        if (open) ImGui.PushStyleColor(ImGuiCol.Button, LitButton);
        if (ImGui.Button("..."))
        {
            // One craft operated at a time: a window's trigger fires the focused craft's weapons.
            _managed = craft;
            _weaponsCraft = null;
        }
        if (open) ImGui.PopStyleColor();
        Tip(open ? "Its window is the one open." : "Everything else about it, in its own window.");
    }

    private void DrawSwitcherName(KSA.Vehicle craft, WeaponInventory inv)
    {
        ImGui.TableNextColumn();

        bool flying = ReferenceEquals(craft, KsaWorld.ControlledVehicle);
        float width = ImGui.GetContentRegionAvail().X;

        // Marked as well as coloured, so colour is not the only cue.
        ImGui.PushStyleColor(ImGuiCol.Button, flying ? FlyingButton : new float4(0f, 0f, 0f, 0f));
        if (flying) ImGui.PushStyleColor(ImGuiCol.ButtonHovered, FlyingButtonHovered);
        float2 leftAligned = new(0f, 0.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, in leftAligned);

        // Indented by a marker's width on every row, so the names line up whether marked or not.
        if (ImGui.Button($"     {KsaWorld.DisplayName(craft)}##name", new float2?(new float2(width, 0f)))
            && !flying)
        {
            KsaWorld.GoTo(craft);
        }

        ImGui.PopStyleVar(1);
        ImGui.PopStyleColor(flying ? 2 : 1);

        if (flying) DrawFlyingMarker(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Markers.Show(craft);
            _watch.Watch(craft);
        }

        string status = _roster.For(craft) is { } e
            ? $"{(e.Policy.AutoEngage ? "GUARDING" : "manual")}  {Tally(e.Weapon)}{Speed(e.Weapon)}"
            : Describe(inv);

        Tip(status + "\n\n" + (flying ? "You are flying it." : "Click to fly it.")
            + " Right-click to look at it and label it without taking the seat.");
    }

    private void DrawGuardButton()
    {
        Guard? guard = null;
        foreach (WeaponSystems.Entry e in _rowSystems)
        {
            if (GuardState.Of(e.Policy.AutoEngage, CanAutoEngage(e)) is not { } one) continue;
            guard = guard is { } soFar ? GuardState.Combine(soFar, one) : one;
        }

        // Nothing aboard engages on its own, as on a craft carrying only stores.
        if (guard is not { } state) return;

        ImColor8 ink = state switch
        {
            Guard.Guarding => GuardInk,
            Guard.Partly => PartlyInk,
            _ => OffInk,
        };

        if (IconButton("##guard", Icon.Shield, ink, lit: state != Guard.Off))
        {
            bool on = GuardState.TurnsOn(state);
            foreach (WeaponSystems.Entry e in _rowSystems)
            {
                if (CanAutoEngage(e)) e.Policy.AutoEngage = on;
            }
        }

        Tip(state switch
        {
            Guard.Guarding => "Guarding: engaging whatever its sensors and IFF allow, on its own. "
                              + "Click to stop. FIRE works either way.",
            Guard.Partly => "Partly guarding: some of its weapons engage on their own and some fire "
                            + "only when you press FIRE. Click to guard with all of them.",
            _ => "Not guarding: it fires only when you press FIRE. Click to let every weapon on it "
                 + "engage on its own.",
        });
    }

    private static bool CanAutoEngage(WeaponSystems.Entry e)
        => WeaponFit.Of(e.Weapon.Profile, e.Weapon.Sensor).AutoEngages;

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
            + "the burst, or about two seconds after the round has nothing left to arrive at. "
            + "Right-drag looks around the round and the wheel moves in or out; let go and the view "
            + "eases back behind it.");
    }

    private void DrawWeaponsButton(KSA.Vehicle craft)
    {
        bool open = _weaponsOpen && ReferenceEquals(Focused, craft);

        if (IconButton("##weapons", Icon.Missile, open ? ChaseInk : OffInk, lit: open))
        {
            if (open)
            {
                _weaponsOpen = false;
            }
            else
            {
                _weaponsCraft = craft;
                _weaponsOpen = true;

                // Another craft's window would fire this craft's weapons from its own trigger.
                if (_managed is not null && !ReferenceEquals(_managed, craft)) _managed = null;
            }
        }

        Tip(open ? "Its weapons are open." : "Its weapons: what each is loaded with, whether it is armed, and a trigger.");
    }

    // A click opens the menu, where the first team is created; right-click steps to the next team.
    private void DrawTeamButton(KSA.Vehicle craft)
    {
        List<string> teams = _config.TeamNames;
        string? team = TeamOf(craft);
        int group = GroupOf(team, teams);

        ImColor8 ink = group < teams.Count ? Ink(TeamColour(group)) : OffInk;

        if (IconButton("##team", Icon.Flag, ink, lit: false)) ImGui.OpenPopup("##teams");

        // Straight after the button, while it is still the last item.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && teams.Count > 0)
        {
            SetTeam(Teams.Next(team, teams));
        }

        Tip($"On {team ?? "no team"}: the side it fights for, which its IFF sorts every contact "
            + "against -- and the side every other craft sees it and its rounds on. "
            + (teams.Count == 0
               ? "Click to create the first team."
               : "Click to pick a team or create one, or right-click for the next team."));

        if (!ImGui.BeginPopup("##teams")) return;

        DrawTeamMenu(teams, team, group);
        ImGui.EndPopup();
    }

    private void DrawTeamMenu(List<string> teams, string? team, int group)
    {
        for (int i = 0; i < teams.Count; i++)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, TeamColour(i));
            if (ImGui.MenuItem($"{teams[i]}##{i}", string.Empty, group == i)) SetTeam(teams[i]);
            ImGui.PopStyleColor();
        }

        if (teams.Count > 0) ImGui.Separator();

        ImGui.PushStyleColor(ImGuiCol.Text, Grey);
        if (ImGui.MenuItem("No team", string.Empty, team is null)) SetTeam(null);
        ImGui.PopStyleColor();

        ImGui.Separator();

        if (ImGui.IsWindowAppearing())
        {
            _menuTeamEntry = string.Empty;

            // With nothing to pick, creating a team is the only reason the menu is open.
            if (teams.Count == 0) ImGui.SetKeyboardFocusHere(0);
        }

        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10f);
        if (TextField("##newteam", ref _menuTeamEntry, "New team")
            && Teams.Declare(teams, _menuTeamEntry) is { } created)
        {
            SetTeam(created);
            ImGui.CloseCurrentPopup();
        }
        Tip("Type a name and press Enter to create the team and put this craft on it.");
    }

    // Every system and director on the craft. KSArmoryMod declares the team roster off these same
    // policies, so this is also the side everyone else reads the craft on.
    private void SetTeam(string? team)
    {
        foreach (WeaponSystems.Entry e in _rowSystems) e.Policy.Iff.OwnTeam = team;
        foreach (OpticalHeads.Entry h in _rowHeads) h.Policy.Iff.OwnTeam = team;
    }

    // Its selected weapon's team, or its director's when it carries no weapon.
    private string? TeamOf(KSA.Vehicle craft)
    {
        if (_roster.For(craft) is { } entry) return entry.Policy.Iff.OwnTeam;

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
    private static readonly float4 FlyingButton = new(0.16f, 0.42f, 0.24f, 1f);
    private static readonly float4 FlyingButtonHovered = new(0.20f, 0.52f, 0.30f, 1f);
    private static readonly ImColor8 FlyingMarker = new(235, 255, 235, 255);

    // Drawn rather than typed: the font carries basic Latin only.
    private static void DrawFlyingMarker(float2 min, float2 max)
    {
        float h = max.Y - min.Y;
        float x = min.X + (h * 0.3f);

        ImGui.GetWindowDrawList().AddTriangleFilled(new float2(x, min.Y + (h * 0.25f)),
                                                    new float2(x, min.Y + (h * 0.75f)),
                                                    new float2(x + (h * 0.4f), min.Y + (h * 0.5f)),
                                                    FlyingMarker);
    }
    private static readonly ImColor8 GuardInk = new(100, 240, 120, 255);
    private static readonly ImColor8 PartlyInk = new(255, 200, 70, 255);
    private static readonly ImColor8 ChaseInk = new(130, 190, 255, 255);
    private static readonly ImColor8 OffInk = new(140, 140, 150, 255);
    private static readonly ImColor8 Lens = new(24, 26, 32, 255);

    private enum Icon { Shield, Camera, Flag, Missile }

    // Drawn rather than typed: KSA's fonts carry basic Latin only and a symbol renders as a box.
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

            case Icon.Missile:
                draw.AddQuadFilled(P(0.20f, 0.70f), P(0.28f, 0.78f), P(0.70f, 0.36f), P(0.62f, 0.28f), ink);
                draw.AddTriangleFilled(P(0.62f, 0.28f), P(0.70f, 0.36f), P(0.84f, 0.14f), ink);
                draw.AddTriangleFilled(P(0.24f, 0.74f), P(0.14f, 0.58f), P(0.32f, 0.66f), ink);
                draw.AddTriangleFilled(P(0.24f, 0.74f), P(0.40f, 0.84f), P(0.32f, 0.66f), ink);
                break;
        }
    }

    private void DrawManageWindow()
    {
        if (_managed is not { } craft || !KsaWorld.IsAlive(craft))
        {
            _managed = null;
            return;
        }

        // Re-focused here: the frame's Focus used last frame's selection, and would show and edit
        // the previous system for a frame. Unarmed craft still open, with what they have.
        bool armed = Focus(craft);
        if (!armed && _heads.FirstOn(craft) is null && !CarriesSearchSet(craft))
        {
            _managed = null;
            return;
        }

        bool open = true;

        // ###id keeps the window in place while the title changes.
        string flying = ReferenceEquals(craft, KsaWorld.ControlledVehicle) ? "" : " - not flying";

        if (ImGui.Begin($"{KsaWorld.DisplayName(craft)}{flying}###KSArmorySystem", ref open))
        {
            // Above the tabs: why it is or is not shooting must show whichever tab is open.
            if (armed) DrawSystemHeader();

            if (ImGui.BeginTabBar("##systemtabs"))
            {
                if (ImGui.BeginTabItem("Components")) { DrawComponents(craft); ImGui.EndTabItem(); }

                // Not gated on `armed`: the computer flies the whole vehicle.
                if (_icbms.For(craft) is { } computer && ImGui.BeginTabItem("Ballistic"))
                {
                    DrawIcbm(computer);
                    ImGui.EndTabItem();
                }

                if (armed)
                {
                    // Only for a set that presents a picture: a seeker or designation set has none.
                    string? scope = ScopeTab(_sensor);

                    if (scope is not null && ImGui.BeginTabItem(scope))
                    {
                        DrawScope();
                        DrawTrackList();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Tuning")) { DrawTuning(); ImGui.EndTabItem(); }
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();

        if (!open) _managed = null;
    }

    // Against the ground under it: in the ecliptic every craft on the planet reads 29.8 km/s.
    private static string Speed(IWeaponSystemView system)
    {
        if (system.Platform is not { } craft || !KsaWorld.IsAlive(craft)) return "";

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

    private static string Tally(WeaponSystem system)
    {
        WeaponFit fit = WeaponFit.Of(system.Profile, system.Sensor);
        return string.Join("  ", fit.Armaments.Select(a => a.Tally(LiveState(system, a).Remaining)));
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

    // Null for a set that presents no picture. Everything offering a scope asks this.
    private static string? ScopeTab(SensorProfile sensor) => sensor.Scope switch
    {
        ScopePresentation.Search => "Radar",
        ScopePresentation.Emitters => "Emitters",
        _ => null,
    };

    private void DrawPaneToggles()
    {
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

            if (shown++ > 0) ImGui.SameLine();

            // Read once: the button toggles pane.Open, and re-reading would unbalance the colour push.
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

            if (ImGui.Begin(pane.Title, ref pane.Open)) pane.Body();
            ImGui.End();
        }
    }

    // The shared Catalogue instances: tuning reaches every system running that loadout.
    private LauncherProfile _profile => _system.Profile;
    private MunitionProfile _munition => _system.Munition;
    private SensorProfile _sensor => _system.Sensor;

    // Read fresh: the tuning sliders edit the profiles it is derived from.
    private WeaponFit _fit => WeaponFit.Of(_system.Profile, _system.Sensor);

    // The one place that maps an armament kind onto the system's per-weapon counters.
    private static (int Remaining, bool Firing) LiveState(WeaponSystem system, Armament arm)
        => arm.Kind switch
        {
            ArmamentKind.Belt => (system.GunAmmo, system.GunsFiring),
            ArmamentKind.Beam => (system.BeamSecondsLeft, system.BeamLasing),
            _ => (system.Ammo, false),
        };

    // Speeds worth a button. KSA's own roller stops at 0.1x; these go two decades below.
    private static readonly (string Label, double Speed)[] SlowMotionSpeeds =
    [
        ("0.01x", 0.01), ("0.05x", 0.05), ("0.1x", 0.1), ("0.25x", 0.25), ("1x", 1.0),
    ];

    // 30 s at 300 m/s spawns 9 km out, inside the 1RS1's 36 km range.
    private float _spawnSeconds = 30f;
    private float _spawnSpeed = 300f;
    private float _spawnMiss = 1500f;
    private int _spawnCraft;   // index into TestTarget.StockCraft; past the end = clone

    private static void Toggle(HashSet<string> set, string team, bool wanted)
    {
        if (wanted) set.Add(team);
        else set.Remove(team);
    }

    // Out of every policy in the world: one still holding a removed team keeps it as its side.
    private void ForgetTeam(string team)
    {
        _config.TeamNames.RemoveAll(t => string.Equals(t, team, StringComparison.OrdinalIgnoreCase));

        foreach (WeaponSystems.Entry e in _roster.All) e.Policy.Iff.Forget(team);
        foreach (OpticalHeads.Entry h in _heads.All) h.Policy.Iff.Forget(team);
        _config.ForgetRules(team);
    }

    private static float4 AllegianceColour(Allegiance a) => a switch
    {
        Allegiance.Friendly => Green,
        Allegiance.Hostile => Red,
        Allegiance.Neutral => Grey,
        _ => Amber,
    };

    private static bool TextField(string label, ref string value, string? hint = null)
    {
        Span<byte> buffer = stackalloc byte[64];
        int written = System.Text.Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
        buffer[Math.Min(written, buffer.Length - 1)] = 0;

        bool entered = hint is null
            ? ImGui.InputText(label, buffer, ImGuiInputTextFlags.EnterReturnsTrue)
            : ImGui.InputTextWithHint(label, hint, buffer, ImGuiInputTextFlags.EnterReturnsTrue);

        if (!entered) return false;

        int end = buffer.IndexOf((byte)0);
        value = System.Text.Encoding.UTF8.GetString(buffer[..(end < 0 ? buffer.Length : end)]);
        return true;
    }

    // Wrapped, because a bare tooltip is not and runs off the screen.
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

    private static void Help(string text)
    {
        ImGui.TextDisabled("(?)");
        Tip(text);
    }

}
