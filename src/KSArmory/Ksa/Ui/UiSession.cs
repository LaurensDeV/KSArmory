using Brutal.ImGuiApi;

namespace KSArmory;

/// <summary>
/// What belongs to the session rather than to any one installation: the world clock, what gets
/// drawn, and what gets heard.
///
/// <para>Separate from the per-system panes because the test in <c>CLAUDE.md</c> is whether two
/// sites could sensibly disagree, and none of this passes it — there is one screen, one pair of
/// ears and one clock. Drawn inside a craft's window, all of it reads as that craft's.</para>
///
/// <para>Nothing here may read <c>_battery</c> without checking <c>_crewed</c> first. These are
/// reachable with no weapons system selected at all, which is the whole point of them.</para>
/// </summary>
internal sealed partial class Ui
{
    // The one session setting that changes what the weapons do rather than how they are watched,
    // which is why it sits with the panes rather than under Debug.
    private void DrawWarpHold()
    {
        ImGui.Checkbox("Hold timewarp down while rounds fly", ref _config.LimitWarpInFlight);

        ImGui.Checkbox("Dirty nuclear smoke", ref _config.DirtyNuclearSmoke);
        ImGui.TextDisabled(_config.DirtyNuclearSmoke
                               ? "  a cloud tints every plume in the world while it stands"
                               : "  off: the cloud is white, and nothing else is touched");
        ImGui.TextDisabled($"  Above ~{MaxTrackableWarp:F0}x a round cannot be simulated. Held only");
        ImGui.TextDisabled("  while something is in the air, and given back after.");
        if (!_config.LimitWarpInFlight)
        {
            ImGui.TextDisabled("  Off: rounds under warp will lag the world and miss.");
        }
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
    // heard, and the one setting that changes how the weapons behave.
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
        DrawWorldClock();
        ImGui.Separator();
        DrawBurstTool();
        ImGui.Separator();

        // Inline rather than a pane of its own. It is one tick box and a line of state, and a
        // window holding that is a window to open, move and close for nothing.
        DrawCraftMover();
        ImGui.Separator();
        DrawLogging();
        ImGui.Separator();

        DrawPaneGroup(null, PaneGroup.Debug);
    }

    // Everything drawn in the world. One screen, so one set of switches.
    private void DrawDisplayPane()
    {
        ImGui.Checkbox("World overlay", ref _config.DrawOverlays);
        ImGui.TextDisabled("  everything drawn in the world around a system");

        if (_config.DrawOverlays)
        {
            ImGui.Checkbox("Only the system shown in the panel",
                           ref _config.DrawOverlayForFocusedOnly);
            ImGui.TextDisabled("  off: every crewed system draws its own");
        }

        ImGui.SeparatorText("Effects");
        ImGui.Checkbox("Warhead effects", ref _config.DrawExplosions);
        ImGui.TextDisabled("  the fireball, not a debug line -- kept when those are off");

        ImGui.Checkbox("Rocket motor plume", ref _config.MotorPlume);

        ImGui.Checkbox("Rocket smoke trail", ref _config.MotorSmoke);
        ImGui.TextDisabled("  hangs for 20 minutes and drifts on the wind - the engine's own");
        ImGui.TextDisabled("  lifetime, shared with mushroom clouds");
        ImGui.TextDisabled("  flame at the nozzle while the motor burns; needs warhead effects on");

        ImGui.SeparatorText("Systems");
        ImGui.Checkbox("Weapons-system markers", ref _config.DrawSystemMarkers);
        ImGui.TextDisabled("  brackets over every system; Look at in the list pins a label");
        ImGui.Checkbox("Lock cue", ref _config.DrawLockCue);
        ImGui.TextDisabled("  brackets on what the selected weapon is engaging; they close as it locks");
        ImGui.Checkbox("Radar volume", ref _config.DrawRadarVolume);
        ImGui.Checkbox("Drive facing line", ref _config.DrawTurretFacing);
        ImGui.TextDisabled("  where the drives think they point, not where they are told to");
        ImGui.Checkbox("Bearing reference", ref _config.DrawBearingReference);
        ImGui.TextDisabled("  white to north, green along each face of the array as the scope reads it");
        ImGui.SliderFloat("Cone draw length (m)", ref _config.ConeDisplayMetres, 200f, 20000f);
        ImGui.TextDisabled("  cosmetic only; detection range is set on the sensor");

        ImGui.SeparatorText("Contacts");
        ImGui.Checkbox("Tracks", ref _config.DrawTracks);
        ImGui.Checkbox("Track marker spheres", ref _config.DrawTrackMarkers);
        ImGui.TextDisabled("  large ball on each contact; scales with range");
        ImGui.Checkbox("Predicted pass point", ref _config.DrawClosestApproach);
        ImGui.TextDisabled("  where a threat will pass if it holds course");

        ImGui.SeparatorText("Rounds");
        ImGui.Checkbox("Rounds", ref _config.DrawMissiles);
        ImGui.Checkbox("Round tracer spheres", ref _config.DrawRoundMarkers);

        // Bodies and tracers are placed by entirely separate paths, so toggling this while
        // watching a round in flight says which of the two is misbehaving.
        ImGui.Checkbox("Round bodies (off = tracers only)", ref _config.UseRoundBodies);
        ImGui.Checkbox("Tube markers (debug)", ref _config.DrawTubeMarkers);

        // Reads a system, so it only appears when there is one. The switch above is the session's
        // and stands whatever is selected; this line is a report about the selected system.
        if (!_crewed) return;

        ImGui.TextDisabled(_battery.RoundBodyCount > 0 && _battery.RoundBodiesWork
            ? "  rounds have real bodies; the tracer hides them up close"
            : "  no round bodies available - tracers are all there is");
    }

    // One pair of ears. Each sound is a switch and a volume, and the volume only exists when the
    // switch is on -- a slider that does nothing is worse than no slider.
    private void DrawSoundPane()
    {
        ImGui.Checkbox("Explosion sound", ref _config.BurstSound);
        if (_config.BurstSound)
        {
            ImGui.SliderFloat("Explosion volume", ref _config.BurstVolume, 0f, 1f);
        }

        ImGui.Checkbox("Rocket motor sound", ref _config.MotorSound);
        if (_config.MotorSound)
        {
            ImGui.SliderFloat("Motor volume", ref _config.MotorVolume, 0f, 1f);
            ImGui.TextDisabled("  before the engine's own distance and pressure falloff, so a");
            ImGui.TextDisabled("  round in vacuum is silent whatever this says");
        }

        ImGui.Checkbox("Cannon sound", ref _config.CannonSound);
        if (_config.CannonSound)
        {
            ImGui.SliderFloat("Cannon volume", ref _config.CannonVolume, 0f, 1f);
            ImGui.TextDisabled("  pitched from each gun's own rate, so the buzz is its cycle");
        }
    }
}
