using Brutal.ImGuiApi;

namespace KSArmory;

/// <summary>
/// The sensor, guidance and warhead numbers. They edit the shared profiles, so every system running
/// that loadout feels them; a system's own choices are on its component rows.
/// </summary>
internal sealed partial class Ui
{
    private const float TsarBombaKg = 50_000_000_000f;

    // Logarithmic: a charge spans thirteen orders of magnitude. Typed input is off because it
    // would edit the logarithm.
    private static bool ChargeSlider(string label, ref float kg, float minKg, float maxKg)
    {
        float lo = MathF.Log10(minKg);
        float hi = MathF.Log10(maxKg);
        float at = Math.Clamp(MathF.Log10(Math.Max(kg, minKg)), lo, hi);

        if (!ImGui.SliderFloat(label, ref at, lo, hi, Charge.Say(kg),
                               ImGuiSliderFlags.NoInput | ImGuiSliderFlags.AlwaysClamp))
        {
            if (kg > maxKg) kg = maxKg;
            return false;
        }

        kg = Math.Clamp(MathF.Pow(10f, at), minKg, maxKg);
        return true;
    }

    // Rounds the guidance node does not already tune: a second armament's round is otherwise unreachable.
    private void DrawOtherArmamentRounds()
    {
        IReadOnlyList<Armament> armaments = _fit.Armaments;
        for (int i = 0; i < armaments.Count; i++)
        {
            Armament arm = armaments[i];
            if (arm.Kind == ArmamentKind.Beam) continue;

            bool other = arm.Munition != _munition.Name;
            if (!other && arm.Kind != ArmamentKind.Belt) continue;

            MunitionProfile round = Catalogue.MunitionNamed(arm.Munition);

            ImGui.Separator();
            ImGui.TextDisabled($"{arm.Label}: {round.DisplayName}");

            if (other)
            {
                ImGui.Checkbox($"Timed airburst (flak)##{arm.Label}", ref round.TimedFuse);
                Tip("On: rounds burst at the lead solution's flight time, and the proximity fuse still "
                    + "runs. Off: rounds burst on proximity only.");
            }

            if (arm.Kind == ArmamentKind.Belt) DrawTracers(round, arm.Label);
        }
    }

    private static void DrawTracers(MunitionProfile round, string label)
    {
        ImGui.SliderInt($"Tracer every##{label}", ref round.TracerEvery, 0, 10,
                        round.TracerEvery > 0 ? "1 in %d" : "none");
        Tip("How the belt is loaded: one round in this many burns a tracer. The rest are drawn as a "
            + "faint grey streak, so the stream reads while the tracers stay the only bright thing in it.");

        if (round.TracerEvery > 0)
        {
            ImGui.SliderFloat($"Tracer burn (s)##{label}", ref round.TracerBurnSeconds, 0.5f, 10f, "%.1f s");
            Tip("How long a tracer burns before it goes out. The round flies on unseen.");
        }
    }

    private void DrawTuning()
    {
        // Said at the top: the shared Catalogue profiles are edited, which a window titled with one
        // craft's name does not suggest.
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

        // A fixed id, so the fold keeps its state when the selected weapon changes.
        if (!ImGui.TreeNode($"{_sensor.DisplayName}###sensortuning")) return;

        ImGui.SliderFloat("Range (m)", ref _sensor.Range, 500f, 40000f);
        ImGui.SliderFloat("Cone half-angle (deg)", ref _sensor.ConeDeg, 5f, 180f);
        ImGui.SliderFloat("Threat radius (m)", ref _sensor.ThreatRadius, 100f, 10000f);
        ImGui.SliderFloat("Threat horizon (s)", ref _sensor.ThreatHorizonSeconds, 5f, 120f);
        ImGui.SliderFloat("Lock time (s)", ref _sensor.LockSeconds, 0f, 5f);
        ImGui.SliderFloat("Min target speed (m/s)", ref _sensor.MinTargetSpeed, 0f, 200f);

        if (_sensor.Infrared) DrawInfraredControls();
        DrawDiscriminationControls();
        DrawHorizonControls();

        ImGui.TreePop();
    }

    private void DrawInfraredControls()
    {
        float microwatts = _sensor.IrThresholdWPerM2 * 1.0e6f;
        if (ImGui.SliderFloat("Threshold (uW/m2)", ref microwatts, 0.05f, 10f, "%.2f",
                              ImGuiSliderFlags.Logarithmic))
        {
            _sensor.IrThresholdWPerM2 = microwatts * 1.0e-6f;
        }
        Tip("The least heat on the aperture this set detects. Lower sees further: a fighter at "
            + "military power is seen about as far as the square root of the change.");

        ImGui.SliderFloat("Look-down penalty", ref _sensor.LookDownPenalty, 1f, 50f);
        Tip("What the threshold is multiplied by for a contact against the ground rather than the "
            + "sky, because warm ground is what it has to be picked out of. 1 is none.");
    }

    // All three are off at zero, where they ship: each is a capability with a real cost.
    private void DrawDiscriminationControls()
    {
        ImGui.SliderFloat("Reference RCS (m2)", ref _sensor.ReferenceCrossSectionM2, 0f, 2000f);
        Tip("The cross-section the range is quoted against. Above zero, a contact's own size scales "
            + "the range by the fourth root of the ratio, so a target a hundredth the size is seen at "
            + "a third of the range. Zero: the set reaches the same distance whatever it looks at.");

        if (_sensor.ReferenceCrossSectionM2 > 0f)
        {
            // Shows the fourth-root law at work.
            double small = ThreatModel.DetectionRange(
                _sensor, new ThreatModel.ContactSignature(1.0, double.PositiveInfinity));

            ImGui.TextDisabled($"  a 1 m contact at {small / 1000.0:F1} km of {_sensor.Range / 1000f:F1}");
        }

        ImGui.SliderFloat("Doppler notch (m/s)", ref _sensor.NotchSpeed, 0f, 100f);
        if (_sensor.NotchSpeed > 0f)
        {
            ImGui.TextDisabled("  rejects clutter, and loses a target crossing exactly abeam");
        }

        ImGui.SliderFloat("Chaff notch (m/s)", ref _sensor.ChaffNotchMps, 0f, 100f);
        Tip("A target closing slower than this, with chaff in its resolution cell outshining it, is lost. "
            + "Zero: chaff never breaks this set's track.");
        if (_sensor.ChaffNotchMps > 0f)
        {
            ImGui.SliderFloat("Reacquire after chaff (s)", ref _sensor.ChaffReacquireSeconds, 0f, 15f);
            ImGui.Checkbox("Optical channel holds the track", ref _sensor.OpticalBackup);
            Tip("On: once found again, the target is held on the camera for as long as it stays in the notch, "
                + "so the same manoeuvre does not break the track twice.");
        }

        ImGui.SliderFloat("Clutter floor (m)", ref _sensor.ClutterFloorMetres, 0f, 2000f);
        if (_sensor.ClutterFloorMetres > 0f)
        {
            ImGui.TextDisabled("  nothing below this over the mean sphere is seen at all");
        }
    }

    // Every sample is a height-map fetch per contact per scan, and its frame cost is unmeasured.
    private void DrawHorizonControls()
    {
        ImGui.Checkbox("Horizon masking", ref _sensor.HorizonMasking);
        Tip("On: the planet blocks the set. Off: the set sees through the planet.");

        if (!_sensor.HorizonMasking) return;

        ImGui.SliderFloat("Limb margin (m)", ref _sensor.TerrainMarginMetres, 0f, 5000f);
        ImGui.SliderInt("Terrain samples", ref _sensor.TerrainSamples, 0, 64);
        Tip("Up to this many height lookups per contact per scan, deciding whether a ridge hides it. "
            + "Zero: the mean sphere only, so a contact behind a ridge is still seen.");

        if (_sensor.TerrainSamples > 0)
        {
            ImGui.SliderFloat("Terrain clearance (m)", ref _sensor.TerrainClearanceMetres, 0f, 300f);
        }
    }

    // Rates only, and only for gear the system has: a slider for an axis that does not turn looks
    // like one the engine is refusing.
    private void DrawDriveNodes()
    {
        WeaponFit fit = _fit;

        if (fit.Aims)
        {
            bool turretOpen = ImGui.TreeNode("Turret");
            ImGui.SameLine();
            Help("Tracking and manual aim are on the launcher, under Components.");

            if (turretOpen)
            {
                if (fit.Traverses) ImGui.SliderFloat("Traverse rate (deg/s)", ref _profile.SlewRateDeg, 5f, 180f);
                if (fit.Elevates) ImGui.SliderFloat("Elevation rate (deg/s)", ref _profile.ElevationRateDeg, 5f, 120f);
                ImGui.SliderFloat("Settle before firing (s)", ref _profile.SettleSeconds, 0f, 2f);
                ImGui.TreePop();
            }
        }

        if (fit.SweepsASearchArray && ImGui.TreeNode("Search array"))
        {
            ImGui.SliderFloat("Search array (rpm)", ref _profile.SearchRadarRpm, 0f, 60f);
            Tip("Stopping this system's array is on its sensor, under Components.");
            ImGui.TreePop();
        }
    }

    private void DrawSeekerBand()
    {
        ImGui.Text("Seeker");
        ImGui.SameLine();
        if (ImGui.RadioButton("radar##band", _munition.Band == SeekerBand.Radar)) _munition.Band = SeekerBand.Radar;
        ImGui.SameLine();
        if (ImGui.RadioButton("infrared##band", _munition.Band == SeekerBand.Infrared)) _munition.Band = SeekerBand.Infrared;
        ImGui.SameLine();
        if (ImGui.RadioButton("unfoolable##band", _munition.Band == SeekerBand.None)) _munition.Band = SeekerBand.None;
        Tip("What decoy can take it: flares fool an infrared seeker, chaff a radar one, nothing the third.");

        if (_munition.Band == SeekerBand.None) return;

        ImGui.SliderFloat("Countermeasure resistance", ref _munition.CountermeasureResistance, 0f, 1f);
        Tip($"How much of a decoy's {SeekerLock.SeductionChance:P0} chance of taking the seeker it refuses. "
            + "Each decoy is judged once, the first moment it outshines the target.");
        ImGui.Checkbox("Looks again after a decoy", ref _munition.ReacquiresAfterDecoy);
        Tip("On: once the decoy is spent the seeker goes back to the target if it can still see it. Off: it flies on blind.");

        if (_munition.Band == SeekerBand.Radar)
        {
            ImGui.SliderFloat("Doppler gate (m/s)", ref _munition.DopplerGateMps, 0f, 200f);
            Tip("Zero is no gate. Otherwise chaff is only seen while its closing speed is within this of the "
                + "target's, which for chaff that has stopped in the air means only while the target beams.");
        }
    }

    private void DrawGuidanceNode()
    {
        if (ImGui.TreeNode("Guidance"))
        {
            // A ballistic round reads none of these.
            if (_fit.Steers)
            {
                // Every steering round uses these two, a tail kit on a falling store included.
                ImGui.SliderFloat("Nav constant N", ref _munition.NavConstant, 1f, 8f);
                ImGui.SliderFloat("Max lateral (g)", ref _munition.MaxLateralG, 0f, 80f);

                // A tail kit steers the fall it predicts, gravity included, so it has none to cancel.
                if (_munition.Guidance != GuidanceMode.Inertial)
                {
                    ImGui.SliderFloat("Gravity compensation", ref _munition.GravityCompensation, 0f, 1.5f);
                }
                ImGui.SliderFloat("Fin deflection (deg)", ref _munition.FinDeflectionDeg, 0f, 30f);
                Tip("Drawn only; it steers nothing. It is set larger than life, or the blades do not read.");

                // A released store has no seeker.
                if (_fit.Powered)
                {
                    ImGui.SliderFloat("Seeker FOV (deg)", ref _munition.SeekerFovDeg, 10f, 90f);
                    ImGui.SliderFloat("Coast before steering (s)", ref _munition.SeparationSeconds, 0f, 3f);
                    Tip("A round leaves along the tube and is clear before it turns.");

                    // Only a seeker the round carries can be fooled; a command link and an
                    // anti-radiation head see nothing a flare or chaff makes.
                    if (_munition.Guidance == GuidanceMode.Seeker) DrawSeekerBand();
                }
            }

            // A missile's motor, or an unguided rocket's; a shell or a bomb has neither.
            if (_fit.Powered || _munition.BoostSeconds > 0f)
            {
                ImGui.SliderFloat("Boost accel (m/s2)", ref _munition.BoostAccel, 0f, 800f);
                ImGui.SliderFloat("Boost time (s)", ref _munition.BoostSeconds, 0f, 10f);
            }

            ImGui.SliderFloat("Launch speed (m/s)", ref _munition.LaunchSpeed, 5f, 300f);
            ImGui.SliderFloat("Max flight time (s)", ref _munition.MaxFlightSeconds, 3f, 180f);

            // The envelope the system commits inside, not how far the round flies: the 1RS1 sees
            // 36 km and the 57E6 is committed out to 20.
            ImGui.SliderFloat("Min engagement range (m)", ref _munition.MinRange, 0f, 5000f);
            ImGui.SliderFloat("Max engagement range (m)", ref _munition.MaxRange, 500f, 40000f);

            ImGui.Checkbox("Eject along the tube", ref _profile.LaunchAlongTube);
            Tip("On: a round leaves along the tube, pointing where the launcher points. "
                + "Off: it slews to the target on launch, plus loft.");
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Warhead"))
        {
            ImGui.SliderFloat("Fuse radius (m)", ref _munition.FuseRadius, 2f, 200f);
            ImGui.SliderFloat("Fuse arm delay (s)", ref _munition.FuseArmSeconds, 0f, 5f);
            // One slider: the radii are read off the charge. Up to 50 Mt, or a nuclear charge would
            // be clamped the first time anybody touched it; a dial-a-yield bomb stops at its own top.
            float maxCharge = _munition.MaxChargeKg > 0f ? _munition.MaxChargeKg : TsarBombaKg;
            ChargeSlider("Explosive charge", ref _munition.ChargeKg, 0.01f, maxCharge);
            ImGui.TextDisabled($"  lethal {_munition.LethalRadius:F0} m, "
                               + $"blast {_munition.BlastRadius:F0} m, "
                               + $"fireball {_munition.FireballRadius:F0} m");

            ImGui.SliderInt("Fragments", ref _munition.FragmentCount, 0, 30000);
            Tip("How many fragments the warhead throws. They are what breaks parts past a few metres; "
                + "the blast alone only breaks what it is right beside. Zero throws none.");
            if (_munition.FragmentCount > 0)
            {
                ImGui.SliderFloat("Fragment mass (g)", ref _munition.FragmentMassGrams, 0.5f, 50f);
                ImGui.SliderFloat("Fragment speed (m/s)", ref _munition.FragmentSpeed, 300f, 3000f);
                ImGui.SliderFloat("Spray angle (deg)", ref _munition.SprayAngleDeg, 0f, 180f);
                Tip("The middle of the band the fragments fly in, from the nose: 90 is straight out of the side. "
                    + "A moving warhead's band arrives tilted forward.");
                ImGui.SliderFloat("Spray half-width (deg)", ref _munition.SprayHalfWidthDeg, 1f, 90f);
            }

            ImGui.SliderFloat("Ignition chance", ref _munition.IgnitionChance, 0f, 1f);
            Tip("The chance the round sets fire to a part holding propellant when it strikes it, in air. "
                + "A burning part loses its health over 20 to 100 seconds and then breaks.");
            if (_munition.FragmentCount > 0)
            {
                ImGui.SliderFloat("Fragment ignition chance", ref _munition.FragmentIgnitionChance, 0f, 1f);
                Tip("The chance each fragment that hits a part holding propellant sets it on fire.");
            }
            if (_munition.HitsTerrain)
            {
                ImGui.SliderFloat("Burst height (m)", ref _munition.BurstHeightMetres, 0f, 10000f,
                                  "%.0f m", ImGuiSliderFlags.Logarithmic);
                Tip("A radar or barometric fuse: it fires this far over the ground on the way down. "
                    + "Zero bursts on contact. Released lower than this, it bursts on the ground.");

                ImGui.SliderFloat("Parachute", ref _munition.ChuteSinkMetresPerSecond, 0f, 100f,
                                  _munition.HasChute ? "falls at %.0f m/s" : "none");
                Tip("A retarding parachute, set by how fast the bomb comes down under it at sea level. "
                    + "Slower gives whoever dropped it longer to get away. Zero is no parachute.");

                if (_munition.HasChute)
                {
                    ImGui.SliderFloat("Parachute opens after (s)", ref _munition.ChuteOpensSeconds, 0f, 10f);
                }

                if (TsarBombaKg <= maxCharge)
                {
                    if (ImGui.Button("Tsar Bomba"))
                    {
                        _munition.ChargeKg = TsarBombaKg;
                        _munition.BurstHeightMetres = 4000f;
                        _munition.ChuteSinkMetresPerSecond = 18f;
                        _munition.ChuteOpensSeconds = 1.5f;
                    }

                    Tip("50 Mt, bursting 4,000 m up, on a 1,600 m² chute that brought 27 t down at "
                        + "about 18 m/s. Dropped from 10.5 km it took 188 s to fall, which is what let "
                        + "the Tu-95 get 45 km away. Edits every store of this kind in the world.");
                }
            }

            // Spacing is between rounds of one salvo, so a launcher holding one round has none. A
            // launcher with no tubes never reloads them: its belt has its own timer.
            if (_profile.TubeCount > 1)
            {
                ImGui.SliderFloat("Salvo spacing (s)", ref _profile.SalvoSpacing, 0.05f, 3f);
            }

            if (_profile.TubeCount > 0)
            {
                ImGui.SliderFloat("Reload time (s)", ref _profile.ReloadSeconds, 0f, 60f,
                                  _profile.ReloadSeconds > 0f ? "%.1f s" : "never");
                Tip("How long after the last round leaves before the launcher is full again. "
                    + "Zero is never: a rail or rack that has let its store go stays empty.");
            }

            DrawOtherArmamentRounds();
            ImGui.TreePop();
        }

    }
}
