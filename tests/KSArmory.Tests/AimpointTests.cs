using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Guidance needs a position, a velocity and a size, and does not care whether they came from a
/// craft, a component or a coordinate.
/// </summary>
public class AimpointTests
{
    private static readonly object Handle = new();
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);

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
    /// A coordinate has nothing in the world behind it, so a bomb aimed at one keeps its aimpoint
    /// until it arrives.
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
        { Munition = BuiltIns.Missile57E6,
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
    /// Every other test here hands the round a target object, but <see cref="Aimpoint.AtPoint"/> has
    /// nothing to name; steering tied to <c>TargetRef</c> would fly a designated round straight on.
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
        { Munition = BuiltIns.Missile57E6,
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
    /// The other tests fly against a target at zero velocity, where a bare ecliptic coordinate is
    /// correct; so this runs one engagement in two frames, as <c>docs/FRAMES-AND-EPOCHS.md</c> prescribes.
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

        // A metre of slack for the sub-step landing at a slightly different instant; a
        // frame-dependent answer is 251 m out at the fuse and unbounded in the guidance.
        Assert.Equal(still.miss, moving.miss, 0);
        Assert.Equal(still.age, moving.age, 2);
    }

    /// <summary>
    /// Without this the invariance test above would pass as readily against code that never had the
    /// bug: the failing form is a zero-velocity aimpoint at a position nothing advances.
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

        // Anchored, it is resampled every frame as the KSA side does a Ground aimpoint; otherwise
        // it is a coordinate written down once, which the planet leaves behind at the frame speed.
        Aimpoint aim = anchored
                           ? Aimpoint.OnGround(TargetHandle, Vec.Zero, ground, frame, 5.0)
                           : Aimpoint.AtPoint(ground, 5.0);

        var round = new Interceptor(Vec.Zero, new double3(600, 0, 0) + frame, TargetHandle, 1,
                                    Vec.Zero, frame)
        { Munition = BuiltIns.Missile57E6,
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

    [Fact]
    public void AnUnguidedRoundCarriesAnAimpointToo()
    {
        var slug = new Slug(Vec.Zero, new double3(600, 0, 0), TargetHandle, 1, Vec.Zero, Vec.Zero)
        { Munition = BuiltIns.Cannon30Mm,
            Aimpoint = Aimpoint.AtPoint(new double3(1000, 0, 0)),
        };

        Assert.Equal(AimpointKind.Point, slug.Aimpoint.Kind);
        Assert.False(slug.Aimpoint.NeedsHandle);
    }

    /// <summary>
    /// A place on a body is only still in that body's frame; held as the coordinate it was named at,
    /// it is left behind by ~29.8 km/s of orbital motion plus the site's spin.
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
    /// Why a designation is an aimpoint rather than a contact: nothing ever reports a hillside, so a
    /// contact-shaped one would be dropped the moment the world stopped mentioning it.
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
