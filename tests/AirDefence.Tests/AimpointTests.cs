using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

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
            _ => Aimpoint.AtPoint(where, 5.0),
        };

        var round = new Interceptor(Vec.Zero, new double3(600, 0, 0), TargetHandle, 1, Vec.Zero, Vec.Zero)
        {
            Aimpoint = aim,
        };

        for (int i = 0; i < 3600 && round.State == RoundState.Flying; i++)
        {
            round.Update(1.0 / 60.0, aim.ToTargetState(), NoGravity, Vec.Zero, Vec.Zero, munition);
        }

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.Equal(kind, round.Aimpoint.Kind);
    }

    /// <summary>A slug carries an aimpoint the same way, since it is on the contract.</summary>
    [Fact]
    public void AnUnguidedRoundCarriesAnAimpointToo()
    {
        var slug = new Slug(Vec.Zero, new double3(600, 0, 0), TargetHandle, 1, Vec.Zero, Vec.Zero)
        {
            Aimpoint = Aimpoint.AtPoint(new double3(1000, 0, 0)),
        };

        Assert.Equal(AimpointKind.Point, slug.Aimpoint.Kind);
        Assert.False(slug.Aimpoint.NeedsHandle);
    }
}
