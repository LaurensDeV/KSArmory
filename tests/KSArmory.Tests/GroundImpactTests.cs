using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A bomb has nothing to arrive at but the ground. Every other round in the arsenal is aimed
/// upwards and passes through terrain, which is cheap and invisible; this is the one that cannot.
/// </summary>
public class GroundImpactTests
{
    private const double Dt = 1.0 / 60.0;
    private const double PlanetRadius = 6_371_000.0;

    private static readonly double3 Centre = new(0, 0, 0);

    /// <summary>A spherical planet, which is what one frame of ground track looks like anyway.</summary>
    private sealed class Ball(double radius) : IGroundTest
    {
        public int Samples;

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            Samples++;
            centreEcl = Centre;
            surfaceRadius = radius;
            return true;
        }
    }

    /// <summary>
    /// A planet that moves, sampled where the engine samples one: at the frame's <em>end</em>
    /// (<c>docs/KSA-FRAME-ORDER.md</c> §5), so it is one step ahead of a round part-way through the
    /// frame. <see cref="Ball"/> cannot see the fault at all — constant centre, motionless round.
    /// </summary>
    private sealed class MovingBall(double radius, double3 velocity, double frame) : IGroundTest
    {
        public int FramesIssued;

        public double3 SampleEcl => Centre + velocity * ((FramesIssued + 1) * frame);

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = SampleEcl;
            surfaceRadius = radius;
            return true;
        }
    }

    private static MunitionProfile Bomb(bool hitsTerrain = true) => new()
    {
        Name = "TESTBOMB",
        DisplayName = "test bomb",
        Guidance = GuidanceMode.None,
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 60f,
        DragK = 0f,
        FuseRadius = 0f,
        ChargeKg = 250f,
        HitsTerrain = hitsTerrain,
    };

    // Released from rest 500 m up, with gravity pointing at the planet's centre.
    private static Slug Dropped(IGroundTest? ground, MunitionProfile munition, double altitude = 500.0)
    {
        double3 start = new(PlanetRadius + altitude, 0, 0);

        return new Slug(start, Vec.Zero, null, 1, start, Vec.Zero)
        {
            Munition = munition,
            Ground = ground,
        };
    }

    private static void Fall(Slug bomb, int frames = 4000)
    {
        for (int i = 0; i < frames && bomb.State == RoundState.Flying; i++)
        {
            double3 gravity = Vec.Unit(Centre - bomb.PositionEcl) * 9.81;
            bomb.Update(Dt, null, gravity, Vec.Zero, Vec.Zero, bomb.Munition);
        }
    }

    /// <summary>
    /// The whole feature. Dropped from 500 m it falls, meets the ground and bursts there — not at
    /// MaxFlightSeconds, and not somewhere under the surface.
    /// </summary>
    [Fact]
    public void ABombDroppedFromRestBurstsOnTheGround()
    {
        Slug bomb = Dropped(new Ball(PlanetRadius), Bomb());

        Fall(bomb);

        Assert.Equal(RoundState.Detonated, bomb.State);
        Assert.True(bomb.HitGround);

        // On the surface, not through it. A sub-step at impact speed is ~0.5 m, so anything much
        // larger than that means the crossing was not backed up to.
        double altitude = Vec.Len(bomb.PositionEcl - Centre) - PlanetRadius;
        Assert.True(Math.Abs(altitude) < 0.5, $"burst at {altitude:F2} m, expected the surface");
    }

    /// <summary>
    /// Without the flag the terrain is not there at all, which is how every other round behaves and
    /// what keeps a 150-shell burst from paying for a terrain sample each. Without this the test
    /// above passes against a round that detonates on anything.
    /// </summary>
    [Fact]
    public void ARoundThatDoesNotHitTerrainFallsStraightThrough()
    {
        Ball ground = new(PlanetRadius);
        Slug shell = Dropped(ground, Bomb(hitsTerrain: false));

        Fall(shell);

        Assert.Equal(RoundState.Expired, shell.State);
        Assert.False(shell.HitGround);
        Assert.Equal(0, ground.Samples);
    }

    /// <summary>
    /// One terrain sample a frame, whatever the sub-step count. That is the reason the seam answers
    /// with a centre and a radius rather than an altitude — an altitude would have to be re-read
    /// per sub-step to mean anything, and the sample is the expensive half.
    /// </summary>
    [Fact]
    public void TheGroundIsSampledOncePerFrameNotPerSubStep()
    {
        Ball ground = new(PlanetRadius);
        Slug bomb = Dropped(ground, Bomb());

        int frames = 0;
        while (bomb.State == RoundState.Flying && frames < 4000)
        {
            frames++;
            double3 gravity = Vec.Unit(Centre - bomb.PositionEcl) * 9.81;
            bomb.Update(Dt, null, gravity, Vec.Zero, Vec.Zero, bomb.Munition);
        }

        Assert.True(Interceptor.SubStep < Dt, "a frame must span several sub-steps for this to bite");
        Assert.Equal(frames, ground.Samples);
    }

    /// <summary>
    /// The burst instant is inside the step it happened in, negative because the world sample is
    /// end-of-frame. Detonate back-dates the world by it to place the blast, so a value outside
    /// that range puts the explosion in the wrong place by a whole frame of the planet's motion.
    /// </summary>
    [Fact]
    public void TheBurstInstantSitsInsideTheStepThatCausedIt()
    {
        Slug bomb = Dropped(new Ball(PlanetRadius), Bomb());

        Fall(bomb);

        Assert.Equal(RoundState.Detonated, bomb.State);
        Assert.InRange(bomb.DetonationElapsedInFrame, -Dt, 0.0);
    }

    /// <summary>
    /// A ground test that will not answer leaves the round flying rather than bursting it in mid
    /// air. Same rule as the hull test: what cannot be established is not a hit.
    /// </summary>
    [Fact]
    public void NoAnswerIsNotAnImpact()
    {
        Slug bomb = Dropped(null, Bomb());

        Fall(bomb);

        Assert.Equal(RoundState.Expired, bomb.State);
        Assert.False(bomb.HitGround);
    }

    /// <summary>
    /// The centre is a position, and the body it names moves. The engine's celestial sample is at
    /// the frame's end while the round crosses the frame, so a centre held for the frame drifts
    /// against the round by <c>bodyVelocity × (frame − elapsed)</c> — and the round then stops when
    /// *that* distance reaches the radius rather than when it reaches the ground.
    ///
    /// <para>Flown at 29.8 km/s of carrier before the drift was carried: stop heights of 248–412 m,
    /// which at the 13.8° arrival of a reentry vehicle is 1.0–1.7 km of ground.
    /// <c>docs/MIRV-NEXT.md</c> items 8l and 8n.</para>
    ///
    /// <para>The body's velocity is set across the radius rather than along it, so both a radial
    /// and a tangential component exist — which is what any real geometry gives.</para>
    /// </summary>
    [Fact]
    public void TheGroundCentreIsCarriedWithTheBodyAcrossAFrame()
    {
        double3 bodyVelocity = Vec.Unit(new double3(1, 1, 0)) * 29_800.0;

        MovingBall ground = new(PlanetRadius, bodyVelocity, Dt);
        double3 start = Centre + new double3(PlanetRadius + 500.0, 0, 0);

        // Co-moving with the body, so relative to the ground it is simply dropped from 500 m.
        Slug bomb = new(start, bodyVelocity, null, 1, start, Vec.Zero)
        {
            Munition = Bomb(),
            Ground = ground,

            // Composed by the caller, exactly as WeaponSystem composes it. secondsIntoFrame arrives
            // back-dated and negative, so this walks the centre back to the sub-step's own instant.
            GroundCentreDriftAt = s => bodyVelocity * s,
        };

        for (int i = 0; i < 4000 && bomb.State == RoundState.Flying; i++)
        {
            double3 gravity = Vec.Unit(ground.SampleEcl - bomb.PositionEcl) * 9.81;
            bomb.Update(Dt, null, gravity, Vec.Zero, Vec.Zero, bomb.Munition);
            ground.FramesIssued++;
        }

        Assert.Equal(RoundState.Detonated, bomb.State);
        Assert.True(bomb.HitGround);

        // Where the body honestly was when it burst: the last issued sample is that frame's end,
        // and DetonationElapsedInFrame is measured back from it.
        double3 centreAtBurst = Centre + bodyVelocity * (ground.FramesIssued * Dt)
                                + bodyVelocity * bomb.DetonationElapsedInFrame;

        double altitude = Vec.Len(bomb.PositionEcl - centreAtBurst) - PlanetRadius;

        // The same bar the stationary case is held to. Against a centre frozen for the frame this
        // is -88.3 m, and in flight it was 248-412 m.
        Assert.True(Math.Abs(altitude) < 0.5,
                    $"burst {altitude:F1} m from the surface of a body that was moving; "
                    + "the cached ground centre is not being carried with it");
    }

    /// <summary>
    /// Ground at a constant gradient along +Y, sampled like the engine's body at the frame's end. The
    /// height under a point depends on where the point is, which is the one thing a sphere — and so
    /// every other ground in this file — cannot show.
    /// </summary>
    private sealed class Ramp(double gradient, double3 velocity = default, double frame = 0.0) : IGroundTest
    {
        public int FramesIssued;

        public double3 SampleEcl => Centre + velocity * ((FramesIssued + 1) * frame);

        public double RadiusUnder(double3 bodyRelative)
            => PlanetRadius + gradient * PlanetRadius * Math.Atan2(bodyRelative.Y, bodyRelative.X);

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = SampleEcl;
            surfaceRadius = RadiusUnder(positionEcl - centreEcl);
            return true;
        }
    }

    /// <summary>
    /// A reentry vehicle's last few kilometres — 3 km/s at 32 degrees below the horizontal — flown
    /// onto a ramp at a stated frame. Returns where it stopped relative to the body, and how far
    /// that is above the true surface there.
    /// </summary>
    private static (double3 Stop, double Altitude) Arrive(Ramp ground, double frame, bool resample,
                                                           double above = 2_000.0,
                                                           double3 bodyVelocity = default)
    {
        double g = 32.0 * Math.PI / 180.0;
        double3 start = Centre + new double3(PlanetRadius + above, 0, 0);
        double3 v = new double3(-Math.Sin(g), Math.Cos(g), 0) * 3_000.0;

        Slug round = new(start, bodyVelocity + v, null, 1, start, Vec.Zero)
        {
            Munition = Bomb(),
            Ground = ground,
            GroundCentreDriftAt = s => bodyVelocity * s,
            ResampleGroundNearImpact = resample,
        };

        for (int i = 0; i < 200_000 && round.State == RoundState.Flying; i++)
        {
            double3 gravity = Vec.Unit(ground.SampleEcl - round.PositionEcl) * 9.81;
            round.Update(frame, null, gravity, Vec.Zero, Vec.Zero, round.Munition);
            ground.FramesIssued++;
        }

        Assert.True(round.HitGround, "the round never reached the ground");

        double3 centreAtBurst = Centre + bodyVelocity * (ground.FramesIssued * frame)
                                + bodyVelocity * round.DetonationElapsedInFrame;
        double3 stop = round.PositionEcl - centreAtBurst;

        return (stop, Vec.Len(stop) - ground.RadiusUnder(stop));
    }

    /// <summary>
    /// On a slope the frame's first sample is the height of ground the round has already left, so it
    /// stops that far off the surface — and flown, that error times <c>cot(gamma)</c> is the whole of
    /// a warhead's walk from its release probe. Re-reading the ground under each sub-step near the
    /// surface stops it where it meets it. <c>docs/ACCURACY-PLAN.md</c> 3cr.
    ///
    /// <para>The truth is the same round at a tenth of a millisecond, where a frame's sample is a
    /// quarter of a metre stale. Several frames and release heights, because the held error depends
    /// on where in its frame the crossing falls.</para>
    /// </summary>
    [Theory]
    [InlineData(-0.30)]   // seat 3's ground, falling away downrange
    [InlineData(+0.21)]   // rising, where the held sphere lets the round into the hillside
    public void ReReadingTheGroundStopsTheRoundOnASlopeWhereItMeetsIt(double gradient)
    {
        (double3 truth, _) = Arrive(new Ramp(gradient), 0.0001, resample: false);

        double worstHeld = 0.0;
        double worstReread = 0.0;

        foreach (double frame in new[] { 0.016, 0.023, 0.040 })
        {
            foreach (double above in new[] { 2_000.0, 2_017.0, 2_041.0 })
            {
                (double3 truthHere, _) = above == 2_000.0
                                             ? (truth, 0.0)
                                             : Arrive(new Ramp(gradient), 0.0001, resample: false, above);

                (double3 held, _) = Arrive(new Ramp(gradient), frame, resample: false, above);
                (double3 reread, double altitude) = Arrive(new Ramp(gradient), frame, resample: true, above);

                worstHeld = Math.Max(worstHeld, Vec.Len(held - truthHere));
                worstReread = Math.Max(worstReread, Vec.Len(reread - truthHere));

                Assert.True(Math.Abs(altitude) < 0.5,
                            $"re-reading the ground at a {frame * 1000:F0} ms frame, the round stopped "
                            + $"{altitude:+0.0;-0.0} m off the surface under it");
            }
        }

        Assert.True(worstHeld > 10.0,
                    $"holding the frame's sample the round stopped at most {worstHeld:F1} m from where "
                    + "it meets the ground, so this geometry does not reproduce the fault");
        Assert.True(worstReread < 1.0,
                    $"re-reading the ground the round still stopped {worstReread:F1} m from where it "
                    + "meets it");
    }

    /// <summary>
    /// Each re-read is a lookup at a position, measured against a body sampled at the frame's end and
    /// moving at ~30 km/s. So it has to be back-dated to its own sub-step exactly as the frame's first
    /// sample is, or it reads the slope hundreds of metres from where the round is.
    /// </summary>
    [Fact]
    public void AReReadIsTakenAtItsOwnSubStepsInstant()
    {
        double3 moving = Vec.Unit(new double3(1, 1, 0)) * 29_800.0;
        const double frame = 0.023;

        (double3 still, _) = Arrive(new Ramp(-0.30), frame, resample: true);
        (double3 carried, double altitude) =
            Arrive(new Ramp(-0.30, moving, frame), frame, resample: true, bodyVelocity: moving);

        Assert.True(Math.Abs(altitude) < 0.5,
                    $"on a body doing 29.8 km/s the round stopped {altitude:+0.0;-0.0} m off the "
                    + "surface; the re-read is not at its own instant");
        Assert.True(Vec.Len(carried - still) < 0.5,
                    $"the same arrival stopped {Vec.Len(carried - still):F1} m apart on a moving and a "
                    + "still body");
    }
}
