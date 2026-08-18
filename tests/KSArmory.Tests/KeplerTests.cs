using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Coasting in closed form, checked against the integrator that shares none of its maths.
///
/// <para>The window search asks where the vehicle will be at hundreds of candidate moments, so this
/// has to be both fast and right. Fast is why it is not the integrator; right is why it is tested
/// against one.</para>
/// </summary>
public class KeplerTests
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static readonly BallisticBody Earth = new(Mu, R, new double3(0, 0, 1), 0.0);

    /// <summary>The same state flown with RK4 at a step fine enough to be the reference.</summary>
    private static void Integrate(double3 r, double3 v, double seconds, double step,
                                  out double3 rOut, out double3 vOut)
    {
        for (double t = 0; t < seconds; t += step)
        {
            double h = Math.Min(step, seconds - t);

            double3 k1v = Earth.GravityCci(r);
            double3 k2v = Earth.GravityCci(r + v * (h * 0.5));
            double3 k3v = Earth.GravityCci(r + (v + k1v * (h * 0.5)) * (h * 0.5));
            double3 k4v = Earth.GravityCci(r + (v + k2v * (h * 0.5)) * h);

            double3 nextR = r + (v * 6.0 + (k1v + k2v + k3v) * h) * (h / 6.0);
            double3 nextV = v + (k1v + k2v * 2.0 + k3v * 2.0 + k4v) * (h / 6.0);

            r = nextR;
            v = nextV;
        }

        rOut = r;
        vOut = v;
    }

    [Theory]
    [InlineData(300_000.0, 1.0, 600.0)]     // circular, ten minutes
    [InlineData(300_000.0, 1.0, 5000.0)]    // circular, most of a revolution
    [InlineData(200_000.0, 1.12, 3000.0)]   // an ellipse
    [InlineData(400_000.0, 0.85, 1500.0)]   // one that dips low
    [InlineData(500_000.0, 1.42, 4000.0)]   // hyperbolic
    public void CoastingMatchesFlyingIt(double altitude, double speedFactor, double seconds)
    {
        double3 r0 = new(R + altitude, 0, 0);
        double circular = Math.Sqrt(Mu / (R + altitude));
        double3 v0 = new(0, circular * speedFactor, 0);

        Assert.True(Kepler.TryCoast(Mu, r0, v0, seconds, out double3 r1, out double3 v1));

        Integrate(r0, v0, seconds, 0.25, out double3 rRef, out double3 vRef);

        double positionError = Vec.Len(r1 - rRef);
        double velocityError = Vec.Len(v1 - vRef);

        Assert.True(positionError < 50.0, $"position off by {positionError:F1} m after {seconds:F0} s");
        Assert.True(velocityError < 0.05, $"velocity off by {velocityError:F3} m/s");
    }

    [Fact]
    public void CoastingAWholePeriodComesBackToWhereItStarted()
    {
        double3 r0 = new(R + 300_000.0, 0, 0);
        double3 v0 = new(0, Math.Sqrt(Mu / (R + 300_000.0)) * 1.08, 0);

        double period = Kepler.PeriodSeconds(Mu, r0, v0);
        Assert.True(period > 0.0 && double.IsFinite(period));

        Assert.True(Kepler.TryCoast(Mu, r0, v0, period, out double3 r1, out double3 v1));

        Assert.True(Vec.Len(r1 - r0) < 1.0, $"came back {Vec.Len(r1 - r0):F3} m away");
        Assert.True(Vec.Len(v1 - v0) < 0.001);
    }

    [Fact]
    public void AnUnboundTrajectoryHasNoPeriod()
    {
        double3 r0 = new(R + 300_000.0, 0, 0);
        double3 escaping = new(0, Math.Sqrt(2.0 * Mu / (R + 300_000.0)) * 1.1, 0);

        Assert.True(double.IsNaN(Kepler.PeriodSeconds(Mu, r0, escaping)));
    }

    [Fact]
    public void CoastingNowhereChangesNothing()
    {
        double3 r0 = new(R + 300_000.0, 0, 0);
        double3 v0 = new(0, 7000, 0);

        Assert.True(Kepler.TryCoast(Mu, r0, v0, 0.0, out double3 r1, out double3 v1));
        Assert.Equal(r0, r1);
        Assert.Equal(v0, v1);
    }


    /// <summary>
    /// A stable orbit never arrives, and the conic says so without anything being flown. That is
    /// what a computer holding for a burn window sits in, so the alternative is integrating the
    /// whole horizon several times a second to reach the same conclusion.
    /// </summary>
    [Fact]
    public void AStableOrbitIsKnownNotToComeDownWithoutFlyingIt()
    {
        double3 r0 = new(R + 300_000.0, 0, 0);
        double3 circular = new(0, Math.Sqrt(Mu / (R + 300_000.0)), 0);

        double periapsis = Kepler.PeriapsisRadius(Mu, r0, circular);
        Assert.True(periapsis > R, $"periapsis came out at {(periapsis - R) / 1000.0:F0} km altitude");

        BallisticBody earth = new(Mu, R, new double3(0, 0, 1), 0.0);
        Assert.False(ImpactPredictor.TryPredict(earth, r0, circular, 2.0,
                                                ImpactPredictor.DefaultMaxSeconds, out _));
    }

    /// <summary>And one that does dip into the ground still gets flown, which is the other half.</summary>
    [Fact]
    public void AnOrbitThatGrazesTheGroundIsStillFlown()
    {
        double3 r0 = new(R + 300_000.0, 0, 0);
        double3 slow = new(0, Math.Sqrt(Mu / (R + 300_000.0)) * 0.85, 0);

        double periapsis = Kepler.PeriapsisRadius(Mu, r0, slow);
        Assert.True(periapsis < R, "this one should reach the ground");

        BallisticBody earth = new(Mu, R, new double3(0, 0, 1), 0.0);
        Assert.True(ImpactPredictor.TryPredict(earth, r0, slow, 2.0, 20_000.0, out _));
    }

    [Fact]
    public void AnEscapeTrajectoryHasNoPeriapsisToReportOn()
    {
        double3 r0 = new(R + 300_000.0, 0, 0);
        double3 escaping = new(0, Math.Sqrt(2.0 * Mu / (R + 300_000.0)) * 1.2, 0);

        Assert.True(double.IsNaN(Kepler.PeriapsisRadius(Mu, r0, escaping)));
    }
}
