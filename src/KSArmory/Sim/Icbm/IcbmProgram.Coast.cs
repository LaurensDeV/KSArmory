using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    /// <summary>
    /// Ask for the arc to be re-solved from where the bus is now, to the aim it has now.
    ///
    /// <para>What turns the aim correction from a readout back into a lever after the engines stop.
    /// The warheads coast along whatever arc the bus is on, so moving the aim alone changes nothing
    /// — re-solving the transfer to the corrected point gives the trim something to null onto, and
    /// the trim is the only thing left aboard that can still move the impact.</para>
    ///
    /// <para>The arrival stays where the burn committed it. Solving to a fixed instant is what makes
    /// the plant a plain one: the aim moves, the arc follows it, and the impact moves by about as
    /// much again.</para>
    /// </summary>
    public void CorrectCoastArc() => _resolveCoastArc = true;

    private void ResolveCoastArc(in IcbmState state)
    {
        double remaining = CommittedArrivalFromNow;
        if (!double.IsFinite(remaining)) return;

        // From the vehicle's own position, not from the cutoff state. The bus has coasted since, and
        // a velocity required at a point it has left is one the trim would spend propellant flying
        // back to.
        if (!BallisticArc.TrySolve(state.Body, state.PositionCci, state.AimNowCci, remaining,
                                   out BallisticArc.Solution corrected, LongWay))
        {
            return;
        }

        Arc = corrected;
        ReferencePositionCci = state.PositionCci;
        SecondsSinceReference = 0.0;
    }

    private IcbmCommand Coasting(in IcbmState state)
    {
        if (_resolveCoastArc)
        {
            _resolveCoastArc = false;
            ResolveCoastArc(state);
        }

        // Off the record, not _toGain: the line below zeroes that, so every coast frame after the
        // first would report the shortfall as nothing.
        double shortBy = _fellShort ? ResidualAtCutoff : 0.0;
        Phase = IcbmPhase.Coast;
        _toGain = 0.0;
        if (_fellShort) Reach = IcbmReach.ShortOfPropellant;

        // Late enough that the separation kick has little flight left to grow in, and the trim has
        // had the whole coast to converge. The altitude stays as the floor under it: it is what
        // stops a release inside the air, which the time alone would allow on a short shot.
        double toArrival = CommittedArrivalFromNow;
        double gate = ReleaseGate;
        bool closeEnough = gate <= 0.0 || !double.IsFinite(toArrival) || toArrival <= gate;

        // The altitude is a floor on the way *up* only. Applied on the descent it shuts the gate
        // exactly when the release is meant to happen -- the vehicle drops back through it on the
        // way to the target -- and the warheads ride the bus into the atmosphere still aboard.
        bool climbing = Vec.Dot(state.VelocityCci, state.UpCci) > 0.0;
        bool highEnough = !climbing || state.Altitude >= Config.DeployAltitudeMetres;

        // A short shot that stopped in the air lets its warheads go now: they fly from here on their
        // own drag, which the prediction models, where the whole stack coasting on would bend the arc
        // further than the bus can trim -- 58 m/s owed on the first 50 km shot flown.
        bool ready = !_fellShort && ((highEnough && closeEnough) || _releasesAtCutoff);

        // A burn that ended because the tanks did is not the same as one that ended because the
        // shot was complete, and the two are indistinguishable from every other number on the
        // panel. The warheads are held back as well as the message changing: releasing them on a
        // trajectory known to fall short spreads them across whatever is under the short fall.
        string hold = _fellShort
            ? !_everLit ? NothingEverLit()
            : $"burn ended {shortBy:F0} m/s short of the solution"
            : ready ? "coasting, warheads may be released"
            : closeEnough ? "coasting to release altitude"

            // Counted to the release rather than to the arrival. The arrival is already the
            // headline above this line, and it is not the thing being waited for: a coast ends
            // when the warheads go, minutes earlier.
            : $"holding the warheads, release in {Clock(toArrival - gate)}";

        // The line it was cut off on, not the airflow. The warheads leave along it, and a bus that
        // swings to prograde the moment the engines stop throws them off the solution it just spent
        // the whole burn arriving at.
        double3 held = _thrustDirCci.Equals(Vec.Zero) ? Vec.Unit(state.AirflowCci) : _thrustDirCci;

        return new IcbmCommand(IcbmPhase.Coast, held, 0.0, EngineOn: false,
                               RequestStage: false, VelocityToGain: shortBy, SecondsToCutoff: 0.0,
                               ReadyToDeploy: ready, Hold: hold, Reach: Reach,
                               SecondsToArrival: double.NaN, SecondsToBurn: double.NaN,
                               ShortfallMetresPerSecond: _fellShort ? shortBy : _shortfall);
    }
}
