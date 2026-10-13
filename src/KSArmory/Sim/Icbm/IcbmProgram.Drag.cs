using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    /// <summary>How close to cutoff <see cref="IcbmConfig.ShortShotSolvesWithDrag"/> starts flying arcs.</summary>
    public const double DragSolveWithinSeconds = 20.0;

    private const int DragIterations = 2;
    private const double DragStepSeconds = 1.0;

    /// <summary>How far the drag solve has moved the aim, in metres; zero when it has not.</summary>
    public double DragOffsetMetres => Math.Sqrt(_dragEast * _dragEast + _dragNorth * _dragNorth);

    /// <summary>Where the drag solve's last arc lands from the target, in metres; NaN when it has not flown one.</summary>
    public double DragMissMetres { get; private set; } = double.NaN;

    private double _dragEast;
    private double _dragNorth;
    private bool _dragEngaged;

    // Not for a solid that is absorbing what it cannot help adding: there the flight time is re-picked
    // whenever the aim moves, so moving the aim does not move the landing one for one.
    // Nor on an arc that climbs above the release altitude: released there, the aim correction after
    // cutoff answers drag, and an offset baked into the burn is only trim spent taking it back out.
    private bool SolvesWithDrag(in IcbmState state)
        => Config.ShortShotSolvesWithDrag && Config.FlyAnyRange && _shortShot && !_paused
           && Phase == IcbmPhase.ClosedLoop && !(_unavoidable > 0.0)
           && state.DensityRatioAt is not null && state.Warhead is not null
           && StaysUnderTheReleaseAltitude(state);

    // A point moved east and north on the ground, kept at its own radius.
    private static double3 Offset(double3 aimCci, double east, double north, BallisticBody body)
    {
        if (east == 0.0 && north == 0.0) return aimCci;
        (double3 e, double3 n) = EastNorth(aimCci, body);
        return Vec.Unit(aimCci + e * east + n * north) * Vec.Len(aimCci);
    }

    private static (double3 East, double3 North) EastNorth(double3 pointCci, BallisticBody body)
    {
        double3 up = Vec.Unit(pointCci);
        double3 east = Vec.Unit(Vec.Cross(Vec.Unit(body.SpinAxisCci), up));
        return (east, Vec.Cross(up, east));
    }

    // The arc flown with the warhead's drag from the cutoff it was solved from, against the target at
    // the same instant the solve carried it to: a target at any other epoch is wrong by the ground's
    // turn over the difference, 465 m/s on the equator.
    private static bool TryDragMiss(in IcbmState state, in BurnoutGuidance.Command command, out double3 missCci)
    {
        missCci = default;
        if (!double.IsFinite(command.CarrySeconds)) return false;

        // With the tube's throw, as the warhead leaves: 0.5 m/s along the line is 100-190 m at these ranges,
        // and flown without it every low arc landed that much long.
        if (!ImpactPredictor.TryPredict(state.Body, command.CutoffPositionCci + state.ReleaseOffsetCci,
                                        command.Arc.RequiredVelocityCci + state.ReleaseImpulseCci,
                                        DragStepSeconds, ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                        drag: new ImpactPredictor.Drag(state.DensityRatioAt!, state.Warhead!)))
        {
            return false;
        }

        double3 target = state.Body.CarryCci(state.AimNowCci, command.CarrySeconds);
        double3 miss = hit.GroundFixedPointCci - target;
        double3 up = Vec.Unit(target);
        missCci = miss - up * Vec.Dot(miss, up);
        return Vec.IsFinite(missCci);
    }

    // Moved by what the drag-flown arc misses by and solved again, inside this pass: with the arrival
    // pinned, the landing follows the aim about one for one. An offset is kept only if it lands nearer,
    // and it keeps moving to cutoff: the offset a shot needs changes at hundreds of metres a second as
    // cutoff nears, and frozen under a slowed line that lasted to cutoff, a flown miss grew from 0 to
    // 856 m in the last 0.8 s.
    private BurnoutGuidance.Command SolveWithDrag(in IcbmState state, BurnoutGuidance.Command command,
                                                  double arrivalFromNow)
    {
        _dragEngaged = true;
        if (!TryDragMiss(state, command, out double3 miss)) return command;
        DragMissMetres = Vec.Len(miss);

        // A guard against a projection gone wrong, not a bound on what drag can cost: a stack that cuts
        // off at 19 km needs more than 15 km at 150 km, and capped there it landed 3.3 km out.
        double limit = Math.Max(40_000.0, 0.25 * Vec.Len(state.AimNowCci - state.PositionCci));

        for (int i = 0; i < DragIterations; i++)
        {
            (double3 e, double3 n) = EastNorth(state.Body.CarryCci(state.AimNowCci, command.CarrySeconds), state.Body);
            double east = _dragEast - Vec.Dot(miss, e);
            double north = _dragNorth - Vec.Dot(miss, n);

            double length = Math.Sqrt(east * east + north * north);
            if (length > limit)
            {
                east *= limit / length;
                north *= limit / length;
            }

            if (!BurnoutGuidance.TrySteer(state.Body, state.PositionCci, state.VelocityCci,
                                          Offset(state.AimNowCci, east, north, state.Body), state.Booster,
                                          out BurnoutGuidance.Command trial, Config.Loft, LongWay, _cutoffSeed,
                                          _flightSeed, arrivalFromNow, 0.0, _unavoidable)
                || !TryDragMiss(state, trial, out double3 trialMiss)
                || Vec.Len(trialMiss) >= Vec.Len(miss))
            {
                break;
            }

            _dragEast = east;
            _dragNorth = north;
            command = trial;
            miss = trialMiss;
            DragMissMetres = Vec.Len(miss);
        }

        return command;
    }
}
