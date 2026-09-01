using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What a second of holding the warheads costs, measured off the trajectory the bus is actually on.
///
/// <para><b>This is the floor under the miss.</b> <see cref="PostBoostAim"/> releases once the
/// predicted miss falls under <c>cycle seconds x this</c>, so the number set here is what the
/// correction settles for — measured over 96 flights as a 156 m threshold, 109 m accepted and 125 m
/// flown.</para>
///
/// <para><b>And it is not a constant.</b> <see cref="PostBoostAim.HoldingCostsMetresPerSecond"/> is
/// one flight's answer at one geometry; flown against the predictor it runs from 0.82 m/s on a
/// 500 km shot to 21.79 on a 12,900 km one, so any single value is wrong everywhere but the range it
/// was taken at. Measuring it is two predictions and a coast.</para>
///
/// <para>The kick's leverage decays because the arc it is applied to is running out: the same
/// impulse steers less the nearer the impact is. So this differences <em>what the release impulse is
/// worth</em> now against what it is worth a second later, which is exactly how the shipped constant
/// was originally taken.</para>
/// </summary>
internal static class HoldingCost
{
    /// <summary>
    /// How far ahead the second probe is flown.
    ///
    /// <para><b>The baseline divides the predictor's own noise, so it cannot be short.</b> A second
    /// looked right — the decay is smooth over it and the answer is wanted per second — and it is
    /// wrong on real ground: the impact prediction wanders by the terrain's own roughness, and one
    /// second of baseline turns a hundred metres of that into a hundred metres a second of decay.
    /// Flown at 12,902 km it reported 15.78, 112.40, 150.05 and 194.82 m/s against a true value
    /// near 3, and released the correction over a kilometre out.</para>
    ///
    /// <para>106 s because that is what the shipped constant was taken over, and because it divides
    /// the same wander by a hundred. The decay is near enough linear across it — measured across
    /// range in <c>HoldingCostTests</c>.</para>
    /// </summary>
    public const double ProbeSeconds = 106.0;

    /// <summary>
    /// The most this may report, as a guard rather than a preference.
    ///
    /// <para>A predictor that answers badly for one of the four probes gives a difference of two
    /// unrelated impacts, which is a huge number rather than an obviously wrong one. Nothing
    /// measured has come near this.</para>
    /// </summary>
    public const double MaxMetresPerSecond = 200.0;

    /// <summary>
    /// The decay of the release impulse's leverage, or false if the probes could not be flown.
    ///
    /// <para>A refusal is not a zero: zero would let the correction run for ever at no charge, so a
    /// caller that cannot measure keeps whatever it was using.</para>
    ///
    /// <para><b>Deliberately no terrain.</b> This is a property of the arc — how fast the kick's
    /// leverage decays — and not of the hillside under the aim. Flown against the real height field
    /// the two probes land on different relief, so their difference carries the ground's roughness
    /// rather than the decay: measured across baselines from 1 s to 300 s, a target like 12,902 km's
    /// gives 12 to 42 m/s of spread and a median wrong by an order of magnitude, where the same
    /// probes on the reference sphere hold to about 1 m/s. It is what put 194.82 m/s into a flown
    /// threshold.</para>
    /// </summary>
    public static bool TryMeasure(BallisticBody body, double3 positionCci, double3 velocityCci,
                                  double3 kickCci, double stepSeconds, out double metresPerSecond,
                                  ImpactPredictor.Drag? drag = null,
                                  double probeSeconds = ProbeSeconds)
    {
        metresPerSecond = double.NaN;

        if (!body.IsUsable) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci) || !Vec.IsFinite(kickCci)) return false;
        if (kickCci.Equals(Vec.Zero)) return false;

        if (!TryWorth(body, positionCci, velocityCci, kickCci, stepSeconds, drag, out double now))
        {
            return false;
        }

        if (!(probeSeconds > 0.0) || !double.IsFinite(probeSeconds)) return false;

        if (!Kepler.TryCoast(body.Mu, positionCci, velocityCci, probeSeconds,
                             out double3 laterPos, out double3 laterVel))
        {
            return false;
        }

        if (!TryWorth(body, laterPos, laterVel, kickCci, stepSeconds, drag, out double later))
        {
            return false;
        }

        double decay = (now - later) / probeSeconds;

        // A kick worth more later than now is the predictor's own noise on two nearly identical
        // arcs, not a shot that improves by waiting. Refused rather than clamped to zero, for the
        // same reason a failed probe is.
        if (!(decay > 0.0) || decay > MaxMetresPerSecond) return false;

        metresPerSecond = decay;
        return true;
    }

    // How far the release impulse moves the impact, from one state.
    private static bool TryWorth(BallisticBody body, double3 positionCci, double3 velocityCci,
                                 double3 kickCci, double stepSeconds,
                                 ImpactPredictor.Drag? drag, out double metres)
    {
        metres = double.NaN;

        if (!ImpactPredictor.TryPredict(body, positionCci, velocityCci, stepSeconds,
                                        ImpactPredictor.DefaultMaxSeconds,
                                        out ImpactPredictor.Impact plain, null, null, drag))
        {
            return false;
        }

        if (!ImpactPredictor.TryPredict(body, positionCci, velocityCci + kickCci, stepSeconds,
                                        ImpactPredictor.DefaultMaxSeconds,
                                        out ImpactPredictor.Impact kicked, null, null, drag))
        {
            return false;
        }

        // Body-fixed, so the planet's turn over the two flights does not enter a difference that is
        // metres against a spin worth hundreds of them.
        metres = (plain.GroundFixedPointCci - kicked.GroundFixedPointCci).Length();
        return double.IsFinite(metres);
    }
}
