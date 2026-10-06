using Brutal.ImGuiApi;

namespace KSArmory;

/// <summary>
/// What belongs to the session rather than to any one installation: the world clock, the teams,
/// what gets drawn, and what gets heard.
///
/// <para>Separate from the per-system panes because the test in <c>CLAUDE.md</c> is whether two
/// sites could sensibly disagree, and none of this passes it — there is one screen, one pair of
/// ears and one clock. Drawn inside a craft's window, all of it reads as that craft's.</para>
///
/// <para>Nothing here may read <c>_system</c> without checking <c>_crewed</c> first. These are
/// reachable with no weapons system selected at all, which is the whole point of them.</para>
/// </summary>
internal sealed partial class Ui
{
    // The one session setting that changes what the weapons do rather than how they are watched,
    // which is why it sits with the panes rather than under Debug.
    private void DrawWarpHold()
    {
        ImGui.Checkbox("Hold timewarp down while rounds fly", ref _config.LimitWarpInFlight);
        Tip($"Above ~{MaxTrackableWarp:F0}x a round cannot be simulated. Held only while something is "
            + "in the air, and given back after.");
        if (!_config.LimitWarpInFlight)
        {
            ImGui.TextDisabled("  Off: rounds under warp will lag the world and miss.");
        }

        // Beside the warp hold because it is the other session setting that trades away something
        // a player can see for something the simulation needs -- there, fidelity for speed; here,
        // the world's own debris for frame time.
        ImGui.Checkbox("Remove spent stages once they are clear", ref _config.DisposeSpentStages);
        Tip($"On: a spent stage is destroyed once it is {StageDisposal.ClearOfTheCraftMetres / 1000.0:F0} km "
            + "clear, so it stops costing frame time while it falls. Off: it falls and is simulated "
            + "the whole way down.\n\nFrame time is what buys simulation rate -- about 2 ms a vehicle, "
            + "and a rocket sheds four. The half a MIRV bus drops is never taken.");

        // The third of the same shape: what a warhead does to a craft is one world-level rule, so
        // two sites could not sensibly disagree about it. It also trades frame time -- one craft
        // becomes several, and every fragment is simulated.
        ImGui.Checkbox("Break individual parts, not whole craft", ref _config.DamageIndividualParts);
        Tip("On: each part is judged on its own distance from the burst and its own strength, so a weak "
            + "part breaks further out than a dense one, and losing enough of them at once still "
            + "destroys the craft outright. Off: inside the lethal radius the whole craft is destroyed.");

        ImGui.SliderFloat("Damage scale", ref _config.DamageScale, 0.001f, 10f, "%.3fx", ImGuiSliderFlags.Logarithmic);
        Tip("How much of a part's health one burst takes. Every part starts whole and loses what each "
            + "burst puts on it, so a load short of breaking it is not forgotten. At 1x a fresh part breaks "
            + "where it always has; at 0.1x it takes ten such bursts, and a shell has to strike the same "
            + "part ten times before it breaks off -- no one hit takes more than a whole part, however "
            + "close it burst. Parts only: with individual parts off, a burst in the "
            + "lethal radius still destroys the craft.");

        ImGui.Checkbox("Holed tanks leak", ref _config.TankLeaks);
        Tip("On: a tank a shell has holed loses its liquid through every hole under the level, driven by a low "
            + "tank pressure and by the depth above the hole and the pull the craft feels, and streams where it "
            + "goes. The liquid settles against that pull, so a craft on its side drains from its lower holes and "
            + "a hole above the level leaks nothing. Coasting in orbit the liquid floats, and every hole leaks as "
            + "often as it is under it -- about as often as the tank is full.");

        ImGui.Checkbox("Nuclear bursts black out radar", ref _config.NuclearBlackout);
        Tip("On: a nuclear fireball ionises the air round it, and a radar cannot see through that or "
            + "out of it until it cools -- about a minute for a megatonne, seconds for a fraction of a "
            + "kilotonne. Only sets that transmit are blinded. Off: radar looks straight through a burst.");

        ImGui.Checkbox("Weapons on one craft share a target count", ref _config.ShareTargetsAcrossWeapons);
        Tip("On: two rails on one craft will not each fire a full salvo at the same target. "
            + "Off: each weapon counts only its own rounds.");

        ImGui.Checkbox("High bursts send a red wave", ref _config.RedWave);
        Tip("On: a burst 60 km up or higher sends a faint red sphere out through the upper air for "
            + "minutes, as Teak did -- a night sight. Off: it is not drawn.");

        ImGui.Checkbox("Mushroom clouds", ref _config.NuclearClouds);
        Tip("On: a nuclear burst leaves a cloud standing, at 6-8 ms a frame. "
            + "Off: the burst does the same damage and leaves nothing behind.");

    }

    // Slow motion, well below what the game's speed control reaches. An engagement is over in a
    // couple of seconds of real time and the interesting part — the round leaving the tube, the
    // endgame turn, the fuse — happens far faster than it can be watched. Nothing in KSA stops the
    // simulation running at a hundredth of real time; its roller is simply built in tenths.
    //
    // Under Debug because the game has a speed control of its own: this one exists for looking at
    // what the mod did, which is what everything else in that group is for.
    private void DrawWorldClock()
    {
        ImGui.Text($"Sim speed: {KsaWorld.SimulationSpeed:0.###}x");

        foreach ((string label, double speed) in SlowMotionSpeeds)
        {
            ImGui.SameLine();
            if (ImGui.Button(label)) KsaWorld.SetSimulationSpeed(speed);
        }
    }

    // Everything that belongs to the session and to playing with the mod: what is drawn, what is
    // heard, and the settings that change how the weapons behave.
    //
    // A window rather than a tree on the main panel, because the panel is a list of the systems in
    // the world and that list is the only thing on it that changes as the world does.
    private void DrawSettingsPane()
    {
        if (ImGui.CollapsingHeader("Display", ImGuiTreeNodeFlags.DefaultOpen)) DrawDisplayPane();
        if (ImGui.CollapsingHeader("Sound")) DrawSoundPane();

        ImGui.SeparatorText("Weapons");
        DrawWarpHold();
    }

    // The developer tools, in a window of their own rather than a section of the settings one.
    //
    // Most of this answers questions about the mod rather than about the engagement, which is what
    // separates the two windows -- but slow motion, the target spawner and the log are all reached
    // during an engagement, and nothing wanted at that moment belongs behind a fold inside another
    // window.
    private void DrawDebugPane()
    {
        // A report, not a second switch. Config.DrawOverlays has exactly one control, under
        // Display beside the sub-switches it governs; a second one here would be the same field
        // under a second name, so toggling either would silently move the other.
        ImGui.TextDisabled(_config.DrawOverlays
                               ? "World overlay is on - Settings > Display has its switches"
                               : "World overlay is off - turn it on under Settings > Display");

        ImGui.Separator();
        if (Build.Developer)
        {
            DrawCaptureForClaude();
            ImGui.Separator();
        }

        DrawWorldClock();
        ImGui.Separator();
        DrawBurstTool();
        ImGui.Separator();

        // Inline rather than a pane of its own. It is one tick box and a line of state, and a
        // window holding that is a window to open, move and close for nothing.
        DrawCraftMover();
        DrawSendToBody();
        ImGui.Separator();
        if (Build.Developer)
        {
            DrawFinTest();
            ImGui.Separator();
        }

        DrawLogging();
        ImGui.Separator();

        DrawPaneGroup(null, PaneGroup.Debug);
    }

    // What the player is looking at, kept for whoever is diagnosing it: frames, the state and the
    // log at that moment. A report from play in words is a guess at a cause; this is the evidence.
    private static void DrawCaptureForClaude()
    {
        if (ImGui.Button("Capture for Claude")) Bridge.RequestPlayerCapture();
        Tip("Saves eight frames of what is on screen with a note of the game's state and the end of "
            + "the log, beside the log in bridge/out. Nothing is sent anywhere. Press it the moment "
            + "you see something, and say so.");

        if (Bridge.LastPlayerCapture is { } last) ImGui.TextDisabled($"  saved {last}");
    }

    // Everything drawn in the world. One screen, so one set of switches.
    private void DrawDisplayPane()
    {
        ImGui.Checkbox("World overlay", ref _config.DrawOverlays);
        Tip("Everything drawn in the world around a system.");


        ImGui.Checkbox("Shell holes", ref _config.BulletHoles);
        Tip($"On: a shell that strikes a hull leaves a hole with soot round it, painted on the hull and "
            + $"carried with it. The newest {BulletHoles.MaxHoles} are kept, and the nearest "
            + $"{CloudPass.MostHolesPainted} in view are painted. Off: nothing is marked.");

        ImGui.SliderFloat("Rounds between tracers", ref _config.BallRoundBrightness, 0f, 8f,
                          _config.BallRoundBrightness > 0f ? "%.1f" : "hidden");
        Tip("How bright a gun round that is not a tracer is drawn in daylight: a faint grey streak, so "
            + "the stream between the tracers reads. It fades with the sun, and at night only the "
            + "tracers are seen. A tracer is 24. Zero shows only the tracers.");

        if (_config.DrawOverlays)
        {
            ImGui.Checkbox("Only the system shown in the panel",
                           ref _config.DrawOverlayForFocusedOnly);
            Tip("Off: every crewed system draws its own.");
        }

        ImGui.SeparatorText("Effects");
        ImGui.Checkbox("Warhead effects", ref _config.DrawExplosions);
        Tip("KSA's own explosion, flash and sound included -- not a debug line, so kept when the "
            + "world overlay is off.");

        ImGui.Checkbox("Rocket motor plume", ref _config.MotorPlume);
        Tip("The flame at the nozzle while the motor burns.");
        if (_config.MotorPlume && !_config.DrawExplosions)
        {
            ImGui.TextDisabled("  needs Warhead effects on");
        }

        ImGui.Checkbox("Rocket smoke trail", ref _config.MotorSmoke);
        Tip("Hangs for 20 minutes and drifts on the wind -- the engine's own lifetime, shared with "
            + "mushroom clouds.");

        ImGui.SliderFloat("Smoke width", ref _config.MotorSmokeWidth, 0.1f, 4f);
        Tip("A multiple of each round's own size. Live: smoke laid from now on uses it.");

        ImGui.SeparatorText("Systems");
        ImGui.Checkbox("Weapons-system markers", ref _config.DrawSystemMarkers);
        Tip("Brackets over every system. Right-clicking a name in the list pins its label.");
        ImGui.Checkbox("Lock cue", ref _config.DrawLockCue);
        Tip("Brackets on what the selected weapon is engaging; they close as it locks.");
        ImGui.Checkbox("Radar volume", ref _config.DrawRadarVolume);
        ImGui.Checkbox("Drive facing line", ref _config.DrawTurretFacing);
        Tip("Where the drives think they point, not where they are told to.");
        ImGui.Checkbox("Bearing reference", ref _config.DrawBearingReference);
        Tip("White to north, green along each face of the array as the scope reads it.");
        ImGui.SliderFloat("Cone draw length (m)", ref _config.ConeDisplayMetres, 200f, 20000f);
        Tip("Cosmetic only; detection range is set on the sensor.");

        ImGui.SeparatorText("Contacts");
        ImGui.Checkbox("Tracks", ref _config.DrawTracks);
        ImGui.Checkbox("Track marker spheres", ref _config.DrawTrackMarkers);
        Tip("A large ball on each contact, scaled with range.");
        ImGui.Checkbox("Predicted pass point", ref _config.DrawClosestApproach);
        Tip("Where a threat will pass if it holds course.");

        ImGui.SeparatorText("Rounds");
        ImGui.Checkbox("Rounds", ref _config.DrawMissiles);
        ImGui.Checkbox("Round tracer spheres", ref _config.DrawRoundMarkers);

        // Bodies and tracers are placed by entirely separate paths, so toggling this while
        // watching a round in flight says which of the two is misbehaving.
        ImGui.Checkbox("Round bodies (off = tracers only)", ref _config.UseRoundBodies);
        if (Build.Developer) ImGui.Checkbox("Tube markers (debug)", ref _config.DrawTubeMarkers);

        // Reads a system, so it only appears when there is one. The switch above is the session's
        // and stands whatever is selected; this line is a report about the selected system.
        if (!_crewed) return;

        ImGui.TextDisabled(_system.RoundBodyCount > 0 && _system.RoundBodiesWork
            ? "  rounds have real bodies; the tracer hides them up close"
            : "  no round bodies available - tracers are all there is");
    }

    // One pair of ears. Each sound is a switch and a volume, and the volume only exists when the
    // switch is on -- a slider that does nothing is worse than no slider.
    private void DrawSoundPane()
    {
        ImGui.Checkbox("Rocket motor sound", ref _config.MotorSound);
        if (_config.MotorSound)
        {
            ImGui.SliderFloat("Motor volume", ref _config.MotorVolume, 0f, 1f);
            Tip("Before the engine's own distance and pressure falloff, so a round in vacuum is "
                + "silent whatever this says.");
        }

        ImGui.Checkbox("Cannon sound", ref _config.CannonSound);
        if (_config.CannonSound)
        {
            ImGui.SliderFloat("Cannon volume", ref _config.CannonVolume, 0f, 1f);
            Tip("Pitched from each gun's own rate, so the buzz is its cycle.");
        }
    }
}
