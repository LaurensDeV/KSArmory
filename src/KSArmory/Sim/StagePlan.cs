using System.Collections.Generic;

namespace KSArmory;

/// <summary>One stretch of constant thrust and mass flow in a <see cref="StagePlan"/>.</summary>
/// <param name="Solid">Burns to the end once lit, so reaching this phase spends all of it.</param>
/// <param name="MinThrottle">The least throttle its engines hold, or zero if the caller cannot tell.</param>
/// <param name="StartsStage">The first phase of a sequence, as opposed to engines burning out inside one.</param>
internal readonly record struct StagePhase(
    double ThrustNewtons,
    double MassFlowKgPerSec,
    double StartMassKg,
    double DurationSeconds,
    bool Solid = false,
    double MinThrottle = 0.0,
    bool StartsStage = true)
{
    public double ExhaustVelocity => MassFlowKgPerSec > 0.0 ? ThrustNewtons / MassFlowKgPerSec : 0.0;

    public double PropellantKg => MassFlowKgPerSec * DurationSeconds;

    public double EndMassKg => StartMassKg - PropellantKg;

    public double DeltaV => ExhaustVelocity * Math.Log(StartMassKg / EndMassKg);

    public bool IsUsable => ThrustNewtons > 0.0 && MassFlowKgPerSec > 0.0 && DurationSeconds > 0.0
                            && double.IsFinite(DurationSeconds) && EndMassKg > 0.0 && double.IsFinite(StartMassKg);

    /// <summary>How long this phase's law takes to gain <paramref name="deltaV"/>, carried past its end if need be.</summary>
    public double SecondsToGain(double deltaV)
        => StartMassKg / MassFlowKgPerSec * (1.0 - Math.Exp(-deltaV / ExhaustVelocity));

    /// <summary>Distance along the thrust over what a coast covers, from this phase's start.</summary>
    public double DisplacementAfter(double seconds)
    {
        double tau = StartMassKg / MassFlowKgPerSec;
        if (seconds >= tau) return 0.5 * ThrustNewtons / StartMassKg * seconds * seconds;
        return ExhaustVelocity * ((tau - seconds) * Math.Log(1.0 - seconds / tau) + seconds);
    }
}

/// <summary>
/// Every burn the stack has left, staging included, so a countdown that runs past a staging knows the upper stage's
/// thrust and the dry mass thrown away. <see cref="BoosterPerformance"/> carries the running stage's law past its
/// burnout instead. <c>docs/FLIGHT-COMPUTER-COMPARISON.md</c> 2b.
/// </summary>
internal sealed class StagePlan
{
    private readonly StagePhase[] _phases;

    public StagePlan(IEnumerable<StagePhase> phases)
    {
        List<StagePhase> usable = [];
        foreach (StagePhase phase in phases)
        {
            if (phase.IsUsable) usable.Add(phase);
        }
        _phases = [.. usable];
    }

    public IReadOnlyList<StagePhase> Phases => _phases;

    public bool IsEmpty => _phases.Length == 0;

    public double DeltaVRemaining
    {
        get
        {
            double dv = 0.0;
            foreach (StagePhase phase in _phases) dv += phase.DeltaV;
            return dv;
        }
    }

    public double BurnSecondsRemaining
    {
        get
        {
            double seconds = 0.0;
            foreach (StagePhase phase in _phases) seconds += phase.DurationSeconds;
            return seconds;
        }
    }

    /// <summary>
    /// The first phase taking the engines' live figures, with the propellant the plan gave it: KSA's design thrust is
    /// at a stated pressure, and the live thrust beside the design delta-v would mix two airs.
    /// </summary>
    public StagePlan WithRunning(in BoosterPerformance live)
    {
        if (IsEmpty || !live.CanThrust || !(live.MassFlowKgPerSec > 0.0)) return this;

        StagePhase first = _phases[0];
        StagePhase running = first with
        {
            ThrustNewtons = live.ThrustNewtons,
            MassFlowKgPerSec = live.MassFlowKgPerSec,
            StartMassKg = live.TotalMassKg,
            DurationSeconds = first.PropellantKg / live.MassFlowKgPerSec,
        };

        StagePhase[] phases = (StagePhase[])_phases.Clone();
        phases[0] = running;
        return new StagePlan(phases);
    }

    /// <summary>
    /// Full-throttle seconds to gain <paramref name="deltaV"/> across the phases. A solid reached is spent whole; past
    /// the last phase its law is carried on, so the answer stays finite where the stack falls short.
    /// </summary>
    public double SecondsToGain(double deltaV)
    {
        if (!(deltaV > 0.0)) return 0.0;
        if (IsEmpty) return double.PositiveInfinity;

        double seconds = 0.0;
        double left = deltaV;
        foreach (StagePhase phase in _phases)
        {
            double dv = phase.DeltaV;
            if (left <= dv)
            {
                return seconds + (phase.Solid ? phase.DurationSeconds : phase.SecondsToGain(left));
            }
            seconds += phase.DurationSeconds;
            left -= dv;
        }

        StagePhase last = _phases[^1];
        StagePhase beyond = last with { StartMassKg = last.EndMassKg, DurationSeconds = 0.0 };
        double extra = beyond.SecondsToGain(left);
        return double.IsFinite(extra) ? seconds + extra : double.PositiveInfinity;
    }

    /// <summary>How far the stack travels along the thrust in <paramref name="seconds"/>, over what it would have coasted.</summary>
    public double ThrustDisplacement(double seconds)
    {
        if (!(seconds > 0.0) || IsEmpty) return 0.0;

        double displacement = 0.0;
        double gained = 0.0;
        double left = seconds;
        StagePhase last = _phases[^1];
        foreach (StagePhase phase in _phases)
        {
            double d = Math.Min(left, phase.DurationSeconds);
            displacement += gained * d + phase.DisplacementAfter(d);
            if (left <= phase.DurationSeconds) return displacement;
            gained += phase.DeltaV;
            left -= phase.DurationSeconds;
        }

        StagePhase beyond = last with { StartMassKg = last.EndMassKg, DurationSeconds = 0.0 };
        return displacement + gained * left + beyond.DisplacementAfter(left);
    }
}
