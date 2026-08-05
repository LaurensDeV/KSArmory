using Brutal.ImGuiApi;

namespace KSArmory;

/// <summary>
/// The panes that change how a system behaves: which side it is on, and the sensor, guidance and
/// warhead numbers.
///
/// <para>The distinction the sliders keep making: IFF is per battery, so it edits
/// <c>_policy</c>; weapon performance belongs to the profiles, so it edits those and every system
/// of that type feels it. See <see cref="Config"/> for why.</para>
/// </summary>
internal sealed partial class Ui
{
    // Teams and IFF. The whole subsystem shipped tested and unreachable: nothing outside
    // Config.cs ever wrote Config.Iff or Config.TeamNames, so every session anyone has played
    // ran with no teams declared and every contact Unknown.
    private void DrawIff()
    {
        ImGui.TextDisabled("KSA has no team field. A craft joins a team when the team's name");
        ImGui.TextDisabled("appears anywhere in its name - so \"Red\" also matches \"Redstone\".");
        ImGui.TextDisabled("Longest match wins. Name teams distinctly.");
        ImGui.Separator();

        if (TextField("Own team", ref _ownTeamEntry))
        {
            _policy.Iff.OwnTeam = string.IsNullOrWhiteSpace(_ownTeamEntry) ? null : _ownTeamEntry.Trim();
            Remember(_policy.Iff.OwnTeam);
        }

        ImGui.SameLine();
        ImGui.TextDisabled(_policy.Iff.OwnTeam is null ? "(none - everything is Unknown)" : "");

        if (TextField("Add team", ref _newTeamEntry) && !string.IsNullOrWhiteSpace(_newTeamEntry))
        {
            Remember(_newTeamEntry.Trim());
            _newTeamEntry = string.Empty;
        }

        if (_config.TeamNames.Count == 0)
        {
            ImGui.TextDisabled("  no teams declared; every contact classifies as Unknown");
        }

        for (int i = _config.TeamNames.Count - 1; i >= 0; i--)
        {
            string team = _config.TeamNames[i];
            bool own = string.Equals(team, _policy.Iff.OwnTeam, StringComparison.OrdinalIgnoreCase);

            bool allied = _policy.Iff.AlliedTeams.Contains(team);
            bool neutral = _policy.Iff.NeutralTeams.Contains(team);

            ImGui.TextColored(AllegianceColour(_policy.Iff.Classify(team)), $"  {team}");
            ImGui.SameLine();

            if (own)
            {
                ImGui.TextDisabled("own team");
            }
            else
            {
                if (ImGui.Checkbox($"allied##a{i}", ref allied))
                {
                    Toggle(_policy.Iff.AlliedTeams, team, allied);
                    if (allied) _policy.Iff.NeutralTeams.Remove(team);
                }
                ImGui.SameLine();
                if (ImGui.Checkbox($"neutral##n{i}", ref neutral))
                {
                    Toggle(_policy.Iff.NeutralTeams, team, neutral);
                    if (neutral) _policy.Iff.AlliedTeams.Remove(team);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button($"remove##t{i}"))
            {
                _config.TeamNames.RemoveAt(i);
                _policy.Iff.AlliedTeams.Remove(team);
                _policy.Iff.NeutralTeams.Remove(team);
            }
        }

        ImGui.Separator();

        bool engageUnknown = _policy.Iff.EngageUnknown;
        if (ImGui.Checkbox("Engage unknown contacts", ref engageUnknown)) _policy.Iff.EngageUnknown = engageUnknown;

        bool engageNeutral = _policy.Iff.EngageNeutral;
        if (ImGui.Checkbox("Engage neutrals", ref engageNeutral)) _policy.Iff.EngageNeutral = engageNeutral;

        bool protectFriendly = _policy.Iff.ProtectFriendly;
        if (ImGui.Checkbox("Never engage friendlies", ref protectFriendly)) _policy.Iff.ProtectFriendly = protectFriendly;

    }

    private void DrawTuning()
    {
        if (!ImGui.TreeNode("Radar")) { DrawGuidanceNode(); return; }

        ImGui.SliderFloat("Range (m)", ref _config.Sensor.Range, 500f, 40000f);
        ImGui.SliderFloat("Cone half-angle (deg)", ref _config.Sensor.ConeDeg, 5f, 180f);
        ImGui.SliderFloat("Threat radius (m)", ref _config.Sensor.ThreatRadius, 100f, 10000f);
        ImGui.SliderFloat("Threat horizon (s)", ref _config.Sensor.ThreatHorizonSeconds, 5f, 120f);
        ImGui.SliderFloat("Lock time (s)", ref _config.Sensor.LockSeconds, 0f, 5f);
        ImGui.SliderFloat("Min target speed (m/s)", ref _config.Sensor.MinTargetSpeed, 0f, 200f);
        ImGui.TreePop();

        if (ImGui.TreeNode("Turret"))
        {
            ImGui.Checkbox("Track with turret", ref _policy.TurretTracking);
            ImGui.SliderFloat("Traverse rate (deg/s)", ref _profile.SlewRateDeg, 5f, 180f);
            ImGui.SliderFloat("Elevation rate (deg/s)", ref _profile.ElevationRateDeg, 5f, 120f);
            ImGui.SliderFloat("Settle before firing (s)", ref _profile.SettleSeconds, 0f, 2f);
            ImGui.Checkbox("Eject along the tube", ref _profile.LaunchAlongTube);
            ImGui.TextDisabled("  off: slew to the target on launch, plus loft");

            ImGui.Separator();
            ImGui.TextDisabled("Drive it by hand - neither needs a target:");
            ImGui.Checkbox("Spin continuously", ref _policy.TurretSpin);
            ImGui.Checkbox("Manual aim", ref _policy.TurretManual);
            ImGui.SliderFloat("Bearing (deg)", ref _policy.TurretManualBearingDeg, -180f, 180f);
            ImGui.SliderFloat("Elevation (deg)", ref _policy.TurretManualElevationDeg, 0f, 82f);
            ImGui.TextDisabled("  Elevation applies to spin as well as manual aim.");

            ImGui.Separator();
            ImGui.SliderFloat("Search array (rpm)", ref _profile.SearchRadarRpm, 0f, 60f);
            ImGui.Checkbox("Stop the search array", ref _policy.SearchRadarStopped);
            ImGui.TreePop();
        }

        DrawGuidanceNode();
    }

    private void DrawGuidanceNode()
    {
        if (ImGui.TreeNode("Guidance"))
        {
            ImGui.SliderFloat("Nav constant N", ref _munition.NavConstant, 1f, 8f);
            ImGui.SliderFloat("Max lateral (g)", ref _munition.MaxLateralG, 5f, 80f);
            ImGui.SliderFloat("Seeker FOV (deg)", ref _munition.SeekerFovDeg, 10f, 90f);
            ImGui.SliderFloat("Gravity compensation", ref _munition.GravityCompensation, 0f, 1.5f);
            ImGui.SliderFloat("Boost accel (m/s2)", ref _munition.BoostAccel, 0f, 800f);
            ImGui.SliderFloat("Boost time (s)", ref _munition.BoostSeconds, 0f, 10f);
            ImGui.SliderFloat("Launch speed (m/s)", ref _munition.LaunchSpeed, 5f, 300f);
            ImGui.SliderFloat("Max flight time (s)", ref _munition.MaxFlightSeconds, 3f, 90f);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Warhead"))
        {
            ImGui.SliderFloat("Fuse radius (m)", ref _munition.FuseRadius, 2f, 200f);
            ImGui.SliderFloat("Fuse arm delay (s)", ref _munition.FuseArmSeconds, 0f, 5f);
            // One slider, because the radii are read off the charge rather than chosen. Showing
            // what it buys keeps the cube root visible: ten times the explosive is a bit over
            // twice the reach, which is not what a reader expects and is the point.
            ImGui.SliderFloat("Explosive charge (kg)", ref _munition.ChargeKg, 0.01f, 500f,
                              "%.2f", ImGuiSliderFlags.Logarithmic);
            ImGui.TextDisabled($"  lethal {_munition.LethalRadius:F0} m, "
                               + $"blast {_munition.BlastRadius:F0} m, "
                               + $"fireball {_munition.FireballRadius:F0} m");
            ImGui.SliderInt("Rounds per target", ref _policy.RoundsPerTarget, 1, _profile.TubeCount);
            ImGui.SliderFloat("Salvo spacing (s)", ref _profile.SalvoSpacing, 0.05f, 3f);
            ImGui.SliderFloat("Reload time (s)", ref _profile.ReloadSeconds, 0f, 60f);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Display"))
        {
            ImGui.Checkbox("World overlay", ref _config.DrawOverlays);
            ImGui.TextDisabled("  everything drawn in the world around a system");

            if (_config.DrawOverlays)
            {
                ImGui.Checkbox("Only the system shown in the panel",
                               ref _config.DrawOverlayForFocusedOnly);
                ImGui.TextDisabled("  off: every crewed system draws its own");
            }

            ImGui.Separator();
            ImGui.Checkbox("Warhead effects", ref _config.DrawExplosions);
            ImGui.TextDisabled("  the fireball, not a debug line -- kept when those are off");

            ImGui.Checkbox("Weapons-system markers", ref _config.DrawSystemMarkers);
            ImGui.TextDisabled("  brackets over every system; (+) in the list pins a label");
            ImGui.Checkbox("Radar volume", ref _config.DrawRadarVolume);
            ImGui.Checkbox("Drive facing line", ref _config.DrawTurretFacing);
            ImGui.TextDisabled("  where the drives think they point, not where they are told to");
            ImGui.SliderFloat("Cone draw length (m)", ref _config.ConeDisplayMetres, 200f, 20000f);
            ImGui.TextDisabled("  cosmetic only; detection range is set under Radar");
            ImGui.Checkbox("Tracks", ref _config.DrawTracks);
            ImGui.Checkbox("Track marker spheres", ref _config.DrawTrackMarkers);
            ImGui.TextDisabled("  large ball on each contact; scales with range");
            ImGui.Checkbox("Predicted pass point", ref _config.DrawClosestApproach);
            ImGui.TextDisabled("  where a threat will pass if it holds course");
            ImGui.Checkbox("Rounds", ref _config.DrawMissiles);
            ImGui.Checkbox("Round tracer spheres", ref _config.DrawRoundMarkers);
            // Bodies and tracers are placed by entirely separate paths, so toggling this while
            // watching a round in flight says which of the two is misbehaving.
            ImGui.Checkbox("Round bodies (off = tracers only)", ref _config.UseRoundBodies);
            ImGui.Checkbox("Tube markers (debug)", ref _config.DrawTubeMarkers);
            ImGui.TextDisabled(_battery.RoundBodyCount > 0 && _battery.RoundBodiesWork
                ? "  rounds have real bodies; the tracer hides them up close"
                : "  no round bodies available - tracers are all there is");
            ImGui.TreePop();
        }
    }
}
