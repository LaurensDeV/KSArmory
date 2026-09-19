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
    /// <param name="bodyVelocity">
    /// The velocity of the body that ground belongs to. The ground carries the mount round the body's centre at
    /// the difference, so the pull and the air are read where the round is against where that centre has gone:
    /// held still against the mount, the pull turns the wrong way by that speed times the time over the radius,
    /// and a 5"/54 laid 60 km out on Earth came down 80 m off.
    /// </param>
    /// <param name="gravityAt">
    /// The pull at a position at the current instant. A field rather than one vector, because on a
    /// small world it turns under the shell: 3.6° across 15.7 km of a 250 km body.
    /// </param>
    /// <param name="densityAt">
    /// Air density as a multiple of sea level, at a position at the current instant — the air alone, never the
    /// ocean: a target on the sea is on the waterline, and a flight reading water a hair under it never arrives.
    /// </param>
    /// <param name="directionHint">Where the previous solve pointed, or zero. Only where the search starts.</param>
    /// <param name="groundVelocityAt">
    /// The ground's velocity at a position at the current instant, which is the air's; null holds the air still
    /// against the mount's ground. On a world that turns the air a distance x away moves at the spin times x
    /// against the mount, 1.7 m/s at 23 km: held still, a 5"/54 laid onto the ground there lands 5-12 m off by
    /// bearing, and a place on the ground reads as moving through the air.
    /// </param>
    public static bool TrySolveFlown(double3 shooterPos, double3 shooterVelocity, double3 groundVelocity,
                                     double3 groundAcceleration, double3 bodyVelocity,
                                     double3 targetPos, double3 targetVelocity, double3 targetAccelerationEcl,
                                     DragShape? targetDragShape,
                                     MunitionProfile munition, Func<double3, double3> gravityAt,
                                     Func<double3, double> densityAt, double3 directionHint,
                                     out double3 aimPoint, out double flightTimeSeconds,
                                     Func<double3, double3>? groundVelocityAt = null)
    {
        ArgumentNullException.ThrowIfNull(munition);
        ArgumentNullException.ThrowIfNull(gravityAt);
        ArgumentNullException.ThrowIfNull(densityAt);

        aimPoint = targetPos;
        flightTimeSeconds = 0.0;

        double muzzleSpeed = munition.LaunchSpeed;
        if (!(muzzleSpeed > 0.0) || !double.IsFinite(muzzleSpeed)) return false;
        if (!Vec.IsFinite(shooterPos) || !Vec.IsFinite(shooterVelocity) || !Vec.IsFinite(groundVelocity)
            || !Vec.IsFinite(groundAcceleration) || !Vec.IsFinite(bodyVelocity)
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
                                groundAcceleration, groundVelocity - bodyVelocity, munition, gravityAt, densityAt,
                                groundVelocity, groundVelocityAt);

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

    /// <summary>A place a shell can be sent: where it is, and how it moves.</summary>
    public readonly record struct Place(double3 Position, double3 Velocity, double3 Acceleration);

    /// <summary>
    /// <see cref="TrySolveFlown"/>, or, for a target out of reach, the farthest place on the way to it the
    /// shell can get to.
    ///
    /// <para>A gun given nothing it can reach has only the line of sight to lay along, which leaves the
    /// barrel near level and the shell a kilometre or two out whatever the range. Thrown as far as it
    /// goes instead, it lands as close to the target as the gun allows.</para>
    ///
    /// <para><b>Over the ground, where the target is on it.</b> The straight line to a place far out runs
    /// under the ground between: from Mars the point 90.3 km along it was 1,457 m under, and the shell laid
    /// through it came down 3.9 km short of it. <paramref name="along"/> gives places on the ground instead,
    /// each moving as its own ground does; without it they are on the straight line and move with the
    /// target.</para>
    ///
    /// <para>Found by halving along the way, up to <see cref="ReachHalvings"/> solves. A target that has
    /// not moved far is settled by confirming the last answer instead — that place still solves and a
    /// little further does not — which is three.</para>
    /// </summary>
    /// <param name="reachHint">Last frame's <paramref name="reachFraction"/>, or one when there was none.</param>
    /// <param name="reachFraction">
    /// One when the target itself was solved; otherwise how far along the way to it the lay reaches.
    /// </param>
    /// <param name="along">The place that fraction of the way to the target, or null for the straight line.</param>
    public static bool TrySolveFlownOrReach(double3 shooterPos, double3 shooterVelocity, double3 groundVelocity,
                                            double3 groundAcceleration, double3 bodyVelocity,
                                            double3 targetPos, double3 targetVelocity, double3 targetAccelerationEcl,
                                            DragShape? targetDragShape,
                                            MunitionProfile munition, Func<double3, double3> gravityAt,
                                            Func<double3, double> densityAt, double3 directionHint, double reachHint,
                                            out double3 aimPoint, out double flightTimeSeconds, out double reachFraction,
                                            Func<double, Place?>? along = null,
                                            Func<double3, double3>? groundVelocityAt = null)
    {
        reachFraction = 1.0;

        if (TrySolveFlown(shooterPos, shooterVelocity, groundVelocity, groundAcceleration, bodyVelocity, targetPos,
                          targetVelocity, targetAccelerationEcl, targetDragShape, munition, gravityAt, densityAt,
                          directionHint, out aimPoint, out flightTimeSeconds, groundVelocityAt))
        {
            return true;
        }

        // Once more from nothing before calling it out of reach. Seeded from last frame's answer the first miss is
        // already small, and near the longest reach the turns that take a small miss out are learnt too slowly to
        // beat the stall rule -- so a lay that solved last frame failed this one and was thrown to the longest
        // reach instead, on 47 frames in 300 at 23 km. A search from nothing takes big turns and learns them.
        if (Vec.Len2(directionHint) > 0.0
            && TrySolveFlown(shooterPos, shooterVelocity, groundVelocity, groundAcceleration, bodyVelocity, targetPos,
                             targetVelocity, targetAccelerationEcl, targetDragShape, munition, gravityAt, densityAt,
                             Vec.Zero, out aimPoint, out flightTimeSeconds, groundVelocityAt))
        {
            return true;
        }

        double3 toTarget = targetPos - shooterPos;
        double3 hint = directionHint;

        bool Solves(double fraction, out double3 aim, out double time)
        {
            aim = default;
            time = 0.0;

            Place? place = along is null
                               ? new Place(shooterPos + (toTarget * fraction), targetVelocity, targetAccelerationEcl)
                               : along(fraction);

            return place is { } at
                   && TrySolveFlown(shooterPos, shooterVelocity, groundVelocity, groundAcceleration, bodyVelocity,
                                    at.Position, at.Velocity, at.Acceleration, targetDragShape, munition, gravityAt,
                                    densityAt, hint, out aim, out time, groundVelocityAt);
        }

        double near = 0.0;
        double far = 1.0;
        bool found = false;

        if (reachHint > 0.0 && reachHint < 1.0 && Solves(reachHint, out double3 atHint, out double hintTime))
        {
            found = true;
            near = reachHint;
            aimPoint = atHint;
            flightTimeSeconds = hintTime;
            hint = atHint - shooterPos;

            if (!Solves(Math.Min(1.0, reachHint + ReachResolution), out _, out _))
            {
                reachFraction = reachHint;
                return true;
            }
        }

        for (int i = 0; i < ReachHalvings && far - near > ReachResolution; i++)
        {
            double mid = 0.5 * (near + far);

            if (Solves(mid, out double3 aim, out double time))
            {
                found = true;
                near = mid;
                aimPoint = aim;
                flightTimeSeconds = time;
                hint = aim - shooterPos;
            }
            else
            {
                far = mid;
            }
        }

        reachFraction = near;
        return found;
    }

    // A thousandth of the way: 24 m at 24 km, inside what the fall scatters a shell by anyway.
    private const double ReachResolution = 0.001;
    private const int ReachHalvings = 12;

    /// <summary>
    /// The places on the ground on the way from under one point to under another, for
    /// <see cref="TrySolveFlownOrReach"/>: round the body's centre, each at the ground's own height there and
    /// moving as that ground does. Null where the ground cannot be read, and for two points on opposite sides
    /// of the body, which have no one way round.
    /// </summary>
    internal static Func<double, Place?> AlongTheGround(IGroundTest ground, double3 from, double3 to,
                                                        Func<double3, double3> velocityAt,
                                                        Func<double3, double3> accelerationAt)
        => fraction =>
        {
            if (!ground.TryGround(to, out double3 centre, out double radius) || !(radius > 0.0)) return null;

            double3 start = Vec.Unit(from - centre);
            double3 end = Vec.Unit(to - centre);
            double angle = Vec.AngleBetween(start, end);
            if (!Vec.IsFinite(start) || !Vec.IsFinite(end) || !(angle < Math.PI - 1e-6)) return null;

            double3 way = angle > 1e-12
                              ? Vec.Unit((start * Math.Sin((1.0 - fraction) * angle)) + (end * Math.Sin(fraction * angle)))
                              : end;

            if (!ground.TryGround(centre + (way * radius), out double3 under, out double height) || !(height > 0.0))
            {
                return null;
            }

            double3 at = under + (way * height);
            return Vec.IsFinite(at) ? new Place(at, velocityAt(at), accelerationAt(at)) : null;
        };

    // One shell and the target it is flying at, in the ground's frame: every position an offset from
    // the mount and every velocity against the air. So the point density is read at is where over the
    // ground each will be, not where the ecliptic will have carried that point by then.
    private readonly struct Flight
    {
        private const double Step = Medium.FaithfulStepInAir;

        // Over the time straight up takes on flat ground, for a world whose ground falls away under a long shot.
        private const double FallHorizonMargin = 1.25;

        // Below this a target has no airspeed to read a drag off.
        private const double MinTargetSpeed = 1.0;

        private readonly double3 _mount;
        private readonly double3 _mountVelocity;
        private readonly double3 _targetOffset;
        private readonly double3 _targetVelocity;
        private readonly MunitionProfile _munition;
        private readonly Func<double3, double3> _gravityAt;
        private readonly Func<double3, double> _densityAt;

        // The air's velocity where a point is, and the mount's ground it is measured against.
        private readonly Func<double3, double3>? _groundVelocityAt;
        private readonly double3 _groundVelocity;

        // The ground's own acceleration, which the round and the target are both flown against.
        private readonly double3 _frameAcceleration;

        // How fast that ground carries the frame round the body's centre. The body is sampled once, where it
        // was when the flight began, so the round's lookups are carried back by what the frame has travelled.
        private readonly double3 _frameSpin;

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
                      double3 targetAcceleration, DragShape? targetShape, double3 frameAcceleration, double3 frameSpin,
                      MunitionProfile munition, Func<double3, double3> gravityAt, Func<double3, double> densityAt,
                      double3 groundVelocity, Func<double3, double3>? groundVelocityAt)
        {
            _mount = mount;
            _groundVelocity = groundVelocity;
            _groundVelocityAt = groundVelocityAt;
            _frameAcceleration = frameAcceleration;
            _frameSpin = frameSpin;
            _mountVelocity = mountVelocity;
            _targetOffset = targetOffset;
            _targetVelocity = targetVelocity;
            _munition = munition;
            _gravityAt = gravityAt;
            _densityAt = densityAt;

            double3 felt = targetAcceleration - Pull(gravityAt, mount + targetOffset);
            double3 airspeed = targetVelocity - Wind(mount + targetOffset, 0.0);
            double speed = Vec.Len(airspeed);

            double density = Density(densityAt, mount + targetOffset);
            double along = speed > MinTargetSpeed ? Vec.Dot(felt, airspeed) / speed : 0.0;

            if (targetShape is { IsUsable: true } shape)
            {
                _targetShape = shape;
                _targetDrag = 0.0;
                _targetPush = felt - shape.DragAcceleration(airspeed, density);
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
                _targetPush = felt - (airspeed * (along / speed));
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

            // A round the ground stops runs no clock while its path can only land, so it is followed as long as a
            // shell leaving this fast could stay up under the pull here -- straight up, with margin for a round
            // world, and air only shortens it. From Mars the longest reach is past three minutes, where following
            // it for its two-minute life ended the gun's reach at 90 km. Anything else is followed for its life.
            double horizon = _munition.MaxFlightSeconds > 0f ? _munition.MaxFlightSeconds : DefaultHorizonSeconds;
            if (_munition.HitsTerrain)
            {
                double straightUp = 2.0 * Vec.Len(_mountVelocity + ejection)
                                    / Vec.Len(Pull(_gravityAt, _mount) - _frameAcceleration);
                if (double.IsFinite(straightUp) && straightUp > 0.0) horizon = FallHorizonMargin * straightUp;
            }
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

                    // Interpolated across a whole step, the meeting lands a hair early or late, and at a
                    // head-on closing speed of 1,300 m/s a tenth of a millisecond is 0.13 m of miss along
                    // the closing line -- which no turn of the barrel takes out, so the solve stalled
                    // against it short of its tolerance. One straight-line closest approach removes it.
                    double3 apart = met.Round - met.Target;
                    double3 relative = met.RoundVelocity - met.TargetVelocity;
                    double early = -Vec.Dot(apart, relative) / Vec.Len2(relative);
                    if (!double.IsFinite(early) || Math.Abs(early) > Step) early = 0.0;

                    time = t + part + early;
                    round = met.Round + (met.RoundVelocity * early);
                    target = met.Target + (met.TargetVelocity * early);
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
            double3 roundKick = RoundAcceleration(s.Round, s.RoundVelocity, s.Time);
            double3 targetKick = TargetAcceleration(s.Target, s.TargetVelocity, s.Time);

            var mid = new State(s.Round + (s.RoundVelocity * (0.5 * h)), s.RoundVelocity + (roundKick * (0.5 * h)),
                                s.Target + (s.TargetVelocity * (0.5 * h)), s.TargetVelocity + (targetKick * (0.5 * h)),
                                s.Time + (0.5 * h));

            return new State(s.Round + (mid.RoundVelocity * h),
                             s.RoundVelocity + (RoundAcceleration(mid.Round, mid.RoundVelocity, mid.Time) * h),
                             s.Target + (mid.TargetVelocity * h),
                             s.TargetVelocity + (TargetAcceleration(mid.Target, mid.TargetVelocity, mid.Time) * h),
                             s.Time + h);
        }

        private double3 RoundAcceleration(double3 x, double3 v, double t)
        {
            double3 at = Sampled(x, t);
            return Medium.Coasting(Pull(_gravityAt, at) - _frameAcceleration, v - Wind(at, t), _munition,
                                   Density(_densityAt, at));
        }

        // The air at a sampled point against this frame, which is the mount's ground accelerating with it: on a
        // world that turns, the spin times the offset from the mount.
        private double3 Wind(double3 at, double t)
        {
            if (_groundVelocityAt is null) return Vec.Zero;

            double3 wind = _groundVelocityAt(at) - _groundVelocity - (_frameAcceleration * t);
            return Vec.IsFinite(wind) ? wind : Vec.Zero;
        }

        // Where the round is t seconds in against the body as it was sampled, the frame's own acceleration
        // included: the spin carries the mount 77 m toward the axis over a 72 s flight, 67 m of it down, and read
        // without it the air is 0.8% thin where the shell is lowest -- metres of range at 23 km.
        private double3 Sampled(double3 offset, double t)
            => _mount + offset + (_frameSpin * t) + (_frameAcceleration * (0.5 * t * t));

        private double3 TargetAcceleration(double3 y, double3 w, double t)
        {
            // Read where the frame began, unlike the round's: the target's acceleration was measured against the
            // pull there, and what holds it up is held with it. Turning the pull alone walks a place on the
            // ground away by as much as the round's own correction brings it back.
            double3 pull = Pull(_gravityAt, _mount + y) - _frameAcceleration;
            double3 air = w - Wind(Sampled(y, t), t);

            if (_targetShape is { } shape)
            {
                double mass = shape.MassAt(_targetMassFlow, t);
                double3 push = t < _targetBurnSeconds ? shape.Carry(_targetPush, t) * (shape.Mass / mass) : Vec.Zero;
                return pull + push + (shape with { Mass = mass }).DragAcceleration(air, Density(_densityAt, _mount + y), t);
            }

            if (_targetDrag <= 0.0) return pull + _targetPush;

            double airspeed = Vec.Len(air);
            return pull + _targetPush - (air * (_targetDrag * airspeed * Density(_densityAt, _mount + y)));
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
