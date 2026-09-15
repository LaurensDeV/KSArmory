using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where an unguided round has to be aimed to arrive where a moving target will be.
///
/// <para>A missile does not need this: it steers, so pointing the launcher at the target is
/// enough and the round closes the rest. A shell cannot, so everything the engagement needs has
/// to be in the barrel's direction at the instant it leaves — the target's motion during the
/// flight, and the drop over the same interval.</para>
///
/// <para>Neither term is small at cannon ranges. A 300 m/s target crosses ~1.4 km during a 4 km
/// shot, and the round falls ~100 m over the same 4.5 s.</para>
///
/// <para>Nor is the target's own acceleration once the flight is long. A lead that assumes the
/// target holds its velocity misses anything slowing or falling by half that acceleration times
/// the flight time squared: nothing over a CIWS's second, and hundreds of metres over a five-inch
/// gun's eight.</para>
/// </summary>
public static class BallisticLead
{
    // Time of flight depends on where the target will be, which depends on time of flight, so it
    // is solved by iterating to a fixed point.
    //
    // Run to a tolerance rather than a fixed pass count. The iteration contracts by roughly the
    // ratio of target speed to muzzle speed, so a fixed four passes is only enough while that
    // ratio is small -- which was true of the aircraft it was calibrated on and false of the
    // thing point defence exists for. Intercepting a missile at 576 m/s with a 956 m/s shell
    // leaves ~13% of the error after four passes, tens of metres of lead, and it amplifies the
    // frame-to-frame variation in the target's position instead of settling it.
    private const double ToleranceSeconds = 1e-7;

    // Bounded so a ratio near or above 1 cannot spin: past that the round never arrives and the
    // solve has no answer to converge on.
    private const int MaxPasses = 32;

    /// <summary>
    /// The point to aim at, in the same frame as the inputs. False if there is no solution —
    /// no muzzle speed, or a target so fast the round can never arrive.
    /// </summary>
    /// <remarks>
    /// Takes both velocities and differences them here rather than accepting a relative one.
    /// The subtraction carries the whole frame contract — both terms hold the planet's ~29.8 km/s
    /// around its star, the round is launched with the shooter's share already in it, and leading
    /// on the common part throws the aim point a hundred kilometres wide. Accepting the difference
    /// would leave that subtraction at a call site in <c>Ksa/</c>, which no test can reach.
    ///
    /// <para>This is the convention for the whole of <c>Sim/</c>: an entry point takes both
    /// frame-carrying terms and differences them itself. See docs/FRAMES-AND-EPOCHS.md.</para>
    /// </remarks>
    /// <param name="gravityEcl">Acceleration acting on the round, not on the shooter.</param>
    public static bool TrySolve(double3 shooterPos, double3 shooterVelocity,
                                double3 targetPos, double3 targetVelocity,
                                double muzzleSpeed, double3 gravityEcl, out double3 aimPoint)
        => TrySolve(shooterPos, shooterVelocity, targetPos, targetVelocity, muzzleSpeed, gravityEcl,
                    out aimPoint, out _);

    /// <summary>
    /// The same solve, also reporting the time of flight it converged on.
    ///
    /// <para>Handed out rather than recomputed by the caller: a timed fuse set from a second,
    /// separately derived number would burst somewhere the gun was not aiming.</para>
    /// </summary>
    public static bool TrySolve(double3 shooterPos, double3 shooterVelocity,
                                double3 targetPos, double3 targetVelocity,
                                double muzzleSpeed, double3 gravityEcl, out double3 aimPoint,
                                out double flightTimeSeconds)
        => TrySolve(shooterPos, shooterVelocity, targetPos, targetVelocity, Vec.Zero, muzzleSpeed,
                    gravityEcl, out aimPoint, out flightTimeSeconds);

    /// <summary>
    /// As above, for a target that is changing its velocity: slowing, turning or falling.
    /// </summary>
    /// <param name="targetAccelerationEcl">
    /// Everything accelerating the target, gravity included. Not differenced against the shooter:
    /// the round leaves with the shooter's velocity but not its acceleration, whose only
    /// acceleration afterwards is <paramref name="gravityEcl"/>.
    /// </param>
    public static bool TrySolve(double3 shooterPos, double3 shooterVelocity,
                                double3 targetPos, double3 targetVelocity, double3 targetAccelerationEcl,
                                double muzzleSpeed, double3 gravityEcl, out double3 aimPoint,
                                out double flightTimeSeconds)
    {
        aimPoint = targetPos;
        flightTimeSeconds = 0.0;
        if (!(muzzleSpeed > 0.0) || !double.IsFinite(muzzleSpeed)) return false;
        if (!Vec.IsFinite(shooterPos) || !Vec.IsFinite(targetPos)
            || !Vec.IsFinite(shooterVelocity) || !Vec.IsFinite(targetVelocity)
            || !Vec.IsFinite(targetAccelerationEcl))
        {
            return false;
        }

        double3 targetVelocityRelative = targetVelocity - shooterVelocity;

        double flightTime = Vec.Len(targetPos - shooterPos) / muzzleSpeed;

        bool converged = false;

        for (int i = 0; i < MaxPasses; i++)
        {
            double3 predicted = targetPos + targetVelocityRelative * flightTime
                                + targetAccelerationEcl * (0.5 * flightTime * flightTime);
            double range = Vec.Len(predicted - shooterPos);
            double next = range / muzzleSpeed;

            if (!double.IsFinite(next)) return false;

            double moved = Math.Abs(next - flightTime);
            flightTime = next;

            if (moved <= ToleranceSeconds) { converged = true; break; }
        }

        // A solve that ran out of passes has not found an intercept, it has been walking away from
        // one: the target is outrunning the round. Reporting the last iterate would be an aim point
        // presented with the same confidence as a real one.
        if (!converged) return false;

        // Aim above the intercept by exactly what the round will fall, so the two cancel on the
        // way. Sign matters: gravity points down, so subtracting raises the aim.
        double3 intercept = targetPos + targetVelocityRelative * flightTime
                            + targetAccelerationEcl * (0.5 * flightTime * flightTime);
        aimPoint = intercept - gravityEcl * (0.5 * flightTime * flightTime);
        flightTimeSeconds = flightTime;

        return Vec.IsFinite(aimPoint);
    }

    // A flown solve has converged once the shell's closest approach to where the target will be is
    // this near: far inside any fuse, and well above what the flight's own step can move.
    private const double FlownToleranceMetres = 0.1;

    // How long a shell is flown for when its profile sets no lifetime of its own.
    private const double DefaultHorizonSeconds = 60.0;

    /// <summary>
    /// The same solve for a round that slows down: the shell is flown through the air and the
    /// gravity it will meet, rather than assumed to keep its muzzle speed.
    ///
    /// <para>Distance over muzzle speed falls short of the real flight time by whatever speed the
    /// air takes away, and the lead, the drop and a timed fuse all hang off that one number. A
    /// five-inch shell 15.7 km out at 80° flies 43.4 s, not the 19.4 s the division gives, so a
    /// fuse set from it bursts 5.5 km short. Flown, all three come off one trajectory.</para>
    ///
    /// <para>The flight is measured against the ground the round flies over, because that is what
    /// its drag reads — so the mount's, the target's and the air's velocities are all differenced
    /// here, and motion common to the three cannot reach the answer. So is the ground's acceleration,
    /// which on a world that turns is not zero.</para>
    ///
    /// <para>The target is flown too. Its acceleration is split into gravity, a drag along its
    /// airspeed and whatever is left, and the drag is carried as a drag — so a craft coasting in air
    /// is not assumed to keep slowing at the rate it is slowing now.</para>
    /// </summary>
    /// <param name="targetAccelerationEcl">Everything accelerating the target, gravity included.</param>
    /// <param name="targetDragShape">
    /// The target's drag box, attitude, turn and mass, so its drag is computed as its airflow turns and
    /// its thrust turns with it; null holds the coefficient it was measured with.
    /// </param>
    /// <param name="groundVelocity">The motion of the air at the mount: what the round's airspeed is measured against.</param>
    /// <param name="groundAcceleration">
    /// How that ground is accelerating — on a world that turns, the spin carrying it round. Taken off the
    /// round's and the target's accelerations alike, so neither falls under a gravity the ground does not feel.
    /// </param>
    /// <param name="gravityAt">
    /// The pull at a position at the current instant. A field rather than one vector, because on a
    /// small world it turns under the shell: 3.6° across 15.7 km of a 250 km body.
    /// </param>
    /// <param name="densityAt">Air density as a multiple of sea level, at a position at the current instant.</param>
    /// <param name="directionHint">Where the previous solve pointed, or zero. Only where the search starts.</param>
    public static bool TrySolveFlown(double3 shooterPos, double3 shooterVelocity, double3 groundVelocity,
                                     double3 groundAcceleration,
                                     double3 targetPos, double3 targetVelocity, double3 targetAccelerationEcl,
                                     DragShape? targetDragShape,
                                     MunitionProfile munition, Func<double3, double3> gravityAt,
                                     Func<double3, double> densityAt, double3 directionHint,
                                     out double3 aimPoint, out double flightTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(munition);
        ArgumentNullException.ThrowIfNull(gravityAt);
        ArgumentNullException.ThrowIfNull(densityAt);

        aimPoint = targetPos;
        flightTimeSeconds = 0.0;

        double muzzleSpeed = munition.LaunchSpeed;
        if (!(muzzleSpeed > 0.0) || !double.IsFinite(muzzleSpeed)) return false;
        if (!Vec.IsFinite(shooterPos) || !Vec.IsFinite(shooterVelocity) || !Vec.IsFinite(groundVelocity)
            || !Vec.IsFinite(groundAcceleration)
            || !Vec.IsFinite(targetPos) || !Vec.IsFinite(targetVelocity)
            || !Vec.IsFinite(targetAccelerationEcl) || !Vec.IsFinite(directionHint))
        {
            return false;
        }

        double3 direction = Vec.Unit(directionHint);
        if (Vec.Len2(direction) == 0.0)
        {
            double3 pull = gravityAt(shooterPos);
            if (!Vec.IsFinite(pull)) pull = Vec.Zero;

            direction = TrySolve(shooterPos, shooterVelocity, targetPos, targetVelocity,
                                 targetAccelerationEcl - groundAcceleration, muzzleSpeed, pull - groundAcceleration,
                                 out double3 vacuum, out _)
                ? Vec.Unit(vacuum - shooterPos)
                : Vec.Unit(targetPos - shooterPos);

            if (Vec.Len2(direction) == 0.0) return false;
        }

        var flight = new Flight(shooterPos, shooterVelocity - groundVelocity, targetPos - shooterPos,
                                targetVelocity - groundVelocity, targetAccelerationEcl, targetDragShape,
                                groundAcceleration, munition, gravityAt, densityAt);

        // A pass that converges takes most of the miss out. One that has not cut it by a fifth in several
        // passes is walking, and a target out of reach is the usual cause: flying every remaining pass to
        // the end of a long-lived shell's flight is what waiting it out would cost, every frame.
        const double StallProgress = 0.8;
        const int StallPasses = 4;
        double bestMiss = double.MaxValue;
        int stalled = 0;

        // How a turn of the barrel moves the miss, as the three columns of a matrix, corrected along each turn by
        // what that turn did (Broyden's update) -- so no pass flies more than its one shell.
        double3 byX = Vec.Zero, byY = Vec.Zero, byZ = Vec.Zero;
        double3 lastMiss = Vec.Zero, lastTurn = Vec.Zero;

        static double3 Moves(double3 byX, double3 byY, double3 byZ, double3 turn)
            => (byX * turn.X) + (byY * turn.Y) + (byZ * turn.Z);

        for (int i = 0; i < MaxPasses; i++)
        {
            if (!flight.TryClosestApproach(direction * muzzleSpeed, out double time,
                                           out double3 round, out double3 target))
            {
                return false;
            }

            double3 miss = target - round;
            double reach = Vec.Len(round);
            if (!(reach > 1.0)) return false;

            double missBy = Vec.Len(miss);
            if (missBy <= FlownToleranceMetres)
            {
                aimPoint = shooterPos + (direction * Vec.Len(target));
                flightTimeSeconds = time;
                return Vec.IsFinite(aimPoint);
            }

            if (missBy < bestMiss * StallProgress)
            {
                bestMiss = missBy;
                stalled = 0;
            }
            else if (++stalled >= StallPasses)
            {
                return false;
            }

            // Flat, a turn moves the shell by the reach times the angle, and turning along the miss takes it out.
            // Lobbed, the shell comes down steeper than it went up, so a turn upwards moves its path mostly along
            // itself: turned along the miss the correction shrinks by the square of that cosine and is gone before
            // the barrel reaches 45 degrees -- a shell that goes 64.8 km could not be laid past 55. So the flat
            // answer is only where it starts, and what a turn really does is learnt from the turns flown.
            if (i == 0)
            {
                byX = new double3(-reach, 0, 0);
                byY = new double3(0, -reach, 0);
                byZ = new double3(0, 0, -reach);
            }
            else if (Vec.Len2(lastTurn) > 0.0)
            {
                double3 surprise = (miss - lastMiss - Moves(byX, byY, byZ, lastTurn)) / Vec.Len2(lastTurn);
                byX += surprise * lastTurn.X;
                byY += surprise * lastTurn.Y;
                byZ += surprise * lastTurn.Z;
            }

            // The turn square to the barrel that best takes out the miss, by least squares.
            double3 up = Vec.AnyPerpendicular(direction);
            double3 side = Vec.Cross(direction, up);
            double3 byUp = Moves(byX, byY, byZ, up);
            double3 bySide = Moves(byX, byY, byZ, side);
            double uu = Vec.Dot(byUp, byUp), us = Vec.Dot(byUp, bySide), ss = Vec.Dot(bySide, bySide);
            double towardsUp = -Vec.Dot(byUp, miss), towardsSide = -Vec.Dot(bySide, miss);
            double det = (uu * ss) - (us * us);

            double3 turn = det > 1e-9 * uu * ss
                ? (up * (((ss * towardsUp) - (us * towardsSide)) / det)) + (side * (((uu * towardsSide) - (us * towardsUp)) / det))
                : Vec.RejectFrom(miss, direction) / reach;
            if (!Vec.IsFinite(turn)) turn = Vec.RejectFrom(miss, direction) / reach;
            turn = Vec.ClampLength(turn, Math.Max(MaxTurnRadians, 2.0 * missBy / reach));

            lastMiss = miss;
            lastTurn = turn;
            direction = Vec.Unit(direction + turn);
        }

        // Out of passes is walking rather than converging, as in the fixed-point solve above.
        return false;
    }

    // The most a pass turns the barrel while the miss is small against the reach. Near the longest reach the
    // turn that would take a miss out grows without bound, and one taken whole lands on the far side of it.
    private const double MaxTurnRadians = 0.1;

    // One shell and the target it is flying at, in the ground's frame: every position an offset from
    // the mount and every velocity against the air. So the point density is read at is where over the
    // ground each will be, not where the ecliptic will have carried that point by then.
    private readonly struct Flight
    {
        private const double Step = Medium.FaithfulStepInAir;

        // Below this a target has no airspeed to read a drag off.
        private const double MinTargetSpeed = 1.0;

        private readonly double3 _mount;
        private readonly double3 _mountVelocity;
        private readonly double3 _targetOffset;
        private readonly double3 _targetVelocity;
        private readonly MunitionProfile _munition;
        private readonly Func<double3, double3> _gravityAt;
        private readonly Func<double3, double> _densityAt;

        // The ground's own acceleration, which the round and the target are both flown against.
        private readonly double3 _frameAcceleration;

        // What is left of the target's acceleration once gravity and drag are taken out — an engine,
        // or lift — held as it was measured, and turned with the body when its shape says it is turning.
        private readonly double3 _targetPush;

        // Its drag as a coefficient on airspeed squared at sea-level density, so the deceleration it
        // was measured with eases as it slows and thins as it climbs. Holding the deceleration itself
        // put the Mk 42's bursts 152 m behind a coasting drone at 8.4 s, flown. Only read where there is
        // no shape to compute the drag from.
        private readonly double _targetDrag;

        // The engine's own drag, off the body, which changes as the airflow turns against it. Holding the
        // coefficient put every first shell 25 m behind and above.
        private readonly DragShape? _targetShape;

        // And how fast that push is burning its mass away, and for how long it can: a steady force over a
        // falling mass accelerates harder, and stops when the propellant does.
        private readonly double _targetMassFlow;
        private readonly double _targetBurnSeconds;

        public Flight(double3 mount, double3 mountVelocity, double3 targetOffset, double3 targetVelocity,
                      double3 targetAcceleration, DragShape? targetShape, double3 frameAcceleration,
                      MunitionProfile munition, Func<double3, double3> gravityAt, Func<double3, double> densityAt)
        {
            _mount = mount;
            _frameAcceleration = frameAcceleration;
            _mountVelocity = mountVelocity;
            _targetOffset = targetOffset;
            _targetVelocity = targetVelocity;
            _munition = munition;
            _gravityAt = gravityAt;
            _densityAt = densityAt;

            double3 felt = targetAcceleration - Pull(gravityAt, mount + targetOffset);
            double speed = Vec.Len(targetVelocity);

            double density = Density(densityAt, mount + targetOffset);
            double along = speed > MinTargetSpeed ? Vec.Dot(felt, targetVelocity) / speed : 0.0;

            if (targetShape is { IsUsable: true } shape)
            {
                _targetShape = shape;
                _targetDrag = 0.0;
                _targetPush = felt - shape.DragAcceleration(targetVelocity, density);
                _targetMassFlow = shape.MassFlowFor(_targetPush);
                _targetBurnSeconds = shape.BurnSeconds(_targetMassFlow);
                return;
            }

            _targetShape = null;
            _targetMassFlow = 0.0;
            _targetBurnSeconds = double.PositiveInfinity;

            // Without one, only a deceleration along the airspeed is read as drag. A target speeding up
            // is under power, and nothing about its air can be learned from that.
            if (along < 0.0 && density > Medium.NoticeableDensity)
            {
                _targetDrag = -along / (speed * speed * density);
                _targetPush = felt - (targetVelocity * (along / speed));
            }
            else
            {
                _targetDrag = 0.0;
                _targetPush = felt;
            }
        }

        public bool TryClosestApproach(double3 ejection, out double time, out double3 round, out double3 target)
        {
            time = 0.0;
            round = Vec.Zero;
            target = _targetOffset;

            double horizon = _munition.MaxFlightSeconds > 0f ? _munition.MaxFlightSeconds : DefaultHorizonSeconds;
            var now = new State(Vec.Zero, _mountVelocity + ejection, _targetOffset, _targetVelocity, 0.0);

            double closing = now.Closing;
            if (!(closing < 0.0)) return false;

            for (double t = 0.0; t < horizon; t += Step)
            {
                State next = Advance(now, Step);
                double nextClosing = next.Closing;
                if (!double.IsFinite(nextClosing)) return false;

                if (nextClosing >= 0.0)
                {
                    double part = Step * (closing / (closing - nextClosing));
                    State met = Advance(now, part);

                    time = t + part;
                    round = met.Round;
                    target = met.Target;
                    return Vec.IsFinite(round) && Vec.IsFinite(target);
                }

                (now, closing) = (next, nextClosing);
            }

            // Still closing when the round would expire: it never gets there.
            return false;
        }

        private readonly record struct State(double3 Round, double3 RoundVelocity, double3 Target,
                                             double3 TargetVelocity, double Time)
        {
            // Closing while the separation and the relative velocity point opposite ways.
            public double Closing => Vec.Dot(Round - Target, RoundVelocity - TargetVelocity);
        }

        private State Advance(State s, double h)
        {
            double3 roundKick = RoundAcceleration(s.Round, s.RoundVelocity);
            double3 targetKick = TargetAcceleration(s.Target, s.TargetVelocity, s.Time);

            var mid = new State(s.Round + (s.RoundVelocity * (0.5 * h)), s.RoundVelocity + (roundKick * (0.5 * h)),
                                s.Target + (s.TargetVelocity * (0.5 * h)), s.TargetVelocity + (targetKick * (0.5 * h)),
                                s.Time + (0.5 * h));

            return new State(s.Round + (mid.RoundVelocity * h),
                             s.RoundVelocity + (RoundAcceleration(mid.Round, mid.RoundVelocity) * h),
                             s.Target + (mid.TargetVelocity * h),
                             s.TargetVelocity + (TargetAcceleration(mid.Target, mid.TargetVelocity, mid.Time) * h),
                             s.Time + h);
        }

        private double3 RoundAcceleration(double3 x, double3 v)
            => Medium.Coasting(Pull(_gravityAt, _mount + x) - _frameAcceleration, v, _munition,
                               Density(_densityAt, _mount + x));

        private double3 TargetAcceleration(double3 y, double3 w, double t)
        {
            double3 pull = Pull(_gravityAt, _mount + y) - _frameAcceleration;

            if (_targetShape is { } shape)
            {
                double mass = shape.MassAt(_targetMassFlow, t);
                double3 push = t < _targetBurnSeconds ? shape.Carry(_targetPush, t) * (shape.Mass / mass) : Vec.Zero;
                return pull + push + (shape with { Mass = mass }).DragAcceleration(w, Density(_densityAt, _mount + y), t);
            }

            if (_targetDrag <= 0.0) return pull + _targetPush;

            double airspeed = Vec.Len(w);
            return pull + _targetPush - (w * (_targetDrag * airspeed * Density(_densityAt, _mount + y)));
        }

        private static double3 Pull(Func<double3, double3> gravityAt, double3 at)
        {
            double3 pull = gravityAt(at);
            return Vec.IsFinite(pull) ? pull : Vec.Zero;
        }

        private static double Density(Func<double3, double> densityAt, double3 at)
        {
            double density = densityAt(at);
            return double.IsFinite(density) && density >= 0.0 ? density : 1.0;
        }
    }
}
