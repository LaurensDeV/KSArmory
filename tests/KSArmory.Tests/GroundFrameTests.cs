using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Where a round meets the ground is a fact about the ground, not about the frame it is flown in.
///
/// <para><see cref="IGroundTest"/> answers with a body centre and a surface radius, and the round
/// holds both for the frame. The centre is a sample of something travelling at ~29.8 km/s in the
/// ecliptic while the round crosses the frame carrying that same motion — so differencing the two
/// reads the carrier as a change of altitude, and the round meets the ground wherever the ecliptic
/// happens to point. On a deorbit arrival, where the arc covers eleven metres of ground per metre
/// of height, that is kilometres.</para>
///
/// <para>The whole family is in <c>docs/FRAMES-AND-EPOCHS.md</c>; this is the one that survived
/// into the ground test, because every headless rig before it flew the round about a planet sitting
/// still at the origin, which is the one case where the fault is exactly zero.</para>
/// </summary>
public class GroundFrameTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;
    private const double EarthSpin = 7.2921159e-5;

    /// <summary>Near Earth, and it is the whole point of the test.</summary>
    private const double CarrierSpeed = 29_800.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    // The planet under the round, moving through the ecliptic as a real one does. Re-set by the
    // harness each frame, exactly as GroundTest re-reads the celestial each frame.
    private sealed class TravellingGround : IGroundTest
    {
        public double3 Centre;

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Centre;
            surfaceRadius = R;
            return true;
        }
    }

    // The flown shot: a deorbit from 200 km arriving about 2,764 km downrange, at the ~5° that
    // makes the arrival shallow enough for a metre of altitude to be eleven metres of ground.
    private static BallisticArc.Solution Deorbit(out double3 from, out double3 target)
    {
        from = new double3(R + 200_000.0, 0, 0);
        double range = 2_764_000.0;
        target = new double3(R * Math.Cos(range / R), R * Math.Sin(range / R), 0);

        double3 circular = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target, out BallisticArc.Solution s));
        return s;
    }

    // The world the round is given, mutated per frame so the lambdas the round holds see this
    // frame's sample rather than the one they were built with.
    private sealed class World
    {
        public double3 Centre;
        public double3 Velocity;
    }

    /// <summary>
    /// Flies the warhead in an ecliptic frame moving at <paramref name="carrier"/>, and answers
    /// where it came down as a place on the ground.
    ///
    /// <para>Every sample the round is given is the one <c>WeaponSystem.UpdateRounds</c> gives it,
    /// at the phase <c>docs/FRAMES-AND-EPOCHS.md</c> records: the celestial state belongs to the
    /// start of the step about to be integrated, so it is in step with the round's position before
    /// the step rather than after it.</para>
    /// </summary>
    private static double3 FlyThroughTheEcliptic(double3 fromCci, double3 velocityCci,
                                                 MunitionProfile munition, double3 carrier, double dt)
    {
        BallisticBody body = Earth;
        TravellingGround ground = new();
        World world = new() { Velocity = carrier };

        Slug round = new(fromCci, velocityCci + carrier, null, 1, Vec.Zero, carrier)
        {
            Munition = munition,
            Ground = ground,

            // The mirror of WeaponSystem.AirDensityIntoFrame: the body stands still through the
            // frame while the round crosses it, so the round's position comes back to the body's
            // epoch before the air is read.
            AirDensityAt = (positionEcl, secondsIntoFrame)
                => DensityAt(positionEcl - world.Centre - world.Velocity * secondsIntoFrame),
        };

        double frameStart = 0.0;
        double burst = 0.0;

        for (int i = 0; i < (int)(3000 / dt) && round.State == RoundState.Flying; i++)
        {
            world.Centre = carrier * frameStart;
            ground.Centre = world.Centre;

            double3 positionCci = round.PositionEcl - world.Centre;
            double radius = Vec.Len(positionCci);

            round.Update(dt, null, Vec.Unit(-positionCci) * (Mu / (radius * radius)),
                         carrier + body.GroundVelocityCci(positionCci), Vec.Zero,
                         munition, DensityAt(positionCci));

            burst = frameStart + dt + round.DetonationElapsedInFrame;
            frameStart += dt;
        }

        Assert.NotEqual(RoundState.Flying, round.State);

        return body.UncarryCci(round.PositionEcl - carrier * burst, burst);
    }

    private static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    /// <summary>
    /// The same shot flown in a frame that is moving and in one that is not has to land in the same
    /// place. It is a Galilean translation of every input, so anything that moves is a frame leak.
    ///
    /// <para>Measured against the ground test reading a frozen centre: <b>850 m</b> with the
    /// carrier pointing straight up at the impact point, <b>3.5 km</b> pointing straight down, on a
    /// 16.7 ms frame — and 1.1 km / 10.8 km on a 50 ms one, because the term is one frame of the
    /// planet's own travel. The two radial directions differ because a centre drifting *away*
    /// hides the ground until the round is already under it, which one frame of descent bounds,
    /// while one drifting *towards* the round brings the ground up to meet it with no bound at
    /// all.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(0.05)]
    public void WhereAWarheadMeetsTheGroundDoesNotMoveWithTheEclipticCarrier(double dt)
    {
        BallisticArc.Solution arc = Deorbit(out double3 from, out double3 target);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;

        double3 still = FlyThroughTheEcliptic(from, arc.RequiredVelocityCci, warhead, Vec.Zero, dt);

        double3 up = Vec.Unit(target);
        double3 downrange = Vec.Unit(Vec.Cross(new double3(0, 0, 1), up));

        foreach ((string named, double3 direction) in new (string, double3)[]
                 {
                     ("straight up at the impact point", up),
                     ("straight down at it", -up),
                     ("along the ground track", downrange),
                 })
        {
            double3 carried = FlyThroughTheEcliptic(from, arc.RequiredVelocityCci, warhead,
                                                    direction * CarrierSpeed, dt);

            double moved = GroundMetres(carried, still);
            Out.WriteLine($"{dt * 1000:F1} ms frame, carrier {named}: impact moved {moved:F0} m");

            Assert.True(moved < 25.0,
                        $"a {CarrierSpeed / 1000.0:F1} km/s ecliptic carrier {named} moved the "
                        + $"impact {moved:F0} m on a {dt * 1000:F1} ms frame");
        }
    }

    /// <summary>
    /// And the round then lands where its own release prediction says it will, which is the number
    /// the ballistic computer's release probe reports.
    ///
    /// <para><see cref="ImpactPredictor"/> integrates in the body's own frame, where there is no
    /// carrier to leak, so it never saw this. The probe read 0.1 km while the warheads landed
    /// 431 m to 1.4 km out — a difference no aim correction can remove, because the correction's
    /// only observer is the prediction.</para>
    /// </summary>
    [Fact]
    public void AWarheadLandsWhereItsOwnReleasePredictionSaysItWill()
    {
        BallisticArc.Solution arc = Deorbit(out double3 from, out double3 target);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;

        Assert.True(ImpactPredictor.TryPredict(Earth, from, arc.RequiredVelocityCci, 2.0, 12_000.0,
                                               out ImpactPredictor.Impact predicted, null, null,
                                               new ImpactPredictor.Drag(DensityAt, warhead)));

        double3 carrier = Vec.Unit(target) * CarrierSpeed;
        double3 landed = FlyThroughTheEcliptic(from, arc.RequiredVelocityCci, warhead, carrier,
                                               1.0 / 60.0);

        double apart = GroundMetres(landed, predicted.GroundFixedPointCci);
        Out.WriteLine($"round to its own prediction, flown through the ecliptic: {apart:F0} m");

        Assert.True(apart < 250.0,
                    $"the round landed {apart:F0} m from the prediction of the same release state");
    }
}
