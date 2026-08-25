using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the <see cref="ImpactPredictor"/>'s own step through the air is worth.
///
/// <para>The aim correction has exactly one observer and this is it. Six shots flown at 12,902 km
/// walked a median <b>2,352 m downrange</b> from their own release probe and arrived <b>0.48 s
/// early</b>, on well-conditioned flat ground — fifteen times what <c>ProbeGapTests</c> prices the
/// round's own integrator at. That leaves the other integrator, and it was never measured because
/// its step was a constant nothing could vary.</para>
///
/// <para><b>Measurement, not a threshold.</b> Nothing here asserts a number the predictor must
/// beat; it asserts the shape — that the answer converges, and that the shipped step is far enough
/// from converged to matter.</para>
/// </summary>
public class PredictorStepTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    /// <summary>The finest step anything here integrates at, which everything else is scored against.</summary>
    private const double ConvergedSeconds = 0.002;

    /// <summary>
    /// A reentry closing on the ground fast enough for the step to matter, which is the case the
    /// flown shots are in and the deorbit rig is not: <c>DeorbitShot</c> arrives from a 200 km
    /// pickup 3,459 km downrange, where the flown salvo comes in at 4.6 km/s from five times that.
    /// </summary>
    private static (double3 Position, double3 Velocity) Entry(double speedMetresPerSecond,
                                                              double flightPathDeg)
    {
        double3 up = new(1, 0, 0);
        double3 along = new(0, 1, 0);

        double gamma = flightPathDeg * Math.PI / 180.0;

        // Coming down, so the radial component is negative and the horizontal carries the rest.
        double3 velocity = speedMetresPerSecond * (along * Math.Cos(gamma) - up * Math.Sin(gamma));

        return (up * (DeorbitShot.R + 90_000.0), velocity);
    }

    private static double3 ImpactAt(double airStepSeconds, double speed, double gammaDeg)
    {
        (double3 position, double3 velocity) = Entry(speed, gammaDeg);

        Assert.True(ImpactPredictor.TryPredict(Earth, position, velocity, 2.0, 3_000.0,
                                               out ImpactPredictor.Impact hit,
                                               drag: new ImpactPredictor.Drag(DeorbitShot.DensityAt,
                                                                             DeorbitShot.Warhead),
                                               atmosphericStepSeconds: airStepSeconds),
                    $"no impact at a {airStepSeconds * 1000.0:F0} ms air step");

        return hit.PointCci;
    }

    /// <summary>
    /// The shipped step against a converged one, on the entry the flown shots actually fly.
    ///
    /// <para>If this is small then the predictor is not what the six shots walked and the search
    /// moves on; if it is kilometres then the correction loop has been converging against its own
    /// integration error.</para>
    /// </summary>
    [Theory]
    [InlineData(4_640.0, 13.7)]
    [InlineData(4_640.0, 7.0)]
    [InlineData(3_000.0, 13.7)]
    public void WhatTheShippedAirStepCostsAgainstAConvergedOne(double speed, double gammaDeg)
    {
        double3 converged = ImpactAt(ConvergedSeconds, speed, gammaDeg);

        Out.WriteLine($"{speed:F0} m/s at {gammaDeg:F1} deg, against a {ConvergedSeconds * 1000.0:F0} ms reference:");

        foreach (double step in new[] { 0.25, 0.10, 0.05, 0.02, 0.01 })
        {
            double moved = DeorbitShot.GroundMetres(ImpactAt(step, speed, gammaDeg), converged);
            Out.WriteLine($"  {step * 1000.0,4:F0} ms air step: {moved,12:F4} m from converged");
        }
    }

    /// <summary>
    /// <b>The negative this file exists to record.</b> The shipped air step is already converged: it
    /// costs a fraction of a metre across an order of magnitude of refinement, non-monotonically,
    /// which is the <see cref="ImpactPredictor.CrossingToleranceMetres"/> bisection floor rather
    /// than integration error.
    ///
    /// <para>So the predictor's own step is <em>not</em> the 2,352 m the six flown shots walked from
    /// their probes, and refining it buys nothing. Asserted rather than left as a printout, because
    /// the tempting fix — tighten the step — is now ruled out and should stay ruled out.</para>
    /// </summary>
    [Fact]
    public void TheShippedAirStepIsAlreadyConverged()
    {
        double3 converged = ImpactAt(ConvergedSeconds, 4_640.0, 13.7);

        foreach (double step in new[] { 0.25, 0.10, 0.05, 0.02, 0.01 })
        {
            double moved = DeorbitShot.GroundMetres(ImpactAt(step, 4_640.0, 13.7), converged);

            Assert.True(moved < 5.0,
                        $"a {step * 1000.0:F0} ms air step moves the impact {moved:F1} m, so the "
                        + "predictor's own step is back in play as an error term");
        }
    }
}
