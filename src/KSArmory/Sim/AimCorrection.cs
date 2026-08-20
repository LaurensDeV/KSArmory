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
    /// <summary>How much of each measured error is taken out. Below one, or it rings.</summary>
    public const double Gain = 0.5;

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

    /// <summary>What is currently being added to the aim point, in the body's inertial frame.</summary>
    public double3 BiasCci { get; private set; }

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

        // An error this large is not a terrain effect being trimmed out — it is an arc that has not
        // been aimed yet, which is every cycle before the solve settles and all of them for a
        // vehicle picked up mid-flight. Folding one in moves the accumulated bias straight into its
        // own limit, and a bias pinned there feeds the solver a displaced aim point, which produces
        // an arc whose error keeps it pinned. The loop can then no longer tell converged from stuck.
        //
        // Rejecting the observation rather than clamping what it accumulates is what separates the
        // two: a shot that genuinely cannot be made goes on producing errors this large, none of
        // them are folded in, the bias stays where it was and the miss is reported honestly.
        //
        // Flown at 3,459 km from a near-orbital pickup: pinned at the 300 km limit with 229 km of
        // miss, against 29 km of bias and 0.06 km of miss once these are ignored.
        if (Vec.Len(error) > MaxMetres) return;

        BiasCci = Vec.ClampLength(BiasCci - error * Gain, MaxMetres);
    }

    public void Reset() => BiasCci = Vec.Zero;
}
