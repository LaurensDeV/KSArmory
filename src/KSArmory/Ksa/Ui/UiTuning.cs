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

    // Rounds this system throws that the guidance node does not already tune. Sliders enumerated
    // by hand only ever reach the first weapon's round, which leaves every field on a second
    // armament's round unreachable from the panel.
    private void DrawOtherArmamentRounds()
    {
        IReadOnlyList<Armament> armaments = _fit.Armaments;
        for (int i = 0; i < armaments.Count; i++)
        {
            Armament arm = armaments[i];
            if (arm.Munition == _munition.Name) continue;

            MunitionProfile round = Arsenal.MunitionNamed(arm.Munition);

            ImGui.Separator();
            ImGui.TextDisabled($"{arm.Label}: {round.DisplayName}");

            ImGui.Checkbox($"Timed airburst (flak)##{arm.Label}", ref round.TimedFuse);
            ImGui.TextDisabled(round.TimedFuse
                                   ? "  rounds burst at the lead solution's flight time"
                                   : "  rounds burst on proximity only");
        }
    }

    private void DrawTuning()
    {
        // Said once, at the top, and it is the most surprising thing about this tab. These
        // sliders edit the shared Arsenal profiles, so they reach every system in the world
        // running this loadout -- which is the intent, and is invisible from a window titled
        // with one craft's name. It is also why they are not on the component rows: a number
        // under a named part on a named craft reads as belonging to that one.
        ImGui.TextDisabled($"What a {_profile.DisplayName} is, not what this one is doing.");
        ImGui.TextDisabled("Changes reach every system in the world running it.");
        ImGui.Separator();

        DrawSensorNode();
        DrawDriveNodes();
        DrawGuidanceNode();
    }

    private void DrawSensorNode()
    {
        if (!_fit.Searches)
        {
            ImGui.TextDisabled("No sensor: nothing to tune, and nothing to detect with.");
            return;
        }

        if (!ImGui.TreeNode("Radar")) return;

        ImGui.SliderFloat("Range (m)", ref _sensor.Range, 500f, 40000f);
        ImGui.SliderFloat("Cone half-angle (deg)", ref _sensor.ConeDeg, 5f, 180f);
        ImGui.SliderFloat("Threat radius (m)", ref _sensor.ThreatRadius, 100f, 10000f);
        ImGui.SliderFloat("Threat horizon (s)", ref _sensor.ThreatHorizonSeconds, 5f, 120f);
        ImGui.SliderFloat("Lock time (s)", ref _sensor.LockSeconds, 0f, 5f);
        ImGui.SliderFloat("Min target speed (m/s)", ref _sensor.MinTargetSpeed, 0f, 200f);

        DrawDiscriminationControls();
        DrawHorizonControls();

        ImGui.TreePop();
    }

    // What the set can tell targets apart by. All three are off at zero, which is where they ship:
    // together they are the substrate chaff needs, and separately each is a real capability with a
    // real cost the player should be choosing rather than inheriting.
    private void DrawDiscriminationControls()
    {
        ImGui.SliderFloat("Reference RCS (m2)", ref _sensor.ReferenceCrossSectionM2, 0f, 2000f);

        if (_sensor.ReferenceCrossSectionM2 <= 0f)
        {
            ImGui.TextDisabled("  the set reaches the same distance whatever it looks at");
        }
        else
        {
            // Shown because the fourth-root law is not something anyone should have to take on
            // trust while dragging a slider: it is what makes a small target reachable at all.
            double small = ThreatModel.DetectionRange(
                _sensor, new ThreatModel.ContactSignature(1.0, double.PositiveInfinity));

            ImGui.TextDisabled($"  a 1 m contact at {small / 1000.0:F1} km of {_sensor.Range / 1000f:F1}");
        }

        ImGui.SliderFloat("Doppler notch (m/s)", ref _sensor.NotchSpeed, 0f, 100f);
        if (_sensor.NotchSpeed > 0f)
        {
            ImGui.TextDisabled("  rejects clutter, and loses a target crossing exactly abeam");
        }

        ImGui.SliderFloat("Clutter floor (m)", ref _sensor.ClutterFloorMetres, 0f, 2000f);
        if (_sensor.ClutterFloorMetres > 0f)
        {
            ImGui.TextDisabled("  nothing below this over the mean sphere is seen at all");
        }
    }

    // What the world hides from this set. The sample count is the cost knob and is shown as one:
    // every sample is a height-map fetch spent once per contact per scan, and nobody has measured
    // what that costs in a frame.
    private void DrawHorizonControls()
    {
        ImGui.Checkbox("Horizon masking", ref _sensor.HorizonMasking);

        if (!_sensor.HorizonMasking)
        {
            ImGui.TextDisabled("  the set sees through the planet");
            return;
        }

        ImGui.SliderFloat("Limb margin (m)", ref _sensor.TerrainMarginMetres, 0f, 5000f);
        ImGui.SliderInt("Terrain samples", ref _sensor.TerrainSamples, 0, 64);

        if (_sensor.TerrainSamples <= 0)
        {
            ImGui.TextDisabled("  mean sphere only - a contact behind a ridge is still seen");
        }
        else
        {
            ImGui.SliderFloat("Terrain clearance (m)", ref _sensor.TerrainClearanceMetres, 0f, 300f);
            ImGui.TextDisabled($"  up to {_sensor.TerrainSamples} height lookups per contact per scan");
        }
    }

    // The drives, each node existing only if the system has that gear. A rate slider for an axis
    // that does not turn is indistinguishable from one the engine is refusing.
    private void DrawDriveNodes()
    {
        WeaponFit fit = _fit;

        if (fit.Aims && ImGui.TreeNode("Turret"))
        {
            ImGui.Checkbox("Track with turret", ref _policy.TurretTracking);
            if (fit.Traverses) ImGui.SliderFloat("Traverse rate (deg/s)", ref _profile.SlewRateDeg, 5f, 180f);
            if (fit.Elevates) ImGui.SliderFloat("Elevation rate (deg/s)", ref _profile.ElevationRateDeg, 5f, 120f);
            ImGui.SliderFloat("Settle before firing (s)", ref _profile.SettleSeconds, 0f, 2f);

            ImGui.Separator();
            ImGui.TextDisabled("Drive it by hand - neither needs a target:");
            if (fit.Traverses) ImGui.Checkbox("Spin continuously", ref _policy.TurretSpin);
            ImGui.Checkbox("Manual aim", ref _policy.TurretManual);
            if (fit.Traverses) ImGui.SliderFloat("Bearing (deg)", ref _policy.TurretManualBearingDeg, -180f, 180f);
            if (fit.Elevates)
            {
                ImGui.SliderFloat("Elevation (deg)", ref _policy.TurretManualElevationDeg, 0f, 82f);
                ImGui.TextDisabled("  Elevation applies to spin as well as manual aim.");
            }
            ImGui.TreePop();
        }

        if (fit.SweepsASearchArray && ImGui.TreeNode("Search array"))
        {
            ImGui.SliderFloat("Search array (rpm)", ref _profile.SearchRadarRpm, 0f, 60f);
            ImGui.Checkbox("Stop the search array", ref _policy.SearchRadarStopped);
            ImGui.TreePop();
        }
    }

    private void DrawGuidanceNode()
    {
        if (ImGui.TreeNode("Guidance"))
        {
            // Steering, boost and the seeker are only read by rounds the guidance model flies.
            // A ballistic round ignores every one of them, and a slider that changes nothing is
            // worse than no slider: it reads as the setting having no effect.
            if (_fit.Steers)
            {
                ImGui.SliderFloat("Nav constant N", ref _munition.NavConstant, 1f, 8f);
                ImGui.SliderFloat("Max lateral (g)", ref _munition.MaxLateralG, 5f, 80f);
                ImGui.SliderFloat("Seeker FOV (deg)", ref _munition.SeekerFovDeg, 10f, 90f);
                ImGui.SliderFloat("Gravity compensation", ref _munition.GravityCompensation, 0f, 1.5f);
                ImGui.SliderFloat("Boost accel (m/s2)", ref _munition.BoostAccel, 0f, 800f);
                ImGui.SliderFloat("Boost time (s)", ref _munition.BoostSeconds, 0f, 10f);
                ImGui.SliderFloat("Coast before steering (s)", ref _munition.SeparationSeconds, 0f, 3f);
                ImGui.TextDisabled("  a round leaves along the tube and is clear before it turns");
            }

            ImGui.SliderFloat("Launch speed (m/s)", ref _munition.LaunchSpeed, 5f, 300f);
            ImGui.SliderFloat("Max flight time (s)", ref _munition.MaxFlightSeconds, 3f, 90f);

            // The envelope the battery commits inside, which is not how far the round can fly:
            // the set sees 36 km and the round reaches 20, and firing at everything detected
            // spends the magazine on contacts the rounds expire short of.
            ImGui.SliderFloat("Min engagement range (m)", ref _munition.MinRange, 0f, 5000f);
            ImGui.SliderFloat("Max engagement range (m)", ref _munition.MaxRange, 500f, 40000f);

            ImGui.Checkbox("Eject along the tube", ref _profile.LaunchAlongTube);
            ImGui.TextDisabled("  off: slew to the target on launch, plus loft");
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Warhead"))
        {
            ImGui.SliderFloat("Fuse radius (m)", ref _munition.FuseRadius, 2f, 200f);
            ImGui.SliderFloat("Fuse arm delay (s)", ref _munition.FuseArmSeconds, 0f, 5f);
            // One slider, because the radii are read off the charge rather than chosen. Showing
            // what it buys keeps the cube root visible: ten times the explosive is a bit over
            // twice the reach, which is not what a reader expects and is the point.
            //
            // The top of the range is nuclear, and it has to be: a 300 t device is 300,000 kg, so
            // a slider that stopped at a cannon shell would silently clamp one to a firework the
            // first time anybody touched it. Logarithmic, or the whole conventional range -- every
            // round the mod otherwise ships -- lives in the first thousandth of the travel.
            //
            // 340 kt is the top of the B61's own dial, so the slider covers the real weapon rather
            // than stopping partway up it. It is well past playable at a launch site -- the lethal
            // radius alone is 7.8 km -- which is a reason to ship at the bottom of the range, not a
            // reason to hide the top of it.
            ImGui.SliderFloat("Explosive charge (kg)", ref _munition.ChargeKg, 0.01f, 340_000_000f,
                              "%.2f", ImGuiSliderFlags.Logarithmic);
            ImGui.TextDisabled($"  lethal {_munition.LethalRadius:F0} m, "
                               + $"blast {_munition.BlastRadius:F0} m, "
                               + $"fireball {_munition.FireballRadius:F0} m"
                               + (_munition.ChargeKg >= 1000f
                                      ? $"   ({_munition.ChargeKg / 1e6f:F2} kt)"
                                      : ""));
            ImGui.SliderInt("Rounds per target", ref _policy.RoundsPerTarget,
                            1, Math.Max(1, _fit.SalvoCapacity));
            ImGui.SliderFloat("Salvo spacing (s)", ref _profile.SalvoSpacing, 0.05f, 3f);
            ImGui.SliderFloat("Reload time (s)", ref _profile.ReloadSeconds, 0f, 60f);

            DrawOtherArmamentRounds();
            ImGui.TreePop();
        }

    }
}
