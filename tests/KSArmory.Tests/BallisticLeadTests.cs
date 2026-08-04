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
    /// place; aiming at where the target *is* cannot, and that is what the gun was doing.
    /// </summary>
    [Fact]
    public void TheSolutionPutsTheRoundAndACrossingTargetInTheSamePlace()
    {
        double3 shooter = Vec.Zero;
        double3 target = new(4000, 0, 0);
        double3 velocity = new(0, 300, 0);        // straight across the line of sight

        Assert.True(BallisticLead.TrySolve(shooter, target, velocity, MuzzleSpeed, NoGravity,
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

        // What the turret did before the lead existed: point straight at the contact.
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

        Assert.True(BallisticLead.TrySolve(shooter, target, Vec.Zero, MuzzleSpeed, gravity,
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

        Assert.True(BallisticLead.TrySolve(Vec.Zero, target, Vec.Zero, MuzzleSpeed, NoGravity,
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

        Assert.True(BallisticLead.TrySolve(Vec.Zero, target, closing, MuzzleSpeed, NoGravity,
                                           out double3 aim));

        Assert.True(aim.X < target.X, "a closing target is met nearer than it currently is");
    }

    /// <summary>
    /// The solver takes motion <em>relative to the shooter</em>. Handing it an absolute ecliptic
    /// velocity leads on the planet's ~29.8 km/s around its star — motion the round already
    /// carries — and throws the aim more than a hundred kilometres wide, which in flight reads as
    /// the turret swinging to a wrong bearing the instant the cannon take the engagement.
    /// </summary>
    [Fact]
    public void CommonMotionMustNotBeLedOn()
    {
        double3 shooter = Vec.Zero;
        double3 target = new(3000, 0, 0);
        double3 relative = new(0, 300, 0);

        Assert.True(BallisticLead.TrySolve(shooter, target, relative, MuzzleSpeed, NoGravity,
                                           out double3 correct));

        // What the battery must never pass: the planet's motion, shared by shooter and round.
        double3 ecliptic = new(0, 29_800, 0);
        Assert.True(BallisticLead.TrySolve(shooter, target, relative + ecliptic, MuzzleSpeed,
                                           NoGravity, out double3 wrong));

        Assert.True(Vec.Len(wrong - correct) > 100_000.0,
                    "leading on absolute velocity should be catastrophically wrong, "
                    + $"but the two aim points differ by only {Vec.Len(wrong - correct):F0} m");
    }

    [Fact]
    public void NoMuzzleSpeedHasNoSolution()
    {
        Assert.False(BallisticLead.TrySolve(Vec.Zero, new double3(1000, 0, 0), Vec.Zero,
                                            0.0, NoGravity, out _));
        Assert.False(BallisticLead.TrySolve(Vec.Zero, new double3(1000, 0, 0), Vec.Zero,
                                            double.NaN, NoGravity, out _));
    }

    [Fact]
    public void NonFiniteInputsAreRefusedRatherThanPropagated()
    {
        double3 bad = new(double.NaN, 0, 0);

        Assert.False(BallisticLead.TrySolve(Vec.Zero, bad, Vec.Zero, MuzzleSpeed, NoGravity, out _));
        Assert.False(BallisticLead.TrySolve(bad, new double3(1000, 0, 0), Vec.Zero, MuzzleSpeed,
                                            NoGravity, out _));
    }
}
