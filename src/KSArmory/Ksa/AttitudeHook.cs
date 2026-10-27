using System.Collections.Generic;
using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// The only window in which a write to a vehicle's flight computer survives.
/// <para>KSA double-buffers the flight computer: <c>ApplyVehicleSolvers</c> writes the worker's result
/// over it and <c>ExecuteNextVehicleSolvers</c> snapshots it before the GUI pass, so a write from any
/// StarMap hook is overwritten unread. A prefix on <see cref="Vehicle.PrepareWorker"/> runs just before
/// the snapshot. <c>docs/KSA-FRAME-ORDER.md</c> has the order.</para>
/// <para>Nothing in the prefix may throw: it runs inside the engine's frame loop.</para>
/// </summary>
internal static class AttitudeHook
{
    /// <summary>What a vehicle should be pointed at, until told otherwise.</summary>
    internal readonly record struct Aim(double3 DirectionCci, double3 RollReferenceCci, bool Loosely);

    private const string HarmonyId = "com.kesslersystems.ksarmory.attitude";

    private static readonly Dictionary<Vehicle, Aim> Wanted = [];

    // Craft whose manual thrust is in the engine's pulse mode this frame. Held here rather than
    // written where the trim decides it, because the flight computer is copied over wholesale when
    // a worker's results are applied -- the same reason the attitude is written from this window.
    private static readonly HashSet<Vehicle> Pulsing = [];

    // Every craft the mod has put in pulse mode and not yet handed back, pulsing this frame or not:
    // the mode is on the double-buffered computer, so one not restated stays in pulse mode for good
    // and a later hold delivers a millisecond a frame. docs/ACCURACY-PLAN.md 3cu.
    private static readonly HashSet<Vehicle> Pulsed = [];

    // Those owed one more Direct write, which has to happen in this window like every other one.
    private static readonly HashSet<Vehicle> Restore = [];

    // Craft drifting on purpose, held out of KSA's own attitude hold every frame: left holding, a stack warped 11 h to
    // its burn window fired the bus's jets through the warp and emptied them, about 19 kg an hour, flown.
    private static readonly HashSet<Vehicle> Drifting = [];

    // Craft owed a stage. Staging rebuilds the sequence list the vehicle worker reads, and outside
    // this window that worker may be running: with the engine-control gauge or the staging list open
    // KSA recomputes the controlled craft's stage performance off the main thread, and a list rebuilt
    // under it throws "Update task failed" out of SequencePerformanceList.Recompute.
    private static readonly HashSet<Vehicle> Staging = [];

    // Craft owed a stage only once a move of the staging list has gone through, in the same window.
    private static readonly Dictionary<Vehicle, Func<bool>> StagingAfter = [];

    // Craft owed a blast's push, as an impulse and an angular impulse about the centre of mass in
    // the craft's assembly frame. Written here for the reason attitude is: a velocity written from
    // any other hook is overwritten by the worker's results before anything reads it.
    private static readonly Dictionary<Vehicle, (double3 Linear, double3 Angular, double Wind, double3 AwayEcl)> Shoves = [];

    // Which way a craft's throttle key is held this frame, for the frames KSA clears held keys on the craft being flown.
    private static readonly Dictionary<Vehicle, int> ThrottleKeys = [];

    // Crafts whose held translation jets are put back after KSA clears them, and what was held this frame.
    private static readonly HashSet<Vehicle> JetsThroughTheClear = [];
    private static readonly Dictionary<Vehicle, ThrusterMapFlags> HeldJets = [];

    private const ThrusterMapFlags Translation = ThrusterMapFlags.TranslateForward | ThrusterMapFlags.TranslateBackward
                                                 | ThrusterMapFlags.TranslateRight | ThrusterMapFlags.TranslateLeft
                                                 | ThrusterMapFlags.TranslateDown | ThrusterMapFlags.TranslateUp;

    // Shoves read back two steps later, once the worker that integrated them has been applied, since
    // a frame wrong in the conversion shows as a craft thrown the wrong way; then traced a few seconds.
    private sealed class Probe
    {
        public required Vehicle Craft;
        public required double3 Before;
        public required double3 WrittenEcl;
        public required double3 AwayEcl;
        public int Steps;
        public double Seconds;
        public double NextTrace;
    }

    private static readonly List<Probe> Probes = [];

    private const double TraceSeconds = 5.0;
    private const double TraceEverySeconds = 0.25;

    private static Harmony? _harmony;
    private static bool _complained;

    /// <summary>Whether the patch is in place. False means nothing this mod does can steer.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Why it is not installed, for the panel to show.</summary>
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

            MethodInfo postfix = typeof(AttitudeHook).GetMethod(
                nameof(AfterPrepareWorker), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));

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
        Staging.Clear();
        StagingAfter.Clear();
        Shoves.Clear();

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
    public static void Hold(Vehicle craft, double3 directionCci, double3 rollReferenceCci, bool loosely = false)
    {
        if (!KsaWorld.IsAlive(craft)) return;
        Drifting.Remove(craft);
        Wanted[craft] = new Aim(directionCci, rollReferenceCci, loosely);
    }

    /// <summary>Hold no attitude at all, KSA's own included, every frame until <see cref="Hold"/> or <see cref="StopDrifting"/>.</summary>
    public static void Drift(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;
        Wanted.Remove(craft);
        Drifting.Add(craft);
    }

    public static void StopDrifting(Vehicle craft) => Drifting.Remove(craft);

    /// <summary>
    /// Fire this craft's translation in pulses this frame, or hold it directly. Stated every frame like
    /// the aim, since it lives on the same double-buffered flight computer.
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

    /// <summary>
    /// Hold a craft's throttle key up (+1), down (-1) or neither (0) through KSA's clear of held keys, which it makes on
    /// the craft being flown while the UI has the keyboard. Elsewhere the key itself is what moves the throttle.
    /// </summary>
    public static void ThrottleKey(Vehicle craft, int direction)
    {
        if (direction == 0) ThrottleKeys.Remove(craft);
        else ThrottleKeys[craft] = direction;
    }

    /// <summary>
    /// Keep a craft's held translation jets through KSA's clear of held keys, as <see cref="ThrottleKey"/> keeps its
    /// throttle moving: the clear zeroes them inside <c>PrepareWorker</c>, after the prefix.
    /// </summary>
    public static void KeepJetsThroughTheClear(Vehicle craft, bool keep)
    {
        if (keep) JetsThroughTheClear.Add(craft);
        else JetsThroughTheClear.Remove(craft);
    }

    /// <summary>
    /// Fire this craft's next stage when its worker is next prepared, if <paramref name="first"/> succeeds there first --
    /// a change to the staging list has to be made in the same window the stage is.
    /// </summary>
    public static void StageAfter(Vehicle craft, Func<bool> first)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        if (!Installed)
        {
            if (first()) VehicleCommand.Stage(craft);
            return;
        }

        StagingAfter[craft] = first;
    }

    /// <summary>
    /// Fire this craft's next stage when its worker is next prepared. Staged directly when the hook
    /// is not installed, because a stage that never fires is worse than one that can race.
    /// </summary>
    public static void Stage(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        if (!Installed)
        {
            VehicleCommand.Stage(craft);
            return;
        }

        Staging.RemoveWhere(v => !KsaWorld.IsAlive(v));
        Staging.Add(craft);
    }

    /// <summary>
    /// Push this craft when its worker is next prepared: <paramref name="impulseAsmb"/> in newton
    /// seconds and <paramref name="angularImpulseAsmb"/> about its centre of mass, both in its own
    /// assembly frame, by a wind of <paramref name="windSpeed"/> it cannot be thrown faster than.
    /// Dropped when the hook is not installed, because written anywhere else it is overwritten before
    /// the physics reads it.
    /// </summary>
    public static void Shove(Vehicle craft, double3 impulseAsmb, double3 angularImpulseAsmb, double windSpeed,
                             double3 awayFromBurstEcl)
    {
        if (!Installed || !KsaWorld.IsAlive(craft)) return;
        if (!Vec.IsFinite(impulseAsmb) || !Vec.IsFinite(angularImpulseAsmb)) return;

        Shoves.TryGetValue(craft, out (double3 Linear, double3 Angular, double Wind, double3 AwayEcl) owed);
        Shoves[craft] = (owed.Linear + impulseAsmb, owed.Angular + angularImpulseAsmb, Math.Max(owed.Wind, windSpeed),
                         awayFromBurstEcl);
    }

    /// <summary>
    /// Reads back the shoves written two steps ago -- how the craft's velocity changed against what
    /// was written, gravity over those steps included -- and then traces the craft for a few seconds:
    /// its speed over the ground, how steeply it is climbing, its height and how fast it is turning.
    /// </summary>
    public static void CheckShoves(double dt)
    {
        const double deg = 180.0 / Math.PI;

        for (int i = Probes.Count - 1; i >= 0; i--)
        {
            Probe probe = Probes[i];
            Vehicle craft = probe.Craft;
            if (!KsaWorld.IsAlive(craft))
            {
                Probes.RemoveAt(i);
                continue;
            }

            probe.Steps++;
            if (probe.Steps < 2) continue;

            double3 up = KsaWorld.LocalUp(craft);
            if (probe.Steps == 2)
            {
                double3 change = KsaWorld.VelocityEcl(craft) - probe.Before;
                Log.Info($"shove read back on {KsaWorld.DisplayName(craft)}: velocity changed by {Vec.Len(change):F2} m/s, "
                         + $"{Vec.AngleBetween(change, probe.WrittenEcl) * deg:F1} deg from the {Vec.Len(probe.WrittenEcl):F2} m/s written, "
                         + $"{Vec.AngleBetween(change, probe.AwayEcl) * deg:F1} deg from straight away from the burst, "
                         + $"{90.0 - (Vec.AngleBetween(change, up) * deg):F1} deg above the horizon "
                         + $"(the burst's line {90.0 - (Vec.AngleBetween(probe.AwayEcl, up) * deg):F1})");
            }

            probe.Seconds += Math.Max(dt, 0.0);
            if (probe.Seconds < probe.NextTrace) continue;
            probe.NextTrace += TraceEverySeconds;

            double3 positionEcl = KsaWorld.PositionEcl(craft);
            double3 overGround = KsaWorld.VelocityEcl(craft) - KsaWorld.GroundVelocityAt(craft, positionEcl);
            double height = Detonation.BodyFor(craft) is { } body ? KsaWorld.HeightAboveTerrain(body, positionEcl) : double.NaN;
            double turning = 0.0;
            try { turning = Vec.Len(craft.BodyRates) * deg; } catch { /* Reported as none. */ }

            Log.Info($"  shove trace {probe.Seconds:F2} s: {Vec.Len(overGround):F1} m/s over the ground, "
                     + $"climbing at {90.0 - (Vec.AngleBetween(overGround, up) * deg):F1} deg, "
                     + $"{height:F0} m up, turning at {turning:F0} deg/s");

            if (probe.Seconds >= TraceSeconds) Probes.RemoveAt(i);
        }
    }

    /// <summary>Stop pointing it. The vehicle is the player's again.</summary>
    public static void Release(Vehicle craft)
    {
        Wanted.Remove(craft);
        Drifting.Remove(craft);

        // Left in pulse mode, the player's own translation keys would fire in millisecond taps. The
        // write is owed to the prefix rather than made here, because one made outside that window is
        // copied over before anything reads it.
        Pulsing.Remove(craft);
        if (Pulsed.Remove(craft)) Restore.Add(craft);
    }

    // After the clear and after the worker's copy is taken, so the jets go into both.
    private static void AfterPrepareWorker(Vehicle __instance)
    {
        try
        {
            if (HeldJets.Count > 0 && HeldJets.Remove(__instance, out ThrusterMapFlags jets) && jets != ThrusterMapFlags.None)
            {
                KsaWorld.TryRestoreJets(__instance, jets);
            }
        }
        catch (Exception e)
        {
            Log.Error("held jets could not be put back", e);
        }
    }

    // Runs inside KSA's frame loop, immediately before the flight computer is snapshotted for the
    // worker. Everything here is wrapped, because an exception at this point is not a log line.
    private static void BeforePrepareWorker(Vehicle __instance)
    {
        try
        {
            if (Staging.Count > 0 && Staging.Remove(__instance)) VehicleCommand.Stage(__instance);
            if (StagingAfter.Count > 0 && StagingAfter.Remove(__instance, out Func<bool>? first) && first())
            {
                VehicleCommand.Stage(__instance);
            }

            if (JetsThroughTheClear.Count > 0 && JetsThroughTheClear.Contains(__instance)
                && KsaWorld.DiscardsHeldControls(__instance, out _))
            {
                HeldJets[__instance] = __instance.GetThrusterFlags() & Translation;
            }

            // The clear runs inside PrepareWorker after this and takes the held keys, never the throttle value, so the
            // step the key would have made is made here, at KSA's own 0.7 a second of the player's frame.
            if (ThrottleKeys.Count > 0 && ThrottleKeys.TryGetValue(__instance, out int key)
                && KsaWorld.DiscardsHeldControls(__instance, out _))
            {
                KsaWorld.TryNudgeThrottle(__instance, key * KsaWorld.PlayerDeltaTime * 0.7);
            }

            // Tank contents are written here for the same reason attitude is: anywhere else the
            // worker's copy goes over them.
            Leaks.Apply(__instance);

            // And a launcher's mass, lighter by what it has fired, for the same reason.
            StoreMass.Apply(__instance);

            if (Shoves.Count > 0 && Shoves.Remove(__instance, out (double3 Linear, double3 Angular, double Wind, double3 AwayEcl) shove))
            {
                double3 before = KsaWorld.VelocityEcl(__instance);
                if (VehicleCommand.TryShove(__instance, shove.Linear, shove.Angular, shove.Wind,
                                            out double3 dv, out double3 dw))
                {
                    Probes.Add(new Probe
                    {
                        Craft = __instance,
                        Before = before,
                        WrittenEcl = KsaWorld.VehicleAsmbDirectionToEcl(__instance, dv),
                        AwayEcl = shove.AwayEcl,
                    });
                    Log.Info($"the blast wind pushed {KsaWorld.DisplayName(__instance)} by "
                             + $"{Vec.Len(dv):F2} m/s and set it turning at {Vec.Len(dw) * 180.0 / Math.PI:F2} deg/s, "
                             + $"in a {shove.Wind:F0} m/s wind");
                }
            }

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

            if (Drifting.Count > 0 && Drifting.Contains(__instance))
            {
                if (VehicleCommand.HoldsAttitude(__instance)) VehicleCommand.ReleaseAttitude(__instance);
                return;
            }

            if (Wanted.Count == 0) return;
            if (!Wanted.TryGetValue(__instance, out Aim aim)) return;

            VehicleCommand.TryAim(__instance, aim.DirectionCci, aim.RollReferenceCci, aim.Loosely);
        }
        catch (Exception e)
        {
            // Stand down rather than throwing again next frame. One report, then silence.
            Wanted.Remove(__instance);
            Pulsing.Remove(__instance);
            Pulsed.Remove(__instance);
            Restore.Remove(__instance);
            Staging.Remove(__instance);
            StagingAfter.Remove(__instance);
            Shoves.Remove(__instance);

            if (_complained) return;
            _complained = true;
            Log.Error("attitude hook failed; that craft is no longer being pointed", e);
        }
    }

    // Never called: puts the patched method in docs/KSA-API-SURFACE.md, so a signature change is a build error.
    private static void PinTheSignature(Vehicle vehicle, SimStep step) => vehicle.PrepareWorker(step);
}
