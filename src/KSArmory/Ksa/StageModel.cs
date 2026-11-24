using System.Collections.Generic;
using System.Diagnostics;
using KSA;

namespace KSArmory;

/// <summary>
/// A computer-flown craft's stage performance recomputed by the mod. KSA recomputes it only for the controlled craft,
/// and only while its staging list or engine-control gauge is open (<c>PhysicsBubble.RunVehiclePostWorkInner</c>), so
/// every other rocket reads an empty list, or a stale one.
/// <para>Run from <see cref="AttitudeHook"/>'s prefix: <c>PrepareWorker</c> is called on the main thread after the last
/// vehicle job is joined and before the next is queued, so no worker reads the list while it is rebuilt.</para>
/// </summary>
internal static class StageModel
{
    // Wall clock, because under warp a sim-time gate runs every step.
    private const long RefreshMilliseconds = 250;
    private const long ForgetMilliseconds = 2000;
    private const long ReportMilliseconds = 10000;

    private sealed class Entry
    {
        public long WantedAt;
        public long RefreshedAt = -RefreshMilliseconds;
        public long ReportedAt = -ReportMilliseconds;
        public double WorstMs;
        public int Refreshes;
    }

    private static readonly Dictionary<Vehicle, Entry> Craft = [];
    private static readonly List<Vehicle> Gone = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>Keep this craft's stage performance current. Stated every frame; a craft not stated for two seconds is let go.</summary>
    public static void Want(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;
        if (!Craft.TryGetValue(craft, out Entry? entry)) Craft[craft] = entry = new Entry();
        entry.WantedAt = Clock.ElapsedMilliseconds;
    }

    public static void Clear() => Craft.Clear();

    // From the prefix; nothing here may throw.
    internal static void Apply(Vehicle craft)
    {
        if (Craft.Count == 0 || !Craft.TryGetValue(craft, out Entry? entry)) return;

        long now = Clock.ElapsedMilliseconds;
        if (now - entry.WantedAt > ForgetMilliseconds)
        {
            Craft.Remove(craft);
            return;
        }

        SequencePerformanceList list;
        try
        {
            list = craft.Parts.PerformanceSequences;
        }
        catch
        {
            return;
        }

        // A staging marks the list dirty, and the countdown wants the new stage the frame it lights.
        if (!list.IsDirty && now - entry.RefreshedAt < RefreshMilliseconds) return;
        entry.RefreshedAt = now;

        try
        {
            float stackBefore = list.TotalDeltaV;
            float stageBefore = list.PerformanceSequences.IsEmpty ? float.NaN : list.FindActiveSequenceDeltaV();
            float ambient = craft.Parent is Celestial body ? KsaWorld.AtmosphericPressureAt(body, KsaWorld.PositionEcl(craft)) : 0f;

            long start = Stopwatch.GetTimestamp();
            list.RecomputeForFlight(ambient);
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            entry.Refreshes++;
            entry.WorstMs = Math.Max(entry.WorstMs, ms);

            if (now - entry.ReportedAt < ReportMilliseconds) return;
            entry.ReportedAt = now;

            Log.Info($"stage model on {KsaWorld.DisplayName(craft)}: stack {stackBefore:F0} -> {list.TotalDeltaV:F0} m/s, "
                     + $"running stage {stageBefore:F0} -> {list.FindActiveSequenceDeltaV():F0} m/s, "
                     + $"{list.PerformanceSequences.Length} sequences, recompute {ms:F3} ms (worst {entry.WorstMs:F3} over "
                     + $"{entry.Refreshes}), ambient {ambient:F0} Pa");
        }
        catch (Exception e)
        {
            Craft.Remove(craft);
            Log.Warn($"stage model on {KsaWorld.DisplayName(craft)} could not be recomputed, left to KSA: {e.Message}");
        }
    }

    /// <summary>
    /// Every burn the craft has left, as <c>SequencePerformanceList.FillStagedBurn</c> lays them out, the running one on
    /// the engines' live figures; null when the list is empty or cannot be read.
    /// </summary>
    public static StagePlan? Plan(Vehicle craft, in BoosterPerformance live)
    {
        try
        {
            List<StagePhase> phases = [];
            // From index 0: a spent sequence has no phases, which skips it.
            foreach (ref readonly SequencePerformance sequence in craft.Parts.PerformanceSequences.PerformanceSequences)
            {
                if (sequence.Phases is not { } info) continue;

                double mass = sequence.WetMass;
                bool startsStage = true;
                for (int i = 0; i < info.Count; i++)
                {
                    SequencePhaseInfo phase = info[i];
                    if (!(phase.Duration > 0f && phase.Thrust > 0f && phase.MassFlowRate > 0f)) continue;

                    HashSet<Part>? engines = sequence.PhaseEngineParts is { } parts && i < parts.Count ? parts[i] : null;
                    (bool solid, double floor) = Engines(engines);
                    phases.Add(new StagePhase(phase.Thrust, phase.MassFlowRate, mass, phase.Duration, solid, floor, startsStage));
                    mass -= (double)phase.MassFlowRate * phase.Duration;
                    startsStage = false;
                }
            }

            StagePlan plan = new(phases);
            return plan.IsEmpty ? null : plan.WithRunning(live);
        }
        catch
        {
            return null;
        }
    }

    // Any solid makes the phase uncuttable; the floor is the lowest any of its engines holds.
    private static (bool Solid, double MinThrottle) Engines(HashSet<Part>? parts)
    {
        if (parts is null) return (false, 0.0);

        bool solid = false;
        double floor = double.PositiveInfinity;
        foreach (Part part in parts)
        {
            Span<EngineController> engines = part.Modules.Get<EngineController>();
            for (int i = 0; i < engines.Length; i++)
            {
                floor = Math.Min(floor, engines[i].MinimumThrottle);
                foreach (Rocket rocket in engines[i].Rockets)
                {
                    foreach (RocketCore core in rocket.Cores) solid |= core is SolidMotor;
                }
            }
        }
        return (solid, double.IsFinite(floor) ? floor : 0.0);
    }

    // Craft the census no longer holds, so a destroyed vehicle is not kept reachable.
    public static void Prune()
    {
        if (Craft.Count == 0) return;
        Gone.Clear();
        foreach (Vehicle craft in Craft.Keys)
        {
            if (!KsaWorld.IsAlive(craft)) Gone.Add(craft);
        }
        foreach (Vehicle craft in Gone) Craft.Remove(craft);
    }
}
