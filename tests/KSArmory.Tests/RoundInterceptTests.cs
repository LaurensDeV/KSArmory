using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Shooting down a round in the air.
///
/// <para>A round is not a craft: KSA holds no state for it, so nothing the engine knows about can
/// destroy one. <see cref="IProjectile.ShootDown"/> is the only way it ends, and these pin what it
/// means — including that a round already finished cannot be ended twice, which is what a shell
/// and a warhead arriving in the same frame would otherwise do.</para>
///
/// <para>What is <em>not</em> here is the wiring: resolving one round as another's target, and the
/// blast sweep that reaches it, both live under <c>Ksa/</c> and cannot be reached from this
/// project. See <c>CHECKLIST.md</c>.</para>
/// </summary>
public class RoundInterceptTests
{
    private const double Dt = 1.0 / 60.0;

    private static readonly MunitionProfile Shell = BuiltIns.Cannon30Mm;

    /// <summary>The size of a missile body, which is what the airborne contact reports.</summary>
    private const double BodyRadius = 1.5;

    [Fact]
    public void AShotDownRoundStopsFlying()
    {
        Interceptor round = Missile();

        Assert.Equal(RoundState.Flying, round.State);

        round.ShootDown();

        Assert.Equal(RoundState.ShotDown, round.State);
    }

    /// <summary>
    /// Distinct from detonating. A round shot down never fired its warhead, and reading the two as
    /// one state is what would let an intercepted missile splash the thing it was intercepted
    /// over.
    /// </summary>
    [Fact]
    public void BeingShotDownIsNotDetonating()
    {
        Interceptor round = Missile();

        round.ShootDown();

        Assert.NotEqual(RoundState.Detonated, round.State);
    }

    /// <summary>
    /// A shell and a warhead can reach the same round in one frame. The second must not overwrite
    /// how the first ended it, or a round that burst on its target reads afterwards as intercepted
    /// and its kill is never applied.
    /// </summary>
    [Fact]
    public void ARoundThatHasAlreadyFinishedIgnoresIt()
    {
        // A timed fuse, so it ends where the test says rather than wherever the geometry takes it.
        Slug shell = new(Vec.Zero, new double3(Shell.LaunchSpeed, 0, 0), null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            FuseSeconds = 0.2,
        };

        for (int i = 0; i < 200 && shell.State == RoundState.Flying; i++)
        {
            shell.Update(Dt, null, Vec.Zero, Vec.Zero, Vec.Zero, shell.Munition);
        }

        Assert.Equal(RoundState.Detonated, shell.State);

        shell.ShootDown();

        Assert.Equal(RoundState.Detonated, shell.State);
    }

    /// <summary>
    /// A shell scoring on something missile-sized and missile-fast, which is the engagement the
    /// whole thing exists for. It is the ordinary contact rule with a 1.5 m body rather than a
    /// craft's bounding sphere, so what this pins is that the geometry is reachable at all: a
    /// 30 mm round closing head-on at ~1.9 km/s must not step over a target that small.
    /// </summary>
    [Fact]
    public void AShellStrikesAnIncomingRoundAndNamesIt()
    {
        object hostile = new();

        // Head-on: the shell outbound at its muzzle speed, the round inbound at 900 m/s from
        // 2 km, offset by a metre so it is a strike on the body rather than a dead-centre one.
        Slug shell = new(Vec.Zero, new double3(Shell.LaunchSpeed, 0, 0), hostile, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
        };

        double3 hostilePos = new(2000, 1.0, 0);
        double3 hostileVel = new(-900, 0, 0);

        for (int i = 0; i < 600 && shell.State == RoundState.Flying; i++)
        {
            shell.Update(Dt, new TargetState(hostilePos, hostileVel, BodyRadius, hostile),
                         Vec.Zero, Vec.Zero, Vec.Zero, shell.Munition);
            hostilePos += hostileVel * Dt;
        }

        Assert.Equal(RoundState.Detonated, shell.State);
        Assert.Same(hostile, shell.StruckBody);
    }

    /// <summary>
    /// The same engagement with the round passing well to one side. Without this the test above
    /// passes against a shell that detonates on anything it was pointed at.
    /// </summary>
    [Fact]
    public void AShellThatPassesTheIncomingRoundDoesNot()
    {
        object hostile = new();

        Slug shell = new(Vec.Zero, new double3(Shell.LaunchSpeed, 0, 0), hostile, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
        };

        double3 hostilePos = new(2000, 40.0, 0);
        double3 hostileVel = new(-900, 0, 0);

        for (int i = 0; i < 600 && shell.State == RoundState.Flying; i++)
        {
            shell.Update(Dt, new TargetState(hostilePos, hostileVel, BodyRadius, hostile),
                         Vec.Zero, Vec.Zero, Vec.Zero, shell.Munition);
            hostilePos += hostileVel * Dt;
        }

        Assert.NotEqual(RoundState.Detonated, shell.State);
        Assert.Null(shell.StruckBody);
    }

    /// <summary>
    /// Why <c>KSArmoryMod.CollectAirborne</c> carries its sample forward by the step.
    ///
    /// <para>A <see cref="TargetState"/> is defined at the <em>end</em> of the step, which is where
    /// KSA reports vehicle state; a round's position is advanced by its own launcher's update, so
    /// read live it is at the start of the step or the end of it depending on which system asked
    /// first. This flies one engagement both ways and measures the gap. It is metres — larger than
    /// the shell's fuse radius and the body it is shooting at — so which system updates first would
    /// otherwise decide the hit.</para>
    /// </summary>
    [Fact]
    public void SamplingAnIncomingRoundAtTheWrongEndOfTheStepMovesItMetres()
    {
        double3 hostileVel = new(-900, 200, 0);

        double atEnd = ClosestApproachWith(carryTheSampleForward: true, hostileVel);
        double atStart = ClosestApproachWith(carryTheSampleForward: false, hostileVel);

        // Both are real flights, so neither is degenerate.
        Assert.True(atEnd < 1000.0 && atStart < 1000.0);

        double slip = Math.Abs(atEnd - atStart);

        Assert.True(slip > BodyRadius,
                    $"one step of phase moved the incoming round by {slip:F2} m, which is inside "
                    + "the body it is being shot at - the carry would not be load-bearing");
    }

    private static double ClosestApproachWith(bool carryTheSampleForward, double3 hostileVel)
    {
        object hostile = new();

        Slug shell = new(Vec.Zero, new double3(Shell.LaunchSpeed, 0, 0), hostile, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
        };

        // Far enough out that the shell is armed and still flying when it arrives, and offset so
        // the miss is decided by the transverse term rather than by the closing one.
        double3 hostilePos = new(2500, 60.0, 0);

        for (int i = 0; i < 600 && shell.State == RoundState.Flying; i++)
        {
            double3 sample = carryTheSampleForward ? hostilePos + hostileVel * Dt : hostilePos;

            shell.Update(Dt, new TargetState(sample, hostileVel, BodyRadius, hostile),
                         Vec.Zero, Vec.Zero, Vec.Zero, shell.Munition);

            hostilePos += hostileVel * Dt;
        }

        return shell.ClosestApproach;
    }

    private static Interceptor Missile() =>
        new(Vec.Zero, new double3(50, 0, 0), new object(), 1, Vec.Zero, Vec.Zero)
        {
            Munition = BuiltIns.Missile57E6,
        };
}
