using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The difference between where a shot is aimed and where it has to be aimed to arrive.
///
/// <para>The transfer solver puts the arc through a <em>point</em> in vacuum, and a round stops where
/// the ground is: on a shallow arrival twelve kilometres of ground per kilometre of height. So the aim
/// is moved by what the flown arc loses.</para>
///
/// <para><b>A feedback loop against a solver that then moves the arc</b>, so each step is sized by the
/// measured response rather than taking the whole error.</para>
/// </summary>
internal sealed class AimCorrection
{
    /// <summary>
    /// How much of each measured error is taken out before the loop has watched itself work.
    ///
    /// <para>Only the first step, taken blind; after that the response is measured
    /// (<see cref="Observe"/>).</para>
    /// </summary>
    public const double Gain = 0.25;

    /// <summary>
    /// The range the measured response is trusted within, as impact moved per unit of aim moved.
    ///
    /// <para>Clamping is what lets the estimate come from consecutive cycles rather than a probe.
    /// <b>The lower bound of one is the load-bearing end</b>: under one asks for a step larger than the
    /// miss, and flown at a quarter one noisy reading took the bias from 28.6 km to 192.9.</para>
    /// </summary>
    public const double MinResponse = 1.0;

    /// <inheritdoc cref="MinResponse"/>
    public const double MaxResponse = 6.0;

    /// <summary>How far the aim must have moved for the response it produced to mean anything.</summary>
    public const double ResponseFromMetres = 500.0;

    /// <summary>
    /// The furthest the aim may be moved.
    ///
    /// <para>Larger is a shot that cannot be made, and chasing it turns a visible miss into a wild one.</para>
    /// </summary>
    public const double MaxMetres = 300_000.0;

    /// <summary>
    /// The furthest the aim may be moved and still be <em>flyable</em>, which is a different
    /// question from how far it may be moved at all.
    ///
    /// <para>Priced by <see cref="AimAuthority"/>. Infinite until set, and only the tests set it: the
    /// flight's bound was flown, lost and removed (<c>docs/MIRV-NEXT.md</c>, "And the bound that should
    /// have stopped it does not know what an aim costs").</para>
    /// </summary>
    public double AffordableMetres = double.PositiveInfinity;

    /// <summary>
    /// How much closer the impact must come for a cycle to count as an improvement.
    /// </summary>
    public const double ImprovedByMetres = 250.0;

    /// <summary>
    /// What fraction of the current best a cycle must close to count as an improvement, when the
    /// threshold is allowed to follow the miss.
    ///
    /// <para><b>An absolute threshold goes blind as the shot improves</b>: <see cref="ImprovedByMetres"/>
    /// is many times a metre-scale miss, so no cycle counts (<c>docs/ACCURACY-PLAN.md</c> 3bw). A quarter
    /// is what 250 m was at a kilometre-scale miss.</para>
    /// </summary>
    public const double ImprovedByFraction = 0.25;

    /// <summary>
    /// The floor under that fraction, in metres — below this the predictor cannot tell two aims
    /// apart, so a smaller threshold would be chasing its own noise.
    ///
    /// <para>Over <c>ImpactPredictor</c>'s 0.46 m error and under the round's 1–4 m walk from its release
    /// probe (<c>docs/ACCURACY-PLAN.md</c> 3ae, 3bw).</para>
    /// </summary>
    public const double ImprovedByFloorMetres = 1.0;

    /// <summary>
    /// How much closer a cycle must bring the impact to count, given the best seen so far.
    ///
    /// <para><paramref name="tracksTheMiss"/> false is the flat constant the aim loop ships with.
    /// True scales it with the miss, which is what <see cref="PostBoostAim"/> judges its passes on
    /// under <see cref="IcbmConfig.ReleaseInsideTheTrimFloor"/>.</para>
    /// </summary>
    public static double ImprovementThreshold(double bestMissMetres, bool tracksTheMiss)
    {
        if (!tracksTheMiss) return ImprovedByMetres;
        if (!double.IsFinite(bestMissMetres) || bestMissMetres <= 0.0) return ImprovedByFloorMetres;

        return Math.Max(ImprovedByFloorMetres, ImprovedByFraction * bestMissMetres);
    }

    /// <summary>
    /// How many cycles may fail to improve on the best before the loop calls it a direction rather
    /// than a hump.
    ///
    /// <para><b>The miss is not monotonic in the aim, so the best has to be walked past</b>: at 7,645 km
    /// the worsening patch is five cycles long and its far side the better answer. Twelve is twice that.
    /// Stopping early commits the arrival (<see cref="IsSteady"/>), so waiting is the cheap side; the
    /// other bound is <see cref="IcbmProgram.LatchArrivalWithinSeconds"/>, twelve being six seconds of
    /// burn.</para>
    ///
    /// <para>After cutoff a reading is one per pass, so this effectively never fires there and
    /// <c>PostBoostAim.PassesWithoutImprovement</c> decides (<c>docs/ACCURACY-PLAN.md</c> 3bc).</para>
    /// </summary>
    public const int WorseBeforeStopping = 12;

    /// <summary>How little the aim may move for the correction to count as having stopped.</summary>
    public const double SteadyMetres = 2_000.0;

    /// <summary>
    /// What a metre of aim was last measured to move the impact by — the loop's own plant estimate,
    /// and the divisor every step is sized by.
    ///
    /// <para>A step is <c>error / this</c>: at <see cref="MinResponse"/> the loop takes the whole error at
    /// once.</para>
    /// </summary>
    public double Response => _response;

    /// <summary>The best miss the loop has seen, which is the aim it will revert to.</summary>
    public double BestMissMetres => _bestMiss;

    /// <summary>
    /// How many cycles have re-measured the plant, and what the last raw secant was before the
    /// clamp.
    ///
    /// <para>A response of one is either <see cref="Resume"/>'s seed nothing has corrected or the
    /// <see cref="MinResponse"/> clamp on a real reading; these tell them apart.</para>
    /// </summary>
    public int PlantMeasurements { get; private set; }

    /// <inheritdoc cref="PlantMeasurements"/>
    public double LastRawResponse { get; private set; } = double.NaN;

    /// <summary>
    /// How far the impact moved since the previous cycle, and how much of that the aim move
    /// accounts for.
    ///
    /// <para>Their difference is what moves the impact between readings besides the aim. Recorded every
    /// cycle that takes a reading and <b>NaN on every one that does not</b>, unlike
    /// <see cref="LastRawResponse"/>.</para>
    /// </summary>
    public double LastImpactMoveMetres { get; private set; } = double.NaN;

    /// <inheritdoc cref="LastImpactMoveMetres"/>
    public double LastImpactAlongAimMetres { get; private set; } = double.NaN;

    /// <inheritdoc cref="LastImpactMoveMetres"/>
    public double LastAimMoveMetres { get; private set; } = double.NaN;

    /// <summary>How many readings worse than that best have accumulated.</summary>
    public int WorseFor => _worseFor;

    /// <summary>
    /// Whether an impact predicted from a state in air of this density says anything about the
    /// <em>aim</em>.
    ///
    /// <para><b>A prediction that departs from inside the air is not a miss, it is the air</b>: before the
    /// vehicle has flown, the projected cutoff is the pad, and the arc ploughs through the whole
    /// atmosphere (<c>docs/ACCURACY-PLAN.md</c> 3ah). The threshold is <see cref="Medium.NoticeableDensity"/>.</para>
    /// </summary>
    public static bool DepartureIsWorthObserving(double departureDensityRatio)
        => !double.IsNaN(departureDensityRatio)
        && departureDensityRatio < Medium.NoticeableDensity;

    /// <summary>
    /// How far the aim may actually go: the sanity limit, or what the bus can pay for if that is
    /// nearer. An unset or unusable affordability leaves <see cref="MaxMetres"/> standing rather
    /// than clamping the correction to nothing.
    /// </summary>
    public double Reach
        => AffordableMetres > 0.0 && !double.IsNaN(AffordableMetres)
               ? Math.Min(MaxMetres, AffordableMetres)
               : MaxMetres;

    /// <summary>What is currently being added to the aim point, in the body's inertial frame.</summary>
    public double3 BiasCci { get; private set; }

    /// <summary>
    /// Whether the aim has stopped moving, either by converging or by giving up.
    ///
    /// <para>What the guidance waits for before it commits to an arrival time.</para>
    /// </summary>
    public bool IsSteady => Settled || (_haveLast && Vec.Len(BiasCci - _lastBias) < SteadyMetres);

    /// <summary>
    /// Whether the loop has stopped because it could not improve on its own best.
    ///
    /// <para>Not a failure: what remains wants a different trajectory rather than a different aim.</para>
    /// </summary>
    public bool Settled { get; private set; }

    private double _response = 1.0 / Gain;
    private double3 _lastBias;
    private double3 _lastError;
    private bool _haveLast;
    private double _bestMiss = double.PositiveInfinity;
    private double3 _bestBias;
    private double3 _walkedBias;
    private int _worseFor;

    // The last readings and the aims they were taken with, for a freeze that would rather keep a recent good one.
    private const int RecentReadings = 10;
    private readonly double3[] _recentBias = new double3[RecentReadings];
    private readonly double[] _recentMiss = new double[RecentReadings];
    private int _recentCount;

    /// <summary>
    /// Where to actually aim, given where the shot is meant to land.
    ///
    /// <para>Kept on the target's own radius, or the raw bias walks the aim underground.</para>
    /// </summary>
    public double3 Apply(double3 targetCci)
    {
        double radius = Vec.Len(targetCci);
        if (radius <= 0.0) return targetCci;

        double3 moved = targetCci + BiasCci;
        double length = Vec.Len(moved);
        return length > 0.0 ? moved * (radius / length) : targetCci;
    }

    /// <summary>
    /// Fold in one flown result: the arc was aimed somewhere and landed somewhere else.
    /// </summary>
    /// <param name="landedCci">Where flying the current solution actually puts it.</param>
    /// <param name="targetCci">Where it is supposed to go — never the corrected aim.</param>
    public void Observe(double3 landedCci, double3 targetCci,
                        bool thresholdTracksTheMiss = false)
    {
        if (!Vec.IsFinite(landedCci) || !Vec.IsFinite(targetCci)) return;

        // Cleared first, so a cycle that takes no reading reports none rather than its last value.
        LastAimMoveMetres = double.NaN;
        LastImpactMoveMetres = double.NaN;
        LastImpactAlongAimMetres = double.NaN;

        // Against the target, never the corrected aim, which would score every shot perfect.
        double3 error = landedCci - targetCci;
        if (!Vec.IsFinite(error)) return;

        if (Settled) return;

        // The plant changes under the loop: once the arrival is latched an aim change forces a different
        // trajectory and the impact moves several times further. So the step is sized by the secant every
        // cycle already measures, and the best aim is kept and reverted to.
        double3 movedAim = BiasCci - _lastBias;
        double movedBy = Vec.Len(movedAim);

        if (_haveLast)
        {
            LastAimMoveMetres = movedBy;
            LastImpactMoveMetres = Vec.Len(error - _lastError);
            LastImpactAlongAimMetres = movedBy > 0.0
                                           ? Vec.Dot(error - _lastError, Vec.Unit(movedAim))
                                           : 0.0;
        }

        if (movedBy >= ResponseFromMetres && _haveLast)
        {
            double impactAlong = Vec.Dot(error - _lastError, Vec.Unit(movedAim));

            // A negative response is a plant nothing here models: distrust it rather than invert the loop.
            if (impactAlong > 0.0)
            {
                LastRawResponse = impactAlong / movedBy;
                PlantMeasurements++;
                _response = Math.Clamp(LastRawResponse, MinResponse, MaxResponse);
            }
        }

        _lastBias = BiasCci;
        _lastError = error;
        _haveLast = true;

        double miss = Vec.Len(error);
        _recentBias[_recentCount % RecentReadings] = BiasCci;
        _recentMiss[_recentCount % RecentReadings] = miss;
        _recentCount++;

        double band = ImprovementThreshold(_bestMiss, thresholdTracksTheMiss);

        if (miss < _bestMiss - band)
        {
            _bestMiss = miss;
            _bestBias = BiasCci;
            _worseFor = 0;
        }
        else if (miss > _bestMiss + band)
        {
            if (++_worseFor >= WorseBeforeStopping)
            {
                _walkedBias = BiasCci;
                BiasCci = _bestBias;
                Settled = true;
                return;
            }
        }
        else
        {
            // A pass level with the best ends the excursion: WorseBeforeStopping counts a run, not a total.
            _worseFor = 0;
        }

        BiasCci = Vec.ClampLength(BiasCci - error / _response, Reach);
    }

    /// <summary>
    /// Stop correcting and keep the best aim found.
    ///
    /// <para>Called when the guidance commits to an arrival, after which the plant is one this loop
    /// cannot hold: flown, 4.9 km of predicted miss while free became 13.4 km by cutoff.</para>
    /// </summary>
    /// <param name="keepABetterRecentAim">
    /// Keep the best of the last ten readings' aims when it beat the banked best, which moves only by the band.
    /// Ten, not the last alone: the prediction jumped 1.4 km a second before one latch.
    /// <see cref="IcbmConfig.FreezeKeepsABetterRecentAim"/>.
    /// </param>
    public void Freeze(bool keepABetterRecentAim = false)
    {
        if (Settled) return;

        _walkedBias = BiasCci;

        int recent = -1;
        if (keepABetterRecentAim)
        {
            for (int i = 0; i < Math.Min(_recentCount, RecentReadings); i++)
            {
                if (_recentMiss[i] < _bestMiss && (recent < 0 || _recentMiss[i] < _recentMiss[recent])) recent = i;
            }
        }

        if (recent >= 0) BiasCci = _recentBias[recent];
        else if (double.IsFinite(_bestMiss)) BiasCci = _bestBias;
        Settled = true;
    }

    /// <summary>
    /// Start correcting again from the aim already found, against a plant that has changed.
    ///
    /// <para>Called when the engines stop: only the aim transfers from the burn. The response is seeded at
    /// one, the coast plant, since the arc is re-solved to the aim at a fixed arrival.</para>
    /// </summary>
    public void Resume()
    {
        _recentCount = 0;
        if (!Settled) return;

        Settled = false;
        _bestMiss = double.PositiveInfinity;
        _bestBias = BiasCci;
        _worseFor = 0;
        _response = 1.0;
        _haveLast = false;
    }

    /// <summary>
    /// Start again on a different place, on the same coast.
    ///
    /// <para>The response is the coast's, as <see cref="Resume"/> seeds it: a hop is the same plant.</para>
    ///
    /// <para><b>Carried, the next stop starts from the bias the loop had walked to</b>, not the one
    /// <see cref="Freeze"/> reverted to, which the trim never flew. <see cref="IcbmConfig.CarryAimBiasAcrossHops"/>.</para>
    /// </summary>
    public void Retarget(bool carryBias = false)
    {
        _recentCount = 0;
        BiasCci = carryBias && Vec.IsFinite(_walkedBias) ? _walkedBias : Vec.Zero;
        Settled = false;
        _bestMiss = double.PositiveInfinity;
        _bestBias = BiasCci;
        _worseFor = 0;
        _response = 1.0;
        _lastBias = BiasCci;
        _lastError = Vec.Zero;
        _haveLast = false;
    }

    /// <summary>
    /// Forget the best miss and keep the aim, for a plant that has just stepped under it: readings against the new
    /// prediction would otherwise all count as worse than a best banked on the old one, and settle back onto it.
    /// </summary>
    public void Rebase()
    {
        Settled = false;
        _bestMiss = double.PositiveInfinity;
        _bestBias = BiasCci;
        _worseFor = 0;
        _recentCount = 0;
    }

    public void Reset()
    {
        _recentCount = 0;
        BiasCci = Vec.Zero;
        _walkedBias = Vec.Zero;
        Settled = false;
        _bestMiss = double.PositiveInfinity;
        _bestBias = Vec.Zero;
        _worseFor = 0;
        _response = 1.0 / Gain;
        _lastBias = Vec.Zero;
        _lastError = Vec.Zero;
        _haveLast = false;
    }
}
