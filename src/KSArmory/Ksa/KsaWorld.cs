using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

/// <summary>
/// Most of the mod's contact with KSA, so a game update breaks one place; split by concern across
/// partial files. All positions and velocities are in the ecliptic frame (Ecl): inertial, metres, and the
/// frame both <see cref="Vehicle.GetPositionEcl"/> and gizmo rendering agree on.
/// </summary>
internal static partial class KsaWorld
{
    /// <summary>The vehicle the player is currently flying, or null in menus.</summary>
    public static Vehicle? ControlledVehicle => Program.ControlledVehicle;

    /// <summary>
    /// The player has a craft to fly.
    ///
    /// <para><b>This is not "the flight scene is up".</b> <c>Universe.DestroyVehicle</c> clears
    /// <c>ControlledVehicle</c> and the scene carries on, so anything handing the player's view back
    /// must ask <see cref="InFlightScene"/>.</para>
    /// </summary>
    public static bool InFlight => Program.ControlledVehicle is { IsDisposed: false };

    /// <summary>
    /// The flight scene is up, whether or not the player has a craft in it.
    ///
    /// <para>False in the editor or the menus, which bring their own camera.</para>
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
    /// <para>Not the player-time delta StarMap hands the frame hook: a paused game reports zero and
    /// a warped one the real span. See <see cref="SimClock"/>.</para>
    /// </summary>
    public static double SimStepSeconds => Universe.GetLastSimStep().DeltaTime;

    /// <summary>
    /// The simulated clock, as an absolute number of seconds.
    ///
    /// <para>For comparing epochs, never for integrating: <see cref="ConsumeSimStep"/> is the clock the
    /// mod runs on, since an absolute universe time is too large to subtract as a double.</para>
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
    /// <para><see cref="SimStepSeconds"/> reports the <em>last</em> step, so asking twice without the
    /// engine stepping returns it twice, and integrating it twice adds motion the world never made.</para>
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
        // the world, and GetLastSimStep reports only the most recent one. Differenced in Int128
        // nanoseconds because absolute universe time is too large to subtract as double.
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
    /// Slowest speed worth offering: KSA's SimSpeed names anything under 1e-4 "paused", while
    /// <c>Universe.IsPaused()</c> tests against exactly zero, so the world would keep running.
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
    /// <para>KSA refuses <see cref="WarpPolicy"/>'s speed changes while an auto-warp runs, so the
    /// margin must let it finish before anything needs the world slow.</para>
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
    /// Sets the world's simulation speed, including values below the in-game roller's 0.1x.
    ///
    /// <para><c>SetSimulationSpeed</c> only rejects speeds above <c>SimSpeed.MaxSpeed</c>, and
    /// assigns the field directly rather than queuing an input event.</para>
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
    /// <para>Separate from <see cref="SetSimulationSpeed"/>, which refuses zero and clamps to
    /// <see cref="SlowestSimSpeed"/>. <c>Universe.IsPaused()</c> tests the speed against exactly
    /// zero, and <c>SimClock.Classify</c> reads it.</para>
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
    /// <para>The fallback for noticing a save when <see cref="SaveHook"/> is not installed. Settings
    /// are written when the game writes, never continuously, or a reload has nothing older to restore.</para>
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
    /// <para><see cref="SaveHook"/>'s record where it is installed. Otherwise
    /// <c>GameSaves.Selected</c>, which is only the row highlighted in the save list, and empty on a
    /// sandbox never loaded from a save.</para>
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
