using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Development tools: spawning targets, moving craft, setting off warheads by hand, and the log.
///
/// <para>Nothing here is part of playing with the mod, which is why it all sits behind the
/// collapsed Debug group rather than alongside the system panes.</para>
/// </summary>
internal sealed partial class Ui
{
    private void DrawBurstTool()
    {
        ImGui.Checkbox("Explosions on click", ref _config.BurstTool);

        if (_config.BurstTool)
        {
            ImGui.TextDisabled("  click the ground to set one off there");
            ImGui.Checkbox("Nuclear", ref _config.BurstNuclear);

            if (_config.BurstNuclear)
            {
                // The B61's own dial. Logarithmic because the interesting end is the bottom of it:
                // three orders of magnitude, and the cloud changes shape more between 0.3 and 3 kt
                // than between 100 and 340.
                ImGui.SliderFloat("Yield (kt)", ref _config.BurstYieldKt, 0.3f, 340f,
                                  "%.2f kt", ImGuiSliderFlags.Logarithmic);

                double kt = _config.BurstYieldKt;

                ImGui.TextDisabled($"  fireball {MushroomCloud.PeakFireballRadius(kt) * 2.0:F0} m "
                                   + $"across for {MushroomCloud.FlashSeconds(kt):F1} s");
                ImGui.TextDisabled($"  cloud to {MushroomCloud.DrawnCloudTop(kt) / 1000.0:F2} km, "
                                   + $"cap {MushroomCloud.DrawnCapRadius(kt) * 2.0 / 1000.0:F2} km "
                                   + $"across, over {MushroomCloud.RiseSeconds:F0} s");
                ImGui.TextDisabled($"  lethal {Warhead.LethalRadius(kt * 1.0e6):F0} m"
                                   + "   (the ring is the lethal radius)");

                if (!PlumeSmoke.Available)
                {
                    ImGui.TextColored(Red, "the volumetric trail renderer is unreachable");
                    ImGui.TextDisabled("  the flash will draw and the cloud will not");
                }
            }
            else
            {
                ImGui.SliderFloat("Charge (kg)", ref _config.BurstChargeKg, 0.01f, 500f,
                                  "%.2f", ImGuiSliderFlags.Logarithmic);
                ImGui.TextDisabled($"  lethal {Warhead.LethalRadius(_config.BurstChargeKg):F0} m, "
                                   + $"fireball {Warhead.FireballRadius(_config.BurstChargeKg):F0} m"
                                   + "   (the ring is the lethal radius)");
            }

            ImGui.Checkbox("Fireball (off: airburst)", ref _config.BurstFireball);
        }

        // Straight overhead, for when the pointer is not the question -- it needs no aim and no
        // ground under it, so it still answers "does the effect work at all".
        if (ImGui.Button("Burst overhead")) FireTestBurst(Detonation.Fireball);
        ImGui.SameLine();
        ImGui.TextDisabled("100 m over the system shown");

        if (!Detonation.ParticlesEnabled)
        {
            ImGui.TextColored(Red, "KSA's Particles graphics setting is OFF");
            ImGui.TextDisabled("  nothing will draw until it is turned back on");
        }
        else if (!Detonation.SoftParticles)
        {
            ImGui.TextDisabled("Smoke is drawn as small spheres. KSA's Screen Space");
            ImGui.TextDisabled("Particles setting turns on the volumetric version.");
        }
    }

    // A burst overhead, where it cannot be missed.
    private void FireTestBurst(string emitterId)
    {
        if (!_crewed || _battery.Platform is not { } platform)
        {
            Log.Info("no platform to burst over");
            return;
        }

        double3 at = KsaWorld.PositionEcl(platform) + KsaWorld.LocalUp(platform) * 100.0;
        Log.Info($"test burst: {emitterId} 100 m over {KsaWorld.DisplayName(platform)}");
        Detonation.Show(emitterId, at, platform);
    }

    // Inline beside the other test aids rather than on a component row: it is not something a
    // Mk 82 rack "has", and the answer to "why are the fins moving" must not be behind a fold.
    private void DrawFinTest()
    {
        ImGui.Checkbox("Sweep seated fins (built-in test)", ref _config.FinTestSweep);

        if (!_config.FinTestSweep)
        {
            ImGui.TextDisabled("  exercise a loaded round's fins without dropping it");
            return;
        }

        int hinged = 0;
        foreach (WeaponSystems.Entry e in _batteries.All)
            if (e.Battery.Munition.FinsPerRound > 0) hinged++;

        // Says nothing is happening rather than leaving the tick box looking broken: every
        // launcher in the world may well have no hinged blades to sweep.
        if (hinged == 0)
            ImGui.TextDisabled("  no launcher in this world carries hinged fins");
        else
            ImGui.TextDisabled($"  sweeping on {hinged} launcher(s), "
                               + $"{FinTest.PeriodSeconds:F0} s per cycle, on simulated time");
    }

    private void DrawCraftMover()
    {
        ImGui.Checkbox("Move craft with the mouse", ref _config.MoveCraftWithMouse);

        if (!_config.MoveCraftWithMouse)
        {
            ImGui.TextDisabled("  click a craft to lift it, click the ground to set it down");
            return;
        }

        if (_mover.Held is { } held)
        {
            ImGui.TextColored(Amber, $"  holding {KsaWorld.DisplayName(held)} - click the ground");
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel")) _mover.Release();
        }
        else if (_mover.Hovered is { } over)
        {
            ImGui.TextColored(Green, $"  click to pick up {KsaWorld.DisplayName(over)}");
        }
        else
        {
            ImGui.TextDisabled("  point at a craft; it rings when the click would take it");
        }
    }

    private void DrawTestTargets()
    {
        if (!_crewed) { ImGui.TextDisabled("No weapons system selected."); return; }

        if (_battery.Platform is null)
        {
            ImGui.TextDisabled("no platform");
            return;
        }

        ImGui.SliderFloat("Time to pass (s)", ref _spawnSeconds, 10f, 180f);
        ImGui.SliderFloat("Speed (m/s)", ref _spawnSpeed, 50f, 2000f);
        ImGui.SliderFloat("Miss distance (m)", ref _spawnMiss, 0f, 5000f);

        // Spawning beyond radar range is the easiest way to see nothing happen at all.
        float spawnRange = _spawnSpeed * _spawnSeconds;
        if (spawnRange > _sensor.Range)
        {
            ImGui.TextColored(Amber,
                $"spawns {spawnRange / 1000f:F1} km out - beyond {_sensor.Range / 1000f:F1} km radar range");
            ImGui.TextDisabled("  it will be invisible until it closes to range");
        }
        else
        {
            ImGui.TextDisabled($"spawns {spawnRange / 1000f:F1} km out, inside radar range");
        }

        // Which craft to fly as the drone. A stock vessel reads as an actual intruder;
        // cloning the launcher means air-defence sites attacking air-defence sites.
        ImGui.SliderInt("Drone type", ref _spawnCraft, 0, TestTarget.StockCraft.Length);
        string craft = _spawnCraft < TestTarget.StockCraft.Length
            ? TestTarget.StockCraft[_spawnCraft]
            : "clone of this craft";
        ImGui.TextDisabled($"  {craft}");

        string? craftName = _spawnCraft < TestTarget.StockCraft.Length
            ? TestTarget.StockCraft[_spawnCraft]
            : null;

        if (ImGui.Button("Overhead"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.Overhead,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }
        ImGui.SameLine();
        if (ImGui.Button("Head-on"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.HeadOn,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }
        ImGui.SameLine();
        if (ImGui.Button("Passing by"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.PassingBy,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }

        ImGui.TextDisabled("Arm before they arrive.");
        ImGui.TextDisabled("Head-on dives steepest and holds its speed best in atmosphere.");

    }

    // Logging and the world dump: developer switches, so they sit with the others rather than
    // with the target spawner.
    private void DrawLogging()
    {
        if (ImGui.Checkbox("Verbose log", ref _config.VerboseLog))
        {
            Log.Threshold = _config.VerboseLog ? Log.Level.Debug : Log.Level.Info;
            Log.Info(_config.VerboseLog ? "verbose logging on" : "verbose logging off");
        }
        ImGui.TextDisabled("  developer detail; off in release builds");

        // Writes the battery's whole world view to the log, including why each nearby vehicle was
        // or was not tracked. Far more useful than staring at an empty screen.
        ImGui.BeginDisabled(!_crewed);
        if (ImGui.Button("Write diagnostic dump"))
        {
            Diagnostics.Dump(_battery, _policy);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.Checkbox("Freeze chase transition", ref _config.FreezeChaseTransition);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Diagnostic. The chase takes the view and aims, but does not fly onto\n"
                             + "the round. If the picture still jitters with the camera held still,\n"
                             + "the camera's travel is not what is causing it.");
        }

        if (ImGui.Checkbox("Keep dumping", ref _config.DiagnosticDump))
        {
            Diagnostics.ResetTimer();
        }
        ImGui.TextDisabled("  -> Logs/KSArmory.log");

        // A diagnostic about the render rate rather than a state of any weapon: it means the
        // frames are outrunning the simulation clock, which is what explains stuttering round
        // bodies. Reads the selected system, so it needs one.
        if (_crewed && _battery.FramesWithoutSimStep > 0)
        {
            ImGui.TextColored(Amber,
                $"Frames with no sim step: {_battery.FramesWithoutSimStep}");
            ImGui.TextDisabled("  the render rate is outrunning the simulation clock");
        }
    }

    private void DrawLog()
    {
        if (!_crewed) { ImGui.TextDisabled("No weapons system selected."); return; }

        var events = _battery.Events;
        for (int i = events.Count - 1; i >= 0; i--)
        {
            ImGui.TextDisabled($"[{events[i].AtSeconds:F1}] {events[i].Message}");
        }

    }
}
