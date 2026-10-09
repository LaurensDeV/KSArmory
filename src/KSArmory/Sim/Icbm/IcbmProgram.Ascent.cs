using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    private IcbmCommand Rising(in IcbmState state)
    {
        bool clear = state.Altitude >= AscentProfile.VerticalRiseMetres
                  || Vec.Len(state.AirflowCci) >= AscentProfile.VerticalRiseSpeed;

        if (clear) Phase = IcbmPhase.PitchProgram;

        return Fly(Phase, state.UpCci, state, "vertical rise");
    }

    // KSA's drag acts at the vehicle's centre with no turning moment -- the only aerodynamic torque
    // damps rotation -- and structural failure depends on g-load alone, so an angle of attack costs
    // drag and nothing else in this build. A short shot steers freely: the eight degrees that suit a
    // long ascent are exactly what stops a core lit in thick air pointing where the shot needs.
    // RocketWerkz are reworking aerodynamics; docs/BLOCKED-ON-KSA.md.
    private double AllowedAngleOfAttackDeg(in IcbmState state)
        => _shortShot ? 180.0 : Config.MaxAngleOfAttackDeg;

    // A coast that never climbs to the release altitude has nowhere to wait for a release: the gate
    // opens on the way down, inside the air, after a hold the trim may not finish. Flown at 418 km
    // with an 89 km apogee, that released 44 s before impact and landed 10 km out.
    private bool StaysUnderTheReleaseAltitude(in IcbmState state)
        => Arc is { } arc && arc.ApogeeRadius - state.Body.SurfaceRadius < Config.DeployAltitudeMetres;


    // What the running stage will add whether it is told to stop or not: all of what a solid motor
    // has left. The arc is lofted until it needs at least that, which is the only thing that can be
    // done with velocity that cannot be refused. A stage that can stop is never absorbed: it is cut
    // off when the shot is complete, wherever that is.
    private double Unavoidable(in IcbmState state)
    {
        if (!Config.FlyAnyRange || state.RunningStageCanStop || !state.Booster.CanThrust) return 0.0;

        double solid = double.IsFinite(state.RunningStageDeltaV) && state.RunningStageDeltaV >= 0.0
                           ? state.RunningStageDeltaV
                           : state.Booster.DeltaVRemaining;

        return solid + Math.Max(Config.SolidsLeaveMetresPerSecond, 0.0);
    }

    private IcbmCommand PitchProgram(in IcbmState state)
    {
        // A short shot can top out inside the air, and then no thin air is coming: past the top of
        // the climb the closed loop is the only phase that can still cut off.
        bool clear = state.Altitude >= Config.TurnStartMetres;
        bool climbing = Vec.Dot(state.VelocityCci - state.Body.GroundVelocityCci(state.PositionCci), state.UpCci) > 0.0;
        bool climbOver = Config.FlyAnyRange && clear && !climbing;

        // A stage that can stop and is within the reserve of finishing the shot hands over now: the
        // closed loop cuts it off at the right instant, in the air if that is where the shot is done.
        // Waiting for thin air instead is what overshot -- the schedule cannot cut off, a floor still
        // pushes, and in thick air the stack cannot point where a lofted arc wants it.
        bool complete = Config.FlyAnyRange && clear && state.RunningStageCanStop
                        && ReserveBinds(ThrottleUnderAccelerationCap(1.0, state), state);

        if (climbOver || complete
            || (state.DynamicPressurePa <= Config.HandoverPressurePa && clear))
        {
            _handedOverInTheAir = (climbOver || complete) && state.DynamicPressurePa > Config.HandoverPressurePa;
            _shortShot |= _handedOverInTheAir;
            Phase = IcbmPhase.ClosedLoop;
            _lineCarriedOver = true;
            return ClosedLoop(state);
        }

        double pitch = AscentProfile.PitchDegreesAt(state.Altitude, Config.TurnStartMetres, Config.TurnEndMetres);

        // Matching a solid means a lofted arc, and the schedule would pitch the stack below it. Held
        // at the arc's own climb from above, the path is never kicked over and left to sag: at a
        // gravity and a bit of thrust, eight degrees of attack cannot lift it back.
        if (_absorbing && Arc is { } lofted)
        {
            double3 relative = lofted.RequiredVelocityCci - state.Body.GroundVelocityCci(state.PositionCci);
            double climb = double.RadiansToDegrees(Math.Asin(Math.Clamp(
                Vec.Dot(Vec.Unit(relative), state.UpCci), -1.0, 1.0)));
            pitch = Math.Max(pitch, climb);
        }

        double3 wanted = AscentProfile.Aim(state.UpCci, DownrangeCci, pitch);

        // Steered along what is left, the solids leave their margin ahead of the nose, where the stage
        // after them finishes it without turning; flown on the schedule, it lay up to 67 deg off it.
        if (Config.SolidsLeaveMetresPerSecond > 0.0 && _absorbing && clear && !_toGainVectorCci.Equals(Vec.Zero))
        {
            return Fly(IcbmPhase.PitchProgram, Limit(_toGainVectorCci, state), state, "solids steered onto the arc");
        }

        // Once the reserve binds, the schedule is pointing somewhere the shot no longer needs to go:
        // what is left to gain is the only direction that does not add to it.
        if (!Config.FlyAnyRange && ReserveBinds(ThrottleUnderAccelerationCap(1.0, state), state)
            && !_toGainVectorCci.Equals(Vec.Zero))
        {
            return Fly(IcbmPhase.PitchProgram, Limit(_toGainVectorCci, state), state, "pitch programme, held back");
        }

        return Fly(IcbmPhase.PitchProgram, Limit(wanted, state), state, $"pitch programme, {pitch:F0} deg");
    }
}
