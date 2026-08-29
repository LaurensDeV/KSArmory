using Brutal.Numerics;

namespace KSArmory.Tests;

/// <summary>
/// The post-cutoff loop, run headlessly: the clearance, the sequencer, the trim and the correction
/// in the order <c>Ksa/IcbmComputer.cs</c> runs them, against a bus that moves when it is pushed.
///
/// <para><b>The point is the feedback, not the pieces.</b> Each of those was already testable
/// alone. What could only be reached by flying is that they drive each other: the decoupler's shove
/// is the only thing carrying the halves apart, nulling it is precisely what the trim is for, and
/// so the trim closes the gap that licenses it. Flown, that abandoned <b>87 of 144</b> corrections
/// and cost the whole aim correction each time — <c>docs/MIRV-NEXT.md</c> <b>8y</b>.</para>
///
/// <para>The bus is a point mass with a thruster, which is the whole of what the loop can observe
/// about it. What this rig deliberately does <em>not</em> model is the part tree, the engine's
/// frame ordering and the attitude — so a result here is about the loop's logic and never about
/// whether KSA will fly it.</para>
/// </summary>
internal sealed class PostCutoffRig
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    public BallisticBody Body = new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    /// <summary>Where the bus is, and the reference it is supposed to be on.</summary>
    public double3 PositionCci = new(R + 600_000.0, 0, 0);
    public double3 ReferenceVelocityCci = new(0, 7_000.0, 0);

    /// <summary>What the decoupler left the bus doing, relative to the solution.</summary>
    public double3 ShoveCci = new(1.1, 0, 0);

    /// <summary>How hard the thrusters push, and how coarsely the world is stepped.</summary>
    public double AccelerationMetresPerSecond2 = 0.35;
    public double StepSeconds = 0.066;

    /// <summary>The stack's bounding sphere, which is what the clearance is measured against.</summary>
    public double StageRadiusMetres = 5.3;

    /// <summary>Whether the first pass may spend the budget — <see cref="IcbmConfig.TrimCeilingFromBudget"/>.</summary>
    public bool CeilingFromBudget;

    public double BudgetMetresPerSecond = PostBoostAim.MaxTrimMetresPerSecond;

    /// <summary>What one run came to.</summary>
    internal readonly record struct Outcome(
        bool Abandoned,
        bool TrimFinished,
        double ResidualMetresPerSecond,
        double SecondsRun,
        double ClosestApproachMetres,
        string Said);

    /// <summary>
    /// Run the loop until it ends, and say how.
    ///
    /// <para>The gap between the halves is integrated from their relative velocity rather than
    /// scripted, which is the whole point: the trim's own firing is what changes it.</para>
    /// </summary>
    public Outcome Run(double maxSeconds = 90.0)
    {
        BusTrim trim = new();
        double3 velocity = ReferenceVelocityCci + ShoveCci;

        // The stack is left where the split happened; the bus carries the shove away from it.
        double3 apartCci = Vec.Zero;
        double since = 0.0;
        double closest = double.PositiveInfinity;
        Clearance clearance = default;
        TrimCommand command = default;

        trim.Begin();

        while (since < maxSeconds)
        {
            double apart = Vec.Len(apartCci);
            closest = Math.Min(closest, apart);

            clearance = SeparationClearance.Check(apart, StageRadiusMetres, since);

            PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
                clearance.IsClear, clearance.Abandoned, postBoostCycles: 0,
                BudgetMetresPerSecond, trim.SpentMetresPerSecond, CeilingFromBudget);

            if (plan.Abandon)
            {
                return new Outcome(true, false, Vec.Len(velocity - ReferenceVelocityCci),
                                   since, closest, clearance.Said);
            }

            command = trim.Update(StepSeconds, new TrimSituation(
                Body, PositionCci, velocity,
                PositionCci, ReferenceVelocityCci, SecondsSinceReference: 0.0,
                NoseCci: new double3(1, 0, 0),
                RightCci: new double3(0, 1, 0),
                DownCci: new double3(0, 0, 1),
                MayFire: plan.MayTrim,
                BudgetMetresPerSecond: BudgetMetresPerSecond,
                KeepOutTowardCci: Vec.Zero,
                MaxMetresPerSecond: plan.CeilingMetresPerSecond));

            velocity += Thrust(command.Fire) * (AccelerationMetresPerSecond2 * StepSeconds);

            // The halves part at whatever the bus is doing relative to the solution the stack was
            // left on. Nulling that is what shuts the gate the trim needs open.
            apartCci += (velocity - ReferenceVelocityCci) * StepSeconds;
            since += StepSeconds;

            if (command.Done) break;
        }

        return new Outcome(false, command.Done, Vec.Len(velocity - ReferenceVelocityCci),
                           since, closest, command.Said);
    }

    // The bus's own control axes, as the rig lays them out. One direction at a time, which is what
    // BusTrim commands and what makes its stop threshold readable along the axis being fired.
    //
    // Forward is +Nose, not -Nose: BusTrim picks Forward when the velocity it still has to GAIN
    // points along the nose, and pushes that way. Inverting it gives a rig whose trim drives the
    // error outward while every test still passes -- caught by the residual growing from a 1.1 m/s
    // shove to 2.5, which is the one reading that could not be explained by a working loop.
    private static double3 Thrust(TrimAxes fire)
        => fire switch
        {
            TrimAxes.Forward => new double3(1, 0, 0),
            TrimAxes.Backward => new double3(-1, 0, 0),
            TrimAxes.Right => new double3(0, 1, 0),
            TrimAxes.Left => new double3(0, -1, 0),
            TrimAxes.Down => new double3(0, 0, 1),
            TrimAxes.Up => new double3(0, 0, -1),
            _ => Vec.Zero,
        };
}
