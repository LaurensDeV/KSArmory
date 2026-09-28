using System.Runtime.InteropServices;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The weapon switcher: which of a craft's weapons the panel and the trigger are pointed at.
///
/// <para>Its own window rather than a row on the header strip, because switching weapons is done
/// <em>while flying</em> and the manage window is not somewhere to be during an attack run. Small,
/// movable, and it stays where it is put.</para>
///
/// <para>Every weapon shows its ammo and whether it is guarding. The ammo is not decoration: a craft
/// carrying two racks has two magazines, so "nothing happened when I pressed FIRE" is nearly always
/// one of them being empty, and a switcher that showed only names would leave the operator to guess
/// which.</para>
/// </summary>
internal partial class Ui
{
    private bool _weaponsOpen;

    private void DrawWeaponsWindow()
    {
        if (!_weaponsOpen) return;

        // The craft the panel is showing, whose button opened it: every craft with a weapon has one,
        // and the panel's own trigger already fires that craft's.
        KSA.Vehicle? craft = Focused ?? KsaWorld.ControlledVehicle;

        _batteries.AllOn(craft, _weaponScratch);

        ImGui.SetNextWindowSize(new float2(320f, 0f), ImGuiCond.FirstUseEver);

        bool open = _weaponsOpen;
        if (ImGui.Begin("Weapons###KSArmoryWeapons", ref open))
        {
            if (_weaponScratch.Count == 0)
            {
                ImGui.TextColored(Grey, craft is null
                    ? "no craft"
                    : "no weapons on this craft");
            }
            else
            {
                DrawWeaponList(craft);
            }
        }

        ImGui.End();
        _weaponsOpen = open;
    }

    // The stations of one group, gathered fresh each frame. A field rather than a local so drawing
    // a switcher does not allocate on every frame it is open.
    private readonly List<WeaponSystems.Entry> _stations = [];
    private readonly List<int> _stationAmmo = [];

    // Which station of each group fired last, so a symmetric pair alternates instead of one wing
    // draining. Keyed by the group's part Id, which is what makes two rails one weapon.
    private readonly Dictionary<string, int> _lastFired = [];

    // The trigger, wherever it is pressed. Both buttons reach the same stations in the same order,
    // which is the whole of what makes a symmetric pair fire as a pair -- and FireGroup below is
    // the only thing in the panel that reaches FireAtLock, so a third button cannot quietly go
    // straight at one station.
    private void FireSelectedGroup()
    {
        if (_batteries.For(Focused) is { } selected) FireGroup(selected);
    }

    // Fires the next station of the selected weapon's group. The trigger is the group's, so it
    // steps between stations rather than always reaching the one whose row happens to be selected.
    private void FireGroup(WeaponSystems.Entry selected)
    {
        int at = NextStationIndex(selected);

        // Every station dry. Fire the selected one anyway so its own refusal is announced: the
        // operator gets "launcher empty" from fire control rather than a button that does nothing.
        if (at < 0) { selected.Battery.FireAtLock(); return; }

        _lastFired[selected.Battery.Profile.PartId] = at;
        _stations[at].Battery.FireAtLock();
    }

    // Which station of the selected weapon's group the trigger would reach if it were pressed now,
    // or -1 when every one of them is dry. Leaves the group itself in _stations.
    //
    // Asked by the trigger and by the line beside it, because they have to be about the same
    // station: a pair whose first rail has just fired reads "out of rounds" off that rail while the
    // second is loaded and clear to go.
    private int NextStationIndex(WeaponSystems.Entry selected)
    {
        string partId = selected.Battery.Profile.PartId;
        GatherGroup(partId, _stations);

        if (_stations.Count <= 1) return _stations.Count - 1;

        _stationAmmo.Clear();
        foreach (WeaponSystems.Entry s in _stations)
        {
            // Whatever the trigger fires. A gun's belt is not its magazine, so asking a gun for
            // rounds would report every station of it empty and the trigger would never reach one.
            _stationAmmo.Add(s.Battery.TriggerArmament == ArmamentKind.Tubes ? s.Battery.Ammo : s.Battery.GunAmmo);
        }

        return WeaponSelection.NextStation(CollectionsMarshal.AsSpan(_stationAmmo),
                                           _lastFired.GetValueOrDefault(partId, -1));
    }

    // The station a line beside the trigger has to describe. Falls back to the selected one only
    // when the whole group is empty, which is the one case where its refusal is the right thing to
    // read.
    private WeaponSystems.Entry TriggerStation(WeaponSystems.Entry selected)
    {
        int at = NextStationIndex(selected);

        return at < 0 ? selected : _stations[at];
    }

    // The system a line beside the trigger has to speak for. An unresolvable selection falls back
    // to the one in hand rather than to nothing, which would paint a green "clear to fire" over a
    // launcher that is holding.
    private WeaponSystem TriggerSystem(WeaponSystem inHand)
        => _batteries.For(Focused) is { } selected ? TriggerStation(selected).Battery : inHand;

    /// <summary>
    /// The weapon a cue drawn over the world has to speak for, which is the station the trigger
    /// would reach rather than the one selected.
    ///
    /// <para>Two rails carrying the same store are one weapon with two stations, so a cue read off
    /// the selection paints a refusal over a target the loaded rail is clear to shoot at. Both
    /// triggers and the line beside them already go through the group; this is the same question
    /// from the glass, answered in the same place so the two cannot drift.</para>
    /// </summary>
    public WeaponSystem? TriggerWeaponOn(KSA.Vehicle? craft)
    {
        if (craft is null || _batteries.For(craft) is not { } selected) return null;

        // Every consumer of _weaponScratch refills it before reading it, so filling it here for a
        // craft the panel is not showing cannot disturb what the panel does with it.
        _batteries.AllOn(craft, _weaponScratch);
        return TriggerStation(selected).Battery;
    }

    /// <summary>
    /// Whether this station draws its weapon's pipper: the one the trigger reaches next, so a
    /// weapon of eight racks draws one sight and it is the one the next bomb will follow.
    /// </summary>
    public bool AimsTheSight(WeaponSystems.Entry station)
    {
        _batteries.AllOn(station.Craft, _weaponScratch);
        int at = NextStationIndex(station);

        return ReferenceEquals(at < 0 ? _stations[0] : _stations[at], station);
    }

    // One line for both triggers, so the two cannot drift: the reason, and whether it binds, come
    // from the same station either button would fire.
    private void DrawHoldLine(WeaponSystem inHand, bool autoEngage)
    {
        WeaponSystem speaking = TriggerSystem(inHand);

        DrawHoldReason(speaking, autoEngage);
        DrawBeyondReach(speaking);
        DrawStoreReach(StationSteering() ?? speaking);
    }

    // Not a hold: the trigger still fires, and the shell is thrown as far as it goes. Said under the
    // trigger because an aim point out of reach looks exactly like one in reach until the shell lands.
    private static void DrawBeyondReach(WeaponSystem speaking)
    {
        double shortBy = speaking.GunLayShortMetres;
        if (!(shortBy > 1.0)) return;

        double range = speaking.GunLayRangeMetres;
        ImGui.TextColored(Amber, $"Aim point beyond reach: {range / 1000.0:F1} km, the gun reaches "
                                 + $"{(range - shortBy) / 1000.0:F1} km -- shells land {shortBy / 1000.0:F1} km short");
    }

    // The falling bomb the marks still steer, and how far it can still move: the number that says
    // whether marking now is worth doing. Not a hold, since it is about a round already gone.
    private WeaponSystem? StationSteering()
    {
        if (_batteries.For(Focused) is not { } selected) return null;

        _batteries.StationsOf(selected, _stations);
        foreach (WeaponSystems.Entry s in _stations)
        {
            if (s.Battery.Steerable is not null) return s.Battery;
        }

        return null;
    }

    private void DrawStoreReach(WeaponSystem speaking)
    {
        if (speaking.Steerable is null) return;

        TailKitReach reach = _reachFor(speaking).Latest;
        if (!reach.Known) return;

        ImGui.TextColored(Grey, $"Bomb falling: {reach.SecondsToGo:F0} s left, can still move "
                                + Distance.Say(reach.RadiusMetres));
        Tip("Shift-click inside the blue ring to steer it there. Releasing the next bomb locks it.");
    }

    // What the trigger will do, in a word: Ready, or Holding and the reason. A reason that only
    // holds automatic fire does not stop the trigger, so it is Ready with the reason after it in
    // grey, and only while auto-engage is on, since otherwise nothing is waiting on it.
    private void DrawHoldReason(WeaponSystem speaking, bool autoEngage)
    {
        if (speaking.TriggerHold is { BindsTrigger: true } held)
        {
            ImGui.TextColored(Amber, $"Holding: {held.Reason}");
            Tip("FIRE will not work until this clears.");
            return;
        }

        ImGui.TextColored(Green, "Ready");
        Tip(autoEngage ? "FIRE works now." : "FIRE works now. Auto-engage is off, so it only fires when you press it.");

        if (autoEngage && speaking.TriggerHold is { } waiting)
        {
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"auto: {waiting.Reason}");
            Tip("Why auto-engage is not firing on its own. FIRE still works.");
        }
    }

    // Every station carrying the same store, in ordinal order.
    private void GatherGroup(string partId, List<WeaponSystems.Entry> into)
    {
        into.Clear();
        foreach (WeaponSystems.Entry e in _weaponScratch)
        {
            if (e.Battery.Profile.PartId == partId) into.Add(e);
        }
    }

    // One weapon as the operator picks it: a store type with however many stations carry it, and
    // one armament of it where a launcher carries two.
    private readonly record struct WeaponChoice(WeaponSystems.Entry First, Armament Arm, int Stations,
                                                int Left, bool Guarding, bool Named);

    private readonly List<WeaponChoice> _choices = [];

    // Every weapon on a craft, in part order: one per store carried rather than per station, and
    // one per armament. Two LAU-118s under one aircraft are one weapon to whoever is flying it --
    // the stations take turns -- and a launcher with tubes and a belt is two, because the trigger
    // fires one of them. The systems stay separate underneath; see WeaponSelection.NextStation for
    // why pooling the magazines instead would let a store come back.
    private void CollectWeapons(KSA.Vehicle? craft, List<WeaponChoice> into)
    {
        into.Clear();
        _batteries.AllOn(craft, _weaponScratch);

        for (int i = 0; i < _weaponScratch.Count; i++)
        {
            string partId = _weaponScratch[i].Battery.Profile.PartId;

            // Ordinal order, so the first station of a group stands for it and every later one
            // folds into it.
            bool seen = false;
            for (int j = 0; j < i; j++)
            {
                if (_weaponScratch[j].Battery.Profile.PartId == partId) { seen = true; break; }
            }
            if (seen) continue;

            GatherGroup(partId, _stations);

            WeaponSystems.Entry first = _stations[0];
            IReadOnlyList<Armament> armaments = WeaponFit.Of(first.Battery.Profile, first.Battery.Sensor).Armaments;

            foreach (Armament arm in armaments)
            {
                int left = 0;
                bool guarding = false;
                foreach (WeaponSystems.Entry s in _stations)
                {
                    left += arm.Kind == ArmamentKind.Tubes ? s.Battery.Ammo : s.Battery.GunAmmo;
                    guarding |= s.Policy.AutoEngage;
                }

                into.Add(new WeaponChoice(first, arm, _stations.Count, left, guarding, armaments.Count > 1));
            }
        }
    }

    private static string ChoiceName(WeaponChoice c)
    {
        string name = c.Named ? $"{c.First.DisplayName}: {c.Arm.Label}" : c.First.DisplayName;
        return c.Stations > 1 ? $"{name}  x{c.Stations}" : name;
    }

    private static string ChoiceLeft(WeaponChoice c)
        => c.Arm.Kind == ArmamentKind.Tubes ? $"{c.Left} round(s)" : $"{c.Left} belt";

    private static bool IsChosen(WeaponSystems.Entry? selected, WeaponChoice c)
        => selected is not null
           && selected.Battery.Profile.PartId == c.First.Battery.Profile.PartId
           && selected.Battery.TriggerArmament == c.Arm.Kind;

    private void Choose(KSA.Vehicle? craft, WeaponChoice c)
    {
        _batteries.Select(craft, c.First.Ordinal);

        // Every station, because the trigger steps between them and each fires its own.
        _batteries.StationsOf(c.First, _stations);
        foreach (WeaponSystems.Entry s in _stations) s.Battery.TriggerArmament = c.Arm.Kind;
        Focus(Focused);
    }

    // The weapon the trigger beside it fires, picked where it is fired. Nothing is drawn for a craft
    // with one weapon: there is nothing to choose, and FIRE says what it does on its own.
    private bool DrawWeaponPicker(KSA.Vehicle? craft)
    {
        CollectWeapons(craft, _choices);
        if (_choices.Count < 2) return false;

        WeaponSystems.Entry? selected = _batteries.For(craft);

        string preview = "";
        foreach (WeaponChoice c in _choices)
        {
            if (IsChosen(selected, c)) preview = $"{ChoiceName(c)} - {ChoiceLeft(c)}";
        }

        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 18f);
        if (ImGui.BeginCombo("##weapon", preview, ImGuiComboFlags.None))
        {
            for (int i = 0; i < _choices.Count; i++)
            {
                WeaponChoice c = _choices[i];
                bool chosen = IsChosen(selected, c);

                ImGui.PushID(i);
                if (ImGui.Selectable($"{ChoiceName(c)} - {ChoiceLeft(c)}", chosen, ImGuiSelectableFlags.None,
                                     new float2(0f, 0f)))
                {
                    Choose(craft, c);
                }
                if (chosen) ImGui.SetItemDefaultFocus();
                ImGui.PopID();
            }

            ImGui.EndCombo();
        }

        Tip("Which weapon FIRE releases. Several racks carrying the same store are one weapon, and "
            + "FIRE steps between them.");
        return true;
    }

    private void DrawWeaponList(KSA.Vehicle? craft)
    {
        WeaponSystems.Entry? selected = _batteries.For(craft);

        CollectWeapons(craft, _choices);
        for (int row = 0; row < _choices.Count; row++) DrawWeaponRow(craft, selected, _choices[row], row);

        ImGui.Separator();

        if (selected is null) return;

        GatherGroup(selected.Battery.Profile.PartId, _stations);

        // The trigger, on the selected weapon, so the switcher is usable without the manage window
        // open at all -- which is the whole point of it being a window of its own.
        if (ImGui.Button("FIRE")) FireGroup(selected);

        // This window is the trigger, so its line has to be about the trigger. Auto-engage off
        // blocks nothing FIRE does, and reporting it here makes a working button look broken.
        ImGui.SameLine();
        DrawHoldLine(selected.Battery, selected.Policy.AutoEngage);
    }

    private void DrawWeaponRow(KSA.Vehicle? craft, WeaponSystems.Entry? selected, WeaponChoice c, int row)
    {
        bool isSelected = IsChosen(selected, c);

        ImGui.PushID(row);

        // The whole row selects, rather than a button beside a label: the row *is* the choice,
        // and a target the width of the window is one that can be hit without looking.
        if (ImGui.Selectable($"##row{row}", isSelected, ImGuiSelectableFlags.None,
                             new float2(0f, ImGui.GetTextLineHeight() * 1.4f)))
        {
            Choose(craft, c);
        }

        ImGui.SameLine(0f, 0f);

        // Empty is the state worth colouring, because it is the one that makes FIRE do
        // nothing -- and it is what "the second bomb did not detach" turns out to mean when
        // the operator is still on the weapon that just fired.
        float4 tint = c.Left <= 0 ? Grey : isSelected ? Green : Amber;

        ImGui.TextColored(tint, $"{row + 1}. {ChoiceName(c)}");

        ImGui.SameLine();
        ImGui.TextDisabled($"  {ChoiceLeft(c)}");

        ImGui.SameLine();
        if (c.Guarding) ImGui.TextColored(Red, "GUARDING");
        else ImGui.TextDisabled("manual");

        ImGui.PopID();
    }
}
