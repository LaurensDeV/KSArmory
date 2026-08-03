using KSA;
using StarMap.API;

namespace AirDefence;

/// <summary>
/// StarMap entry point. StarMap loads the assembly named by mod.toml's EntryAssembly and
/// instantiates the first type carrying <see cref="StarMapModAttribute"/>, then dispatches
/// to the attributed methods below.
///
/// Frame work is wrapped so a fault degrades the mod instead of taking the game down, and
/// repeated faults disable it rather than filling the log.
/// </summary>
[StarMapMod]
public sealed class AirDefenceMod
{
    private const int FaultLimit = 10;

    private double _lastSimSpeed = 1.0;
    private readonly Config _config = new();
    private DefenceBattery? _battery;
    private Ui? _ui;
    private int _faults;
    private bool _disabled;

    [StarMapImmediateLoad]
    public void OnImmediateLoad(Mod mod)
    {
        Log.Info($"loading (mod id: {mod.Id})");
    }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        _battery = new DefenceBattery(_config);
        _ui = new Ui(_config, _battery);
        Log.Info($"ready - {Arsenal.PantsirS1.TubeCount} tubes, safe. Open the 'Air Defence' panel to arm.");
    }

    /// <summary>
    /// Simulation tick.
    ///
    /// <para>StarMap passes a <em>player-time</em> clock and delta, and those are deliberately
    /// ignored. Player time is wall-clock: it runs through a pause, so the battery used to
    /// mature a lock and fire into a frozen world, and it ignores timewarp, so under warp the
    /// world moved many seconds while rounds moved one frame. Both were seen in game. The
    /// simulation clock is the one that matches what the world did.</para>
    /// </summary>
    [StarMapAfterOnFrame]
    public void OnAfterFrame(double currentPlayerTime, double dtPlayer)
    {
        if (_disabled || _battery is null) return;
        if (!KsaWorld.InFlight) return;

        // Sim speed and pause state change what everything else in the log means, and they
        // change because someone moved a slider - so record them rather than inferring them
        // later from frozen timestamps, which is a mistake already made once.
        double speed = KsaWorld.SimulationSpeed;
        if (Math.Abs(speed - _lastSimSpeed) > 1e-9)
        {
            Log.Info($"simulation speed {_lastSimSpeed:F2}x -> {speed:F2}x"
                     + (KsaWorld.IsPaused ? " (paused)" : ""));
            _lastSimSpeed = speed;
        }

        // Nothing here: the simulation runs in OnAfterGui, alongside the drawing it feeds.
    }

    /// <summary>
    /// Panel and world overlay.
    ///
    /// The gizmo drawing has to happen here, not in the frame hook. KSA's whole frame runs
    /// inside OnFrame: it calls GizmosRenderer.ResetInstances() near the top, draws the UI,
    /// then renders. A postfix on OnFrame therefore lands *after* the render, so anything it
    /// submits is cleared by the next frame's reset before it is ever drawn. This hook is a
    /// postfix on OnDrawUiViewports, which sits between the reset and the render.
    /// </summary>
    [StarMapAfterGui]
    public void OnAfterGui(double dt)
    {
        if (_disabled || _ui is null || _battery is null) return;

        try
        {
            // Simulate here, immediately before drawing, rather than in the frame hook.
            //
            // KSA's order within a frame is: reset gizmos -> draw UI (this hook) -> render ->
            // postfix on OnFrame. So a simulation step in the frame hook lands AFTER this pass,
            // and every draw necessarily used an offset produced one frame earlier while
            // anchoring it to the platform's position now. A round is drawn as
            // `AnchorEgo + OffsetFromPlatform`, so that one-frame gap put it exactly one step of
            // the platform's ecliptic motion downrange - measured at 0.999 steps along the
            // orbital direction with 0.4 m across it, on all 221 samples taken. About 600 m at
            // 1x, and the same shift at launch as at the intercept, because a rigid drag moves
            // the whole flight equally.
            //
            // Correcting it at draw time cannot work: the drag is the platform's motion over one
            // step, so any correction carries a dt that changes frame to frame, and it comes
            // straight back as the `v * dstep` jitter fixed in Interceptor.Update. Tried, and it
            // reintroduced exactly that.
            //
            // Stepping here removes the gap instead of compensating for it. The offset and the
            // anchor are then produced in the same pass, so they share an epoch by construction
            // and there is no dt anywhere in the placement.
            if (KsaWorld.InFlight) StepSimulation(dt);

            _ui.Draw();

            if (KsaWorld.InFlight) Visuals.Draw(_battery, _config);
        }
        catch (Exception e)
        {
            Fault("gui", e);
        }
    }

    /// <summary>
    /// One simulation step, run from the GUI hook so it shares an epoch with the draw.
    /// </summary>
    private void StepSimulation(double dtPlayer)
    {
        if (_battery is null) return;

        // Every frame, before the clock gate. This reads where the world is, and the whole
        // overlay is drawn against it — leaving it inside the gated step froze the drawing's
        // frame of reference whenever the simulation did not advance.
        _battery.SampleWorld();

        // Gate on the step the engine actually applied, NOT on the pause flag.
        //
        // Universe.IsPaused() is `simulationSpeed == 0.0`, which is a statement about the speed
        // setting, not about whether the world moved this frame. On the frame the speed drops to
        // zero the engine still applies one real step: the platform sample advances and, with the
        // old guard, the round did not. The drawn offset is `P - Q`, so the whole of that step
        // landed in it - and because it is a difference of integrated positions, it stayed there.
        //
        // Measured, one line per pause, each within ~20 ms of a `0.00x -> 0.05x` transition:
        //
        //   offset moved 29.38 m | round could only fly 0.34 m | platform moved 29.56 m
        //   step consumed 0.9902 ms | platform implies 0.9902 ms
        //
        // The offset moved by exactly the platform's displacement, which is the signature of Q
        // advancing while P stood still. Pause and resume repeatedly and the round walks away a
        // step at a time - reported from play as "every single time it teleports further".
        //
        // ConsumeSimStep already answers the real question - did the engine advance the world
        // since we last integrated - and returns zero when it did not. So it is a strictly better
        // guard than the pause flag, and it cannot disagree with what the engine did.
        {
            // Simulated seconds elapsed over THIS frame, READ from the engine rather than
            // estimated from the frame time.
            //
            // The drawn offset advances the platform across the stepping interval to meet the
            // round, so that interval has to be the one the platform sample actually moved
            // over. dtPlayer alone ignores warp, so rounds crawled while the world raced.
            // dtPlayer * SimulationSpeed corrects for warp but is still a guess at what the
            // engine did, and a probe measured the error directly: the assumed step
            // missed the real one by up to 0.9 ms, which against ~29.8 km/s of ecliptic
            // motion is 27 m of misplacement, alternating sign frame to frame. Worst at 0.1x
            // and 2x, and worst of all on the frame the speed changes - where the engine
            // applies one step at the old rate while the estimate has already switched to the
            // new one. That is the jump.
            //
            // GetLastSimStep().DeltaTime is not an approximation of that interval, it is that
            // interval: measured against the platform's own displacement over its own
            // velocity - two independent readings off the same vehicle - it agreed to four
            // decimal places on every frame sampled, at every speed from 0.01x to 4x.
            //
            // An earlier attempt at this was reverted for causing jitter. That jitter was the
            // drawn offset's own phase error, fixed separately in Interceptor.Update - see
            // the offset note in CLAUDE.md - and it was never about the step at all.
            // Consumed, not peeked: the engine answers with the LAST step, so asking again
            // without it having stepped returns the same one - and integrating it a second time
            // adds motion the world never made. See KsaWorld.ConsumeSimStep.
            double dtSim = KsaWorld.ConsumeSimStep();

            // No step reported, no step taken. Do NOT substitute an estimate here.
            //
            // This used to fall back to dtPlayer * SimulationSpeed "so a frame is never wasted",
            // and that is precisely backwards: the engine reports nothing exactly when it
            // advanced nothing, so the fallback integrated the round across an interval the world
            // did not move over. The round's position then gains v * dtEstimate while the
            // platform sample gains zero, and the whole of that difference lands in the drawn
            // offset - a full step of ecliptic motion, from a frame that never happened.
            //
            // Reported from play: pause, select 0.05x, pause again, and the round sits ~20 m to
            // one side. 29800 m/s * (22 ms * 0.05) = 33 m, which is this mechanism at that speed,
            // on the resume frame - the engine has not yet applied a step at the new rate while
            // the estimate has already switched to it.
            //
            // Skipping costs one frame of round motion - under a metre at 0.05x - and nothing
            // here accumulates, so the next frame with a real step recovers it exactly.
            // Zero means the world did not move, which covers a genuine pause as well as any
            // frame the engine chose not to step. Nothing fires into a frozen world because
            // nothing is stepped at all.
            if (double.IsFinite(dtSim) && dtSim > 0.0)
                _battery.Update(Math.Min(dtSim, Interceptor.MaxFaithfulStep));
        }

        // Outside the clock gate on purpose. Placing the round bodies is drawing, not
        // simulating, and it has to happen on every rendered frame or the rounds sit still
        // through any frame that advanced no simulated time while the world moved past
        // them. Cheap, and it only reads state.
        _battery.SyncRoundBodies();
}

    [StarMapUnload]
    public void Unload()
    {
        _battery?.Reset();
        KsaWorld.ResetSimStepTracking();
        _battery = null;
        _ui = null;
        Log.Info("unloaded");
    }

    private void Fault(string where, Exception e)
    {
        _faults++;
        Log.Error($"{where} failed ({_faults}/{FaultLimit})", e);

        if (_faults < FaultLimit) return;

        _disabled = true;
        _battery?.Reset();
        Log.Error("too many faults - air defence disabled for this session");
    }
}
