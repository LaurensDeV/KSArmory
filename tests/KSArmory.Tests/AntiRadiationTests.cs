using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="GuidanceMode.AntiRadiation"/>: a round that homes on an emission rather than on the
/// airframe carrying it.
///
/// <para>Three properties matter and each is pinned by a pair — a case that hits and a case that
/// must miss. Without the miss, a hit proves only that the round can fly straight.</para>
/// </summary>
public class AntiRadiationTests
{
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);

    private static MunitionProfile Harm() => new()
    {
        Name = "arm",
        DisplayName = "arm",
        DragK = 0f,
        Guidance = GuidanceMode.AntiRadiation,
        SeekerFovDeg = 70f,
        NavConstant = 4f,
        MaxLateralG = 20f,
        BoostSeconds = 3f,
        BoostAccel = 250f,
        SeparationSeconds = 0.2f,
        FuseRadius = 16f,
        FuseArmSeconds = 0.5f,
        MaxFlightSeconds = 60f,
    };

    /// <summary>
    /// Flies one engagement. <paramref name="emitting"/> and <paramref name="localOffset"/> are
    /// both functions of flight time, so a set can be shut down part way and the site can then be
    /// driven away from where it was transmitting.
    /// </summary>
    /// <param name="carrier">
    /// Ecliptic motion both the round and the target sit inside. Non-zero is the case that
    /// separates a remembered <em>state</em> from a remembered coordinate — see the test that
    /// uses it.
    /// </param>
    private static (RoundState State, double ClosestToTruth, bool HasEmission) Fly(
        Func<double, bool> emitting,
        Func<double, double3> localOffset,
        Func<double, double3> localVelocity,
        double3 carrier = default,
        MunitionProfile? profile = null)
    {
        MunitionProfile munition = profile ?? Harm();

        var round = new Interceptor(
            positionEcl: default,
            velocityEcl: carrier + new double3(munition.LaunchSpeed, 0, 0),
            TargetHandle,
            tube: 1,
            platformEcl: default,
            frameVelocityEcl: carrier) { Munition = munition };

        const double dt = 1.0 / 60.0;
        double t = 0.0;
        double closest = double.MaxValue;

        while (round.State == RoundState.Flying && t < 40.0)
        {
            // Where the site actually is, as opposed to where it was last heard. Paired with the
            // round's pre-step position, which belongs to the same instant.
            double3 truthEcl = localOffset(t) + carrier * t;
            closest = Math.Min(closest, Vec.Len(truthEcl - round.PositionEcl));

            // The sample handed to the round is the state at the END of the step it is about to
            // integrate, because that is what KSA writes -- Interceptor back-dates by exactly
            // frameSeconds on that assumption. Passing the start-of-step state instead leaves
            // every line of sight one frame of the frame's own motion out, which is 497 m at
            // 29.8 km/s and invisible at rest.
            double next = t + dt;
            round.Update(dt,
                         new TargetState(localOffset(next) + carrier * next,
                                         localVelocity(next) + carrier, 5.0, TargetHandle,
                                         emitting(next)),
                         NoGravity, frameVelocityEcl: carrier, platformEcl: default, munition);
            t = next;
        }

        return (round.State, closest, round.HasEmission);
    }

    private static double3 Site(double _) => new(3000, 400, 0);
    private static double3 Still(double _) => Vec.Zero;

    [Fact]
    public void ARadiatingSiteIsHit()
    {
        var (state, closest, heard) = Fly(_ => true, Site, Still);

        Assert.Equal(RoundState.Detonated, state);
        Assert.True(heard);
        // The fuse triggers at FuseRadius + the target's radius, so 21 m is a hit. This is
        // sampled once a frame while the round closes ~12 m per frame, so the recorded minimum
        // sits a little above the true one -- loose enough to allow that, and nowhere near the
        // hundreds of metres the miss cases record.
        Assert.True(closest < 40.0, $"closest approach was {closest:F0} m");
    }

    /// <summary>
    /// The discrimination half of the pair above. A round that never hears anything has nothing to
    /// steer at, so the identical geometry must miss — otherwise the hit proves only that a round
    /// launched roughly at a target arrives at it.
    /// </summary>
    [Fact]
    public void ASiteThatNeverRadiatesIsNotEngaged()
    {
        var (state, closest, heard) = Fly(_ => false, Site, Still);

        Assert.NotEqual(RoundState.Detonated, state);
        Assert.False(heard);
        Assert.True(closest > 300.0,
            $"a silent site was passed within {closest:F0} m — this geometry is winnable without "
            + "homing, so it cannot prove the emission gate");
    }

    /// <summary>
    /// Shutting down does not save a set that stays where it was: the round carries on to where
    /// the emission last came from, and the site is still there.
    /// </summary>
    [Fact]
    public void ASiteThatShutsDownButStaysPutIsStillHit()
    {
        var (state, closest, heard) = Fly(t => t < 1.5, Site, Still);

        Assert.True(heard);
        Assert.Equal(RoundState.Detonated, state);
        // The fuse triggers at FuseRadius + the target's radius, so 21 m is a hit. This is
        // sampled once a frame while the round closes ~12 m per frame, so the recorded minimum
        // sits a little above the true one -- loose enough to allow that, and nowhere near the
        // hundreds of metres the miss cases record.
        Assert.True(closest < 40.0, $"closest approach was {closest:F0} m");
    }

    /// <summary>
    /// And the other half of that trade: going quiet buys the time to leave, so a set that shuts
    /// down <em>and</em> drives away survives. The round is committed to a place, not to a track.
    ///
    /// <para>It still <em>detonates</em> — on the patch of ground the emission came from, which is
    /// what an anti-radiation round does and not a miss in the ordinary sense. What decides
    /// whether the site lives is how far that burst is from where the site has got to, so that is
    /// what this asserts.</para>
    /// </summary>
    [Fact]
    public void ASiteThatShutsDownAndMovesAwaySurvives()
    {
        const double quiet = 1.5;
        double3 vel = new(0, 220, 0);

        var (state, closest, _) = Fly(
            t => t < quiet,
            t => new double3(3000, 400, 0) + (t < quiet ? Vec.Zero : vel * (t - quiet)),
            t => t < quiet ? Vec.Zero : vel);

        Assert.Equal(RoundState.Detonated, state);
        Assert.True(closest > 200.0,
            $"the site was still caught at {closest:F0} m after shutting down and running — the "
            + "round is tracking the target rather than the last emission");
    }

    /// <summary>
    /// The remembered emission is a position <em>and the velocity it was seen with</em>, replayed
    /// on the round's own clock — never a bare ecliptic coordinate.
    ///
    /// <para>Everything here sits inside 29.8 km/s of ecliptic motion, and the site is stationary
    /// on its planet. A memory that stored only the point is left behind by the whole carrier the
    /// instant the set goes quiet: ~30 km per second of flight, which is not a near miss but a
    /// different part of the solar system. This is the same rule the draw anchor and the round
    /// bodies obey, and it is why <c>Interceptor</c> keeps a velocity beside the point.</para>
    /// </summary>
    [Fact]
    public void TheRememberedEmissionCarriesTheFramesEclipticMotion()
    {
        double3 carrier = new(0, 0, 29800);

        var (state, closest, heard) = Fly(t => t < 1.5, Site, Still, carrier);

        Assert.True(heard);
        Assert.Equal(RoundState.Detonated, state);
        Assert.True(closest < 40.0,
            $"closest approach was {closest:F0} m against a site that never moved — the memory is "
            + "not keeping pace with the frame");
    }

    /// <summary>
    /// <see cref="TargetState.Emitting"/> defaults to true and only this guidance reads it, so
    /// every other weapon behaves exactly as it did before the field existed.
    /// </summary>
    [Fact]
    public void OtherGuidanceIgnoresEmissionEntirely()
    {
        MunitionProfile seeker = Harm();
        seeker.Guidance = GuidanceMode.Seeker;

        var (state, closest, heard) = Fly(_ => false, Site, Still, profile: seeker);

        Assert.Equal(RoundState.Detonated, state);
        // The fuse triggers at FuseRadius + the target's radius, so 21 m is a hit. This is
        // sampled once a frame while the round closes ~12 m per frame, so the recorded minimum
        // sits a little above the true one -- loose enough to allow that, and nowhere near the
        // hundreds of metres the miss cases record.
        Assert.True(closest < 40.0, $"closest approach was {closest:F0} m");
        Assert.False(heard, "only an anti-radiation round records an emission");
    }
}
