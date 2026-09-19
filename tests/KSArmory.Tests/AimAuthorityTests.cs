using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What an aim move costs, and how far the budget therefore reaches.
///
/// <para>The rates here are the ones <see cref="ArrivalDebtTests"/> measures by flying a rocket and
/// differencing two transfers by hand. This is the same number arrived at from a cutoff state
/// handed in, so the two are an independent check on each other: a change that broke the pricing
/// would have to break both identically to pass.</para>
/// </summary>
public class AimAuthorityTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double EarthSpin = 7.2921159e-5;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    private static double3 Downrange(double metres)
        => new(R * Math.Cos(metres / R), R * Math.Sin(metres / R), 0);

    /// <summary>A cutoff state that reaches the aim, found by solving for it.</summary>
    private static (double3 From, double3 Aim, double Seconds) Shot(double metres, double flight)
        => (new double3(R + 300_000.0, 0, 0), Downrange(metres), flight);

    [Theory]
    [InlineData(3_459_000.0, 407.9)]
    [InlineData(8_500_000.0, 1107.8)]
    [InlineData(12_902_000.0, 1791.5)]
    public void TheRateIsAPropertyOfTheTrajectory(double metres, double flight)
    {
        (double3 from, double3 aim, double seconds) = Shot(metres, flight);

        Assert.True(AimAuthority.TryRate(Earth, from, aim, seconds, out double rate));

        Out.WriteLine($"{metres / 1000:F0} km: {rate * 1000.0:F2} m/s per km of aim");

        // Falls with range, which is the whole reason the bound cannot be a constant.
        Assert.True(rate > 0.0);
    }

    [Fact]
    public void ALongerShotHasACheaperAim()
    {
        (double3 from, double3 near, double nearT) = Shot(3_459_000.0, 407.9);
        (_, double3 far, double farT) = Shot(12_902_000.0, 1791.5);

        Assert.True(AimAuthority.TryRate(Earth, from, near, nearT, out double cheap));
        Assert.True(AimAuthority.TryRate(Earth, from, far, farT, out double dear));

        Assert.True(dear < cheap);
    }

    /// <summary>
    /// The rate must not depend on the probe, or it is a constant of the tool rather than of the
    /// trajectory. This is what the sphere projection in <see cref="AimAuthority.TryRate"/> buys.
    /// </summary>
    [Fact]
    public void TheRateIsLinearOverTheDistanceACorrectionWalks()
    {
        (double3 from, double3 aim, double seconds) = Shot(12_902_000.0, 1791.5);

        Assert.True(AimAuthority.TryRate(Earth, from, aim, seconds, out double rate));

        // The same rate arrived at over twenty kilometres rather than one.
        Assert.True(BallisticArc.TrySolve(Earth, from, aim, seconds, out BallisticArc.Solution here));

        double3 up = Vec.Unit(aim);
        double3 downrange = Vec.Unit(Vec.Cross(Vec.Cross(from, aim), up));
        double3 far = Vec.Unit(aim + downrange * 20_000.0) * Vec.Len(aim);

        Assert.True(BallisticArc.TrySolve(Earth, from, far, seconds, out BallisticArc.Solution there));

        double over20Km = Vec.Len(there.RequiredVelocityCci - here.RequiredVelocityCci) / 20_000.0;

        // A ratio, not a tolerance in m/s per metre: the rate is 5e-4 and any absolute bound tight
        // enough to mean something there is one nobody can read. The two agree to 0.07%.
        Out.WriteLine($"1 km: {rate:E6}   20 km: {over20Km:E6}   "
                      + $"{Math.Abs(over20Km / rate - 1.0) * 100.0:F3}% apart");

        Assert.Equal(1.0, over20Km / rate, 2);
    }

    /// <summary>
    /// The bound the budget buys, and that it is under what the correction is allowed to walk.
    /// </summary>
    [Theory]
    [InlineData(3_459_000.0, 407.9)]
    [InlineData(8_500_000.0, 1107.8)]
    [InlineData(12_902_000.0, 1791.5)]
    public void TheBudgetBuysLessAimThanTheCorrectionMayWalk(double metres, double flight)
    {
        (double3 from, double3 aim, double seconds) = Shot(metres, flight);

        Assert.True(AimAuthority.TryMetresFor(Earth, from, aim, seconds,
                                              PostBoostAim.MaxTrimMetresPerSecond, out double bound));

        Out.WriteLine($"{metres / 1000:F0} km: the budget buys {bound / 1000.0:F0} km, "
                      + $"and {AimCorrection.MaxMetres / 1000.0:F0} km is permitted");

        Assert.True(bound < AimCorrection.MaxMetres);
    }

    [Fact]
    public void ABiggerBudgetBuysProportionatelyMoreAim()
    {
        (double3 from, double3 aim, double seconds) = Shot(12_902_000.0, 1791.5);

        Assert.True(AimAuthority.TryMetresFor(Earth, from, aim, seconds, 30.0, out double half));
        Assert.True(AimAuthority.TryMetresFor(Earth, from, aim, seconds, 60.0, out double whole));

        Assert.Equal(2.0, whole / half, 6);
    }

    /// <summary>
    /// A rate that cannot be priced is a refusal, never a bound of zero: an aim clamped to nothing
    /// is the correction switched off, which is the one outcome nobody asked for.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    public void NoBudgetIsARefusalRatherThanNoAim(double budget)
    {
        (double3 from, double3 aim, double seconds) = Shot(12_902_000.0, 1791.5);

        Assert.False(AimAuthority.TryMetresFor(Earth, from, aim, seconds, budget, out double bound));
        Assert.Equal(0.0, bound);
    }

    [Fact]
    public void ATransferThatWillNotSolveIsARefusal()
    {
        (double3 from, double3 aim, _) = Shot(12_902_000.0, 1791.5);

        Assert.False(AimAuthority.TryRate(Earth, from, aim, 0.0, out _));
        Assert.False(AimAuthority.TryMetresFor(Earth, from, aim, 0.0, 60.0, out _));
    }

    [Fact]
    public void AimingAtWhereTheBusAlreadyIsHasNoDownrangeAndIsRefused()
    {
        double3 from = new(R + 300_000.0, 0, 0);

        Assert.False(AimAuthority.TryRate(Earth, from, Vec.Unit(from) * R, 400.0, out _));
    }
}

/// <summary>
/// The bound reaching the loop that walks the aim.
/// </summary>
public class AimReachTests
{
    private const double R = 6_371_000.0;

    /// <summary>Push the loop hard in one direction, which is what a runaway looks like.</summary>
    private static AimCorrection WalkedHard(double affordable)
    {
        AimCorrection aim = new() { AffordableMetres = affordable };

        double3 target = new(R, 0, 0);

        // Each observation says the impact landed far past the target, so the correction keeps
        // pushing the aim the other way. Twelve is inside WorseBeforeStopping, so the loop is
        // still running when it is read.
        for (int i = 0; i < 12; i++)
        {
            aim.Observe(target + new double3(0, 400_000.0, 0), target);
        }

        return aim;
    }

    [Fact]
    public void TheAimIsHeldToWhatTheBusCanPayFor()
    {
        AimCorrection aim = WalkedHard(affordable: 50_000.0);

        Assert.Equal(50_000.0, aim.Reach);
        Assert.True(Vec.Len(aim.BiasCci) <= 50_000.0 + 1.0);
    }

    /// <summary>
    /// The regression: unbounded, the same loop walks past what any bus could fly. This is what
    /// fails against the shipped behaviour.
    /// </summary>
    [Fact]
    public void UnboundedItWalksPastWhatAnyBusCouldFly()
    {
        AimCorrection aim = WalkedHard(affordable: double.PositiveInfinity);

        Assert.Equal(AimCorrection.MaxMetres, aim.Reach);
        Assert.True(Vec.Len(aim.BiasCci) > 50_000.0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void AnUnusableAffordabilityLeavesTheSanityLimitStanding(double affordable)
    {
        AimCorrection aim = new() { AffordableMetres = affordable };

        Assert.Equal(AimCorrection.MaxMetres, aim.Reach);
    }

    [Fact]
    public void TheSanityLimitStillWinsWhenTheBusCouldAffordMore()
    {
        AimCorrection aim = new() { AffordableMetres = 10_000_000.0 };

        Assert.Equal(AimCorrection.MaxMetres, aim.Reach);
    }
}
