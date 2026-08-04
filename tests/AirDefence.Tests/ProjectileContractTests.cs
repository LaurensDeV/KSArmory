using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// The <see cref="IProjectile"/> contract, run against <em>every</em> implementation.
///
/// <para>An interface with one implementor proves nothing — the same trap as a registry with one
/// entry, where "picked the right one" and "picked the only one" are indistinguishable. These
/// theories are parameterised over the implementations so a second kind of weapon either obeys the
/// frame and epoch rules or fails here.</para>
///
/// <para>Those rules are properties of the <b>engine</b>, not of any one weapon: the target sample
/// arrives at the end of the step, the drawn offset is taken after the step, and a body is oriented
/// off local velocity. Every projectile pays them. See <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
/// </summary>
public class ProjectileContractTests
{
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);

    /// <summary>Roughly Earth's orbital velocity — the magnitude behind every frame bug here.</summary>
    private static readonly double3 SolarFrame = new(29_800, 0, 0);

    public enum Kind { GuidedMissile, KineticSlug }

    public static TheoryData<Kind> AllKinds => [Kind.GuidedMissile, Kind.KineticSlug];

    private static IProjectile Make(Kind kind, double3 positionEcl, double3 velocityEcl,
                                    double3 platformEcl, double3 frameVelocityEcl) => kind switch
    {
        Kind.GuidedMissile => new Interceptor(positionEcl, velocityEcl, TargetHandle, 1, platformEcl, frameVelocityEcl),
        _ => new Slug(positionEcl, velocityEcl, TargetHandle, 1, platformEcl, frameVelocityEcl),
    };

    private static MunitionProfile Vacuum() =>
        new() { Name = "test", DisplayName = "test", DragK = 0f, BoostSeconds = 0f, BoostAccel = 0f };

    // ---- Orientation -----------------------------------------------------

    /// <summary>
    /// A projectile must be orientable on the frame it is created, before it has ever been
    /// integrated — because that is a frame it is genuinely drawn on. Getting this wrong pointed
    /// missiles along Earth's orbit at launch, and a slug would do exactly the same.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void LocalVelocityIsUsableBeforeTheFirstUpdate(Kind kind)
    {
        double3 platform = new(1.496e11, 0, 0);

        IProjectile round = Make(kind, platform, SolarFrame + new double3(0, 0, 600), platform, SolarFrame);

        Assert.True(Vec.AngleBetween(new double3(0, 0, 1), round.VelocityLocal) < 0.05,
            $"{kind} does not know its frame at birth - its body will be drawn along the ecliptic");
        Assert.True(Vec.AngleBetween(new double3(0, 0, 1), round.VelocityEcl) > 1.5,
            "the test frame is not fast enough to tell the two apart");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void SpeedIsLocalNotAbsolute(Kind kind)
    {
        double3 platform = new(1.496e11, 0, 0);

        IProjectile round = Make(kind, platform, SolarFrame + new double3(0, 0, 600), platform, SolarFrame);

        Assert.InRange(round.Speed, 599.0, 601.0);
    }

    // ---- Frames ----------------------------------------------------------

    /// <summary>
    /// Travel starts at exactly zero. A body placed at its tube anchor plus travel would otherwise
    /// begin its life displaced by whatever the platform's launch offset happened to be.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void TravelSinceLaunchStartsAtZero(Kind kind)
    {
        double3 platform = new(1.496e11, 0, 0);

        IProjectile round = Make(kind, platform + new double3(0, 0, 5), SolarFrame, platform, SolarFrame);

        Assert.Equal(0.0, Vec.Len(round.TravelSinceLaunch), 9);
    }

    /// <summary>
    /// The drawn offset must not carry the frame's motion. Flown in a 29.8 km/s frame and in a
    /// still one, the same local flight must produce the same offset.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void TheDrawnOffsetIsUnaffectedByTheFramesMotion(Kind kind)
    {
        MunitionProfile munition = Vacuum();
        const double dt = 1.0 / 60.0;

        IProjectile still = Make(kind, Vec.Zero, new double3(0, 0, 300), Vec.Zero, Vec.Zero);
        IProjectile carried = Make(kind, Vec.Zero, SolarFrame + new double3(0, 0, 300), Vec.Zero, SolarFrame);

        double3 platform = Vec.Zero;
        for (int i = 0; i < 30; i++)
        {
            // Advanced BEFORE the update that uses it, which is the phase the engine actually has.
            platform += SolarFrame * dt;

            still.Update(dt, null, NoGravity, Vec.Zero, Vec.Zero, munition);
            carried.Update(dt, null, NoGravity, SolarFrame, platform, munition);
        }

        double drift = Vec.Len(still.OffsetFromPlatform - carried.OffsetFromPlatform);
        Assert.True(drift < 1.0, $"{kind} offset moved {drift:F1} m when the frame was carried at 29.8 km/s");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void DistanceFlownMeasuresLocalMotion(Kind kind)
    {
        MunitionProfile munition = Vacuum();
        const double dt = 1.0 / 60.0;

        IProjectile round = Make(kind, Vec.Zero, SolarFrame + new double3(0, 0, 300), Vec.Zero, SolarFrame);

        double3 platform = Vec.Zero;
        for (int i = 0; i < 60; i++)
        {
            platform += SolarFrame * dt;
            round.Update(dt, null, NoGravity, SolarFrame, platform, munition);
        }

        // One second at 300 m/s local, not 30 km of the planet's orbit.
        Assert.InRange(round.DistanceFlown, 280.0, 320.0);
    }

    // ---- The fuse --------------------------------------------------------

    /// <summary>
    /// The detonation instant is measured against a world sample taken at the END of the step, so
    /// it names an instant at or before that — negative, inside the step just integrated. A caller
    /// that gets the sign wrong doubles the error instead of cancelling it, and the blast finds
    /// nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void DetonationElapsedIsNegativeAndInsideTheStep(Kind kind)
    {
        MunitionProfile munition = Vacuum();
        munition.FuseArmSeconds = 0f;
        const double dt = 1.0 / 30.0;

        IProjectile round = Make(kind, Vec.Zero, SolarFrame + new double3(500, 0, 0), Vec.Zero, SolarFrame);

        // End-of-step sample, which is the convention KSA hands over.
        var target = new TargetState(new double3(20, 0, 0) + SolarFrame * dt, SolarFrame, 1.0);

        round.Update(dt, target, NoGravity, SolarFrame, Vec.Zero, munition);

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.InRange(round.DetonationElapsedInFrame, -dt, 0.0);
    }

    /// <summary>
    /// The trigger includes the target's own radius, so a zero fuse radius is a contact hit. That
    /// has to hold for every projectile, since it is what makes kinetic weapons expressible.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void AZeroFuseRadiusStillTriggersOnContactWithTheTargetBody(Kind kind)
    {
        MunitionProfile munition = Vacuum();
        munition.FuseRadius = 0f;
        munition.FuseArmSeconds = 0f;
        munition.NavConstant = 0f;          // no steering, so both kinds fly the same line

        // Aimed straight through the middle of a 10 m body.
        IProjectile round = Make(kind, Vec.Zero, new double3(800, 0, 0), Vec.Zero, Vec.Zero);
        var target = new TargetState(new double3(400, 0, 0), Vec.Zero, 10.0);

        for (int i = 0; i < 60 && round.State == RoundState.Flying; i++)
        {
            round.Update(1.0 / 60.0, target, NoGravity, Vec.Zero, Vec.Zero, munition);
        }

        Assert.Equal(RoundState.Detonated, round.State);
    }

    /// <summary>A safed fuse must not fire, whatever passes it.</summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void TheFuseStaysSafeUntilArmed(Kind kind)
    {
        MunitionProfile munition = Vacuum();
        munition.FuseArmSeconds = 5f;
        munition.FuseRadius = 50f;

        IProjectile round = Make(kind, Vec.Zero, new double3(100, 0, 0), Vec.Zero, Vec.Zero);
        var target = new TargetState(new double3(1, 0, 0), Vec.Zero, 1.0);

        round.Update(1.0 / 60.0, target, NoGravity, Vec.Zero, Vec.Zero, munition);

        Assert.Equal(RoundState.Flying, round.State);
    }

    // ---- Lifetime and robustness -----------------------------------------

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ARoundWithNothingToChaseExpiresCleanly(Kind kind)
    {
        MunitionProfile munition = Vacuum();
        munition.MaxFlightSeconds = 2f;

        IProjectile round = Make(kind, Vec.Zero, new double3(300, 0, 0), Vec.Zero, Vec.Zero);

        while (round.State == RoundState.Flying)
        {
            round.Update(1.0 / 60.0, null, NoGravity, Vec.Zero, Vec.Zero, munition);
        }

        Assert.Equal(RoundState.Expired, round.State);
        Assert.True(Vec.IsFinite(round.PositionEcl));
        Assert.True(Vec.IsFinite(round.OffsetFromPlatform));
        Assert.InRange(round.Age, 2.0 - 0.05, 2.0 + 0.05);
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ATrailIsRecordedAndStaysLocal(Kind kind)
    {
        MunitionProfile munition = Vacuum();
        const double dt = 1.0 / 60.0;

        IProjectile round = Make(kind, Vec.Zero, SolarFrame + new double3(0, 0, 200), Vec.Zero, SolarFrame);

        double3 platform = Vec.Zero;
        for (int i = 0; i < 120; i++)
        {
            platform += SolarFrame * dt;
            round.Update(dt, null, NoGravity, SolarFrame, platform, munition);
        }

        Assert.True(round.TrailOffsets.Count > 2, $"{kind} recorded no trail");

        for (int i = 1; i < round.TrailOffsets.Count; i++)
        {
            double gap = Vec.Len(round.TrailOffsets[i] - round.TrailOffsets[i - 1]);
            Assert.True(gap < 1000.0, $"{kind} trail points are {gap / 1000.0:F1} km apart - frame motion is leaking in");
        }
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void AZeroOrNegativeStepDoesNothing(Kind kind)
    {
        MunitionProfile munition = Vacuum();
        IProjectile round = Make(kind, Vec.Zero, new double3(300, 0, 0), Vec.Zero, Vec.Zero);

        round.Update(0.0, null, NoGravity, Vec.Zero, Vec.Zero, munition);
        round.Update(-1.0, null, NoGravity, Vec.Zero, Vec.Zero, munition);

        Assert.Equal(0.0, round.Age);
        Assert.Equal(0.0, Vec.Len(round.TravelSinceLaunch), 9);
    }

    // ---- What actually differs between them ------------------------------

    /// <summary>
    /// The discriminator. If both implementations behaved identically the abstraction would be
    /// decoration — a slug must genuinely fail to lead a crossing target that a guided round hits.
    /// </summary>
    [Fact]
    public void OnlyTheGuidedRoundLeadsACrossingTarget()
    {
        MunitionProfile munition = Vacuum();
        munition.FuseArmSeconds = 0f;
        munition.MaxFlightSeconds = 20f;

        static (RoundState State, double Closest) Fly(IProjectile round)
        {
            double3 start = new(2500, 0, 0);
            double3 vel = new(0, 250, 0);
            const double dt = 1.0 / 60.0;

            double t = 0.0, closest = double.MaxValue;
            var munition = new MunitionProfile
            {
                Name = "t", DisplayName = "t", DragK = 0f,
                FuseArmSeconds = 0f, MaxFlightSeconds = 20f,
            };

            while (round.State == RoundState.Flying && t < 20.0)
            {
                double3 pos = start + vel * t;
                closest = Math.Min(closest, Vec.Len(pos - round.PositionEcl));
                round.Update(dt, new TargetState(pos + vel * dt, vel, 5.0), NoGravity, Vec.Zero, Vec.Zero, munition);
                t += dt;
            }
            return (round.State, closest);
        }

        var guided = Fly(new Interceptor(Vec.Zero, new double3(600, 0, 0), TargetHandle, 1, Vec.Zero, Vec.Zero));
        var slug = Fly(new Slug(Vec.Zero, new double3(600, 0, 0), TargetHandle, 1, Vec.Zero, Vec.Zero));

        Assert.Equal(RoundState.Detonated, guided.State);
        Assert.NotEqual(RoundState.Detonated, slug.State);
        Assert.True(slug.Closest > guided.Closest * 10.0,
            $"the slug closed to {slug.Closest:F0} m against the missile's {guided.Closest:F0} m - " +
            "it is steering, which it must not be");
    }

    /// <summary>A slug has no seeker and no lock, and says so rather than pretending.</summary>
    [Fact]
    public void ASlugNeverClaimsALock()
    {
        IProjectile slug = new Slug(Vec.Zero, new double3(600, 0, 0), TargetHandle, 1, Vec.Zero, Vec.Zero);

        Assert.False(slug.HasLock);
        Assert.True(slug.SeekerInView);
        Assert.Equal(1.0, slug.FinDeployment(Vacuum()));
    }
}
