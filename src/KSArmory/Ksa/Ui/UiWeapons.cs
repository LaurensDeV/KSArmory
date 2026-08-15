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

    private void DrawWeaponList(KSA.Vehicle? craft)
    {
        WeaponSystems.Entry? selected = _batteries.For(craft);

        for (int i = 0; i < _weaponScratch.Count; i++)
        {
            WeaponSystems.Entry e = _weaponScratch[i];
            bool isSelected = selected is not null && e.Ordinal == selected.Ordinal;

            ImGui.PushID(i);

            // The whole row selects, rather than a button beside a label: the row *is* the choice,
            // and a target the width of the window is one that can be hit without looking.
            if (ImGui.Selectable($"##row{i}", isSelected, ImGuiSelectableFlags.None,
                                 new float2(0f, ImGui.GetTextLineHeight() * 1.4f)))
            {
                _batteries.Select(craft, e.Ordinal);
                Focus(Focused);
            }

            ImGui.SameLine(0f, 0f);

            int ammo = e.Battery.Ammo;
            int belt = e.Battery.Profile.HasCannon ? e.Battery.GunAmmo : 0;

            // Empty is the state worth colouring, because it is the one that makes FIRE do
            // nothing -- and it is what "the second bomb did not detach" turns out to mean when
            // the operator is still on the weapon that just fired.
            float4 tint = ammo <= 0 && belt <= 0 ? Grey : isSelected ? Green : Amber;

            ImGui.TextColored(tint, $"{i + 1}. {e.DisplayName}");

            ImGui.SameLine();
            if (e.Battery.Profile.TubeCount > 0) ImGui.TextDisabled($"  {ammo} round(s)");
            if (e.Battery.Profile.HasCannon) ImGui.TextDisabled($"  {belt} belt");

            ImGui.SameLine();
            if (e.Policy.Armed) ImGui.TextColored(Red, "ARMED");
            else ImGui.TextDisabled("safe");

            ImGui.PopID();
        }

        ImGui.Separator();

        if (selected is null) return;

        // The two controls an operator actually reaches for mid-run, on the selected weapon, so
        // the switcher is usable without the manage window open at all -- which is the whole point
        // of it being a window of its own.
        ImGui.Checkbox("Master arm", ref selected.Policy.Armed);
        ImGui.SameLine(0f, ImGui.GetFrameHeight());

        if (ImGui.Button("FIRE")) selected.Battery.FireAtLock();

        if (selected.Battery.Hold is { } why) ImGui.TextColored(Amber, $"Holding fire: {why}");
        else ImGui.TextColored(Green, "Clear to fire");
    }
}
