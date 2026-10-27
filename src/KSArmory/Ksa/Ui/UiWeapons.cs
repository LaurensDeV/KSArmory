using System.Runtime.InteropServices;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The weapon switcher: which of a craft's weapons the panel and the trigger are pointed at. Its own
/// window, because weapons are switched while flying, away from the manage window.
/// </summary>
internal partial class Ui
{
    private bool _weaponsOpen;

    // The craft the window was opened for from the switcher, or null for the panel's own focus.
    private KSA.Vehicle? _weaponsCraft;

    private void DrawWeaponsWindow()
    {
        if (!_weaponsOpen) return;

        KSA.Vehicle? craft = Focused ?? KsaWorld.ControlledVehicle;

        _roster.AllOn(craft, _weaponScratch);

        ImGui.SetNextWindowSize(new float2(320f, 0f), ImGuiCond.FirstUseEver);

        bool open = _weaponsOpen;
        string title = craft is null ? "Weapons" : $"Weapons - {KsaWorld.DisplayName(craft)}";
        if (ImGui.Begin($"{title}###KSArmoryWeapons", ref open))
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

    private readonly List<WeaponSystems.Entry> _stations = [];
    private readonly List<int> _stationAmmo = [];

    // Which station of each group fired last, so a symmetric pair alternates instead of one wing
    // draining. Keyed by the group's part Id, which is what makes two rails one weapon.
    private readonly Dictionary<string, int> _lastFired = [];

    // FireGroup is the only thing in the panel that reaches FireAtLock, so every button steps
    // through the group's stations alike.
    private void FireSelectedGroup()
    {
        if (_roster.For(Focused) is { } selected) FireGroup(selected);
    }

    private void FireGroup(WeaponSystems.Entry selected)
    {
        int at = NextStationIndex(selected);

        // Every station dry: fire the selected one anyway so fire control announces its refusal.
        if (at < 0) { selected.Weapon.FireAtLock(); return; }

        _lastFired[selected.Weapon.Profile.PartId] = at;
        _stations[at].Weapon.FireAtLock();
    }

    // -1 when every station is dry. Leaves the group in _stations. The trigger and the line beside
    // it both ask this, so they describe the same station.
    private int NextStationIndex(WeaponSystems.Entry selected)
    {
        string partId = selected.Weapon.Profile.PartId;
        GatherGroup(partId, _stations);

        if (_stations.Count <= 1) return _stations.Count - 1;

        _stationAmmo.Clear();
        foreach (WeaponSystems.Entry s in _stations)
        {
            // Whatever the trigger fires: a gun's belt is not its magazine.
            _stationAmmo.Add(s.Weapon.Remaining(s.Weapon.TriggerArmament));
        }

        return WeaponSelection.NextStation(CollectionsMarshal.AsSpan(_stationAmmo),
                                           _lastFired.GetValueOrDefault(partId, -1));
    }

    // The selected one only when the whole group is empty, where its refusal is the one to read.
    private WeaponSystems.Entry TriggerStation(WeaponSystems.Entry selected)
    {
        int at = NextStationIndex(selected);

        return at < 0 ? selected : _stations[at];
    }

    // Never null: nothing would paint a green "clear to fire" over a launcher that is holding.
    private WeaponSystem TriggerSystem(WeaponSystem inHand)
        => _roster.For(Focused) is { } selected ? TriggerStation(selected).Weapon : inHand;

    /// <summary>
    /// The weapon a cue drawn over the world speaks for: the station the trigger would reach, not
    /// the one selected, answered where the trigger's own line is.
    /// </summary>
    public WeaponSystem? TriggerWeaponOn(KSA.Vehicle? craft)
    {
        if (craft is null || _roster.For(craft) is not { } selected) return null;

        // Safe for another craft: every consumer of _weaponScratch refills it before reading.
        _roster.AllOn(craft, _weaponScratch);
        return TriggerStation(selected).Weapon;
    }

    /// <summary>
    /// Whether this station draws its weapon's pipper: the one the trigger reaches next, so a
    /// weapon of eight racks draws one sight and it is the one the next bomb will follow.
    /// </summary>
    public bool AimsTheSight(WeaponSystems.Entry station)
    {
        _roster.AllOn(station.Craft, _weaponScratch);
        int at = NextStationIndex(station);

        return ReferenceEquals(at < 0 ? _stations[0] : _stations[at], station);
    }

    // One line for both triggers, from the station either would fire.
    private void DrawHoldLine(WeaponSystem inHand, bool autoEngage)
    {
        WeaponSystem speaking = TriggerSystem(inHand);

        DrawHoldReason(speaking, autoEngage);
        DrawBeyondReach(speaking);
        DrawStoreReach(StationSteering() ?? speaking);
    }

    // Not a hold: the shell is thrown as far as it goes, which looks like a shot in reach until it lands.
    private static void DrawBeyondReach(WeaponSystem speaking)
    {
        double shortBy = speaking.GunLayShortMetres;
        if (!(shortBy > 1.0)) return;

        double range = speaking.GunLayRangeMetres;
        ImGui.TextColored(Amber, $"Aim point beyond reach: {range / 1000.0:F1} km, the gun reaches "
                                 + $"{(range - shortBy) / 1000.0:F1} km -- shells land {shortBy / 1000.0:F1} km short");
    }

    // The falling bomb the marks still steer. Not a hold: it is about a round already gone.
    private WeaponSystem? StationSteering()
    {
        if (_roster.For(Focused) is not { } selected) return null;

        _roster.StationsOf(selected, _stations);
        foreach (WeaponSystems.Entry s in _stations)
        {
            if (s.Weapon.Steerable is not null) return s.Weapon;
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

    // A reason that holds only automatic fire reads Ready, with the reason in grey while auto-engage is on.
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

    private void GatherGroup(string partId, List<WeaponSystems.Entry> into)
    {
        into.Clear();
        foreach (WeaponSystems.Entry e in _weaponScratch)
        {
            if (e.Weapon.Profile.PartId == partId) into.Add(e);
        }
    }

    private readonly record struct WeaponChoice(WeaponSystems.Entry First, Armament Arm, int Stations,
                                                int Left, bool Guarding, bool Named);

    private readonly List<WeaponChoice> _choices = [];

    // One per store carried rather than per station, and one per armament. The systems stay separate
    // underneath; pooled magazines would let a store come back (WeaponSelection.NextStation).
    private void CollectWeapons(KSA.Vehicle? craft, List<WeaponChoice> into)
    {
        into.Clear();
        _roster.AllOn(craft, _weaponScratch);

        for (int i = 0; i < _weaponScratch.Count; i++)
        {
            string partId = _weaponScratch[i].Weapon.Profile.PartId;

            // The first station of a group stands for it.
            bool seen = false;
            for (int j = 0; j < i; j++)
            {
                if (_weaponScratch[j].Weapon.Profile.PartId == partId) { seen = true; break; }
            }
            if (seen) continue;

            GatherGroup(partId, _stations);

            WeaponSystems.Entry first = _stations[0];
            IReadOnlyList<Armament> armaments = WeaponFit.Of(first.Weapon.Profile, first.Weapon.Sensor).Armaments;

            foreach (Armament arm in armaments)
            {
                int left = 0;
                bool guarding = false;
                foreach (WeaponSystems.Entry s in _stations)
                {
                    left += s.Weapon.Remaining(arm.Kind);
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
        => c.Arm.Kind switch
        {
            ArmamentKind.Tubes => $"{c.Left} round(s)",
            ArmamentKind.Beam => $"{c.Left} s of chemicals",
            _ => $"{c.Left} belt",
        };

    private static bool IsChosen(WeaponSystems.Entry? selected, WeaponChoice c)
        => selected is not null
           && selected.Weapon.Profile.PartId == c.First.Weapon.Profile.PartId
           && selected.Weapon.TriggerArmament == c.Arm.Kind;

    private void Choose(KSA.Vehicle? craft, WeaponChoice c)
    {
        _roster.Select(craft, c.First.Ordinal);

        // Every station: the trigger steps between them.
        _roster.StationsOf(c.First, _stations);
        foreach (WeaponSystems.Entry s in _stations) s.Weapon.TriggerArmament = c.Arm.Kind;
        Focus(Focused);
    }

    // Nothing is drawn for a craft with one weapon.
    private bool DrawWeaponPicker(KSA.Vehicle? craft)
    {
        CollectWeapons(craft, _choices);
        if (_choices.Count < 2) return false;

        WeaponSystems.Entry? selected = _roster.For(craft);

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
        WeaponSystems.Entry? selected = _roster.For(craft);

        CollectWeapons(craft, _choices);
        for (int row = 0; row < _choices.Count; row++) DrawWeaponRow(craft, selected, _choices[row], row);

        ImGui.Separator();

        if (selected is null) return;

        GatherGroup(selected.Weapon.Profile.PartId, _stations);

        if (ImGui.Button("FIRE")) FireGroup(selected);

        ImGui.SameLine();
        DrawHoldLine(selected.Weapon, selected.Policy.AutoEngage);
    }

    private void DrawWeaponRow(KSA.Vehicle? craft, WeaponSystems.Entry? selected, WeaponChoice c, int row)
    {
        bool isSelected = IsChosen(selected, c);

        ImGui.PushID(row);

        if (ImGui.Selectable($"##row{row}", isSelected, ImGuiSelectableFlags.None,
                             new float2(0f, ImGui.GetTextLineHeight() * 1.4f)))
        {
            Choose(craft, c);
        }

        ImGui.SameLine(0f, 0f);

        // Empty is coloured: it is the state that makes FIRE do nothing.
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
