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
/// <para>Every weapon shows its ammo and whether it is armed. That is not decoration: a craft
/// carrying two racks has two magazines and two master arms, so "nothing happened when I pressed
/// FIRE" is nearly always one of the two, and a switcher that showed only names would leave the
/// operator to guess which.</para>
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
                    : "nothing armed on this craft");
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

    // Fires the next station of the selected weapon's group. The trigger is the group's, so it
    // steps between stations rather than always reaching the one whose row happens to be selected.
    private void FireGroup(WeaponSystems.Entry selected)
    {
        string partId = selected.Battery.Profile.PartId;
        GatherGroup(partId, _stations);

        if (_stations.Count == 1) { _stations[0].Battery.FireAtLock(); return; }

        _stationAmmo.Clear();
        foreach (WeaponSystems.Entry s in _stations)
        {
            // A gun-only mount has no tubes and its belt is what shoots, so asking for rounds
            // would report every station of it empty and the trigger would never reach one.
            _stationAmmo.Add(s.Battery.Profile.TubeCount > 0 ? s.Battery.Ammo : s.Battery.GunAmmo);
        }

        int last = _lastFired.GetValueOrDefault(partId, -1);
        int at = WeaponSelection.NextStation(CollectionsMarshal.AsSpan(_stationAmmo), last);

        // Every station dry. Fire the selected one anyway so its own refusal is announced: the
        // operator gets "launcher empty" from fire control rather than a button that does nothing.
        if (at < 0) { selected.Battery.FireAtLock(); return; }

        _lastFired[partId] = at;
        _stations[at].Battery.FireAtLock();
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
            bool isSelected = selected is not null
                              && selected.Battery.Profile.PartId == partId;

            int ammo = 0, belt = 0;
            bool anyArmed = false;
            foreach (WeaponSystems.Entry s in _stations)
            {
                ammo += s.Battery.Ammo;
                if (s.Battery.Profile.HasCannon) belt += s.Battery.GunAmmo;
                anyArmed |= s.Policy.Armed;
            }

            ImGui.PushID(row);

            // The whole row selects, rather than a button beside a label: the row *is* the choice,
            // and a target the width of the window is one that can be hit without looking.
            if (ImGui.Selectable($"##row{row}", isSelected, ImGuiSelectableFlags.None,
                                 new float2(0f, ImGui.GetTextLineHeight() * 1.4f)))
            {
                _batteries.Select(craft, e.Ordinal);
                Focus(Focused);
            }

            ImGui.SameLine(0f, 0f);

            // Empty is the state worth colouring, because it is the one that makes FIRE do
            // nothing -- and it is what "the second bomb did not detach" turns out to mean when
            // the operator is still on the weapon that just fired.
            float4 tint = ammo <= 0 && belt <= 0 ? Grey : isSelected ? Green : Amber;

            // The station count, because two rails and one rail are different amounts of weapon
            // and the summed ammo alone does not say which it is.
            string label = _stations.Count > 1
                               ? $"{row + 1}. {e.DisplayName}  x{_stations.Count}"
                               : $"{row + 1}. {e.DisplayName}";

            ImGui.TextColored(tint, label);

            ImGui.SameLine();
            if (e.Battery.Profile.TubeCount > 0) ImGui.TextDisabled($"  {ammo} round(s)");
            if (e.Battery.Profile.HasCannon) ImGui.TextDisabled($"  {belt} belt");

            ImGui.SameLine();
            if (anyArmed) ImGui.TextColored(Red, "ARMED");
            else ImGui.TextDisabled("safe");

            ImGui.PopID();
            row++;
        }

        ImGui.Separator();

        if (selected is null) return;

        GatherGroup(selected.Battery.Profile.PartId, _stations);

        // The two controls an operator actually reaches for mid-run, on the selected weapon, so
        // the switcher is usable without the manage window open at all -- which is the whole point
        // of it being a window of its own.
        //
        // Arming reaches every station of the group: they are one weapon here, and a master arm
        // that armed one rail of a pair would be a switch that half works.
        bool armed = selected.Policy.Armed;
        if (ImGui.Checkbox("Master arm", ref armed))
        {
            foreach (WeaponSystems.Entry s in _stations) s.Policy.Armed = armed;
        }

        ImGui.SameLine(0f, ImGui.GetFrameHeight());

        if (ImGui.Button("FIRE")) FireGroup(selected);

        // This window is the trigger, so its line has to be about the trigger. Auto-engage off
        // blocks nothing FIRE does, and reporting it here is what made a working button look
        // broken.
        if (selected.Battery.Hold is { } why) ImGui.TextColored(Amber, $"Holding fire: {why}");
        else if (!selected.Policy.AutoEngage) ImGui.TextColored(Green, "Clear to fire -- on the trigger");
        else ImGui.TextColored(Green, "Clear to fire");
    }
}
