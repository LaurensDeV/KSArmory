using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A warhead whose own release probe finds no impact is kicked against its salvo's last one that did.
///
/// <para>Flown, the four warheads whose probe failed went unkicked and landed 1.73 m and 2.03 m from the
/// aim beside siblings at millimetres. <c>docs/ACCURACY-PLAN.md</c> 3ep.</para>
/// </summary>
public class SalvoProbeTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double Ground = R + PredictorEndingTests.FlownGroundMetres;

    private static ReleaseProbe Probe(double x)
        => new(new double3(x, 0, 0), new double3(0, 1, 0), default, new double3(0, 0, x));

    [Fact]
    public void AWarheadWithItsOwnProbeIsSolvedAgainstIt()
    {
        SalvoProbe salvo = new();

        SalvoProbe.Choice chosen = salvo.Choose(Probe(1), tube: 1);

        Assert.Equal(SalvoProbe.Source.Own, chosen.Source);
        Assert.Equal(Probe(1), chosen.Probe);
    }

    [Fact]
    public void AWarheadWhoseProbeFoundNothingBorrowsTheLastOneOfItsSalvoThatDid()
    {
        SalvoProbe salvo = new();

        salvo.Choose(Probe(4), tube: 4);
        salvo.Advance(0.024);
        salvo.Choose(Probe(5), tube: 5);
        salvo.Advance(0.023);

        SalvoProbe.Choice chosen = salvo.Choose(null, tube: 6);

        Assert.Equal(SalvoProbe.Source.Borrowed, chosen.Source);
        Assert.Equal(Probe(5), chosen.Probe);
        Assert.Equal(5, chosen.FromTube);
        Assert.Equal(0.023, chosen.AgeSeconds, 12);
    }

    [Fact]
    public void TwoFailuresInARowBorrowTheSameProbeAndItKeepsAgeing()
    {
        SalvoProbe salvo = new();

        salvo.Choose(Probe(3), tube: 3);
        salvo.Advance(0.02);
        salvo.Choose(null, tube: 4);
        salvo.Advance(0.02);

        SalvoProbe.Choice chosen = salvo.Choose(null, tube: 5);

        Assert.Equal(SalvoProbe.Source.Borrowed, chosen.Source);
        Assert.Equal(3, chosen.FromTube);
        Assert.Equal(0.04, chosen.AgeSeconds, 12);
    }

    [Fact]
    public void TheFirstWarheadOutHasNothingToBorrow()
    {
        SalvoProbe.Choice chosen = new SalvoProbe().Choose(null, tube: 1);

        Assert.Equal(SalvoProbe.Source.None, chosen.Source);
        Assert.Null(chosen.Probe);
    }

    [Fact]
    public void NothingIsBorrowedAcrossSalvos()
    {
        SalvoProbe salvo = new();

        salvo.Choose(Probe(6), tube: 6);
        salvo.Forget();

        Assert.Equal(SalvoProbe.Source.None, salvo.Choose(null, tube: 1).Source);
    }

    [Fact]
    public void NothingIsBorrowedOnceTheReleaseHasStalledPastTheAge()
    {
        SalvoProbe salvo = new();

        salvo.Choose(Probe(2), tube: 2);
        salvo.Advance(SalvoProbe.MaxAgeSeconds);
        Assert.Equal(SalvoProbe.Source.Borrowed, salvo.Choose(null, tube: 3).Source);

        salvo.Advance(0.001);
        SalvoProbe.Choice chosen = salvo.Choose(null, tube: 3);

        Assert.Equal(SalvoProbe.Source.TooOld, chosen.Source);
        Assert.Null(chosen.Probe);
        Assert.Equal(2, chosen.FromTube);

        // A probe that lands starts the clock again.
        salvo.Choose(Probe(4), tube: 4);
        Assert.Equal(SalvoProbe.Source.Borrowed, salvo.Choose(null, tube: 5).Source);
    }

    [Fact]
    public void AStepThatIsNotAStepAgesNothing()
    {
        SalvoProbe salvo = new();

        salvo.Choose(Probe(1), tube: 1);
        salvo.Advance(double.NaN);
        salvo.Advance(-1.0);
        salvo.Advance(double.PositiveInfinity);

        Assert.Equal(0.0, salvo.Choose(null, tube: 2).AgeSeconds);
    }

    /// <summary>
    /// The planet's spin and the bus's travel a borrowed probe is behind cancel, because everything in it
    /// is from one instant and describes the same arc. What is left is the kick's lever arm, shorter by the
    /// age: 0.09 mm on the ground a frame old and 1.8 mm at the age limit, against a metre unkicked.
    /// </summary>
    [Theory]
    [InlineData(0.024, 0.0001)]
    [InlineData(SalvoProbe.MaxAgeSeconds, 0.002)]
    public void ABorrowedProbeLandsTheWarheadWhereItsOwnWould(double age, double withinMetres)
    {
        BallisticBody earth = new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;

        double3 busAtThen = PredictorEndingTests.FlownPositionCci;
        double3 busVelocityThen = PredictorEndingTests.FlownVelocityCci;

        // 830 km up, so the coast between the two releases is the predictor's own physics exactly.
        Assert.True(Kepler.TryCoast(Mu, busAtThen, busVelocityThen, age, out double3 busAtNow,
                                    out double3 busVelocityNow));

        ReleaseProbe then = Fly(earth, warhead, busAtThen, busVelocityThen, out double3 aimThen);
        ReleaseProbe own = Fly(earth, warhead, busAtNow, busVelocityNow, out _) with
        {
            TargetCci = earth.CarryCci(aimThen, age),
        };
        ReleaseProbe borrowed = then with { TargetCci = aimThen };

        // One tube's mouth off the mean, and the spin it was thrown with, as a flown salvo had them.
        double3 up = Vec.Unit(busAtNow);
        double3 offset = Vec.Unit(Vec.Cross(busVelocityNow, up)) * 0.86;
        double3 spin = (up * -2.8 + Vec.Unit(Vec.Cross(up, offset)) * 3.2) * 1e-3;

        double3 landsOwn = Lands(earth, warhead, busAtNow + offset, busVelocityNow + spin + KickFrom(own));
        double3 landsBorrowed = Lands(earth, warhead, busAtNow + offset, busVelocityNow + spin + KickFrom(borrowed));
        double3 landsUnkicked = Lands(earth, warhead, busAtNow + offset, busVelocityNow + spin);

        double apart = Vec.Len(landsBorrowed - landsOwn);
        double ownMiss = Vec.Len(landsOwn - own.TargetCci);
        double unkickedMiss = Vec.Len(landsUnkicked - own.TargetCci);

        Out.WriteLine($"{age * 1000:F0} ms old: own kick lands {ownMiss * 1000:F2} mm from the aim, borrowed "
                      + $"{apart * 1000:F3} mm from that, unkicked {unkickedMiss:F2} m");

        Assert.True(unkickedMiss > 1.0, $"unkicked {unkickedMiss} m");
        Assert.True(apart < withinMetres, $"borrowed lands {apart * 1000:F3} mm from own");

        double3 KickFrom(ReleaseProbe from)
        {
            ReleaseFocus.Separation kick = ReleaseFocus.Kick(
                earth, from.PositionCci, from.VelocityCci, from.Impact.Seconds, offset, spin,
                focusRing: true, cancelSpin: true,
                new ReleaseFocus.ProbeMiss(from.Impact.GroundFixedPointCci, from.TargetCci, _ => Ground));

            Assert.True(kick.RingFocused);
            Assert.Equal(ReleaseFocus.MissOutcome.Cancelled, kick.Miss);
            return kick.KickCci;
        }
    }

    // The probe the release would fly, and an aim 0.35 m along the ground from where it lands, as the
    // aim loop leaves one.
    private static ReleaseProbe Fly(BallisticBody earth, MunitionProfile warhead, double3 from, double3 velocity,
                                    out double3 aim)
    {
        Assert.True(Predict(earth, warhead, from, velocity, out ImpactPredictor.Impact hit));

        double3 up = Vec.Unit(hit.GroundFixedPointCci);
        double3 across = Vec.Unit(Vec.Cross(up, hit.VelocityCci));
        aim = Vec.Unit(hit.GroundFixedPointCci + across * 0.35) * Ground;

        return new ReleaseProbe(from, velocity, hit, aim);
    }

    private static double3 Lands(BallisticBody earth, MunitionProfile warhead, double3 from, double3 velocity)
    {
        Assert.True(Predict(earth, warhead, from, velocity, out ImpactPredictor.Impact hit));
        return hit.GroundFixedPointCci;
    }

    private static bool Predict(BallisticBody earth, MunitionProfile warhead, double3 from, double3 velocity,
                                out ImpactPredictor.Impact hit)
        => ImpactPredictor.TryPredict(earth, from, velocity, 2.0, ImpactPredictor.DefaultMaxSeconds, out hit,
                                      _ => Ground, null,
                                      new ImpactPredictor.Drag(p => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0),
                                                               warhead),
                                      stopOnTheSurface: true);
}
