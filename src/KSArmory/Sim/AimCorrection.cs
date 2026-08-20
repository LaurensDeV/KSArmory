using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The difference between where a shot is aimed and where it has to be aimed to arrive.
///
/// <para>The transfer solver is exact, and exact for the wrong thing: it puts the arc through a
/// <em>point</em>, in vacuum, and a round does not stop at a point — it stops where the ground is.
/// On a lofted shot those are nearly the same. On a shallow arrival they are not remotely, because
/// the arc covers something like twelve kilometres of ground per kilometre of height near the end,
/// so a target a few kilometres up is tens of kilometres from a solution that is otherwise
/// perfect.</para>
///
/// <para>Nothing about such a trajectory is wrong. It arrives exactly where it was asked to; the
/// asking was wrong. So the aim is moved by what the flown arc actually loses, which is a thing
/// only flying it can measure.</para>
///
/// <para><b>It is a feedback loop against a solver that then moves the arc</b>, so it takes a
/// fraction of the error at a time rather than all of it. Taking all of it overshoots, the solver
/// re-aims, and the pair oscillate instead of settling.</para>
/// </summary>
internal sealed class AimCorrection
{
    /// <summary>
    /// How much of each measured error is taken out before the loop has watched itself work.
    ///
    /// <para>Only the first step. After that the response is measured — see <see cref="Observe"/> —
    /// because the number that would be right here depends on a plant this loop does not choose.
    /// Low, because the first step is the one taken blind.</para>
    /// </summary>
    public const double Gain = 0.25;

    /// <summary>
    /// The range the measured response is trusted within, as impact moved per unit of aim moved.
    ///
    /// <para>Outside it the estimate is noise rather than a plant: two observations a fraction of a
    /// kilometre apart divide by nearly nothing, and one bad reading would otherwise size every
    /// step after it. Clamping is what lets the estimate be taken from consecutive cycles instead
    /// of from a deliberate probe.</para>
    ///
    /// <para><b>The lower bound is one, and it is the load-bearing end.</b> A response under one
    /// says moving the aim barely moves the impact, which asks for a step <em>larger</em> than the
    /// miss being removed — so a single noisy reading buys an enormous wrong move. Flown at a
    /// quarter: 28.6 km of bias to 192.9 in one cycle, then the 300 km clamp and 205 km of miss.
    /// Never stepping further than the error is what makes an estimate taken from two consecutive
    /// cycles safe to use at all.</para>
    /// </summary>
    public const double MinResponse = 1.0;

    /// <inheritdoc cref="MinResponse"/>
    public const double MaxResponse = 6.0;

    /// <summary>How far the aim must have moved for the response it produced to mean anything.</summary>
    public const double ResponseFromMetres = 500.0;

    /// <summary>
    /// The furthest the aim may be moved.
    ///
    /// <para>A correction larger than this is not a terrain effect being trimmed out; it is a shot
    /// that cannot be made, and walking the aim across a continent chasing it turns a visible miss
    /// into a wild one.</para>
    ///
    /// <para>It bounds the <em>observation</em> as well as the accumulation, and that is the load
    /// bearing half — see <see cref="Observe"/>. A limit on the accumulation alone is an integrator
    /// clamp, and one bad reading pins it there for the rest of the flight.</para>
    /// </summary>
    public const double MaxMetres = 300_000.0;

    /// <summary>
    /// How much closer the impact must come for a cycle to count as an improvement, and how many
    /// cycles may make it worse before the loop stops. Two of them, because one bad reading is
    /// noise and a run of them is a direction.
    /// </summary>
    public const double ImprovedByMetres = 250.0;

    /// <inheritdoc cref="ImprovedByMetres"/>
    public const int WorseBeforeStopping = 3;

    /// <summary>How little the aim may move for the correction to count as having stopped.</summary>
    public const double SteadyMetres = 2_000.0;

    /// <summary>What is currently being added to the aim point, in the body's inertial frame.</summary>
    public double3 BiasCci { get; private set; }

    /// <summary>
    /// Whether the aim has stopped moving, either by converging or by giving up.
    ///
    /// <para>What the guidance waits for before it commits to an arrival time. Both loops are
    /// solving the same shot, and pinning one while the other is still moving makes the second
    /// solve against the first's leftovers.</para>
    /// </summary>
    public bool IsSteady => Settled || (_haveLast && Vec.Len(BiasCci - _lastBias) < SteadyMetres);

    /// <summary>
    /// Whether the loop has stopped because it could not improve on its own best.
    ///
    /// <para>Not a failure: it means the aim is as good as this correction can make it, and what
    /// remains is a shot that wants a different trajectory rather than a different aim.</para>
    /// </summary>
    public bool Settled { get; private set; }

    private double _response = 1.0 / Gain;
    private double3 _lastBias;
    private double3 _lastError;
    private bool _haveLast;
    private double _bestMiss = double.PositiveInfinity;
    private double3 _bestBias;
    private int _worseFor;

    /// <summary>
    /// Where to actually aim, given where the shot is meant to land.
    ///
    /// <para>Kept on the target's own radius. The bias is a free vector and the correction is a
    /// displacement <em>along the ground</em>, so adding it raw walks the aim off the surface and
    /// asks the solver for an arc to a point underground, which it rightly refuses.</para>
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
    public void Observe(double3 landedCci, double3 targetCci)
    {
        if (!Vec.IsFinite(landedCci) || !Vec.IsFinite(targetCci)) return;

        // Against the target, never against the aim the correction itself produced. Scoring a
        // correction on its own output reports a perfect shot however far the rounds land.
        double3 error = landedCci - targetCci;
        if (!Vec.IsFinite(error)) return;

        // Stopped means stopped. Without this the loop goes on integrating after it has decided
        // not to, and walks away from the best aim it just chose to keep.
        if (Settled) return;

        // It is a feedback loop and the plant is not always the one the gain was chosen for.
        // Moving the aim moves the impact by about as much again while the solver may pick its own
        // flight time; once the arrival is latched, the same aim change forces a different
        // trajectory to arrive at the same instant, and on a shallow near-orbital shot that
        // amplifies the response past where a gain of a half is stable. The loop then walks away
        // from its own best while the miss it is removing grows.
        //
        // Flown at 3,459 km from a near-orbital pickup: 55.1 km of miss down to 43.7 at 77 km of
        // bias, then 44.7, 47.9, 65.2, 126.1, and pinned at the 300 km limit with 209 km of miss.
        // The same loop converges in eight cycles when the flight time is free, which is why this
        // is not a gain that is too high but a plant that changes underneath one.
        //
        // So: keep the best aim found and stop when it cannot be improved on. What remains after
        // that is a shot that wants a different trajectory rather than a different aim, which is
        // the thing the miss should be reporting.
        // The step is sized by what the aim was last measured to be worth, not by a fixed fraction.
        //
        // A fixed fraction is only right for a fixed plant, and this one changes underneath the
        // loop: while the solver may pick its own flight time, moving the aim moves the impact by
        // about as much again, and a half converges. Once the guidance latches the arrival the same
        // aim change forces a different trajectory to arrive at the same instant, and on a shallow
        // near-orbital arrival the impact moves several times further — at which point a half is
        // above the stability limit and the loop walks away from its own best.
        //
        // Measuring it needs no probe: every cycle already moves the aim and sees what the impact
        // did, which is the same secant a Newton step is built from.
        double3 movedAim = BiasCci - _lastBias;
        double movedBy = Vec.Len(movedAim);

        if (movedBy >= ResponseFromMetres && _haveLast)
        {
            double impactAlong = Vec.Dot(error - _lastError, Vec.Unit(movedAim));

            // Only a response in the direction the aim was pushed. A negative one means the impact
            // went the other way, which is a plant nothing here models, and the safe reading of it
            // is "do not trust the estimate" rather than "invert the loop".
            if (impactAlong > 0.0)
            {
                _response = Math.Clamp(impactAlong / movedBy, MinResponse, MaxResponse);
            }
        }

        _lastBias = BiasCci;
        _lastError = error;
        _haveLast = true;

        double miss = Vec.Len(error);

        if (miss < _bestMiss - ImprovedByMetres)
        {
            _bestMiss = miss;
            _bestBias = BiasCci;
            _worseFor = 0;
        }
        else if (miss > _bestMiss + ImprovedByMetres && ++_worseFor >= WorseBeforeStopping)
        {
            BiasCci = _bestBias;
            Settled = true;
            return;
        }

        BiasCci = Vec.ClampLength(BiasCci - error / _response, MaxMetres);
    }

    /// <summary>
    /// Stop correcting and keep the best aim found.
    ///
    /// <para>Called when the guidance commits to an arrival. Until then the arc follows the aim and
    /// the loop converges; from then on the same aim change forces a different trajectory to arrive
    /// at the same instant, which is the plant it cannot hold. Flown: 4.9 km of predicted miss
    /// while free, degrading to 13.4 km by cutoff if it keeps going.</para>
    /// </summary>
    public void Freeze()
    {
        if (Settled) return;

        if (double.IsFinite(_bestMiss)) BiasCci = _bestBias;
        Settled = true;
    }

    /// <summary>
    /// Start correcting again from the aim already found, against a plant that has changed.
    ///
    /// <para>Called when the engines stop. Everything the loop learned during the burn was learned
    /// about a shot still being flown, and none of it transfers: the response was whatever the
    /// guidance's own re-solving made it, and the record of which cycles helped was kept against
    /// that. What does transfer is the aim itself, which is most of the answer.</para>
    ///
    /// <para>The response is seeded at one because that is what the coast plant actually is — the
    /// arc is re-solved to the aim at a fixed arrival, so the impact moves by about what the aim
    /// did. Seeding it wrong costs the first step, which is the expensive one.</para>
    /// </summary>
    public void Resume()
    {
        if (!Settled) return;

        Settled = false;
        _bestMiss = double.PositiveInfinity;
        _bestBias = BiasCci;
        _worseFor = 0;
        _response = 1.0;
        _haveLast = false;
    }

    public void Reset()
    {
        BiasCci = Vec.Zero;
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
