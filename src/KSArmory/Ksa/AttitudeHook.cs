using System.Collections.Generic;
using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// The one place this mod patches the game, and the only way to point a vehicle at all.
///
/// <para>KSA double-buffers a vehicle's flight computer across the frame.
/// <c>ApplyVehicleSolvers</c> writes the worker's result over it, <c>ExecuteNextVehicleSolvers</c>
/// snapshots it for the next worker, and <em>then</em> the GUI pass runs — so an attitude command
/// written from any StarMap hook is not in the snapshot, and next frame's result overwrites it. The
/// write lands and is gone before anything reads it. Measured in flight over thousands of frames:
/// <c>before Manual/None -&gt; after Auto/Custom</c>, every single one, with the engine's own error
/// angles at zero because it is tracking nothing. <c>docs/BLOCKED-ON-KSA.md</c> has the frame
/// order.</para>
///
/// <para><b>So the command has to be written between those two calls</b>, and
/// <see cref="Vehicle.PrepareWorker"/> is the only thing in that window a mod can reach. A prefix
/// on it runs immediately before the snapshot is taken.</para>
///
/// <para><b>Why this is a far weaker thing than patching usually is.</b> The target is
/// <c>public virtual</c> rather than private, so it is declared API — <see cref="PinTheSignature"/>
/// puts it in this assembly's metadata, which means <c>tools/api-surface.sh</c> tracks it and a KSA
/// change to it is a build error rather than a silent break. That is the property
/// <c>CLAUDE.md</c>'s rule against patching exists to protect. Harmony itself ships with StarMap,
/// so nothing here asks a player to install anything.</para>
///
/// <para><b>Nothing in the prefix may throw.</b> It runs inside the engine's own frame loop, where
/// an exception is the game rather than a log line — the same rule
/// <see cref="LevelHorizonController"/> follows.</para>
/// </summary>
internal static class AttitudeHook
{
    /// <summary>What a vehicle should be pointed at, until told otherwise.</summary>
    internal readonly record struct Aim(double3 DirectionCci, double3 RollReferenceCci);

    private const string HarmonyId = "com.kesslersystems.ksarmory.attitude";

    private static readonly Dictionary<Vehicle, Aim> Wanted = [];

    // Craft whose attitude is actively cancelled each frame rather than pointed.
    private static readonly HashSet<Vehicle> Quieted = [];

    // Those whose quiet should also assert rails. A subset of Quieted rather than a parallel state:
    // any commanded actuator flips rails straight back off, so asserting it anywhere else would be
    // a write the next sub-step undoes.
    private static readonly HashSet<Vehicle> Railed = [];

    // Craft whose manual thrust is in the engine's pulse mode this frame. Held here rather than
    // written where the trim decides it, because the flight computer is copied over wholesale when
    // a worker's results are applied -- the same reason the attitude is written from this window.
    private static readonly HashSet<Vehicle> Pulsing = [];

    // Every craft the mod has put in pulse mode and not yet handed back, whether or not it is
    // pulsing this frame. The mode is a field on that same double-buffered computer, so what is not
    // restated each frame keeps whatever it last had: writing it only while something is pulsing
    // leaves the last one in pulse mode for good, and its next HOLD then delivers a millisecond a
    // frame against metres per second. Flown as a tail frozen at 3.48 m/s across 12 s of holding,
    // struck off as a dead thruster, and released 0.3-1.1 km out. docs/ACCURACY-PLAN.md 3cu.
    private static readonly HashSet<Vehicle> Pulsed = [];

    // Those owed one more Direct write, which has to happen in this window like every other one.
    private static readonly HashSet<Vehicle> Restore = [];

    private static Harmony? _harmony;
    private static bool _complained;

    /// <summary>Whether the patch is in place. False means nothing this mod does can steer.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Why it is not installed, for the panel to show rather than leaving it a mystery.</summary>
    public static string Trouble { get; private set; } = "";

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = typeof(Vehicle).GetMethod(
                nameof(Vehicle.PrepareWorker), [typeof(SimStep)]);

            if (target is null)
            {
                Trouble = "KSA has no Vehicle.PrepareWorker(SimStep) to hook";
                Log.Warn($"attitude control unavailable: {Trouble}");
                return;
            }

            MethodInfo prefix = typeof(AttitudeHook).GetMethod(
                nameof(BeforePrepareWorker), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));

            Installed = true;
            Trouble = "";
            Log.Info("attitude control hooked into Vehicle.PrepareWorker");
        }
        catch (Exception e)
        {
            Trouble = e.Message;
            Log.Error("could not hook attitude control; the ballistic computer cannot fly", e);
        }
    }

    public static void Remove()
    {
        Wanted.Clear();

        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove the attitude hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    /// <summary>Point this craft here, every frame, until <see cref="Release"/>.</summary>
    public static void Hold(Vehicle craft, double3 directionCci, double3 rollReferenceCci)
    {
        if (!KsaWorld.IsAlive(craft)) return;
        Wanted[craft] = new Aim(directionCci, rollReferenceCci);
    }

    /// <summary>
    /// Actively stop the flight computer tracking, every frame, until <see cref="Release"/>.
    ///
    /// <para>Not the same as <see cref="Release"/>. Dropping the standing aim only stops this mod
    /// <em>writing</em> a target; the computer keeps the one it has and goes on firing thrusters to
    /// hold it, which keeps <c>anyActuatorCommanded</c> set and the vehicle off rails. Measured:
    /// a craft with <c>aimed=False</c> still read <c>Auto/Custom</c> and still spent 276 of 376
    /// coast probes off rails, against the pointed arm's 282. Cancelling has to be a write, and it
    /// has to happen in this window like every other one.</para>
    /// </summary>
    public static void Quiet(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        Wanted.Remove(craft);
        Quieted.Add(craft);
    }

    /// <summary>
    /// Assert rails as well as going quiet, so the coast is propagated rather than integrated.
    ///
    /// <para>Only meaningful alongside <see cref="Quiet"/>, and refused otherwise: a commanded
    /// actuator puts the vehicle off rails on the same sub-step, so asserting it while anything is
    /// still pointing is a write undone immediately.</para>
    /// </summary>
    public static void QuietOnRails(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        Quiet(craft);
        Railed.Add(craft);
    }

    /// <summary>
    /// Fire this craft's translation in pulses this frame, or hold it directly.
    ///
    /// <para>Stated every frame like the aim, because it is the same field on the same double-buffered
    /// flight computer: what is not restated is copied back to <c>Direct</c> by the next worker's
    /// results.</para>
    /// </summary>
    public static void PulseMode(Vehicle craft, bool pulsing)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        if (pulsing)
        {
            Pulsing.Add(craft);
            Pulsed.Add(craft);
            Restore.Remove(craft);
            return;
        }

        // Kept in Pulsed rather than dropped: the mode still has to be written back to Direct every
        // frame, and a craft that has stopped pulsing is exactly the one that would otherwise keep it.
        Pulsing.Remove(craft);
    }

    /// <summary>Stop pointing it, and stop quieting it. The vehicle is the player's again.</summary>
    public static void Release(Vehicle craft)
    {
        Wanted.Remove(craft);
        Quieted.Remove(craft);
        Railed.Remove(craft);

        // Left in pulse mode, the player's own translation keys would fire in millisecond taps. The
        // write is owed to the prefix rather than made here, because one made outside that window is
        // copied over before anything reads it.
        Pulsing.Remove(craft);
        if (Pulsed.Remove(craft)) Restore.Add(craft);
    }

    // Runs inside KSA's frame loop, immediately before the flight computer is snapshotted for the
    // worker. Everything here is wrapped, because an exception at this point is not a log line.
    private static void BeforePrepareWorker(Vehicle __instance)
    {
        try
        {
            // Before everything else, and whatever else this craft is doing: the mode decides how a
            // translation command already standing is spent, and it is restated every frame.
            if (Pulsed.Contains(__instance))
            {
                VehicleCommand.SetPulseMode(__instance, Pulsing.Contains(__instance));
            }
            else if (Restore.Remove(__instance))
            {
                VehicleCommand.SetPulseMode(__instance, pulsing: false);
            }

            if (Quieted.Contains(__instance))
            {
                VehicleCommand.ReleaseAttitude(__instance);

                // Releasing the actuator is necessary and not sufficient. PhysicsStates'
                // TryToPutOnRails returns a coasting vehicle to rails only when the bubble origin
                // is Cci, and in a Ccf bubble there is no path back at all -- so a bus quieted
                // inside one stays integrated for the rest of the coast, which is what 3ay
                // measured as 276 of 376 probes off rails against a pointed arm's 282.
                //
                // Asserting rails is the other half. It does not leave the bubble; it makes the
                // bubble irrelevant, because a rails Freefall vehicle takes ApplyFreefallMotion and
                // an exact conic whatever the frame. docs/ACCURACY-PLAN.md 3bv.
                if (Railed.Contains(__instance)) VehicleCommand.TryAssertRails(__instance);

                return;
            }

            if (Wanted.Count == 0) return;
            if (!Wanted.TryGetValue(__instance, out Aim aim)) return;

            VehicleCommand.TryAim(__instance, aim.DirectionCci, aim.RollReferenceCci);
        }
        catch (Exception e)
        {
            // Stand down rather than throwing again next frame. One report, then silence.
            Wanted.Remove(__instance);
            Quieted.Remove(__instance);
            Railed.Remove(__instance);
            Pulsing.Remove(__instance);
            Pulsed.Remove(__instance);
            Restore.Remove(__instance);

            if (_complained) return;
            _complained = true;
            Log.Error("attitude hook failed; that craft is no longer being pointed", e);
        }
    }

    // Never called. It exists so the compiler emits a reference to the patched method, which is
    // what puts Vehicle.PrepareWorker in docs/KSA-API-SURFACE.md and turns a signature change in
    // KSA into a build error here rather than a rocket that quietly stops steering.
    private static void PinTheSignature(Vehicle vehicle, SimStep step) => vehicle.PrepareWorker(step);
}
