using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a round is allowed to shoot at.
///
/// <para>Guidance needs a position, a velocity and a size. Nothing about it cares whether those
/// came from a craft, a component, or a coordinate — which is what lets a ground-attack round
/// exist alongside an anti-air one in the same flight model.</para>
/// </summary>
public class AimpointTests
{
    private static readonly object Handle = new();
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);

    // ---- Construction ----------------------------------------------------

    [Fact]
    public void AVehicleAimpointKeepsItsHandleAndKinematics()
    {
        double3 pos = new(1000, 0, 0);
        double3 vel = new(0, 250, 0);

        Aimpoint a = Aimpoint.OnVehicle(Handle, pos, vel, 5.0);

        Assert.Equal(AimpointKind.Vehicle, a.Kind);
        Assert.Same(Handle, a.Handle);
        Assert.Equal(pos, a.PositionEcl);
        Assert.Equal(vel, a.VelocityEcl);
        Assert.Equal(5.0, a.Radius);
        Assert.True(a.NeedsHandle);
    }

    [Fact]
    public void APartAimpointIsDistinctFromItsCraft()
    {
        Aimpoint a = Aimpoint.OnPart(Handle, new double3(10, 0, 0), Vec.Zero, 0.5);

        Assert.Equal(AimpointKind.Part, a.Kind);
        Assert.True(a.NeedsHandle);
    }

    /// <summary>
    /// A coordinate has nothing in the world behind it, so it can never be lost. That is the
    /// whole point: a bomb aimed at a position keeps its aimpoint until it arrives.
    /// </summary>
    [Fact]
    public void APointAimpointNeedsNothingFromTheWorld()
    {
        Aimpoint a = Aimpoint.AtPoint(new double3(0, 0, 500));

        Assert.Equal(AimpointKind.Point, a.Kind);
        Assert.Null(a.Handle);
        Assert.Equal(Vec.Zero, a.VelocityEcl);
        Assert.False(a.NeedsHandle);
    }

    [Fact]
    public void ResamplingKeepsTheIdentityAndMovesTheKinematics()
    {
        Aimpoint a = Aimpoint.OnVehicle(Handle, Vec.Zero, Vec.Zero, 5.0);
        Aimpoint b = a.Resampled(new double3(100, 0, 0), new double3(0, 50, 0));

        Assert.Equal(a.Kind, b.Kind);
        Assert.Same(a.Handle, b.Handle);
        Assert.Equal(a.Radius, b.Radius);
        Assert.Equal(new double3(100, 0, 0), b.PositionEcl);
        Assert.Equal(new double3(0, 50, 0), b.VelocityEcl);
    }

    [Fact]
    public void TheTargetStateHandedToGuidanceCarriesOnlyKinematics()
    {
        Aimpoint a = Aimpoint.OnPart(Handle, new double3(7, 8, 9), new double3(1, 2, 3), 0.4);
        TargetState t = a.ToTargetState();

        Assert.Equal(a.PositionEcl, t.PositionEcl);
        Assert.Equal(a.VelocityEcl, t.VelocityEcl);
        Assert.Equal(a.Radius, t.Radius);
    }

    // ---- Flying at each kind ---------------------------------------------

    /// <summary>
    /// The flight model is indifferent to the kind. A round flown at a stationary coordinate must
    /// arrive and fuse exactly as one flown at a craft does.
    /// </summary>
    // Indexed rather than typed: xUnit requires a public test class, and AimpointKind is
    // internal, so it cannot appear in a public signature.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ARoundArrivesAtEveryKindOfAimpoint(int index)
    {
        AimpointKind kind = (AimpointKind)index;
        var munition = new MunitionProfile
        {
            Name = "test", DisplayName = "test",
            DragK = 0f, FuseArmSeconds = 0f, MaxFlightSeconds = 30f,
        };

        double3 where = new(3000, 0, 0);
        Aimpoint aim = kind switch
        {
            AimpointKind.Vehicle => Aimpoint.OnVehicle(TargetHandle, where, Vec.Zero, 5.0),
            AimpointKind.Part => Aimpoint.OnPart(TargetHandle, where, Vec.Zero, 0.5),
            AimpointKind.Ground => Aimpoint.OnGround(TargetHandle, where, where, Vec.Zero, 5.0),
            _ => Aimpoint.AtPoint(where, 5.0),
        };

        var round = new Interceptor(Vec.Zero, new double3(600, 0, 0), TargetHandle, 1, Vec.Zero, Vec.Zero)
        { Munition = Arsenal.Missile57E6,
            Aimpoint = aim,
        };

        for (int i = 0; i < 3600 && round.State == RoundState.Flying; i++)
        {
            round.Update(1.0 / 60.0, aim.ToTargetState(), NoGravity, Vec.Zero, Vec.Zero, munition);
        }

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.Equal(kind, round.Aimpoint.Kind);
    }

    /// <summary>
    /// A designated round carries <b>no handle at all</b>, and must still guide and fuse.
    ///
    /// <para>Every other test here hands the round a target object as well as an aimpoint, so a
    /// null one is never exercised — but that is exactly what an operator pointing at a place
    /// produces, because <see cref="Aimpoint.AtPoint"/> has nothing to name. Guidance keys on the
    /// per-frame target state rather than on the handle, and this is what holds that: tie steering
    /// to <c>TargetRef</c> and a designated round flies straight on and expires.</para>
    /// </summary>
    [Fact]
    public void ARoundWithNoTargetHandleStillFliesToItsDesignatedPoint()
    {
        var munition = new MunitionProfile
        {
            Name = "test", DisplayName = "test",
            DragK = 0f, FuseArmSeconds = 0f, MaxFlightSeconds = 30f,
        };

        // Offset across the flight path, so arriving needs steering rather than just coasting.
        Aimpoint aim = Aimpoint.AtPoint(new double3(3000, 400, 0), 5.0);

        var round = new Interceptor(Vec.Zero, new double3(600, 0, 0), target: null, 1, Vec.Zero, Vec.Zero)
        { Munition = Arsenal.Missile57E6,
            Aimpoint = aim,
        };

        Assert.Null(round.TargetRef);

        for (int i = 0; i < 3600 && round.State == RoundState.Flying; i++)
        {
            round.Update(1.0 / 60.0, aim.ToTargetState(), NoGravity, Vec.Zero, Vec.Zero, munition);
        }

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.True(Vec.Len(round.PositionEcl - aim.PositionEcl) <= munition.FuseRadius,
                    $"burst {Vec.Len(round.PositionEcl - aim.PositionEcl):F1} m from the designated point");
    }

    /// <summary>
    /// A place on the ground is still a place when the whole world is moving through the ecliptic.
    ///
    /// <para>The other aimpoint tests cannot see this. They all fly from the origin at 600 m/s
    /// against a target with zero velocity — a universe with no shared motion, in which holding an
    /// aimpoint as a bare ecliptic coordinate is <em>correct</em>. Add the 29.8 km/s every real
    /// body carries and the same code turns the frame into closing speed:
    /// <c>v = 0 - VelocityEcl</c>, and proportional navigation drives the round sideways at full
    /// lateral G.</para>
    ///
    /// <para>So it is written as an invariance: run the identical engagement in two frames and
    /// require the same answer. That is the shape <c>docs/FRAMES-AND-EPOCHS.md</c> prescribes for
    /// anything taking two positions, and the only shape that separates a frame bug from a
    /// geometry bug.</para>
    /// </summary>
    [Fact]
    public void AGroundAimpointIsChasedTheSameInAnyFrame()
    {
        (RoundState state, double miss, double age) still = FlyAtGround(0.0);
        (RoundState state, double miss, double age) moving = FlyAtGround(29800.0);

        Assert.Equal(RoundState.Detonated, still.state);
        Assert.Equal(RoundState.Detonated, moving.state);

        Assert.True(still.miss <= 15.0, $"stationary frame missed by {still.miss:F1} m");
        Assert.True(moving.miss <= 15.0, $"moving frame missed by {moving.miss:F1} m");

        // The whole point: the answer must not depend on the frame. A metre of slack for the
        // sub-step landing at a slightly different instant; a frame-dependent answer is 251 m out
        // at the fuse and unbounded in the guidance.
        Assert.Equal(still.miss, moving.miss, 0);
        Assert.Equal(still.age, moving.age, 2);
    }

    /// <summary>
    /// And the same engagement <b>misses</b> when the place is held as a bare ecliptic coordinate.
    ///
    /// <para>Without this the invariance test above proves nothing: it would pass just as happily
    /// against code that never had the bug. The failing form is zero velocity on the aimpoint and
    /// a position nothing advances, and it has to fail loudly here, because on screen it reads as
    /// the round flying off sideways for no visible reason.</para>
    /// </summary>
    [Fact]
    public void HeldAsABareCoordinateTheSameShotMisses()
    {
        (RoundState state, double miss, double _) = FlyAtGround(29800.0, anchored: false);

        Assert.True(miss > 1000.0 || state != RoundState.Detonated,
                    $"a stale ecliptic coordinate was hit to within {miss:F0} m, so this test "
                    + "cannot tell the frame bug from a working round");
    }

    // One engagement against a ground point, in a frame moving at `frameSpeed`. Everything real
    // shares that motion -- the planet, the launcher, the round and the place on the ground -- so
    // the engagement is identical and only the frame differs.
    private static (RoundState State, double Miss, double Age) FlyAtGround(double frameSpeed,
                                                                          bool anchored = true)
    {
        var munition = new MunitionProfile
        {
            Name = "test", DisplayName = "test",
            DragK = 0f, FuseArmSeconds = 0f, MaxFlightSeconds = 60f,
        };

        double3 frame = new(0, frameSpeed, 0);
        double3 ground = new(3000, 400, 0);

        // Resampled from the world every frame, which is what the KSA side does with a Ground
        // aimpoint: the place keeps up with its body instead of being left behind by it.
        // anchored: what the ground actually is. Otherwise the failing form -- a coordinate written
        // down once, with no velocity, which the planet then leaves behind at the frame speed.
        Aimpoint aim = anchored
                           ? Aimpoint.OnGround(TargetHandle, Vec.Zero, ground, frame, 5.0)
                           : Aimpoint.AtPoint(ground, 5.0);

        var round = new Interceptor(Vec.Zero, new double3(600, 0, 0) + frame, TargetHandle, 1,
                                    Vec.Zero, frame)
        { Munition = Arsenal.Missile57E6,
            Aimpoint = aim,
        };

        const double dt = 1.0 / 60.0;

        for (int i = 0; i < 3600 && round.State == RoundState.Flying; i++)
        {
            ground += frame * dt;
            if (anchored)
            {
                aim = aim.Resampled(ground, frame);
                round.Aimpoint = aim;
            }
            round.Update(dt, aim.ToTargetState(), NoGravity, frame, frame, munition);
        }

        return (round.State, round.MissDistance, round.Age);
    }

    /// <summary>A slug carries an aimpoint the same way, since it is on the contract.</summary>
    [Fact]
    public void AnUnguidedRoundCarriesAnAimpointToo()
    {
        var slug = new Slug(Vec.Zero, new double3(600, 0, 0), TargetHandle, 1, Vec.Zero, Vec.Zero)
        { Munition = Arsenal.Cannon30Mm,
            Aimpoint = Aimpoint.AtPoint(new double3(1000, 0, 0)),
        };

        Assert.Equal(AimpointKind.Point, slug.Aimpoint.Kind);
        Assert.False(slug.Aimpoint.NeedsHandle);
    }

    /// <summary>
    /// A place on a body has to be re-read every frame; nothing else does. It is only still in that
    /// body's own frame, so held as the coordinate it was named at it is left behind by ~29.8 km/s
    /// of orbital motion plus the site's spin — whatever is chasing it slides off within a second.
    /// </summary>
    [Fact]
    public void OnlyAPlaceOnABodyNeedsResampling()
    {
        Assert.True(Aimpoint.OnGround(new object(), default, default, default).NeedsResampling);

        Assert.False(Aimpoint.OnVehicle(new object(), default, default, 1.0).NeedsResampling);
        Assert.False(Aimpoint.AtPoint(default).NeedsResampling);
        Assert.False(Aimpoint.Nothing.NeedsResampling);
    }

    /// <summary>
    /// What can die takes its aimpoint with it; what cannot, keeps it. This is why a designation is
    /// an aimpoint rather than a contact — nothing ever reports a hillside, so a contact-shaped one
    /// would have to be dropped the moment the world stopped mentioning it.
    /// </summary>
    [Fact]
    public void OnlyAThingThatCanDieLosesItsAimpoint()
    {
        Assert.False(Aimpoint.OnVehicle(new object(), default, default, 1.0).Survives(handleAlive: false));
        Assert.True(Aimpoint.OnVehicle(new object(), default, default, 1.0).Survives(handleAlive: true));

        // Neither the ground nor a coordinate has anything to lose.
        Assert.True(Aimpoint.OnGround(new object(), default, default, default).Survives(handleAlive: false));
        Assert.True(Aimpoint.AtPoint(default).Survives(handleAlive: false));

        // Nothing named is not something that survives.
        Assert.False(Aimpoint.Nothing.Survives(handleAlive: true));
    }
}
