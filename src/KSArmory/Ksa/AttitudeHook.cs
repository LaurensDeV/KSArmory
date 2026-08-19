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

    /// <summary>Stop pointing it. The vehicle is the player's again.</summary>
    public static void Release(Vehicle craft) => Wanted.Remove(craft);

    // Runs inside KSA's frame loop, immediately before the flight computer is snapshotted for the
    // worker. Everything here is wrapped, because an exception at this point is not a log line.
    private static void BeforePrepareWorker(Vehicle __instance)
    {
        try
        {
            if (Wanted.Count == 0) return;
            if (!Wanted.TryGetValue(__instance, out Aim aim)) return;

            VehicleCommand.TryAim(__instance, aim.DirectionCci, aim.RollReferenceCci);
        }
        catch (Exception e)
        {
            // Stand down rather than throwing again next frame. One report, then silence.
            Wanted.Remove(__instance);

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
