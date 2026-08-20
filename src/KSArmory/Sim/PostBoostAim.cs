namespace KSArmory;

/// <summary>
/// Correcting the aim after the engines have stopped, with the trim as the actuator.
///
/// <para>During the burn the aim correction and the guidance are two loops solving one shot, and
/// pinning either while the other moves is what stops them converging. Once the engines stop that
/// tension is gone: the trajectory is fixed, so moving the aim and re-solving the transfer from
/// where the bus actually is gives a plant response of one, measured against a vehicle that is not
/// accelerating. It is the same correction against a clean plant.</para>
///
/// <para><b>The trim is what makes it a lever rather than a readout.</b> A correction applied after
/// cutoff changes nothing on its own — the warheads coast along whatever arc the bus is already on.
/// Re-solving that arc to the corrected aim and letting the trim null onto it is the only thing
/// aboard that can still move the impact, and it nulls to hundredths of a metre a second.</para>
///
/// <para><b>They alternate rather than running together.</b> The correction's only observer is a
/// prediction flown from the vehicle's own state, so a measurement taken while the thrusters are
/// firing reads the trim's own displacement as error and burns harder at it. Waiting for the trim
/// to settle before measuring removes the interaction completely instead of damping it.</para>
///
/// <para><b>And holding is not free.</b> A warhead still aboard loses the leverage its ejection kick
/// has along the arc at about <see cref="HoldingCostsMetresPerSecond"/>, so a cycle has to remove
/// more miss than the seconds it spends are worth. That is the whole stopping rule: it stops when
/// another cycle would cost more than it can win, not after a count somebody picked.</para>
/// </summary>
internal sealed class PostBoostAim
{
    /// <summary>
    /// What a second of holding the warheads costs, in metres of miss.
    ///
    /// <para>Measured on the flown shot: the ejection kick is worth 8.421 km applied at cutoff and
    /// 5.672 km at +106 s, and the aim converges for the epoch it is released at. Holding is
    /// therefore a real cost paid against a real gain, which is what lets the two be compared.</para>
    /// </summary>
    public const double HoldingCostsMetresPerSecond = 26.0;

    /// <summary>
    /// The longest the bus may spend correcting before it releases regardless.
    ///
    /// <para>A backstop on the clock, not the budget that decides: the payback rule below stops it
    /// long before this on any shot that is already close. This is for the case where each cycle
    /// keeps promising an improvement it does not deliver.</para>
    /// </summary>
    public const double MaxSeconds = 45.0;

    /// <summary>How many measure-and-retrim cycles are worth running at most.</summary>
    public const int MaxCycles = 5;

    /// <summary>
    /// How long one cycle is assumed to take when deciding whether the next one pays.
    ///
    /// <para>Taken from the cycles already run once there are any, so a bus with weak thrusters
    /// stops sooner than one that settles quickly. This is only the estimate for the first.</para>
    /// </summary>
    public const double FirstCycleSeconds = 6.0;

    /// <summary>What the state machine wants of the caller this step.</summary>
    public readonly record struct Decision(bool MayMeasure, bool MayRelease, string Said);

    /// <summary>Whether the bus is still correcting, which is what holds the warheads aboard.</summary>
    public bool Correcting => _stage is Stage.Settling or Stage.Measuring;

    /// <summary>How many cycles have been run, for the log and the panel.</summary>
    public int Cycles { get; private set; }

    private enum Stage { Settling, Measuring, Finished }

    private Stage _stage = Stage.Settling;
    private double _elapsed;
    private double _cycleStartedAt;
    private double _lastCycleSeconds = FirstCycleSeconds;
    private string _said = "";

    /// <summary>
    /// Step the sequencer.
    /// </summary>
    /// <param name="stepSeconds">Simulated seconds since the last call.</param>
    /// <param name="trimSettled">Whether the trim has nulled onto the arc it was last given.</param>
    /// <param name="predictedMissMetres">
    /// The miss the last measurement reported, or NaN before there has been one. Read only when the
    /// trim is quiet, because that is the only time it means anything.
    /// </param>
    /// <param name="aimHasSettled">
    /// Whether the correction has stopped improving on its own best. It knows things this does not —
    /// that a cycle made the miss worse, that the response is not what it modelled.
    /// </param>
    public Decision Update(double stepSeconds, bool trimSettled, double predictedMissMetres,
                           bool aimHasSettled)
    {
        double step = double.IsFinite(stepSeconds) && stepSeconds > 0.0 ? stepSeconds : 0.0;
        _elapsed += step;

        if (_stage == Stage.Finished) return new Decision(false, true, _said);

        if (_elapsed >= MaxSeconds) return Finish($"released after {_elapsed:F0} s of correcting");

        // Nothing may be read off a vehicle the thrusters are still moving.
        if (!trimSettled)
        {
            _stage = Stage.Settling;
            return new Decision(false, false, _said);
        }

        _stage = Stage.Measuring;

        // The first settle is what the guidance's own cutoff solution earned. Measuring is free from
        // here on, so it always gets taken, and only what to do with it is a decision.
        if (!double.IsFinite(predictedMissMetres)) return new Decision(true, false, _said);

        if (aimHasSettled) return Finish($"aim settled {predictedMissMetres / 1000.0:F1} km out");

        if (Cycles >= MaxCycles) return Finish($"released after {Cycles} corrections");

        // The payback rule. Another cycle costs the seconds it takes, at the rate holding a warhead
        // spends leverage — so it is only worth running while the miss on the table is larger than
        // that. A shot already inside it is made worse by correcting it.
        double nextCycleCosts = _lastCycleSeconds * HoldingCostsMetresPerSecond;

        if (predictedMissMetres <= nextCycleCosts)
        {
            return Finish($"{predictedMissMetres:F0} m out, under the "
                          + $"{nextCycleCosts:F0} m another correction would cost");
        }

        Cycles++;
        _lastCycleSeconds = Cycles > 1 ? Math.Max(step, _elapsed - _cycleStartedAt) : FirstCycleSeconds;
        _cycleStartedAt = _elapsed;
        _stage = Stage.Settling;

        _said = $"correcting the aim, {predictedMissMetres / 1000.0:F1} km out (pass {Cycles})";
        return new Decision(true, false, _said);
    }

    private Decision Finish(string why)
    {
        _stage = Stage.Finished;
        _said = why;
        return new Decision(false, true, _said);
    }

    public void Reset()
    {
        _stage = Stage.Settling;
        _elapsed = 0.0;
        _cycleStartedAt = 0.0;
        _lastCycleSeconds = FirstCycleSeconds;
        Cycles = 0;
        _said = "";
    }
}
