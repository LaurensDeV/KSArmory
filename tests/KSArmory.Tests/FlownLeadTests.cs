using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A lead flown through the air rather than solved as if the shell kept its muzzle speed.
///
/// <para>Every shot is fired as a real <see cref="Slug"/> on the solution and burst on its timed fuse,
/// so what is scored is where the round went, not what the solver believes. The worlds are round, with
/// a central pull and an exponential atmosphere, so a lead that only works on one planet fails here.</para>
/// </summary>
public class FlownLeadTests(ITestOutputHelper output)
{
    private const double Frame = 1.0 / 60.0;

    /// <summary>A round body with the mount on its surface at the origin and local up along +Z.</summary>
    private sealed record World(double SurfaceGravity, double RadiusKm, double SeaLevelDensity, double ScaleHeight)
    {
        private double Radius => RadiusKm * 1000.0;
        private double3 Centre => new(0, 0, -Radius);

        public double3 GravityAt(double3 p)
        {
            double3 toCentre = Centre - p;
            double r = Vec.Len(toCentre);
            return toCentre * (SurfaceGravity * Radius * Radius / (r * r * r));
        }

        public double DensityAt(double3 p)
            => SeaLevelDensity <= 0.0
                ? 0.0
                : SeaLevelDensity * Math.Exp(-Math.Max(0.0, Vec.Len(p - Centre) - Radius) / ScaleHeight);
    }

    private static readonly Dictionary<string, World> Worlds = new()
    {
        ["Earth"] = new(9.80665, 6371.0, 1.0, 8_000.0),
        ["Moon"] = new(1.625, 1737.0, 0.0, 1.0),
        ["Mars"] = new(3.71, 3390.0, 0.016, 11_100.0),
        ["Asteroid"] = new(0.3, 250.0, 0.0, 1.0),
    };

    /// <summary>
    /// A drone that only coasts, under gravity and a drag on its airspeed squared that thins with
    /// height — flown finely ahead of time so a shot can be scored against where it really goes.
    /// </summary>
    private sealed class Coasting
    {
        private const double Dt = 0.002;
        private readonly List<(double3 P, double3 V)> _track = [];

        // Drag on airspeed squared, as a round has.
        public Coasting(World world, double3 start, double3 velocity, double decelerationAtStart)
            : this(start, velocity, SquareLaw(world, start, velocity, decelerationAtStart))
        {
        }

        // The engine's drag off the body, and a rocket fixed to it: a steady force while the propellant
        // lasts, over a mass falling at that force over the exhaust velocity.
        public Coasting(World world, double3 start, double3 velocity, DragShape shape, double3 thrustEcl = default)
            : this(start, velocity, Rocket(world, shape, thrustEcl))
        {
        }

        private static Func<double3, double3, double, double3> Rocket(World world, DragShape shape, double3 thrustEcl)
        {
            double force = Vec.Len(thrustEcl) * shape.Mass;
            double flow = shape.ExhaustVelocity > 0.0 ? force / shape.ExhaustVelocity : 0.0;
            double burnout = flow > 0.0 ? shape.PropellantMass / flow : double.PositiveInfinity;

            return (p, v, t) =>
            {
                double mass = shape.Mass - (flow * Math.Min(t, burnout));
                double3 thrust = t < burnout ? shape.Carry(thrustEcl, t) * (shape.Mass / mass) : Vec.Zero;
                return world.GravityAt(p) + thrust + (shape with { Mass = mass }).DragAcceleration(v, world.DensityAt(p), t);
            };
        }

        public Coasting(double3 start, double3 velocity, Func<double3, double3, double, double3> accel)
        {
            AccelerationAtStart = accel(start, velocity, 0.0);

            double3 position = start;
            double3 speed = velocity;
            for (int i = 0; i * Dt <= 50.0; i++)
            {
                _track.Add((position, speed));

                double t = i * Dt;
                double3 a = accel(position, speed, t);
                double3 midPosition = position + (speed * (0.5 * Dt));
                double3 midSpeed = speed + (a * (0.5 * Dt));

                position += midSpeed * Dt;
                speed += accel(midPosition, midSpeed, t + (0.5 * Dt)) * Dt;
            }
        }

        private static Func<double3, double3, double, double3> SquareLaw(World world, double3 start, double3 velocity,
                                                                         double decelerationAtStart)
        {
            double k = decelerationAtStart / (Vec.Len2(velocity) * world.DensityAt(start));
            return (p, v, _) => world.GravityAt(p) - (v * (k * Vec.Len(v) * world.DensityAt(p)));
        }

        /// <summary>What the engine would report for it at the moment of the shot.</summary>
        public double3 AccelerationAtStart { get; }

        public (double3 P, double3 V) At(double t)
        {
            double s = Math.Clamp(t / Dt, 0.0, _track.Count - 1.001);
            int i = (int)s;
            double f = s - i;

            (double3 p0, double3 v0) = _track[i];
            (double3 p1, double3 v1) = _track[i + 1];
            return (p0 + ((p1 - p0) * f), v0 + ((v1 - v0) * f));
        }
    }

    // The five-inch shell's flight. The proximity fuse is off, so only the fuse time can put a burst
    // on the target.
    private static MunitionProfile Shell(float dragK = -1f) => new()
    {
        Name = "FlownLeadShell",
        DisplayName = "flown lead shell",
        LaunchSpeed = Arsenal.Shell5In54.LaunchSpeed,
        DragK = dragK >= 0f ? dragK : Arsenal.Shell5In54.DragK,
        NeutralDensityRatio = 0f,
        MaxFlightSeconds = 40f,
        FuseRadius = 0f,
        FuseArmSeconds = 0f,
        TimedFuse = true,
        ChargeKg = Arsenal.Shell5In54.ChargeKg,
    };

    private static double3 At(double rangeKm, double elevationDeg)
    {
        double e = elevationDeg * Math.PI / 180.0;
        return new double3(Math.Cos(e), 0, Math.Sin(e)) * (rangeKm * 1000.0);
    }

    private static Func<double, (double3 P, double3 V)> Steady(double3 target, double3 velocity, double3 acceleration)
        => t => (target + (velocity * t) + (acceleration * (0.5 * t * t)), velocity + (acceleration * t));

    private static bool Solve(World world, bool flown, double3 target, double3 velocity, double3 acceleration,
                              out double3 aim, out double flight)
    {
        MunitionProfile shell = Shell();

        return flown
            ? BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, target, velocity, acceleration, null, shell,
                                          world.GravityAt, world.DensityAt, Vec.Zero, out aim, out flight)
            : BallisticLead.TrySolve(Vec.Zero, Vec.Zero, target, velocity, acceleration, shell.LaunchSpeed,
                                     world.GravityAt(Vec.Zero), out aim, out flight);
    }

    // Fires one shell along the aim and flies it a frame at a time, as the game does, until its fuse
    // goes. The miss is measured where the target is at the burst.
    private static Slug Fire(World world, double3 aim, double fuseSeconds, Func<double, (double3 P, double3 V)> targetAt)
    {
        MunitionProfile shell = Shell();
        var slug = new Slug(Vec.Zero, Vec.Unit(aim) * shell.LaunchSpeed, null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = shell,
            FuseSeconds = fuseSeconds,
            GravityAt = (p, _) => world.GravityAt(p),
            AirDensityAt = (p, _) => world.DensityAt(p),
        };

        for (double t = 0.0; slug.State == RoundState.Flying && t < 45.0; t += Frame)
        {
            // Advanced to the end of the frame being handed over: the round back-dates it. A point
            // target, because a shell passing within a body's radius strikes it before the fuse is due.
            (double3 p, double3 v) = targetAt(t + Frame);
            slug.Update(Frame, new TargetState(p, v, 0.0), world.GravityAt(slug.PositionEcl), Vec.Zero, Vec.Zero,
                        shell, world.DensityAt(slug.PositionEcl));
        }

        return slug;
    }

    [Theory]
    [InlineData("Earth", 2.8, 45.0, 0.0, 0.0)]
    [InlineData("Earth", 8.0, 45.0, 300.0, 0.0)]
    [InlineData("Earth", 11.0, 30.0, 300.0, 0.0)]
    [InlineData("Earth", 15.7, 45.0, 0.0, 0.0)]
    [InlineData("Earth", 15.7, 80.0, 0.0, 0.0)]
    [InlineData("Earth", 13.5, 20.0, 250.0, -9.80665)]   // a crosser gliding down
    [InlineData("Moon", 15.7, 10.0, 200.0, 0.0)]
    [InlineData("Moon", 15.7, 60.0, 0.0, -1.625)]
    [InlineData("Mars", 15.7, 45.0, 250.0, 0.0)]
    [InlineData("Mars", 8.0, 5.0, 300.0, 0.0)]
    [InlineData("Asteroid", 15.7, 0.0, 150.0, 0.0)]
    [InlineData("Asteroid", 15.7, 45.0, 0.0, 0.0)]
    public void AShellFiredOnTheFlownLeadBurstsOnTheTarget(string worldName, double rangeKm, double elevationDeg,
                                                           double crossing, double falling)
    {
        World world = Worlds[worldName];
        double3 target = At(rangeKm, elevationDeg);
        double3 velocity = new(0, crossing, 0);
        double3 acceleration = new(0, 0, falling);

        Assert.True(Solve(world, flown: true, target, velocity, acceleration, out double3 aim, out double flight));
        Slug shot = Fire(world, aim, flight, Steady(target, velocity, acceleration));

        Assert.True(Solve(world, flown: false, target, velocity, acceleration, out double3 unflownAim,
                          out double unflown));
        Slug unflownShot = Fire(world, unflownAim, unflown, Steady(target, velocity, acceleration));

        output.WriteLine($"{worldName,-8} {rangeKm,5:F1} km at {elevationDeg,2:F0} deg, crossing {crossing,3:F0} m/s: "
                         + $"flown {flight,5:F2} s -> {shot.MissDistance,5:F2} m; "
                         + $"distance/speed {unflown,5:F2} s -> {unflownShot.MissDistance,6:F0} m");

        Assert.True(shot.BurstOnTime, "the fuse did not go");
        Assert.True(shot.MissDistance < 3.0, $"burst {shot.MissDistance:F1} m from the target");
    }

    /// <summary>
    /// A drone coasting in air slows less as it loses speed, so holding the deceleration it was
    /// measured with over a long flight puts the burst behind it. Flown against the gunnery scenario's
    /// drones: 152 m at 8.4 s.
    /// </summary>
    [Theory]
    [InlineData(6.3, 45.0, 250.0, 12.6)]
    [InlineData(9.0, 35.0, 300.0, 18.0)]
    public void ADroneSlowedByItsDragIsMetWhereTheDragLeavesIt(double rangeKm, double elevationDeg,
                                                              double speed, double deceleration)
    {
        World earth = Worlds["Earth"];
        double3 start = At(rangeKm, elevationDeg);
        double3 velocity = Vec.Unit(new double3(-0.5, 0.85, -0.15)) * speed;   // crossing, closing, sinking
        var drone = new Coasting(earth, start, velocity, deceleration);

        Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, start, velocity, drone.AccelerationAtStart,
                                                null, Shell(), earth.GravityAt, earth.DensityAt, Vec.Zero,
                                                out double3 aim, out double flight));
        Slug shot = Fire(earth, aim, flight, drone.At);

        double3 held = start + (velocity * flight) + (drone.AccelerationAtStart * (0.5 * flight * flight));
        double overshoot = Vec.Len(held - drone.At(flight).P);

        output.WriteLine($"{rangeKm:F1} km, {speed:F0} m/s slowing at {deceleration:F1} m/s2: flight {flight:F2} s, "
                         + $"holding the deceleration is {overshoot:F0} m off its path; burst {shot.MissDistance:F2} m");

        Assert.True(overshoot > 50.0, $"only {overshoot:F0} m between holding the deceleration and the drone's path");
        Assert.True(shot.MissDistance < 3.0, $"burst {shot.MissDistance:F1} m from the drone");
    }

    /// <summary>
    /// The engine's drag is an area fixed to the body, so a drone holding its attitude while gravity bends
    /// its path presents a different area as it goes — flown, 65.8 m² falling to 52.9 over 11 s — and
    /// holding the coefficient put every first shell 25 m behind and above it. Handed the drag box,
    /// the lead flies the area turning with the airflow.
    /// </summary>
    [Theory]
    [InlineData(7.0, 50.0, 176.0, 13.5)]
    [InlineData(10.0, 45.0, 250.0, 20.0)]
    public void ADragAreaThatTurnsWithTheAirflowIsLedWhenTheBoxIsKnown(
        double rangeKm, double elevationDeg, double speed, double deceleration)
    {
        World earth = Worlds["Earth"];
        double3 start = At(rangeKm, elevationDeg);
        double3 velocity = Vec.Unit(new double3(-0.9, 0.3, -0.3)) * speed;   // closing, crossing, beginning to dive

        // Nose on +X and held level: gravity bending the path down turns the air onto its broad underside.
        DragShape shape = TestDrone(earth, start, velocity, deceleration, doubleQuat.Identity);
        var drone = new Coasting(earth, start, velocity, shape);

        Slug led = LedShot(earth, start, velocity, drone, shape);
        Slug held = LedShot(earth, start, velocity, drone, null);

        output.WriteLine($"{rangeKm:F1} km, {speed:F0} m/s slowing at {deceleration:F1} m/s2: "
                         + $"area flown {led.MissDistance:F2} m, coefficient held {held.MissDistance:F1} m");

        Assert.True(held.MissDistance > 10.0, $"holding the coefficient missed by only {held.MissDistance:F1} m");
        Assert.True(led.MissDistance < 3.0, $"burst {led.MissDistance:F1} m from the drone");
    }

    /// <summary>
    /// A tumbling craft turns its drag area under the shell's flight as well as its airflow, and wreckage
    /// tumbles. Handed its turn, the lead flies the area turning; held at the attitude it had, it does not.
    /// </summary>
    [Theory]
    [InlineData(7.0, 50.0, 176.0, 13.5, 20.0)]
    [InlineData(10.0, 45.0, 250.0, 20.0, 45.0)]
    public void ATumblingDroneIsLedWhenItsTurnIsKnown(double rangeKm, double elevationDeg, double speed,
                                                      double deceleration, double degreesPerSecond)
    {
        World earth = Worlds["Earth"];
        double3 start = At(rangeKm, elevationDeg);
        double3 velocity = Vec.Unit(new double3(-0.9, 0.3, -0.3)) * speed;
        double3 rates = Vec.Unit(new double3(0.3, 1.0, 0.5)) * double.DegreesToRadians(degreesPerSecond);

        DragShape shape = TestDrone(earth, start, velocity, deceleration, doubleQuat.Identity, rates);
        var drone = new Coasting(earth, start, velocity, shape);

        Slug led = LedShot(earth, start, velocity, drone, shape);
        Slug still = LedShot(earth, start, velocity, drone, shape with { BodyRates = Vec.Zero });

        output.WriteLine($"{rangeKm:F1} km, tumbling at {degreesPerSecond:F0} deg/s: "
                         + $"turn flown {led.MissDistance:F2} m, attitude held {still.MissDistance:F1} m");

        Assert.True(still.MissDistance > 10.0, $"holding the attitude missed by only {still.MissDistance:F1} m");
        Assert.True(led.MissDistance < 3.0, $"burst {led.MissDistance:F1} m from the drone");
    }

    /// <summary>
    /// A craft under power is not slowing, so no drag can be read off it — and it still has all of its
    /// drag, which grows as the engine speeds it up. It is getting lighter too, so a steady engine
    /// pushes it harder as it goes, and then stops. Computed off the body and its engines, the lead
    /// flies all three.
    /// </summary>
    [Theory]
    [InlineData(9.0, 50.0, 200.0, 18.0, 21.0, true, 40.0)]    // lifting across its path for the whole flight, as flown
    [InlineData(11.0, 35.0, 250.0, 18.0, 25.0, false, 6.0)]   // along its path, burning out partway to the burst
    public void ADroneUnderPowerIsLedOnTheDragItStillHasAndTheMassItBurns(
        double rangeKm, double elevationDeg, double speed, double deceleration, double thrust, bool acrossPath,
        double burnSeconds)
    {
        World earth = Worlds["Earth"];
        double3 start = At(rangeKm, elevationDeg);
        double3 velocity = Vec.Unit(new double3(-0.9, 0.3, -0.3)) * speed;
        doubleQuat noseOnPath = Vec.RotationFromTo(new double3(1, 0, 0), Vec.Unit(velocity));
        double3 thrustLine = acrossPath ? Vec.Unit(Vec.RejectFrom(new double3(0, 0, 1), velocity)) : Vec.Unit(velocity);
        const double exhaust = 2000.0;

        DragShape body = TestDrone(earth, start, velocity, deceleration, noseOnPath);
        double flow = thrust * body.Mass / exhaust;
        DragShape shape = body with { ExhaustVelocity = exhaust, PropellantMass = flow * burnSeconds };
        var drone = new Coasting(earth, start, velocity, shape, thrustLine * thrust);

        Slug led = LedShot(earth, start, velocity, drone, shape);
        Slug steady = LedShot(earth, start, velocity, drone, shape with { ExhaustVelocity = 0.0 });
        Slug unread = LedShot(earth, start, velocity, drone, null);

        output.WriteLine($"{rangeKm:F1} km, {thrust:F0} m/s2 of thrust for {burnSeconds:F0} s against {deceleration:F0} of drag: "
                         + $"all of it {led.MissDistance:F2} m, thrust held steady {steady.MissDistance:F1} m, "
                         + $"drag unread {unread.MissDistance:F1} m");

        Assert.True(steady.MissDistance > 10.0, $"holding the thrust steady missed by only {steady.MissDistance:F1} m");
        Assert.True(unread.MissDistance > 10.0, $"leaving the drag unread missed by only {unread.MissDistance:F1} m");
        Assert.True(led.MissDistance < 3.0, $"burst {led.MissDistance:F1} m from the drone");
    }

    /// <summary>
    /// An incoming round coasts on the pull and the drag it is flown with, so a contact reporting that
    /// acceleration is led onto it. One reporting none is led as if it flew straight.
    /// </summary>
    [Theory]
    [InlineData(9.0, 40.0, 500.0)]
    [InlineData(13.0, 25.0, 700.0)]
    public void AnIncomingShellIsLedOnItsOwnPullAndDrag(double rangeKm, double elevationDeg, double speed)
    {
        World earth = Worlds["Earth"];
        MunitionProfile incoming = Shell();
        double3 start = At(rangeKm, elevationDeg);
        double3 velocity = Vec.Unit(new double3(-0.8, 0.4, -0.2)) * speed;
        var round = new Coasting(start, velocity,
                                 (p, v, _) => Medium.Coasting(earth.GravityAt(p), v, incoming, earth.DensityAt(p)));

        Slug Shot(double3 reported)
        {
            Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, start, velocity, reported, null, Shell(),
                                                    earth.GravityAt, earth.DensityAt, Vec.Zero,
                                                    out double3 aim, out double flight));
            return Fire(earth, aim, flight, round.At);
        }

        Slug led = Shot(round.AccelerationAtStart);
        Slug blind = Shot(Vec.Zero);

        output.WriteLine($"{rangeKm:F1} km, {speed:F0} m/s incoming: pull and drag {led.MissDistance:F2} m, "
                         + $"none {blind.MissDistance:F1} m");

        Assert.True(blind.MissDistance > 10.0, $"reporting no acceleration missed by only {blind.MissDistance:F1} m");
        Assert.True(led.MissDistance < 3.0, $"burst {led.MissDistance:F1} m from the round");
    }

    // The gunnery scenario's drone: its box, with the mass that has it slowing at the deceleration given
    // where the lead first sees it.
    private static DragShape TestDrone(World world, double3 start, double3 velocity, double deceleration,
                                       doubleQuat attitude, double3 rates = default)
    {
        var box = new DragShape(new double3(0.3 * 8.19, 1.2 * 34.5, 1.2 * 33.9),
                                new double3(1.0 * 8.19, 1.2 * 34.5, 1.2 * 33.9),
                                0.1 * 2.0 * (8.19 + 34.5 + 33.9), attitude, rates, 1.0, 1.225, 0.0, 0.0);

        return box with { Mass = Vec.Len(box.DragAcceleration(velocity, world.DensityAt(start))) / deceleration };
    }

    private static Slug LedShot(World world, double3 start, double3 velocity, Coasting drone, DragShape? given)
    {
        Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, start, velocity, drone.AccelerationAtStart,
                                                given, Shell(), world.GravityAt, world.DensityAt, Vec.Zero,
                                                out double3 aim, out double flight));
        return Fire(world, aim, flight, drone.At);
    }

    /// <summary>
    /// The harness above is only evidence if the old lead fails in it. Timed as if the shell kept its
    /// muzzle speed, the burst at 15.7 km is short by the seconds the air took.
    /// </summary>
    [Fact]
    public void TheLeadThatAssumesMuzzleSpeedBurstsFarShortAtRange()
    {
        World earth = Worlds["Earth"];
        double3 target = At(15.7, 80.0);

        Assert.True(Solve(earth, flown: false, target, Vec.Zero, Vec.Zero, out double3 aim, out double flight));
        Slug shot = Fire(earth, aim, flight, Steady(target, Vec.Zero, Vec.Zero));

        Assert.True(shot.MissDistance > 1_000.0, $"burst only {shot.MissDistance:F0} m from the target");
    }

    [Fact]
    public void WithoutAirOrGravityItAgreesWithTheClosedForm()
    {
        MunitionProfile shell = Shell(dragK: 0f);
        double3 target = new(4_000, 0, 1_000);
        double3 velocity = new(-120, 300, 0);

        Assert.True(BallisticLead.TrySolve(Vec.Zero, Vec.Zero, target, velocity, shell.LaunchSpeed, Vec.Zero,
                                           out double3 closed, out double closedFlight));
        Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, target, velocity, Vec.Zero, null, shell,
                                                _ => Vec.Zero, _ => 0.0, Vec.Zero,
                                                out double3 flown, out double flownFlight));

        Assert.True(Vec.AngleBetween(closed, flown) < 1e-5, $"{Vec.AngleBetween(closed, flown)} rad apart");
        Assert.Equal(closedFlight, flownFlight, 3);
    }

    /// <summary>
    /// Motion shared by the mount, the air and the target must not reach the aim: all three carry the
    /// planet's ~29.8 km/s, and the shell's drag reads only the difference.
    /// </summary>
    [Theory]
    [InlineData(0.0, 29_800.0, 0.0)]
    [InlineData(-7_800.0, 0.0, 400.0)]
    public void MotionSharedByTheMountTheAirAndTheTargetDoesNotMoveTheAim(double cx, double cy, double cz)
    {
        World earth = Worlds["Earth"];
        double3 shooter = new(100, -50, 25);
        double3 target = shooter + At(9.0, 40.0);
        double3 velocity = new(0, 250, 0);
        double3 common = new(cx, cy, cz);

        Assert.True(BallisticLead.TrySolveFlown(shooter, Vec.Zero, Vec.Zero, Vec.Zero, target, velocity, Vec.Zero, null, Shell(),
                                                p => earth.GravityAt(p - shooter), p => earth.DensityAt(p - shooter),
                                                Vec.Zero, out double3 still, out double stillFlight));
        Assert.True(BallisticLead.TrySolveFlown(shooter, common, common, Vec.Zero, target, velocity + common, Vec.Zero, null, Shell(),
                                                p => earth.GravityAt(p - shooter), p => earth.DensityAt(p - shooter),
                                                Vec.Zero, out double3 moving, out double movingFlight));

        Assert.True(Vec.Len(moving - still) < 0.5, $"common motion moved the aim by {Vec.Len(moving - still):F2} m");
        Assert.Equal(stillFlight, movingFlight, 3);
    }

    [Fact]
    public void AHintFromSomewhereElseStillFindsTheSameAim()
    {
        World earth = Worlds["Earth"];
        double3 target = At(12.0, 35.0);
        double3 velocity = new(0, 280, 0);

        Assert.True(Solve(earth, flown: true, target, velocity, Vec.Zero, out double3 cold, out double coldFlight));
        Assert.True(BallisticLead.TrySolveFlown(Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, target, velocity, Vec.Zero, null, Shell(),
                                                earth.GravityAt, earth.DensityAt, new double3(0.3, -0.9, 0.2),
                                                out double3 hinted, out double hintedFlight));

        Assert.True(Vec.AngleBetween(cold, hinted) < 2e-5, $"{Vec.AngleBetween(cold, hinted)} rad apart");
        Assert.Equal(coldFlight, hintedFlight, 2);
    }

    [Fact]
    public void ATargetBeyondTheShellsReachHasNoSolution()
    {
        Assert.False(Solve(Worlds["Earth"], flown: true, At(45.0, 10.0), Vec.Zero, Vec.Zero, out _, out _));
    }
}
