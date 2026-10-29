using Brutal.ImGuiApi;

namespace KSArmory;

/// <summary>
/// What belongs to the session rather than to any one installation: the world clock, the teams,
/// what gets drawn, and what gets heard.
/// <para>Nothing here may read <c>_system</c> without checking <c>_crewed</c>: these are reachable
/// with no weapons system selected.</para>
/// </summary>
internal sealed partial class Ui
{
    // One slider for how hard weapons hit: paintball at its far left, a damage scale from a hundredth to
    // ten across the middle, and at its far right the old rule, a burst in the lethal radius destroying the
    // whole craft.
    private void DrawDamage()
    {
        float position = _config.Paintball ? 0f
                         : _config.DamageIndividualParts ? ScaleToPosition(_config.DamageScale)
                         : 1f;
        string shown = _config.Paintball ? "Paintball"
                       : _config.DamageIndividualParts ? $"{_config.DamageScale:0.###}x"
                       : "One-hit kills";

        if (ImGui.SliderFloat("Damage", ref position, 0f, 1f, shown, ImGuiSliderFlags.NoInput))
        {
            _config.Paintball = position < PaintballBelow;
            _config.DamageIndividualParts = position < OneHitAt;
            if (!_config.Paintball && _config.DamageIndividualParts) _config.DamageScale = PositionToScale(position);
        }
        Tip("How hard weapons hit. At 1x a part breaks where the damage model says it does; at 0.1x it takes "
            + "ten such hits, at 10x a tenth of one. Every part keeps what each hit took. At the far right, "
            + "One-hit kills: no part damage at all, and anything inside a warhead's lethal radius is "
            + "destroyed outright. At the far left, Paintball: a hit marks what it struck with the shooter's "
            + "paint and harms nothing -- nothing breaks, dents, burns, leaks or is shot down.");

        ImGui.Checkbox("Part health bars", ref _config.DrawPartHealth);
        Tip($"A bar over every part of every craft within {PartHealthBars.RangeMetres / 1000.0:F0} km of the "
            + "camera, and over any damaged part further out: green whole, red nearly gone.");

        ImGui.Checkbox("Holed tanks leak", ref _config.TankLeaks);
        Tip("A tank a shell has holed loses its liquid through every hole under the level, and a burning "
            + "tank's leak burns. The liquid settles against the pull the craft feels, so a hole above it "
            + "leaks nothing.");
    }

    // The slider's travel: paintball under a twentieth, a hundredth to ten on a log scale from a tenth to
    // nine tenths, and one-hit kills past nineteen twentieths.
    private const float PaintballBelow = 0.05f;
    private const float OneHitAt = 0.95f;
    private const float ScaleFrom = 0.1f, ScaleTo = 0.9f;
    private const double LeastScale = 0.01, MostScale = 10.0;

    private static float ScaleToPosition(float scale)
    {
        double t = Math.Log(Math.Clamp(scale, LeastScale, MostScale) / LeastScale) / Math.Log(MostScale / LeastScale);
        return (float)(ScaleFrom + (t * (ScaleTo - ScaleFrom)));
    }

    private static float PositionToScale(float position)
    {
        double t = Math.Clamp((position - ScaleFrom) / (ScaleTo - ScaleFrom), 0.0, 1.0);
        return (float)(LeastScale * Math.Pow(MostScale / LeastScale, t));
    }

    // What a player rarely needs: the world clock held for rounds, stages taken out, salvos shared, blackout.
    private void DrawAdvancedPane()
    {
        ImGui.Checkbox("Hold timewarp down while rounds fly", ref _config.LimitWarpInFlight);
        Tip($"Above ~{MaxTrackableWarp:F0}x a round cannot be simulated. Held only while something is "
            + "in the air, and given back after.");
        if (!_config.LimitWarpInFlight)
        {
            ImGui.TextDisabled("  Off: rounds under warp will lag the world and miss.");
        }

        ImGui.Checkbox("Remove spent stages once they are clear", ref _config.DisposeSpentStages);
        Tip($"On: a spent stage is destroyed once it is {StageDisposal.ClearOfTheCraftMetres / 1000.0:F0} km "
            + "clear, so it stops costing frame time while it falls. Off: it falls and is simulated "
            + "the whole way down.\n\nFrame time is what buys simulation rate -- about 2 ms a vehicle, "
            + "and a rocket sheds four. The half a MIRV bus drops is never taken.");

        ImGui.Checkbox("Weapons on one craft share a target count", ref _config.ShareTargetsAcrossWeapons);
        Tip("On: two rails on one craft will not each fire a full salvo at the same target. "
            + "Off: each weapon counts only its own rounds.");

        ImGui.Checkbox("Nuclear bursts black out radar", ref _config.NuclearBlackout);
        Tip("On: a nuclear fireball ionises the air round it, and a radar cannot see through that or "
            + "out of it until it cools -- about a minute for a megatonne, seconds for a fraction of a "
            + "kilotonne. Only sets that transmit are blinded. Off: radar looks straight through a burst.");
    }

    // Below KSA's own roller, which stops at tenths; nothing in the engine stops a hundredth.
    private void DrawWorldClock()
    {
        ImGui.Text($"Sim speed: {KsaWorld.SimulationSpeed:0.###}x");

        foreach ((string label, double speed) in SlowMotionSpeeds)
        {
            ImGui.SameLine();
            if (ImGui.Button(label)) KsaWorld.SetSimulationSpeed(speed);
        }
    }

    private void DrawSettingsPane()
    {
        ImGui.SeparatorText("Damage");
        DrawDamage();

        if (ImGui.CollapsingHeader("Display")) DrawDisplayPane();
        if (ImGui.CollapsingHeader("Sound")) DrawSoundPane();
        if (ImGui.CollapsingHeader("Advanced")) DrawAdvancedPane();
    }

    private void DrawDebugPane()
    {
        // A report, not a second switch: Config.DrawOverlays has one control, under Display.
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

    private static void DrawCaptureForClaude()
    {
        if (ImGui.Button("Capture for Claude")) Bridge.RequestPlayerCapture();
        Tip("Saves eight frames of what is on screen with a note of the game's state and the end of "
            + "the log, beside the log in bridge/out. Nothing is sent anywhere. Press it the moment "
            + "you see something, and say so.");

        if (Bridge.LastPlayerCapture is { } last) ImGui.TextDisabled($"  saved {last}");
    }

    private void DrawDisplayPane()
    {
        ImGui.Checkbox("Warhead effects", ref _config.DrawExplosions);
        Tip("KSA's own explosion, flash and sound included.");

        ImGui.Checkbox("Rocket motor plume", ref _config.MotorPlume);
        Tip("The flame at the nozzle while the motor burns.");
        if (_config.MotorPlume && !_config.DrawExplosions)
        {
            ImGui.TextDisabled("  needs Warhead effects on");
        }

        ImGui.Checkbox("Rocket smoke trail", ref _config.MotorSmoke);
        Tip("Hangs for 20 minutes and drifts on the wind -- the engine's own lifetime, shared with "
            + "mushroom clouds.");
        if (_config.MotorSmoke)
        {
            ImGui.SliderFloat("Smoke width", ref _config.MotorSmokeWidth, 0.1f, 4f);
            Tip("A multiple of each round's own size. Smoke laid from now on uses it.");
        }

        ImGui.Checkbox("Mushroom clouds", ref _config.NuclearClouds);
        Tip("On: a nuclear burst leaves a cloud standing, at 6-8 ms a frame. "
            + "Off: the burst does the same damage and leaves nothing behind.");

        ImGui.Checkbox("High bursts send a red wave", ref _config.RedWave);
        Tip("A burst 60 km up or higher sends a faint red sphere out through the upper air for "
            + "minutes, as Teak did -- a night sight.");

        ImGui.Checkbox("Shell holes", ref _config.BulletHoles);
        Tip($"A shell that strikes a hull leaves a hole with soot round it, carried with the hull. The newest "
            + $"{BulletHoles.MaxHoles} are kept, and the nearest {CloudPass.MostHolesPainted} in view are painted.");

        ImGui.SliderFloat("Gun round brightness", ref _config.BallRoundBrightness, 0f, 8f,
                          _config.BallRoundBrightness > 0f ? "%.1f" : "tracers only");
        Tip("How bright a gun round that is not a tracer is drawn in daylight: a faint grey streak, so "
            + "the stream between the tracers reads. It fades with the sun, and at night only the "
            + "tracers are seen. A tracer is 24.");

        ImGui.Checkbox("Weapons-system markers", ref _config.DrawSystemMarkers);
        Tip("Brackets over every system. Right-clicking a name in the list pins its label.");
        ImGui.Checkbox("Lock cue", ref _config.DrawLockCue);
        Tip("Brackets on what the selected weapon is engaging; they close as it locks.");

        ImGui.Checkbox("World overlay", ref _config.DrawOverlays);
        Tip("Diagnostic lines in the world around a system: its search volume, tracks, rounds and drives.");
        if (_config.DrawOverlays) DrawOverlayDetails();
    }

    // What the world overlay draws, shown only while it is on: for working out why a system does what it does.
    private void DrawOverlayDetails()
    {
        ImGui.Indent();

        ImGui.Checkbox("Only the system shown in the panel", ref _config.DrawOverlayForFocusedOnly);
        Tip("Off: every crewed system draws its own.");

        ImGui.Checkbox("Radar volume", ref _config.DrawRadarVolume);
        ImGui.Checkbox("Drive facing line", ref _config.DrawTurretFacing);
        Tip("Where the drives think they point, not where they are told to.");
        ImGui.Checkbox("Bearing reference", ref _config.DrawBearingReference);
        Tip("White to north, green along each face of the array as the scope reads it.");
        ImGui.SliderFloat("Cone draw length (m)", ref _config.ConeDisplayMetres, 200f, 20000f);
        Tip("Cosmetic only; detection range is set on the sensor.");

        ImGui.Checkbox("Tracks", ref _config.DrawTracks);
        ImGui.Checkbox("Track marker spheres", ref _config.DrawTrackMarkers);
        Tip("A large ball on each contact, scaled with range.");
        ImGui.Checkbox("Predicted pass point", ref _config.DrawClosestApproach);
        Tip("Where a threat will pass if it holds course.");

        ImGui.Checkbox("Rounds", ref _config.DrawMissiles);
        ImGui.Checkbox("Round tracer spheres", ref _config.DrawRoundMarkers);

        // Bodies and tracers are placed by separate paths, so this says which one misbehaves.
        ImGui.Checkbox("Round bodies (off = tracers only)", ref _config.UseRoundBodies);
        if (Build.Developer) ImGui.Checkbox("Tube markers (debug)", ref _config.DrawTubeMarkers);

        if (_crewed)
        {
            ImGui.TextDisabled(_system.RoundBodyCount > 0 && _system.RoundBodiesWork
                ? "  rounds have real bodies; the tracer hides them up close"
                : "  no round bodies available - tracers are all there is");
        }

        ImGui.Unindent();
    }

    // A volume shows only while its switch is on.
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
