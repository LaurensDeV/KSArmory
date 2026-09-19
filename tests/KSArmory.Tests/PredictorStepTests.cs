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

    private static double3 ImpactAt(double airStepSeconds, double speed, double gammaDeg,
                                    Func<double3, double>? terrainRadiusAt = null)
    {
        (double3 position, double3 velocity) = Entry(speed, gammaDeg);

        Assert.True(ImpactPredictor.TryPredict(Earth, position, velocity, 2.0, 3_000.0,
                                               out ImpactPredictor.Impact hit,
                                               terrainRadiusAt: terrainRadiusAt,
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
    /// <b>The negative this file records, and the exact ground it was established on.</b> The
    /// shipped air step is converged <em>against a sphere</em>: it costs a fraction of a metre
    /// across an order of magnitude of refinement, non-monotonically, which is the
    /// <see cref="ImpactPredictor.CrossingToleranceMetres"/> bisection floor rather than integration
    /// error.
    ///
    /// <para><b>That is a statement about drag and nothing else</b>, because this passes no
    /// <c>terrainRadiusAt</c> and a null one makes <c>SurfaceUnder</c> answer with the mean sphere —
    /// so what converges here is the entry integration, over the one surface that has no features
    /// to miss. <see cref="TheShippedAirStepIsNotConvergedOverTerrain"/> asks the same question over
    /// ground and gets the opposite answer; keep both.</para>
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

    /// <summary>
    /// The same refinement over ground, which is the surface the predictor actually observes.
    ///
    /// <para>Printed rather than asserted, because the interesting part is the shape: the sphere
    /// converges and the terrain does not, and how far it does not is the size of the term.</para>
    /// </summary>
    [Theory]
    [InlineData(4_640.0, 7.0)]
    [InlineData(4_640.0, 13.7)]
    public void WhatTheShippedAirStepCostsOverTerrain(double speed, double gammaDeg)
    {
        double3 ball = ImpactAt(ConvergedSeconds, speed, gammaDeg);
        double3 rough = ImpactAt(ConvergedSeconds, speed, gammaDeg, DeorbitShot.RoughGround);
        double3 eroded = ImpactAt(ConvergedSeconds, speed, gammaDeg, DeorbitShot.ErodedGround);

        Out.WriteLine($"{speed:F0} m/s at {gammaDeg:F1} deg, each against its own "
                      + $"{ConvergedSeconds * 1000.0:F0} ms reference:");
        Out.WriteLine("   step        sphere        rough       eroded");

        foreach (double step in new[] { 0.25, 0.10, 0.05, 0.02, 0.01, 0.005 })
        {
            double onBall = DeorbitShot.GroundMetres(ImpactAt(step, speed, gammaDeg), ball);
            double onRough = DeorbitShot.GroundMetres(
                ImpactAt(step, speed, gammaDeg, DeorbitShot.RoughGround), rough);
            double onEroded = DeorbitShot.GroundMetres(
                ImpactAt(step, speed, gammaDeg, DeorbitShot.ErodedGround), eroded);

            Out.WriteLine($"  {step * 1000.0,4:F0} ms {onBall,12:F3} {onRough,12:F3} {onEroded,12:F3}");
        }
    }

    /// <summary>
    /// <b>3z, tested and refuted.</b> The predictor's 250 ms step samples 782 m of ground apart,
    /// and 3z reasoned from that plus a slope comparison that the long-range miss was the predictor
    /// aliasing terrain. Over <b>KSA's own erosion spectrum</b> it costs a tenth of a metre.
    ///
    /// <para><b>The mechanism is real and the criterion was wrong.</b> A coarse step does cost
    /// hundreds of metres — <see cref="WhereACoarseStepStartsToCost"/> reads 576 m — but only for an
    /// octave both <em>taller</em> than the arc's drop across one sample interval (about 101 m at
    /// this arrival) and <em>shorter</em> than twice the sample spacing, so it can hide between two
    /// samples. Slope does not predict it at all: 0.79 costs 576 m and 0.84 costs nothing.
    /// KSA's largest sub-Nyquist octave is 62.5 m at 1.33 km, well under the threshold.</para>
    ///
    /// <para><b>All three legs are load-bearing.</b> The sphere says the rig is sound. KSA's
    /// spectrum is the finding. The tall octave is what stops this being another blind test — 3z
    /// exists because the negative before it was established against a surface with nothing to
    /// miss, and a null here means nothing unless the same rig can still see a real effect.</para>
    /// </summary>
    [Fact]
    public void TheShippedAirStepIsConvergedOverKsasTerrainAndTheRigCanStillSeeAnEffect()
    {
        const double shipped = ImpactPredictor.AtmosphericStepSeconds;

        double Moved(Func<double3, double>? ground)
            => DeorbitShot.GroundMetres(ImpactAt(shipped, 4_640.0, 7.0, ground),
                                        ImpactAt(ConvergedSeconds, 4_640.0, 7.0, ground));

        double onBall = Moved(null);
        double onKsa = Moved(DeorbitShot.ErodedGroundKsaSpectrum);
        double onTall = Moved(DeorbitShot.ErodedGroundOf(100.0, 800.0));

        Out.WriteLine($"shipped {shipped * 1000.0:F0} ms step against a {ConvergedSeconds * 1000.0:F0} ms one: "
                      + $"sphere {onBall:F2} m, KSA erosion {onKsa:F2} m, 100 m over 800 m {onTall:F1} m");

        Assert.True(onBall < 5.0, $"the sphere leg moved {onBall:F2} m, so the rig is at fault");

        Assert.True(onKsa < 5.0,
                    $"the shipped step moves {onKsa:F1} m over KSA's erosion spectrum, where it used "
                    + "to move a tenth of a metre. Gating the step on clearance is back on the "
                    + "table -- see docs/ACCURACY-PLAN.md 3z and 3ab.");

        Assert.True(onTall > 100.0,
                    $"an octave 100 m tall and 800 m across moved the impact only {onTall:F1} m, so "
                    + "this file can no longer see the effect it exists to rule out, and the KSA "
                    + "leg above proves nothing. Fix the rig before trusting either.");
    }

    /// <summary>
    /// Whether the predictor's answer over erosion survives sampling far finer than any hill.
    ///
    /// <para>3ac has the round stopping on a hill the probe runs past. The probe samples finer than
    /// the round already, so if it is missing terrain it should stop doing so once the step is well
    /// below the shortest feature. At 0.1 ms this is under half a metre of ground track against a
    /// 166 m shortest octave.</para>
    /// </summary>
    [Fact]
    public void WhetherTheProbesAnswerOverErosionSurvivesAnyRefinement()
    {
        double3 coarse = ImpactAt(0.25, 4_640.0, 7.0, DeorbitShot.ErodedGroundKsaSpectrum);

        foreach (double step in new[] { 0.05, 0.01, 0.002, 0.0005, 0.0001 })
        {
            double3 fine = ImpactAt(step, 4_640.0, 7.0, DeorbitShot.ErodedGroundKsaSpectrum);

            Out.WriteLine($"  {step * 1000.0,6:F2} ms ({4_640.0 * step,7:F2} m of ground track): "
                          + $"{DeorbitShot.GroundMetres(fine, coarse),9:F1} m from the shipped step, "
                          + $"landing over terrain at {DeorbitShot.ErodedGroundKsaSpectrum(fine) - DeorbitShot.R:F1} m");
        }
    }

    /// <summary>What the fixture and the sampling actually are, so a null result can be trusted.</summary>
    [Fact]
    public void WhatThePredictorActuallySamples()
    {
        (double3 position, double3 velocity) = Entry(4_640.0, 7.0);

        foreach (double step in new[] { 0.25, 0.002 })
        {
            List<double3> path = [];
            Assert.True(ImpactPredictor.TryPredict(Earth, position, velocity, 2.0, 3_000.0,
                                                   out ImpactPredictor.Impact hit,
                                                   terrainRadiusAt: DeorbitShot.ErodedGround,
                                                   pathCci: path,
                                                   drag: new ImpactPredictor.Drag(DeorbitShot.DensityAt,
                                                                                 DeorbitShot.Warhead),
                                                   atmosphericStepSeconds: step));

            // Spacing over the last stretch, which is the only part where terrain can be missed.
            double lastGap = DeorbitShot.GroundMetres(path[^1], path[^2]);
            double gap10 = path.Count > 10 ? DeorbitShot.GroundMetres(path[^10], path[^11]) : double.NaN;

            Out.WriteLine($"{step * 1000.0:F0} ms: {path.Count} path points, "
                          + $"last gap {lastGap:F1} m, tenth-from-last gap {gap10:F1} m, "
                          + $"impact at {hit.PointCci.Length() - DeorbitShot.R:F1} m radius-excess");
        }

        // The surface itself, sampled finely along the track through the impact.
        (double3 pos, double3 vel) = Entry(4_640.0, 7.0);
        Assert.True(ImpactPredictor.TryPredict(Earth, pos, vel, 2.0, 3_000.0,
                                               out ImpactPredictor.Impact fine,
                                               terrainRadiusAt: DeorbitShot.ErodedGround,
                                               drag: new ImpactPredictor.Drag(DeorbitShot.DensityAt,
                                                                             DeorbitShot.Warhead),
                                               atmosphericStepSeconds: 0.002));

        double3 u = Vec.Unit(fine.PointCci);
        double3 alongTrack = Vec.Unit(new double3(0, 1, 0) - u * Vec.Dot(new double3(0, 1, 0), u));

        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = -60; i <= 60; i++)
        {
            double3 at = fine.PointCci + alongTrack * (i * 25.0);
            double h = DeorbitShot.ErodedGround(at) - DeorbitShot.R;
            lo = Math.Min(lo, h);
            hi = Math.Max(hi, h);
        }

        Out.WriteLine($"eroded surface over +/-1500 m of track: {lo:F1} to {hi:F1} m, "
                      + $"swing {hi - lo:F1} m");
    }

    /// <summary>
    /// Where a coarse step starts to cost something, swept over the octave that would cause it.
    ///
    /// <para>The criterion 3z reasons from is <em>slope</em> — terrain climbing 0.84 against an arc
    /// descending 0.125. This sweep exists because that is the wrong comparison: what decides
    /// whether a feature can be stepped over is its <b>excursion across one sample interval</b>
    /// against the arc's <b>drop across the same interval</b>, and a short-wavelength octave
    /// returns to its mean several times within one step however steep it is locally.</para>
    /// </summary>
    [Fact]
    public void WhereACoarseStepStartsToCost()
    {
        const double shipped = ImpactPredictor.AtmosphericStepSeconds;

        Out.WriteLine("amplitude x wavelength -> how far the shipped 250 ms step lands from a 2 ms one");

        foreach (double wavelength in new[] { 300.0, 800.0, 1_600.0, 3_200.0 })
        {
            foreach (double amplitude in new[] { 40.0, 100.0, 250.0, 600.0 })
            {
                Func<double3, double> ground = DeorbitShot.ErodedGroundOf(amplitude, wavelength);

                double moved = DeorbitShot.GroundMetres(
                    ImpactAt(shipped, 4_640.0, 7.0, ground),
                    ImpactAt(ConvergedSeconds, 4_640.0, 7.0, ground));

                Out.WriteLine($"  {amplitude,5:F0} m over {wavelength,6:F0} m "
                              + $"(slope {amplitude * 2.0 * Math.PI / wavelength,5:F2}): {moved,10:F1} m");
            }
        }
    }

    /// <summary>
    /// <b>The question 3z actually turns on, against KSA's own spectrum rather than an invented
    /// octave.</b> Seven erosion octaves, undamped, so the biome weight can only make the real
    /// thing smaller than this.
    /// </summary>
    [Theory]
    [InlineData(7.0)]
    [InlineData(13.7)]
    public void WhatTheShippedStepCostsOnKsasOwnErosion(double gammaDeg)
    {
        const double shipped = ImpactPredictor.AtmosphericStepSeconds;

        double moved = DeorbitShot.GroundMetres(
            ImpactAt(shipped, 4_640.0, gammaDeg, DeorbitShot.ErodedGroundKsaSpectrum),
            ImpactAt(ConvergedSeconds, 4_640.0, gammaDeg, DeorbitShot.ErodedGroundKsaSpectrum));

        Out.WriteLine($"{gammaDeg:F1} deg on KSA's erosion spectrum: the shipped "
                      + $"{shipped * 1000.0:F0} ms step lands {moved:F1} m from a "
                      + $"{ConvergedSeconds * 1000.0:F0} ms one");
    }
}
