using Brutal.Numerics;

namespace KSArmory;

/// <summary>Everything the post-boost sequencer needs to know about the bus this step.</summary>
/// <param name="TrimSettled">Whether the trim has nulled onto the arc it was last given.</param>
/// <param name="ReleaseDirectionCci">
/// The line the modelled ejection kick points along, which is the bus's own nose.
///
/// <para>Handed in as a direction rather than as an angle or a rate, because differencing it is
/// what decides whether a reading may be taken at all — and a subtraction done at the call site is
/// one no test can reach.</para>
/// </param>
/// <param name="PredictedMissMetres">
/// The miss the last measurement reported, or NaN before there has been one. Read only when the
/// trim is quiet and the bus is holding still, because that is the only time it means anything.
/// </param>
/// <param name="AimHasSettled">
/// Whether the correction has stopped improving on its own best. It knows things this does not —
/// that a cycle made the miss worse, that the response is not what it modelled.
/// </param>
/// <param name="TrimSpentMetresPerSecond">
/// What the passes have taken out of the tank so far — <see cref="BusTrim.SpentMetresPerSecond"/>.
/// </param>
internal readonly record struct PostBoostSituation(
    bool TrimSettled,
    double3 ReleaseDirectionCci,
    double PredictedMissMetres,
    bool AimHasSettled,
    double TrimSpentMetresPerSecond);

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
/// <para><b>A reading is only worth taking off an instrument that is holding still, and there are
/// two ways for it not to be.</b> The thrusters move the vehicle the prediction is flown from, so
/// the trim has to be quiet. And the prediction adds the ejection kick along the bus's <em>nose</em>,
/// so a nose that is turning moves the predicted impact with nothing about the shot having changed
/// — see <see cref="SteadyWithinDegrees"/>, which is much the larger of the two.</para>
///
/// <para><b>And holding is not free.</b> A warhead still aboard loses the leverage its ejection kick
/// has along the arc at about <see cref="HoldingCostsMetresPerSecond"/>, so a cycle has to remove
/// more miss than the seconds it spends are worth. Every stopping rule here is that trade in a
/// different currency: seconds, passes that buy nothing, and propellant.</para>
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
    /// <para>A backstop on the clock, not the budget that decides: the rules below stop it long
    /// before this on any shot that is either close or getting nowhere. This is for the case where
    /// each cycle keeps promising an improvement it does not deliver.</para>
    /// </summary>
    public const double MaxSeconds = 120.0;

    /// <summary>
    /// How many measure-and-retrim cycles are worth running at most.
    ///
    /// <para><b>The payback and improvement rules are meant to be what stop it, not this.</b> At
    /// five the flown salvo ran out of passes with its predicted miss still falling — 2.9, 2.9,
    /// 2.1, 1.2 km — and the aim it released on was the one it happened to hold when the count ran
    /// out. Headless, the residue that leaves is the largest single term in the whole shot: 760 m
    /// of group offset against 18 m for a correction allowed to finish.</para>
    ///
    /// <para>Raising it costs nothing when the shot is already close, because the payback test
    /// stops those on the first pass. It only spends passes on shots with kilometres on the
    /// table.</para>
    /// </summary>
    public const int MaxCycles = 20;

    /// <summary>
    /// How long one cycle is assumed to take when deciding whether the next one pays.
    ///
    /// <para>Taken from the cycles already run once there are any, so a bus with weak thrusters
    /// stops sooner than one that settles quickly. This is only the estimate for the first.</para>
    /// </summary>
    public const double FirstCycleSeconds = 6.0;

    /// <summary>
    /// How far the modelled release direction may turn across <see cref="SteadySeconds"/> for a
    /// reading taken over it to describe the shot rather than the bus, in degrees.
    ///
    /// <para>The prediction the correction reads flies the bus's state with the ejection kick
    /// already added, and that kick points along the nose — so the nose turning moves the predicted
    /// impact on its own. Measured on the 3,459 km shot with a 2 m/s kick: 14–22 m of predicted
    /// impact per degree near the nose, rising to 30–45 m per degree by 22°. Two degrees is at most
    /// 44 m, which is well inside the 100–400 m that separates the passes of a correction that has
    /// converged.</para>
    ///
    /// <para><b>The whole swing available is 16.0 km</b>, at a kick turned right round — against
    /// 0.17 km the trim can leave behind at a reading this gate admits. The two causes are not
    /// comparable, which is why the settle gate is on the direction and not on the thrusters.</para>
    /// </summary>
    public const double SteadyWithinDegrees = 2.0;

    /// <summary>
    /// How long the release direction has to hold inside <see cref="SteadyWithinDegrees"/> before a
    /// reading is taken.
    ///
    /// <para>A window rather than an instantaneous rate, because the quantity is an angle between
    /// two samples a frame apart and the frame pacing beats — the same reason
    /// <see cref="BusTrim.AccelerationGain"/> is low. Two seconds is about a pass.</para>
    /// </summary>
    public const double SteadySeconds = 2.0;

    /// <summary>
    /// How long to wait for the bus to hold still before giving up on correcting at all.
    ///
    /// <para><b>Not holding out for it</b>, because waiting costs
    /// <see cref="HoldingCostsMetresPerSecond"/> and a bus whose nose will not settle is one whose
    /// observer cannot be trusted — there is nothing at the end of the wait to collect. Ten seconds
    /// is 260 m of leverage, against the 3,120 m that running <see cref="MaxSeconds"/> out on
    /// unreadable measurements costs.</para>
    ///
    /// <para>A separated bus is measured in flight with a 22.11° pointing band, free roll angle and
    /// no elected control part, and its salvo thrown 95–119° off the platform's track across three
    /// identical runs. On this arc that band of directions is 9.8–13.5 km of predicted miss with
    /// nothing about the shot having changed.</para>
    /// </summary>
    public const double SettlesWithinSeconds = 10.0;

    /// <summary>
    /// How much closer a pass has to bring the predicted miss to count as an improvement.
    ///
    /// <para>The same resolution the correction itself judges a cycle at, deliberately aliased
    /// rather than restated: they are the same quantity read off the same prediction, and two
    /// numbers for it would drift.</para>
    /// </summary>
    public const double ImprovedByMetres = AimCorrection.ImprovedByMetres;

    /// <summary>
    /// How many passes may fail to improve on the best seen before it stops.
    ///
    /// <para><b>Failures to improve, not worsenings</b>, and that is the whole difference from
    /// <see cref="AimCorrection.WorseBeforeStopping"/> — which counts passes strictly worse than the
    /// best and so never trips on a reading that oscillates inside the band. Flown: the correction
    /// converges by pass 5, 3.3 km down to 0.4 km, then wanders between 0.1 and 0.5 km for seven
    /// more passes, improving on nothing and satisfying neither that rule nor the payback one.</para>
    ///
    /// <para>Three rather than one, so a single noisy reading and one genuine hump do not end it.
    /// On that flight it stops at pass 8 rather than 12: four passes at about two seconds each,
    /// worth 208 m of leverage and a third of the propellant the correction spends.</para>
    ///
    /// <para><b>The best is a stopping rule and not an aim that gets restored.</b> A bias only
    /// reaches the shot through an arc the trim then has to fly, so going back to an earlier one
    /// costs a whole further pass — which is a different trade from this one and is not made
    /// here.</para>
    /// </summary>
    public const int PassesWithoutImprovement = 3;

    /// <summary>
    /// The most thruster velocity the correction's passes may spend, in metres per second.
    ///
    /// <para>Every pass re-arms the trim onto a moved arc and the trim thrusts until it is back on
    /// it, so passes are what the correction costs in propellant. Measured in flight: 1,943 frames
    /// with thrusters firing against 24 settled, about 36 m/s, on a bus carrying 70–90.</para>
    ///
    /// <para><b>What the reserve protects is the separation null</b>, which is the one piece of
    /// trimming that cannot be skipped: a 1.1 m/s decoupler shove takes the predicted impact from
    /// 0.7 km to 4.5 km on this arc, and a bus that arrives at the release dry cannot take it back
    /// out. Forty leaves 30 m/s on the smallest bus in that range — three nulls at the largest trim
    /// <see cref="BusTrim.MaxMetresPerSecond"/> will accept, or twenty-seven separation shoves — and
    /// sits above what a converged correction spends, so it is the backstop against a loop that will
    /// not stop rather than the thing that stops one.</para>
    /// </summary>
    public const double MaxTrimMetresPerSecond = 40.0;

    /// <summary>What the state machine wants of the caller this step.</summary>
    public readonly record struct Decision(bool MayMeasure, bool MayRelease, string Said);

    /// <summary>Whether the bus is still correcting, which is what holds the warheads aboard.</summary>
    public bool Correcting => _stage is Stage.Settling or Stage.Measuring;

    /// <summary>How many cycles have been run, for the log and the panel.</summary>
    public int Cycles { get; private set; }

    /// <summary>The closest any pass has predicted, or NaN before there has been one.</summary>
    public double BestMissMetres => double.IsPositiveInfinity(_bestMiss) ? double.NaN : _bestMiss;

    /// <summary>Whether the release direction is currently holding still enough to be read off.</summary>
    public bool Steady => _nothingTurning || _steadyFor >= SteadySeconds;

    private enum Stage { Settling, Measuring, Finished }

    private Stage _stage = Stage.Settling;
    private double _elapsed;
    private double _cycleStartedAt;
    private double _lastCycleSeconds = FirstCycleSeconds;
    private double3 _anchor;
    private bool _haveDirection;
    private bool _nothingTurning;
    private double _steadyFor;
    private double _unsteadyFor;
    private double _bestMiss = double.PositiveInfinity;
    private int _noImprovement;
    private string _said = "";

    /// <summary>
    /// Step the sequencer.
    /// </summary>
    /// <param name="stepSeconds">Simulated seconds since the last call.</param>
    /// <param name="now">What the bus is doing — see <see cref="PostBoostSituation"/>.</param>
    public Decision Update(double stepSeconds, in PostBoostSituation now)
    {
        double step = double.IsFinite(stepSeconds) && stepSeconds > 0.0 ? stepSeconds : 0.0;
        _elapsed += step;

        if (_stage == Stage.Finished) return new Decision(false, true, _said);

        Watch(step, now.ReleaseDirectionCci, now.TrimSettled);

        if (_elapsed >= MaxSeconds) return Finish($"released after {_elapsed:F0} s of correcting");

        if (now.TrimSpentMetresPerSecond >= MaxTrimMetresPerSecond)
        {
            return Finish($"released on {now.TrimSpentMetresPerSecond:F0} m/s of trim, "
                          + "which is the bus's budget for correcting");
        }

        // Nothing may be read off a vehicle the thrusters are still moving.
        if (!now.TrimSettled)
        {
            _stage = Stage.Settling;
            return new Decision(false, false, _said);
        }

        // Nor off one whose nose is turning under the kick the prediction adds. Given up on rather
        // than waited out: the wait is charged at the holding rate and a bus that will not settle
        // has nothing to hand over at the end of it.
        if (!Steady)
        {
            _stage = Stage.Settling;

            return _unsteadyFor >= SettlesWithinSeconds
                       ? Finish($"released after {_unsteadyFor:F0} s of the bus not holding still")
                       : new Decision(false, false, _said);
        }

        _stage = Stage.Measuring;

        // The first settle is what the guidance's own cutoff solution earned. Measuring is free from
        // here on, so it always gets taken, and only what to do with it is a decision.
        if (!double.IsFinite(now.PredictedMissMetres)) return new Decision(true, false, _said);

        if (now.AimHasSettled)
        {
            return Finish($"aim settled {now.PredictedMissMetres / 1000.0:F1} km out");
        }

        // A pass that cannot beat the best any pass has managed is a pass that bought nothing, and
        // enough of those in a row is a correction that has finished whatever its readings say.
        if (now.PredictedMissMetres < _bestMiss - ImprovedByMetres)
        {
            _bestMiss = now.PredictedMissMetres;
            _noImprovement = 0;
        }
        else if (++_noImprovement >= PassesWithoutImprovement)
        {
            return Finish($"{_noImprovement} passes without beating {_bestMiss / 1000.0:F1} km");
        }

        if (Cycles >= MaxCycles) return Finish($"released after {Cycles} corrections");

        // The payback rule. Another cycle costs the seconds it takes, at the rate holding a warhead
        // spends leverage — so it is only worth running while the miss on the table is larger than
        // that. A shot already inside it is made worse by correcting it.
        double nextCycleCosts = _lastCycleSeconds * HoldingCostsMetresPerSecond;

        if (now.PredictedMissMetres <= nextCycleCosts)
        {
            return Finish($"{now.PredictedMissMetres:F0} m out, under the "
                          + $"{nextCycleCosts:F0} m another correction would cost");
        }

        Cycles++;
        _lastCycleSeconds = Cycles > 1 ? Math.Max(step, _elapsed - _cycleStartedAt) : FirstCycleSeconds;
        _cycleStartedAt = _elapsed;
        _stage = Stage.Settling;

        _said = $"correcting the aim, {now.PredictedMissMetres / 1000.0:F1} km out (pass {Cycles})";
        return new Decision(true, false, _said);
    }

    // How still the line the warheads leave along is holding. Differenced here rather than handed
    // in as an angle: the subtraction is the measurement, and one done at the call site is one no
    // test reaches.
    //
    // Against the direction the current stretch started at, never against the previous frame. A
    // per-frame turn is an angle between two samples a frame apart, and the frame pacing beats it
    // - so a rate test rejects a bus that is holding perfectly still and accepts one drifting
    // slowly enough to stay under it for ever. Measured from an anchor, the band is what it says:
    // while it reads steady, the kick has been inside SteadyWithinDegrees of one fixed direction
    // for the whole window.
    //
    // A direction that will not resolve counts as steady. Nothing turns on a bus with no modelled
    // kick, so there is no moving instrument to wait for - and blocking on it would hold the
    // warheads for a term that is identically zero.
    /// <param name="waiting">
    /// Whether the sequencer is otherwise ready to read. The give-up clock only runs then: the
    /// thrusters rotate the bus as well as translating it, so a slow null would otherwise spend a
    /// budget that exists to bound waiting rather than trimming.
    /// </param>
    private void Watch(double step, double3 directionCci, bool waiting)
    {
        double3 now = Vec.Unit(directionCci);

        if (!Vec.IsFinite(now) || now.Equals(Vec.Zero))
        {
            _haveDirection = false;
            _nothingTurning = true;
            _steadyFor = 0.0;
            _unsteadyFor = 0.0;
            return;
        }

        _nothingTurning = false;

        if (!_haveDirection)
        {
            _haveDirection = true;
            _anchor = now;
            _steadyFor = 0.0;
            return;
        }

        if (Vec.AngleBetween(_anchor, now) * 180.0 / Math.PI <= SteadyWithinDegrees)
        {
            _steadyFor += step;
            if (Steady) _unsteadyFor = 0.0;
            return;
        }

        _anchor = now;
        _steadyFor = 0.0;
        if (waiting) _unsteadyFor += step;
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
        _anchor = Vec.Zero;
        _haveDirection = false;
        _nothingTurning = false;
        _steadyFor = 0.0;
        _unsteadyFor = 0.0;
        _bestMiss = double.PositiveInfinity;
        _noImprovement = 0;
        Cycles = 0;
        _said = "";
    }
}
