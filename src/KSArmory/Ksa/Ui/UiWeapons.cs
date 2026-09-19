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

    /// <summary>Opens the switcher, for anything that decides the operator wants it.</summary>
    public void OpenWeapons() => _weaponsOpen = true;

    private void DrawWeaponsWindow()
    {
        if (!_weaponsOpen) return;

        // The craft being flown, not the one the panel happens to be showing. A switcher is for
        // the aircraft under your hands; pointing it at a site across the map would be a trigger
        // aimed somewhere the operator is not looking.
        KSA.Vehicle? craft = KsaWorld.ControlledVehicle ?? Focused;

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
    // straight at one station the way the header's did.
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

    // One line for both triggers, so the two cannot drift: the reason, and whether it binds, come
    // from the same station either button would fire.
    private void DrawHoldLine(WeaponSystem inHand, bool autoEngage)
    {
        WeaponSystem speaking = TriggerSystem(inHand);

        DrawHoldReason(speaking, autoEngage);
        DrawBeyondReach(speaking);
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

    private void DrawHoldReason(WeaponSystem speaking, bool autoEngage)
    {
        if (speaking.TriggerHold is not { } held)
        {
            ImGui.TextColored(Green, autoEngage ? "Clear to fire" : "Clear to fire -- on the trigger");
            return;
        }

        string why = held.Reason;

        if (held.BindsTrigger)
        {
            ImGui.TextColored(Amber, $"Holding fire: {why}");
            return;
        }

        // Auto-engage's own gate, said as one. The old wording described a refusal that does not
        // happen: the operator presses FIRE at a target inside the minimum, the round leaves, and
        // the panel had called that holding fire.
        ImGui.TextColored(Amber, $"Auto-engage held: {why}");
        ImGui.SameLine();
        ImGui.TextColored(Green, "-- trigger is clear");
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

    private void DrawWeaponList(KSA.Vehicle? craft)
    {
        WeaponSystems.Entry? selected = _batteries.For(craft);

        // One row per store carried, not per station. Two LAU-118s under one aircraft are one
        // weapon to whoever is flying it: real aircraft select a store type and let the stations
        // take turns, and a list naming each rail separately makes the operator do the bookkeeping.
        // The systems stay separate underneath -- see WeaponSelection.NextStation for why pooling
        // the magazines instead would let a store come back.
        //
        // And one per armament: a launcher with tubes and a belt is two weapons, and the trigger
        // fires one of them.
        int row = 0;
        string? drawn = null;

        for (int i = 0; i < _weaponScratch.Count; i++)
        {
            string partId = _weaponScratch[i].Battery.Profile.PartId;

            // Ordinal order, so the first station of a group is where its row is drawn and every
            // later one folds into it.
            if (partId == drawn) continue;
            bool alreadyDrawn = false;
            for (int j = 0; j < i; j++)
            {
                if (_weaponScratch[j].Battery.Profile.PartId == partId) { alreadyDrawn = true; break; }
            }
            if (alreadyDrawn) continue;
            drawn = partId;

            GatherGroup(partId, _stations);

            WeaponSystems.Entry e = _stations[0];
            IReadOnlyList<Armament> armaments = WeaponFit.Of(e.Battery.Profile, e.Battery.Sensor).Armaments;

            for (int a = 0; a < armaments.Count; a++)
            {
                DrawWeaponRow(craft, selected, e, armaments[a], named: armaments.Count > 1, row++);
            }
        }

        ImGui.Separator();

        if (selected is null) return;

        GatherGroup(selected.Battery.Profile.PartId, _stations);

        // The trigger, on the selected weapon, so the switcher is usable without the manage window
        // open at all -- which is the whole point of it being a window of its own.
        if (ImGui.Button("FIRE")) FireGroup(selected);

        // This window is the trigger, so its line has to be about the trigger. Auto-engage off
        // blocks nothing FIRE does, and reporting it here is what made a working button look
        // broken.
        DrawHoldLine(selected.Battery, selected.Policy.AutoEngage);
    }

    // One armament of the group in _stations.
    private void DrawWeaponRow(KSA.Vehicle? craft, WeaponSystems.Entry? selected, WeaponSystems.Entry e,
                               Armament arm, bool named, int row)
    {
        bool isSelected = selected is not null
                          && selected.Battery.Profile.PartId == e.Battery.Profile.PartId
                          && selected.Battery.TriggerArmament == arm.Kind;

        int left = 0;
        bool anyGuarding = false;
        foreach (WeaponSystems.Entry s in _stations)
        {
            left += arm.Kind == ArmamentKind.Tubes ? s.Battery.Ammo : s.Battery.GunAmmo;
            anyGuarding |= s.Policy.AutoEngage;
        }

        ImGui.PushID(row);

        // The whole row selects, rather than a button beside a label: the row *is* the choice,
        // and a target the width of the window is one that can be hit without looking.
        if (ImGui.Selectable($"##row{row}", isSelected, ImGuiSelectableFlags.None,
                             new float2(0f, ImGui.GetTextLineHeight() * 1.4f)))
        {
            _batteries.Select(craft, e.Ordinal);

            // Every station, because the trigger steps between them and each fires its own.
            foreach (WeaponSystems.Entry s in _stations) s.Battery.TriggerArmament = arm.Kind;
            Focus(Focused);
        }

        ImGui.SameLine(0f, 0f);

        // Empty is the state worth colouring, because it is the one that makes FIRE do
        // nothing -- and it is what "the second bomb did not detach" turns out to mean when
        // the operator is still on the weapon that just fired.
        float4 tint = left <= 0 ? Grey : isSelected ? Green : Amber;

        // The station count, because two rails and one rail are different amounts of weapon
        // and the summed ammo alone does not say which it is.
        string name = named ? $"{e.DisplayName}: {arm.Label}" : e.DisplayName;
        string label = _stations.Count > 1
                           ? $"{row + 1}. {name}  x{_stations.Count}"
                           : $"{row + 1}. {name}";

        ImGui.TextColored(tint, label);

        ImGui.SameLine();
        ImGui.TextDisabled(arm.Kind == ArmamentKind.Tubes ? $"  {left} round(s)" : $"  {left} belt");

        ImGui.SameLine();
        if (anyGuarding) ImGui.TextColored(Red, "GUARDING");
        else ImGui.TextDisabled("manual");

        ImGui.PopID();
    }
}
