using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Where an unguided round has to be thrown. Both terms here are large at cannon range and both
/// are invisible in flight — a shell that lands behind a crossing target and one that lands short
/// under gravity look identical from the launcher, and identical to the gun simply missing.
/// </summary>
public class BallisticLeadTests
{
    private const double MuzzleSpeed = 960.0;
    private static readonly double3 NoGravity = Vec.Zero;

    /// <summary>
    /// The whole point. Flying the solution forward must put the round and the target in the same
    /// place, which aiming at where the target *is* cannot do.
    /// </summary>
    [Fact]
    public void TheSolutionPutsTheRoundAndACrossingTargetInTheSamePlace()
    {
        double3 shooter = Vec.Zero;
        double3 target = new(4000, 0, 0);
        double3 velocity = new(0, 300, 0);        // straight across the line of sight

        Assert.True(BallisticLead.TrySolve(shooter, Vec.Zero, target, velocity, MuzzleSpeed, NoGravity,
                                           out double3 aim));

        // Fly the round along the solution and step the target the same interval.
        double3 direction = Vec.Unit(aim - shooter);
        double flight = Vec.Len(aim - shooter) / MuzzleSpeed;
        double3 roundAt = shooter + direction * (MuzzleSpeed * flight);
        double3 targetAt = target + velocity * flight;

        Assert.True(Vec.Len(roundAt - targetAt) < 1.0,
                    $"missed by {Vec.Len(roundAt - targetAt):F1} m");
    }

    [Fact]
    public void AimingAtTheTargetItselfMissesACrossingTargetBadly()
    {
        double3 shooter = Vec.Zero;
        double3 target = new(4000, 0, 0);
        double3 velocity = new(0, 300, 0);

        // The unled aim: point straight at the contact.
        double flight = Vec.Len(target - shooter) / MuzzleSpeed;
        double3 roundAt = shooter + Vec.Unit(target - shooter) * (MuzzleSpeed * flight);
        double3 targetAt = target + velocity * flight;

        Assert.True(Vec.Len(roundAt - targetAt) > 1000.0,
                    "a 300 m/s target crosses more than a kilometre during a 4 km shot");
    }

    [Fact]
    public void GravityIsCompensatedByAimingHighByExactlyTheDrop()
    {
        double3 shooter = Vec.Zero;
        double3 target = new(4000, 0, 0);
        double3 gravity = new(0, 0, -9.80665);

        Assert.True(BallisticLead.TrySolve(shooter, Vec.Zero, target, Vec.Zero, MuzzleSpeed, gravity,
                                           out double3 aim));

        double flight = 4000.0 / MuzzleSpeed;
        double drop = 0.5 * 9.80665 * flight * flight;

        Assert.Equal(drop, aim.Z, 3);
        Assert.True(drop > 80.0, $"a 4 km shot drops {drop:F0} m, which is not a rounding error");
    }

    [Fact]
    public void AStationaryTargetInFreeFallNeedsNoLead()
    {
        double3 target = new(2000, 0, 0);

        Assert.True(BallisticLead.TrySolve(Vec.Zero, Vec.Zero, target, Vec.Zero, MuzzleSpeed, NoGravity,
                                           out double3 aim));

        Assert.Equal(target.X, aim.X, 6);
        Assert.Equal(target.Y, aim.Y, 6);
        Assert.Equal(target.Z, aim.Z, 6);
    }

    [Fact]
    public void ATargetClosingHeadOnLeadsShortRatherThanLong()
    {
        double3 target = new(4000, 0, 0);
        double3 closing = new(-300, 0, 0);

        Assert.True(BallisticLead.TrySolve(Vec.Zero, Vec.Zero, target, closing, MuzzleSpeed, NoGravity,
                                           out double3 aim));

        Assert.True(aim.X < target.X, "a closing target is met nearer than it currently is");
    }

    /// <summary>
    /// Motion shared by the shooter and the target must not reach the aim point. The round is
    /// launched with the shooter's velocity already in it, so only the difference is worth
    /// leading — and both terms carry the planet's ~29.8 km/s around its star.
    ///
    /// <para>This is an <em>invariance</em> assertion, and the pair below is a sensitivity one:
    /// together they say the common term is removed and that the relative term still matters.
    /// Sensitivity alone, asserted on a pre-differenced argument, passes either way — the
    /// subtraction that decides it then lives at a call site no test reaches.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0, 29_800.0, 0.0)]          // the planet's motion around its star
    [InlineData(-7800.0, 0.0, 0.0)]           // orbital speed, the other way
    [InlineData(1.0, -2.0, 3.0)]              // something small and arbitrary
    public void MotionSharedWithTheTargetDoesNotMoveTheAimPoint(double cx, double cy, double cz)
    {
        double3 shooter = new(100, -50, 25);
        double3 target = new(3100, -50, 25);
        double3 targetVelocity = new(0, 300, 0);

        Assert.True(BallisticLead.TrySolve(shooter, Vec.Zero, target, targetVelocity,
                                           MuzzleSpeed, NoGravity, out double3 still));

        // Both bodies now carry an identical extra velocity. Nothing about the shot has changed.
        double3 common = new(cx, cy, cz);
        Assert.True(BallisticLead.TrySolve(shooter, common, target, targetVelocity + common,
                                           MuzzleSpeed, NoGravity, out double3 moving));

        Assert.True(Vec.Len(moving - still) < 1.0,
                    $"common motion moved the aim point by {Vec.Len(moving - still):F1} m");
    }

    /// <summary>
    /// The companion to the invariance test above: the part that is *not* shared must still be
    /// led on. Without this, a solver that ignored velocity entirely would pass.
    /// </summary>
    [Fact]
    public void MotionRelativeToTheShooterStillMovesTheAimPoint()
    {
        double3 shooter = Vec.Zero;
        double3 target = new(3000, 0, 0);

        Assert.True(BallisticLead.TrySolve(shooter, Vec.Zero, target, Vec.Zero,
                                           MuzzleSpeed, NoGravity, out double3 stationary));
        Assert.True(BallisticLead.TrySolve(shooter, Vec.Zero, target, new double3(0, 300, 0),
                                           MuzzleSpeed, NoGravity, out double3 crossing));

        Assert.True(Vec.Len(crossing - stationary) > 500.0,
                    "a 300 m/s crosser at 3 km needs hundreds of metres of lead, but the aim "
                    + $"point moved only {Vec.Len(crossing - stationary):F0} m");
    }

    [Fact]
    public void NoMuzzleSpeedHasNoSolution()
    {
        Assert.False(BallisticLead.TrySolve(Vec.Zero, Vec.Zero, new double3(1000, 0, 0), Vec.Zero,
                                            0.0, NoGravity, out _));
        Assert.False(BallisticLead.TrySolve(Vec.Zero, Vec.Zero, new double3(1000, 0, 0), Vec.Zero,
                                            double.NaN, NoGravity, out _));
    }

    [Fact]
    public void NonFiniteInputsAreRefusedRatherThanPropagated()
    {
        double3 bad = new(double.NaN, 0, 0);

        Assert.False(BallisticLead.TrySolve(Vec.Zero, Vec.Zero, bad, Vec.Zero, MuzzleSpeed, NoGravity, out _));
        Assert.False(BallisticLead.TrySolve(bad, Vec.Zero, new double3(1000, 0, 0), Vec.Zero, MuzzleSpeed,
                                            NoGravity, out _));
    }

    /// <summary>
    /// The case point defence exists for, and the one the old fixed four passes was never
    /// calibrated against: a target moving at a large fraction of the shell's own speed.
    ///
    /// <para>The iteration contracts by roughly the speed ratio per pass, so against an aircraft
    /// at a twentieth of muzzle speed four passes is plenty and against an inbound missile at
    /// well over half of it, it is not. Measured in flight: a HARM at 576 m/s engaged by shells
    /// at 956 m/s.</para>
    ///
    /// <para>Stated as the fixed point itself rather than as a pass count, so it holds whatever
    /// the solver does internally: the round must arrive exactly when it reaches the aim point.</para>
    /// </summary>
    [Fact]
    public void ASolutionAgainstAFastCrosserActuallyConverges()
    {
        double3 shooter = Vec.Zero;
        double3 target = new(1040, 0, 0);
        double3 targetVelocity = new(-300, 490, 0);   // 576 m/s, mostly closing, partly across

        Assert.True(BallisticLead.TrySolve(shooter, Vec.Zero, target, targetVelocity,
                                           MuzzleSpeed, NoGravity, out double3 aim,
                                           out double flightTime));

        // The defining equation: the shell covers the distance to the aim point in exactly the
        // flight time the target was led by. Four passes leaves this out by metres.
        double travelled = MuzzleSpeed * flightTime;
        double distance = Vec.Len(aim - shooter);

        Assert.Equal(distance, travelled, 3);

        // And the aim point is where the target actually is at that moment.
        double3 whereItWillBe = target + targetVelocity * flightTime;
        Assert.Equal(0.0, Vec.Len(aim - whereItWillBe), 3);
    }

    /// <summary>
    /// A target outrunning the round has no intercept, and the solve must say so rather than hand
    /// back whichever iterate it stopped on. That number carries the same <c>true</c> as a real
    /// solution and would lay the ring on a place the shell can never reach.
    /// </summary>
    [Fact]
    public void ATargetFasterThanTheRoundHasNoSolution()
    {
        Assert.False(BallisticLead.TrySolve(Vec.Zero, Vec.Zero,
                                            new double3(1000, 0, 0), new double3(4000, 0, 0),
                                            MuzzleSpeed, NoGravity, out _, out _));
    }
}
