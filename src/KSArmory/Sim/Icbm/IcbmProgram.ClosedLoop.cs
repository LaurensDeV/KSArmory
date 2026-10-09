using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    // Never looser than HoldDirectionBelow, so a stack burning at full thrust holds where it
    // always did and only a throttled-down one holds later.
    private double HoldDirectionThreshold(in IcbmState state)
    {
        double frame = state.Booster.AccelerationNow * _lastStep
                     * Math.Clamp(state.ThrottleAchieved, 0.0, 1.0);

        if (!(frame > 0.0)) return HoldDirectionBelow;

        return Math.Min(HoldDirectionBelow, HoldDirectionFrames * frame);
    }

    /// <summary>How fast the thrust line may turn under <see cref="IcbmConfig.ShortShotSlowsLineSeconds"/>.</summary>
    public const double SlowLineDegPerSec = 5.0;

    // Only on an arc that stays under the release altitude, like the in-air finish it was built for: a
    // higher arc pauses and relights, the relight's first frame is solved on the line slowed through the
    // coast, and it cuts off at once. Flown, 700 km cut off 73 m/s short and 1,000 km landed 1.0 km out
    // against millimetres with the line followed.
    private bool SlowsTheLine(in IcbmState state)
        => Config.ShortShotSlowsLineSeconds > 0.0 && Config.FlyAnyRange && _shortShot
           && Phase == IcbmPhase.ClosedLoop && state.RunningStageCanStop && StaysUnderTheReleaseAltitude(state);

    private const double AlignBeforeBurningDeg = 15.0;

    // How far the airflow may hold the thrust off what is left to gain before burning stops helping.
    private const double UsefulThrustDeg = 30.0;

    private IcbmCommand ClosedLoop(in IcbmState state)
    {
        bool climbing = Vec.Dot(state.VelocityCci - state.Body.GroundVelocityCci(state.PositionCci), state.UpCci) > 0.0;
        bool thickAir = state.DynamicPressurePa > Config.HandoverPressurePa;

        // Under ShortShotFinishesInTheAir an arc that climbs above the release altitude coasts out until a
        // cutoff would not release in the air at once. Under HandoverPressurePa alone it can still cut off
        // where the air counts: flown at 700 km, at 64 km, released from a stack still turning, with kicks
        // of 0.5-0.66 m/s against a 10 mm/s cap, 0.36 km out; cut off at 98 km the same shot landed 2.2 mm.
        bool highArc = Config.ShortShotFinishesInTheAir && !StaysUnderTheReleaseAltitude(state);
        bool inTheAir = thickAir || (highArc && state.AirDensityRatio >= Medium.NoticeableDensity);

        // A stage that can stop and has finished the shot in thick air pauses instead of ending: the
        // stack coasts up out of the air with the loop still solving, and the engine lights again in
        // thin air -- or at the top of the climb -- to take out what drag cost on the way. The cutoff
        // that counts is then made where the vacuum arc is true.
        if (_paused && (!inTheAir || !climbing))
        {
            _paused = false;
            _lineCarriedOver = true;
            _lowestToGain = double.PositiveInfinity;
        }

        if (_paused)
        {
            _waitingForAttitude = true;
            return Fly(IcbmPhase.ClosedLoop, Limit(_thrustDirCci.Equals(Vec.Zero) ? Vec.Unit(state.VelocityCci) : _thrustDirCci, state),
                       state, "coasting out of the air");
        }

        // Under ShortShotFinishesInTheAir only an arc that stays under the release altitude finishes in the
        // air. One that climbs above it still pauses and relights to cut off above the air, where the
        // long-shot release gives millimetres: released at once in the air instead, 700 km landed 0.80 km
        // out, and coasted on the bus alone through the air to the release altitude, 500 km landed 107 km
        // out with 287 m/s of drag owed to the trim.
        if (ShouldCutOff(state) && Config.FlyAnyRange && _shortShot && state.RunningStageCanStop && inTheAir && climbing
            && !(Config.ShortShotFinishesInTheAir && StaysUnderTheReleaseAltitude(state)))
        {
            _paused = true;
            _waitingForAttitude = true;
            return Fly(IcbmPhase.ClosedLoop, Limit(_thrustDirCci.Equals(Vec.Zero) ? Vec.Unit(state.VelocityCci) : _thrustDirCci, state),
                       state, "coasting out of the air");
        }

        if (ShouldCutOff(state))
        {
            _releasesAtCutoff = _shortShot && (state.AirDensityRatio >= Medium.NoticeableDensity || StaysUnderTheReleaseAltitude(state));

            // Recorded here rather than in Coasting, which clears it. What was left when the
            // engines stopped is the whole story of a shot that lands short on an otherwise
            // perfect trajectory, and reporting a zero says every burn closed perfectly - which is
            // exactly what a burn that ended forty metres a second early also says.
            ResidualAtCutoff = _toGain;
            ResidualVectorCci = _toGainVectorCci;
            AccelerationAtCutoff = state.Booster.AccelerationNow;
            StepAtCutoff = _lastStep;
            ThrottleAtCutoff = state.ThrottleAchieved;
            Phase = IcbmPhase.Coast;
            return Coasting(state);
        }

        _secondsInTheRamp = _countdown < ThrottleDownSeconds ? _secondsInTheRamp + _lastStep : 0.0;
        _throttle = ThrottleDownSeconds > 0.0 && _countdown < ThrottleDownSeconds
                  ? Math.Clamp(_countdown / ThrottleDownSeconds + PushAlongTheLine(state), MinCommandedThrottle, 1.0)
                  : 1.0;

        double3 wanted = _thrustDirCci.Equals(Vec.Zero) ? Vec.Unit(state.VelocityCci) : _thrustDirCci;

        // A short burn is over before a slow vehicle has turned to it, and every second spent pushing
        // the wrong way moves what is left to gain: the loop then chases its own tail until the stage
        // is dry. So the engine waits for the vehicle to come round. A long burn barely notices.
        double3 limited = Limit(wanted, state);

        if (_shortShot && state.RunningStageCanStop)
        {
            // Where the airflow will not let the vehicle point usefully, burning along the limited line
            // only adds to what is left. Climbing, the air thins and the allowance opens, so it waits;
            // with the climb over, or so little left that the bus trim can take it, it stops here.
            bool usable = Vec.AngleBetween(limited, wanted) <= double.DegreesToRadians(UsefulThrustDeg);

            if (!usable && (!climbing || _toGain <= BusTrim.MaxMetresPerSecond))
            {
                _releasesAtCutoff = state.AirDensityRatio >= Medium.NoticeableDensity || StaysUnderTheReleaseAltitude(state);
                ResidualAtCutoff = _toGain;
                ResidualVectorCci = _toGainVectorCci;
                AccelerationAtCutoff = state.Booster.AccelerationNow;
                StepAtCutoff = _lastStep;
                ThrottleAtCutoff = state.ThrottleAchieved;
                Phase = IcbmPhase.Coast;
                return Coasting(state);
            }

            _waitingForAttitude = !usable
                                  || (!state.ThrustAxisCci.Equals(Vec.Zero)
                                      && Vec.AngleBetween(state.ThrustAxisCci, limited) > double.DegreesToRadians(AlignBeforeBurningDeg));
        }

        return Fly(IcbmPhase.ClosedLoop, limited, state,
                   _secondsInTheRamp > StalledRampSeconds
                       ? $"cutoff stalled: the stack is holding its own weight, {_toGain:F0} m/s still to gain"
                       : "guiding to cutoff");
    }

    // Cutting off is a timing problem, not a threshold one. An engine can only be shut down on a
    // frame boundary, and a light upper stage at ten gravities changes its velocity by more in one
    // frame than any sensible tolerance allows - so waiting for the velocity still to gain to fall
    // below a fixed number waits for something that cannot happen. It overshoots instead, turns
    // round to brake, overshoots the other way, and burns the stage dry hunting.
    //
    // So: stop when less than half a frame of burning is left, which puts the cutoff at the frame
    // boundary nearest the ideal instant and leaves the residual symmetric. The rising-again test
    // behind it is the backstop for a solve that never converges at all.
    // A proportional ramp settles where it asks for exactly what pushes against it: two seconds of the stack's drag
    // left to gain, which on a liquid stack in the air never reaches the cutoff. docs/SHORT-RANGE.md.
    private bool PushesThrough(in IcbmState state)
        => Config.ShortShotPushesThroughAStall && _shortShot && Phase == IcbmPhase.ClosedLoop
           && _secondsInTheRamp > PushesThroughAfterSeconds && state.AirDensityRatio >= Medium.NoticeableDensity
           && !_pushCci.Equals(Vec.Zero);

    private double PushAlongTheLine(in IcbmState state)
        => PushesThrough(state) && state.Booster.AccelerationNow > 0.0
           ? Math.Max(Vec.Dot(_pushCci, _thrustDirCci), 0.0) / state.Booster.AccelerationNow
           : 0.0;

    // Pointed ahead of what is left by what the push will add across it before the burn ends.
    private double3 LeadAcrossThePush(in IcbmState state, in BurnoutGuidance.Command command)
    {
        double3 along = Vec.Unit(command.ToGainVectorCci);
        double3 across = _pushCci - along * Vec.Dot(_pushCci, along);
        double closing = Math.Max(state.Booster.AccelerationNow * Math.Clamp(state.ThrottleAchieved, state.MinThrottle, 1.0), 1e-3);
        double3 led = Vec.Unit(command.ToGainVectorCci + across * (command.VelocityToGain / closing));
        return led.Equals(Vec.Zero) ? command.ThrustDirectionCci : led;
    }

    private bool ShouldCutOff(in IcbmState state)
    {
        if (_toGain <= BurnoutGuidance.CutoffMetresPerSecond) return true;

        // Against the throttle the vehicle actually has, never the one that was asked for. A stack
        // whose motors do not throttle, or one still ramping down, would otherwise be cut off on a
        // prediction of how much velocity the last frame adds that is several times too small.
        double achieved = Math.Clamp(state.ThrottleAchieved, 0.0, 1.0);
        if (_countdown <= 0.5 * _lastStep * Math.Max(achieved, 1e-3)) return true;

        double oneStep = state.Booster.AccelerationNow * _lastStep;
        return _lowestToGain < BackstopArmsBelow(state) && _toGain > _lowestToGain + Math.Max(oneStep, 1.0);
    }

    // A short shot that releases after the trim can hand it anything up to its reach. A 7 g core at its floor bottomed
    // out at 2.4 m/s with its thrust lagging the line, missed the 2 m/s backstop, and overshot to 53 m/s and back.
    private double BackstopArmsBelow(in IcbmState state)
        => Config.ShortShotBackstopsAtTheTrim && _shortShot && !StaysUnderTheReleaseAltitude(state)
               ? Math.Max(BackstopBelow, BusTrim.MaxMetresPerSecond)
               : BackstopBelow;
}
