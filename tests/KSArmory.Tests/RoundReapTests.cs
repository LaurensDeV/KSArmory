using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What ends a round nothing has stopped.
///
/// <para>A store the ground stops ends by arriving, so its <em>age</em> is the wrong reaper — a
/// long fall is long, not stuck. The clock counts only where there is air to arrive through, and
/// what catches the store that can never arrive at all is <see cref="RoundReach"/>.</para>
/// </summary>
public class RoundReapTests
{
    private static MunitionProfile Bomb => Catalogue.MunitionNamed("B61");

    private static MunitionProfile Shell => Catalogue.MunitionNamed("20MM");

    private static Slug Released(MunitionProfile munition)
        => new(new double3(6_500_000, 0, 0), new double3(0, 250, 0), null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = munition,
        };

    // Advances a round in one-second steps through a stated medium, without ever giving it ground
    // to hit -- so the only thing that can end it is the rule under test.
    private static void Fly(Slug slug, double seconds, double density)
    {
        for (double t = 0; t < seconds && slug.State == RoundState.Flying; t += 1.0)
        {
            slug.Update(1.0, null, new double3(-9.4, 0, 0), Vec.Zero, Vec.Zero, slug.Munition, density);
        }
    }

    /// <summary>
    /// The fault this replaces. A B61 released at 100 km takes 152 s to reach the ground and one
    /// at 250 km takes 246, nearly all of it above the air — so a clock counting the coast killed
    /// the bomb in flight with the fall still to come.
    /// </summary>
    [Fact]
    public void ACoastAboveTheAirDoesNotSpendTheBudget()
    {
        Slug bomb = Released(Bomb);
        bomb.ApproachAt = (_, _) => Approach.Coasting;

        Fly(bomb, Bomb.MaxFlightSeconds * 3.0, density: 0.0);

        Assert.Equal(RoundState.Flying, bomb.State);
    }

    [Fact]
    public void FallingThroughAirDoesSpendIt()
    {
        Slug bomb = Released(Bomb);
        bomb.ApproachAt = (_, _) => Approach.Arriving;

        Fly(bomb, Bomb.MaxFlightSeconds + 5.0, density: 1.0);

        Assert.Equal(RoundState.Expired, bomb.State);
    }

    /// <summary>
    /// The other half. Nothing else would ever reap a store left in orbit, and timewarp is held
    /// down for as long as one is in the air.
    /// </summary>
    [Fact]
    public void AStoreThatCanNeverArriveIsReapedAtOnce()
    {
        Slug bomb = Released(Bomb);
        bomb.ApproachAt = (_, _) => Approach.Impossible;

        Fly(bomb, 5.0, density: 0.0);

        Assert.Equal(RoundState.Expired, bomb.State);
    }

    /// <summary>
    /// Asked every step rather than latched, because a coast ends: the round that was coasting a
    /// moment ago is the one arriving now, and only re-asking notices. It is closed form, so the
    /// cost is a few multiplies on the one round in the world that has terrain to hit.
    /// </summary>
    [Fact]
    public void TheQuestionIsAskedEveryStep()
    {
        int asked = 0;
        Slug bomb = Released(Bomb);
        bomb.ApproachAt = (_, _) => { asked++; return Approach.Coasting; };

        Fly(bomb, 30.0, density: 0.0);

        Assert.Equal(30, asked);
    }

    /// <summary>
    /// The hole three states exist to close. A body with no atmosphere never starts an air clock,
    /// so a store falling towards the Moon had nothing but the ground to end it — and nothing at
    /// all where the ground could not be read, which is a round that lives for the session holding
    /// timewarp down. <c>Arriving</c> is geometric rather than atmospheric for this reason.
    /// </summary>
    [Fact]
    public void OnAnAirlessBodyTheClockStillRuns()
    {
        Slug bomb = Released(Bomb);
        bomb.ApproachAt = (_, _) => Approach.Arriving;

        Fly(bomb, Bomb.MaxFlightSeconds + 5.0, density: 0.0);

        Assert.Equal(RoundState.Expired, bomb.State);
    }

    /// <summary>
    /// And with nothing able to classify it at all — no body, no ceiling, a trajectory the conic
    /// does not answer — the clock runs on every step, which is its age and the behaviour of every
    /// round before any of this.
    /// </summary>
    [Fact]
    public void AnUnclassifiableRoundIsStillReaped()
    {
        Slug bomb = Released(Bomb);
        bomb.ApproachAt = (_, _) => Approach.Unknown;

        Fly(bomb, Bomb.MaxFlightSeconds + 5.0, density: 0.0);

        Assert.Equal(RoundState.Expired, bomb.State);
    }

    /// <summary>
    /// With nothing able to answer, the age limit is the reaper. Destroying a round on a maybe is
    /// the one outcome with no way back.
    /// </summary>
    [Fact]
    public void WithNoReachTestTheClockStillReaps()
    {
        Slug bomb = Released(Bomb);

        Fly(bomb, Bomb.MaxFlightSeconds + 5.0, density: 1.0);

        Assert.Equal(RoundState.Expired, bomb.State);
    }

    /// <summary>
    /// A round the ground does not stop has no ending of its own, so its age is exactly the right
    /// reaper and nothing here changes for it — including in vacuum, where a shell that misses
    /// would otherwise never be swept up.
    /// </summary>
    [Fact]
    public void ARoundTheGroundDoesNotStopStillGoesOnAge()
    {
        Assert.False(Shell.HitsTerrain);

        Slug shell = Released(Shell);
        shell.ApproachAt = (_, _) => Approach.Coasting;

        Fly(shell, Shell.MaxFlightSeconds + 5.0, density: 0.0);

        Assert.Equal(RoundState.Expired, shell.State);
    }
}
