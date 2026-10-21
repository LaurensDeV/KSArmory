using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

/// <summary>
/// Every direct touch of KSA's internals lives in this class. KSA is pre-release and its API
/// moves, so keeping the surface in one class means a game update breaks one place, not ten. Its
/// partial files are split by concern; this one holds the scene, the clock and the save.
///
/// All positions and velocities are in the ecliptic frame (Ecl): inertial, metres, and the
/// frame both <see cref="Vehicle.GetPositionEcl"/> and gizmo rendering agree on.
/// </summary>
internal static partial class KsaWorld
{
    /// <summary>The vehicle the player is currently flying, or null in menus.</summary>
    public static Vehicle? ControlledVehicle => Program.ControlledVehicle;

    /// <summary>
    /// The player has a craft to fly.
    ///
    /// <para><b>This is not "the flight scene is up", and using it as one strands the camera.</b>
    /// <c>Universe.DestroyVehicle</c> clears <c>ControlledVehicle</c> when the craft being flown is
    /// destroyed, and the scene carries straight on — the engine points the view at the wreckage
    /// and keeps rendering. Anything handing the player's view back must ask
    /// <see cref="InFlightScene"/>, or losing a craft is mistaken for leaving flight and the
    /// hand-back is skipped in the one case that most needs it.</para>
    /// </summary>
    public static bool InFlight => Program.ControlledVehicle is { IsDisposed: false };

    /// <summary>
    /// The flight scene is up, whether or not the player has a craft in it.
    ///
    /// <para>What separates a destroyed craft — still in flight, still one live camera, and
    /// everything the mod borrowed still worth handing back — from actually leaving, which is the
    /// editor or the menus. The new scene brings its own camera, so there the recording describes
    /// something that has gone.</para>
    /// </summary>
    public static bool InFlightScene
    {
        get
        {
            try
            {
                return Program.Editor is null && Universe.CurrentSystem is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The simulated seconds KSA's last step actually advanced the world by.
    ///
    /// <para>This — not the player-time delta StarMap hands the frame hook, and not a
    /// difference of clock samples — is what the system steps on. A paused game reports zero
    /// and a warped one reports the real span, and because it is the step the engine applied
    /// rather than one measured around it, it cannot be a step out of phase with the world.
    /// See <see cref="SimClock"/> for why that distinction is worth tens of metres.</para>
    /// </summary>
    public static double SimStepSeconds => Universe.GetLastSimStep().DeltaTime;

    /// <summary>
    /// The simulated clock, as an absolute number of seconds.
    ///
    /// <para>Nothing integrates against this — <see cref="ConsumeSimStep"/> is the clock the mod
    /// runs on, because a span is what can be integrated and an absolute universe time is far too
    /// large to subtract as a double. This is for comparing epochs, where the question is which
    /// instant a reading belongs to rather than how far anything moved.</para>
    /// </summary>
    public static double SimClockSeconds
    {
        get
        {
            try { return Universe.GetElapsedTime().Seconds(); }
            catch { return double.NaN; }
        }
    }

    // Pure, and in Sim/ so it can be tested. See StepGate.
    private static readonly StepGate<UniverseTime> _stepGate = new();

    /// <summary>
    /// The simulated seconds to integrate now, or zero if the engine has applied no new step
    /// since the last call. <b>Consuming</b> — call once per update and use the result.
    ///
    /// <para><see cref="SimStepSeconds"/> reports the <em>last</em> step, not one since the
    /// previous call, so asking twice without the engine stepping returns it twice. Integrating
    /// it twice adds motion the world never made, and it compounds because it lands in
    /// <c>PositionEcl</c>.</para>
    /// </summary>
    public static double ConsumeSimStep()
    {
        SimStep step = Universe.GetLastSimStep();

        if (_consumedThrough is { } from && !step.NextTime.Equals(from))
        {
            LastLongerOfStepAndSpan = Math.Max(step.DeltaTime, Span(from, step.NextTime));
        }

        _consumedThrough = step.NextTime;

        // The span, not just the step: a frame in which this mod's hook never ran still advanced
        // the world, and GetLastSimStep reports only the most recent one. Taking a screenshot is
        // exactly that case -- ScreenshotCapture sets Program.DrawUI false and KSA guards the call
        // this mod postfixes with it, so the hook is not called at all. Differenced in Int128
        // nanoseconds because absolute universe time is far too large to subtract as double.
        return _stepGate.Consume(step.NextTime, step.DeltaTime, Span);
    }

    private static double Span(UniverseTime from, UniverseTime to)
        => (double)(to.Nanoseconds - from.Nanoseconds) / 1e9;

    private static UniverseTime? _consumedThrough;

    /// <summary>
    /// What the last new step would have been as the longer of the reported step and the span.
    /// Measurement only: <see cref="WarheadTrace"/> sums it beside the step taken, against
    /// <see cref="SimClockNanoseconds"/>, because the longer runs 0.125 ns a frame ahead of the
    /// clock the planets are placed on (<c>docs/ACCURACY-PLAN.md</c> 3el).
    /// </summary>
    public static double LastLongerOfStepAndSpan { get; private set; }

    /// <summary>Universe time in whole nanoseconds, which is the clock KSA places every celestial on.</summary>
    public static Int128 SimClockNanoseconds
    {
        get
        {
            try { return Universe.GetElapsedTime().Nanoseconds; }
            catch { return Int128.MinValue; }
        }
    }

    /// <summary>Forgets which step was last integrated. For unload and scene changes.</summary>
    public static void ResetSimStepTracking()
    {
        _stepGate.Reset();
        _consumedThrough = null;
    }

    /// <summary>True while the simulation is stopped. KSA defines this as speed exactly zero.</summary>
    public static bool IsPaused => Universe.IsPaused();

    /// <summary>Whether KSA is drawing its UI this frame: false while F2 or a screenshot hides it.</summary>
    public static bool UiDrawn => Program.DrawUI;

    /// <summary>Current timewarp factor; 1.0 is real time, 0.0 is paused. Display only.</summary>
    public static double SimulationSpeed => Universe.SimulationSpeed;

    /// <summary>
    /// Slowest speed worth offering. Below this KSA names the speed "paused" — its SimSpeed
    /// constructor calls anything under 1e-4 paused — and a world that runs while every label
    /// says it is stopped is worse than one that will not go slower.
    ///
    /// <para>It is only the *name*: Universe.IsPaused() tests the speed against exactly zero, so
    /// the world really would keep running. That mismatch is the trap, not a limit.</para>
    /// </summary>
    public const double SlowestSimSpeed = 0.001;

    /// <summary>Whether KSA is running its own warp-to-a-time, which it will not be interrupted during.</summary>
    public static bool IsAutoWarpActive => Universe.IsAutoWarpActive;

    /// <summary>
    /// How much of the simulation speed the engine is actually managing, 1.0 when it keeps up.
    ///
    /// <para>The world advances by <c>dtPlayer x this x simSpeed</c>, so it is the whole of what
    /// more work in the world costs in wall clock — see <see cref="SolverLoad"/>. The engine's own
    /// spelling of the name is kept.</para>
    /// </summary>
    public static double AchievedSpeedFraction => Universe.GetAchivedSpeedFraction();

    /// <summary>
    /// What the vehicle solver's slowest recent tick took, in milliseconds.
    ///
    /// <para>The numerator of the fraction above is a deadline of <c>0.9 x min(frame, 1/30)</c>
    /// seconds, so about 30 ms is the budget this has to stay inside.</para>
    /// </summary>
    public static double VehicleSolverTickMs => JobSystems.VehicleSolver?.GetMaxLastTickTime() ?? double.NaN;

    /// <summary>
    /// Ask KSA to warp forward to a moment, stopping a margin short of it.
    ///
    /// <para>The game's own mechanism rather than this mod's. <see cref="WarpPolicy"/> holds the
    /// speed <em>down</em>, which is a different job and one KSA refuses to co-operate with while
    /// an auto-warp is running — so the margin matters: it must be wide enough that the auto-warp
    /// has finished before anything needs the world slow.</para>
    /// </summary>
    public static bool TryAutoWarpTo(double secondsFromNow, double marginSeconds)
    {
        if (!(secondsFromNow > marginSeconds)) return false;
        if (Universe.IsAutoWarpActive) return false;

        try
        {
            Universe.AutoWarpTo(new UniverseTime(Universe.GetElapsedTime().Seconds() + secondsFromNow),
                                marginSeconds);
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not start a warp to the burn window: {e.Message}");
            return false;
        }
    }

    /// <summary>Stop a warp this mod started, for a plan that no longer wants it.</summary>
    public static void StopAutoWarp()
    {
        if (!Universe.IsAutoWarpActive) return;

        try
        {
            Universe.AutoWarpStop(resetSimulationSpeed: true);
        }
        catch (Exception e)
        {
            Log.Warn($"could not stop the warp: {e.Message}");
        }
    }

    /// <summary>
    /// Sets the world's simulation speed, including values slower than the in-game controls
    /// reach. KSA's own roller works in tenths, so 0.1x is as slow as it will go; nothing in
    /// the engine enforces that.
    ///
    /// <para><c>SetSimulationSpeed</c> only rejects speeds above <c>SimSpeed.MaxSpeed</c> — there
    /// is no floor — and it assigns the field directly rather than queuing an input event, so a
    /// value set here holds until something else changes it.</para>
    ///
    /// <para>Everything this mod does is already keyed to simulated time, so a slow world needs
    /// no special handling: the step simply comes back smaller and the system,
    /// the drives and the rounds all scale with it.</para>
    /// </summary>
    /// <returns>False if the value was not finite or not positive; the speed is left alone.</returns>
    public static bool SetSimulationSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0.0) return false;

        Universe.SetSimulationSpeed(new SimSpeed(Math.Max(speed, SlowestSimSpeed)));
        return true;
    }

    /// <summary>
    /// Stops the world, or starts it again at real time.
    ///
    /// <para>Separate from <see cref="SetSimulationSpeed"/>, which refuses anything at or below
    /// zero and clamps to <see cref="SlowestSimSpeed"/>: a caller asking for a slow world and a
    /// caller asking for a stopped one want different things, and a speed argument that silently
    /// becomes a pause is the trap that guard exists to close.</para>
    ///
    /// <para><c>Universe.IsPaused()</c> tests the speed against exactly zero, so this is what makes
    /// that property true — and with it <c>SimClock.Classify</c>'s paused verdict, which is how
    /// everything in this mod stops.</para>
    /// </summary>
    /// <returns>False only if the call threw.</returns>
    public static bool SetPaused(bool paused)
    {
        try
        {
            Universe.SetSimulationSpeed(new SimSpeed(paused ? 0.0 : 1.0));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Where a save keeps its files, or false if it has no folder on disk.</summary>
    public static bool TrySaveFolder(string saveId, out string folder)
    {
        folder = string.Empty;
        if (string.IsNullOrWhiteSpace(saveId)) return false;

        try
        {
            string path = Path.Combine(GameSaves.SaveFolderPath, saveId);
            if (!Directory.Exists(path)) return false;

            folder = path;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// When the open save was last written, as ticks, or 0 if there is none.
    ///
    /// <para>The only way to notice the player saving without patching the engine: StarMap has no
    /// save hook, so the file's own timestamp is the event. Watching it is what lets the mod write
    /// its settings <em>when the game writes</em> rather than continuously — and a continuous
    /// write is what makes reloading a save unable to restore anything, because the file has
    /// already been brought up to date with the session.</para>
    /// </summary>
    public static long CurrentSaveStamp()
    {
        try
        {
            if (CurrentSaveId() is not { Length: > 0 } id) return 0;

            string folder = Path.Combine(GameSaves.SaveFolderPath, id);
            string universe = Path.Combine(folder, "universe.xml");

            if (File.Exists(universe)) return File.GetLastWriteTimeUtc(universe).Ticks;
            return Directory.Exists(folder) ? Directory.GetLastWriteTimeUtc(folder).Ticks : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Which save the player is in, for scoping anything the mod writes down.
    ///
    /// <para>KSA's own save format cannot be extended — <c>UniverseData</c> is a fixed
    /// XML-mapped class with no room for a mod — and StarMap has no save or load hook. So this is
    /// the next best thing: the save's Id, used to key the mod's own file.</para>
    ///
    /// <para><c>GameSaves.Selected</c> is set when a save is picked in the browser, which is what
    /// happens immediately before loading one. It is not a guaranteed "currently loaded" pointer,
    /// and it is empty on a sandbox that was never loaded from a save — hence the caller's
    /// fallback bucket rather than an assumption.</para>
    /// </summary>
    public static string CurrentSaveId()
    {
        try
        {
            if (SaveHook.Installed) return SaveHook.OpenSaveId ?? string.Empty;
            return GameSaves.Selected?.Id ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>The player's frame time, which is what KSA moves a held throttle key by.</summary>
    public static double PlayerDeltaTime => Program.GetPlayerDeltaTime();
}
