using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The Teams window: every team, who is on it, and its rules of engagement, in one place. A team's
/// rules are every system and director on it (<see cref="TeamRules"/>); which team a craft is on is
/// still the flag on its switcher row.
/// </summary>
internal sealed partial class Ui
{
    private void DrawTeamsWindow()
    {
        List<string> teams = _config.TeamNames;
        string? removed = null;

        ImGui.TextWrapped("Who fights whom. A craft's team is the flag on its row in the panel; "
                          + "everything on a team follows that team's rules.");
        ImGui.Separator();

        for (int i = 0; i < teams.Count; i++)
        {
            string team = teams[i];
            ImGui.PushID(i);

            bool open = ImGui.TreeNodeEx($"##team", ImGuiTreeNodeFlags.DefaultOpen);
            ImGui.SameLine();
            ImGui.TextColored(TeamColour(i), team);
            ImGui.SameLine();
            ImGui.TextDisabled(Members(team));
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove")) removed = team;
            Tip("Takes every craft off this team, and this team out of every other's relations.");

            if (open)
            {
                DrawRelations(team, teams);
                DrawTeamRules(_config.RulesFor(team));
                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        // After the loop, so the list is not shortened under the index walking it.
        if (removed is not null) ForgetTeam(removed);

        ImGui.PushID("noteam");
        if (ImGui.TreeNodeEx("##none", teams.Count == 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
        {
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"No team  {Members(null)}");
            ImGui.TextDisabled("  sees every other craft as unknown");
            DrawTeamRules(_config.NoTeamRules);
            ImGui.TreePop();
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"No team  {Members(null)}");
        }
        ImGui.PopID();

        ImGui.Separator();
        if (TextField("Add team", ref _newTeamEntry) && Teams.Declare(teams, _newTeamEntry) is not null)
        {
            _newTeamEntry = string.Empty;
        }
        Tip("A craft with nothing of this mod's fitted -- a drone, an airliner -- has no flag and is "
            + "on no team, whatever it is called.");
    }

    // What this team makes of each other team: one button each, stepping hostile, allied, neutral.
    private void DrawRelations(string team, List<string> teams)
    {
        if (teams.Count < 2) return;

        TeamRules rules = _config.RulesFor(team);
        ImGui.TextDisabled("  sees");

        for (int j = 0; j < teams.Count; j++)
        {
            string other = teams[j];
            if (string.Equals(other, team, StringComparison.OrdinalIgnoreCase)) continue;

            Allegiance now = rules.Towards(other);

            ImGui.SameLine();
            ImGui.PushID(j);
            ImGui.PushStyleColor(ImGuiCol.Text, AllegianceColour(now));
            if (ImGui.SmallButton($"{other}: {Word(now)}")) rules.Set(other, Next(now));
            ImGui.PopStyleColor();
            ImGui.PopID();
        }

        Tip("Click to step hostile, allied, neutral. One way: each team decides for itself.");
    }

    private void DrawTeamRules(TeamRules rules)
    {
        ImGui.Checkbox("Fire on unknown craft", ref rules.EngageUnknown);
        Tip("Craft on no team. On by default, so a world with no teams set up still fights.");
        ImGui.SameLine();
        ImGui.Checkbox("Fire on neutrals", ref rules.EngageNeutral);
        ImGui.SameLine();
        ImGui.Checkbox("Never fire on friendlies", ref rules.ProtectFriendly);
        Tip("Its own team and its allies. Leave it on outside a test range.");
    }

    // Who is on a team, as the switcher lists them.
    private string Members(string? team)
    {
        int n = 0;
        foreach ((KSA.Vehicle craft, _) in _systems)
        {
            if (string.Equals(TeamOf(craft) ?? "", team ?? "", StringComparison.OrdinalIgnoreCase)) n++;
        }

        return n == 1 ? "1 craft" : $"{n} craft";
    }

    private static string Word(Allegiance a) => a switch
    {
        Allegiance.Friendly => "allied",
        Allegiance.Neutral => "neutral",
        _ => "hostile",
    };

    private static Allegiance Next(Allegiance a) => a switch
    {
        Allegiance.Hostile => Allegiance.Friendly,
        Allegiance.Friendly => Allegiance.Neutral,
        _ => Allegiance.Hostile,
    };
}
