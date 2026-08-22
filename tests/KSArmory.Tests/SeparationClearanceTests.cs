using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// When a vehicle may start manoeuvring after dropping a stage.
///
/// <para>Small, and load-bearing for a reason that is not obvious from the arithmetic: every branch
/// here is one that reads the same from outside and behaves oppositely. A wait that never ends
/// holds the whole salvo; a wait that ends too early nulls the separation at the moment the two
/// halves are closest.</para>
/// </summary>
public class SeparationClearanceTests
{
    // A stage a few metres across, and what clearing it therefore asks for.
    private const double StageRadius = 8.0;
    private const double Wanted = StageRadius + SeparationClearance.ClearOfTheSphereMetres;

    /// <summary>
    /// How far is far enough comes from the stage rather than from a number somebody picked: its
    /// own bounding sphere is what the coarse contact test uses, so that is the thing to beat.
    /// </summary>
    [Theory]
    [InlineData(2.0)]
    [InlineData(40.0)]
    public void WhatCountsAsClearScalesWithTheStage(double radius)
    {
        double just = radius + SeparationClearance.ClearOfTheSphereMetres;

        Assert.True(SeparationClearance.Check(just, radius, 1.0).IsClear);
        Assert.False(SeparationClearance.Check(just - 1.0, radius, 1.0).IsClear);
    }

    /// <summary>An unreadable stage still has to clear something, so it falls back to a fixed span.</summary>
    [Fact]
    public void AStageWhoseSizeCannotBeReadUsesTheFallback()
    {
        Assert.True(SeparationClearance.Check(SeparationClearance.FallbackMetres, double.NaN, 1.0).IsClear);
        Assert.False(SeparationClearance.Check(SeparationClearance.FallbackMetres - 1.0, double.NaN, 1.0).IsClear);
    }

    [Fact]
    public void FarEnoughApartIsClear()
    {
        Clearance c = SeparationClearance.Check(Wanted + 1.0, StageRadius, 10.0);

        Assert.True(c.IsClear);
        Assert.False(c.OnTheClock);
        Assert.Contains("clear of the spent stack", c.Said);
    }

    [Fact]
    public void StillCloseIsNotClear()
    {
        Clearance c = SeparationClearance.Check(Wanted - 1.0, StageRadius, 10.0);

        Assert.False(c.IsClear);
        Assert.Contains("waiting", c.Said);
    }

    /// <summary>
    /// A stage that cannot be read must not count as gone.
    ///
    /// <para>A part tree mid-rebuild answers with nothing, which is indistinguishable from a stage
    /// that has genuinely separated — and it is at its most likely in the first frames after a
    /// split, which is exactly when the two are closest. Reading absence as clearance therefore
    /// fires the manoeuvre at the worst possible instant, and looks like it worked.</para>
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1.0)]
    public void AnUnreadableStageWaitsOutTheClockRatherThanCountingAsGone(double reading)
    {
        Assert.False(SeparationClearance.Check(reading, StageRadius, 0.0).IsClear);
        Assert.False(SeparationClearance.Check(reading, StageRadius, SeparationClearance.TimeoutSeconds - 0.1).IsClear);

        Clearance late = SeparationClearance.Check(reading, StageRadius, SeparationClearance.TimeoutSeconds);

        Assert.True(late.IsClear);
        Assert.True(late.OnTheClock);
        Assert.Contains("no clearance reading", late.Said);
    }

    /// <summary>
    /// The wait always ends — but what ends is the <em>manoeuvre</em>, not the distance.
    ///
    /// <para>Thrusting two metres from the stack nulls the shove that is the only thing carrying
    /// the pair apart, so the bus drives back into what it just dropped. Releasing untrimmed costs
    /// the kilometres the trim would have removed; hitting the stack costs the shot, so the trade
    /// only goes one way.</para>
    /// </summary>
    [Fact]
    public void StillTooCloseWhenPatienceRunsOutAbandonsTheTrimRatherThanFiring()
    {
        Clearance c = SeparationClearance.Check(2.0, StageRadius, SeparationClearance.TimeoutSeconds);

        Assert.False(c.IsClear);
        Assert.True(c.Abandoned);

        // And it says the distance it refused at, because that is the number that explains a salvo
        // released without a trim.
        Assert.Contains("2 m", c.Said);
        Assert.Contains("without trimming", c.Said);
    }

    /// <summary>
    /// A stage that cannot be read still goes ahead, and that asymmetry is the point: the trade
    /// above needs a distance to make, and refusing to release on a reading nobody has holds the
    /// warheads for ever.
    /// </summary>
    [Fact]
    public void AnUnreadableStageStillGoesAheadOnTheClock()
    {
        Clearance c = SeparationClearance.Check(double.NaN, StageRadius,
                                                SeparationClearance.TimeoutSeconds);

        Assert.True(c.IsClear);
        Assert.True(c.OnTheClock);
        Assert.False(c.Abandoned);
    }

    /// <summary>
    /// Distance beats the clock, so a vehicle that gets clear early is not made to wait it out.
    /// </summary>
    [Fact]
    public void ClearanceIsReachedOnDistanceRatherThanOnTime()
    {
        Clearance c = SeparationClearance.Check(Wanted, StageRadius, 0.5);

        Assert.True(c.IsClear);
        Assert.False(c.OnTheClock);
    }

    // A bus 20 m ahead of the stage it dropped, parting at a metre a second -- the shape every
    // flown split has: the stage behind, and the shove along the vehicle's own axis.
    private static readonly double3 Bus = new(0, 0, 0);
    private static readonly double3 Stage = new(-20.0, 0, 0);
    private static readonly double3 BusVelocity = new(1.0, 0, 0);
    private static readonly double3 StageVelocity = new(0, 0, 0);

    private static double3 Spendable(double3 toGain)
        => SeparationClearance.WithoutClosingOnTheStack(toGain, Bus, BusVelocity, Stage, StageVelocity);

    /// <summary>
    /// Nulling the shove is the whole of the separation and is spent in full. It is the case the
    /// trim exists for, and clamping any of it away would be clamping away the fix.
    /// </summary>
    [Fact]
    public void TheShoveItselfIsSpentInFull()
    {
        double3 toGain = new(-1.0, 0, 0);

        Assert.Equal(toGain, Spendable(toGain));
    }

    /// <summary>
    /// Past the shove there is nothing left to spend, because the rate the two are parting at is
    /// the entire budget: one metre a second more and the range rate is negative, which closes any
    /// gap given time.
    /// </summary>
    [Fact]
    public void NothingPastTheShoveMayBeSpentBackAlongTheLine()
    {
        double3 spendable = Spendable(new double3(-2.7, 0, 0));

        Assert.Equal(-1.0, spendable.X, 9);
    }

    /// <summary>
    /// And a pair that has stopped parting has no budget at all — which is the state a second null
    /// finds, because the first one spent the shove.
    /// </summary>
    [Fact]
    public void APairThatHasStoppedPartingHasNothingLeftToSpend()
    {
        double3 spendable = SeparationClearance.WithoutClosingOnTheStack(
            new double3(-2.7, 0, 0), Bus, StageVelocity, Stage, StageVelocity);

        Assert.Equal(0.0, spendable.X, 9);
    }

    /// <summary>
    /// A push square to the line between them cannot change the range rate, so it is spent in full
    /// however close the stage is. Clamping the whole command instead would throw away the part of
    /// the trim that was never dangerous.
    /// </summary>
    [Fact]
    public void APushSquareToTheLineIsNeverClamped()
    {
        double3 toGain = new(0, 5.0, -3.0);

        Assert.Equal(toGain, SeparationClearance.WithoutClosingOnTheStack(
                                 toGain, Bus, StageVelocity, Stage, StageVelocity));
    }

    /// <summary>
    /// Pushing away from the stage is never the thing that runs into it, whatever the range rate.
    /// </summary>
    [Fact]
    public void PushingAwayIsNeverClamped()
    {
        double3 toGain = new(4.0, 0, 0);

        Assert.Equal(toGain, SeparationClearance.WithoutClosingOnTheStack(
                                 toGain, Bus, StageVelocity, Stage, StageVelocity));
    }

    /// <summary>
    /// Both halves of each pair are handed in so the differencing happens inside, which is what
    /// makes the answer independent of the frame the two are travelling in — and they travel in one
    /// carrying ~29.8 km/s. See <c>docs/FRAMES-AND-EPOCHS.md</c>.
    /// </summary>
    [Fact]
    public void TheBudgetCarriesNoneOfTheFramesOwnMotion()
    {
        double3 toGain = new(-2.7, 0, 0);
        double3 carrier = new(29_800.0, -11_000.0, 4_000.0);

        double3 still = Spendable(toGain);
        double3 carried = SeparationClearance.WithoutClosingOnTheStack(
            toGain, Bus, BusVelocity + carrier, Stage, StageVelocity + carrier);

        Assert.Equal(still.X, carried.X, 6);
        Assert.Equal(still.Y, carried.Y, 6);
        Assert.Equal(still.Z, carried.Z, 6);
    }

    /// <summary>
    /// A stage that cannot be read bounds nothing, exactly as an unreadable distance waits on the
    /// clock rather than reading as clearance: the two halves of this class agree about what
    /// "no reading" means.
    /// </summary>
    [Fact]
    public void AnUnreadableStageBoundsNothing()
    {
        double3 toGain = new(-2.7, 0, 0);

        Assert.Equal(toGain, SeparationClearance.WithoutClosingOnTheStack(
                                 toGain, Bus, BusVelocity, Bus, StageVelocity));

        Assert.Equal(toGain, SeparationClearance.WithoutClosingOnTheStack(
                                 toGain, Bus, BusVelocity,
                                 new double3(double.NaN, 0, 0), StageVelocity));
    }
}
