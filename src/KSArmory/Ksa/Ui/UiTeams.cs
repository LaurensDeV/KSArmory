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

        if (teams.Count >= 2) DrawRelationGrid(teams);

        // One row a team: its rules across, so ten teams are ten rows rather than thirty.
        if (ImGui.BeginTable("##teamrules", 6, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Team", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Craft");
            ImGui.TableSetupColumn("Unknown");
            ImGui.TableSetupColumn("Neutrals");
            ImGui.TableSetupColumn("Spare friends");
            ImGui.TableSetupColumn("##remove");
            ImGui.TableHeadersRow();

            for (int i = 0; i < teams.Count; i++)
            {
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(TeamColour(i), teams[i]);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(Members(teams[i]));
                DrawTeamRules(_config.RulesFor(teams[i]));
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Remove")) removed = teams[i];
                Tip("Takes every craft off this team, and this team out of every other's relations.");
                ImGui.PopID();
            }

            ImGui.PushID("noteam");
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Grey, "No team");
            Tip("Every craft with no flag. It sees every other craft as unknown.");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(Members(null));
            DrawTeamRules(_config.NoTeamRules);
            ImGui.TableNextColumn();
            ImGui.PopID();

            ImGui.EndTable();
        }

        // After the table, so the list is not shortened under the index walking it.
        if (removed is not null) ForgetTeam(removed);

        ImGui.Separator();
        bool entered = TextField("##addteam", ref _newTeamEntry, "New team name");
        ImGui.SameLine();
        bool clicked = ImGui.Button("Add team");
        Tip("A craft with nothing of this mod's fitted -- a drone, an airliner -- has no flag and is "
            + "on no team, whatever it is called.");
        if ((entered || clicked) && Teams.Declare(teams, _newTeamEntry) is not null) _newTeamEntry = string.Empty;
    }

    // What each team makes of each other, read along its row: one cell a pair, clicked to step
    // hostile, allied, neutral. One way, since each team decides for itself.
    private void DrawRelationGrid(List<string> teams)
    {
        ImGui.TextDisabled("How each row sees each column -- click a cell to change it.");

        // Scrolling across once there are too many teams, so its height has to be given: a scrolling table
        // given none takes the rest of the window, and the rules below it with it.
        float height = ((teams.Count + 1) * ImGui.GetFrameHeightWithSpacing()) + ImGui.GetStyle().ScrollbarSize;
        if (!ImGui.BeginTable("##relations", teams.Count + 1,
                              ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollX,
                              new float2?(new float2(0f, height)), 0f))
        {
            return;
        }

        ImGui.TableSetupColumn("##sees");
        for (int j = 0; j < teams.Count; j++) ImGui.TableSetupColumn(teams[j]);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        for (int i = 0; i < teams.Count; i++)
        {
            TeamRules rules = _config.RulesFor(teams[i]);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(TeamColour(i), teams[i]);

            for (int j = 0; j < teams.Count; j++)
            {
                ImGui.TableNextColumn();
                if (i == j)
                {
                    ImGui.TextDisabled("-");
                    continue;
                }

                Allegiance now = rules.Towards(teams[j]);
                ImGui.PushID((i * teams.Count) + j);
                ImGui.PushStyleColor(ImGuiCol.Text, AllegianceColour(now));
                if (ImGui.SmallButton(Word(now))) rules.Set(teams[j], Next(now));
                ImGui.PopStyleColor();
                ImGui.PopID();
                Tip($"How {teams[i]} sees {teams[j]}. Click to step hostile, allied, neutral.");
            }
        }

        ImGui.EndTable();
        ImGui.Spacing();
    }

    // Three cells of the rules table.
    private void DrawTeamRules(TeamRules rules)
    {
        ImGui.TableNextColumn();
        ImGui.Checkbox("##unknown", ref rules.EngageUnknown);
        Tip("Fire on craft on no team. On by default, so a world with no teams set up still fights.");
        ImGui.TableNextColumn();
        ImGui.Checkbox("##neutral", ref rules.EngageNeutral);
        Tip("Fire on teams this one sees as neutral.");
        ImGui.TableNextColumn();
        ImGui.Checkbox("##friendly", ref rules.ProtectFriendly);
        Tip("Never fire on its own team or its allies. Leave it on outside a test range.");
    }

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
