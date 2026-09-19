using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether six warheads released from one coasting bus are predicted to land in one place.
/// </summary>
/// <remarks>
/// <para>They should be. A bus on a coast puts a warhead released at <c>t</c> and one released at
/// <c>t + 0.15 s</c> on the <em>same arc</em>, so their predicted landings must agree — the later
/// release is the same trajectory sampled further along. The mod's kick is solved per warhead off
/// its own probe, so anything that makes those probes disagree is cancelled as though it were real,
/// and lands as error.</para>
///
/// <para>Flown, 87% of that per-warhead differential turns out <b>not</b> to be a real difference
/// between the warheads: the within-rocket landing deviation regresses on the probe's at
/// <b>−0.869</b> where zero would mean the kick was cancelling something true
/// (<c>docs/ACCURACY-PLAN.md</c> 3ed). This asks where the disagreement comes from with the game out
/// of the way — the ejection impulse applied at six points of the arc, or the predictor's own
/// state-to-state scatter.</para>
/// </remarks>
public class ProbeRepeatabilityTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    /// <summary>The flown geometry: released near 877 km, arriving about 32 degrees down.</summary>
    private static double3 Release(out double3 from)
    {
        from = new double3(R + 877_000.0, 0, 0);
        const double Range = 1_500_000.0;
        double lat = -26.485 * Math.PI / 180.0;
        double3 target = new(R * Math.Cos(Range / R), R * Math.Sin(Range / R) * Math.Cos(lat),
                             R * Math.Sin(Range / R) * Math.Sin(lat));
        double speed = Math.Sqrt(Mu / (R + 877_000.0));
        double3 coasting = new(0, speed * Math.Cos(lat), speed * Math.Sin(lat));
        Assert.True(BallisticArc.TryCheapest(Earth, from, coasting, target, out BallisticArc.Solution s));
        return s.RequiredVelocityCci;
    }

    /// <summary>
    /// The predicted landing, brought back to ONE epoch.
    /// </summary>
    /// <remarks>
    /// <c>GroundFixedPointCci</c> is un-carried by its own flight time, so a release <c>t</c> later
    /// is expressed in a body-fixed frame <c>t</c> younger. Comparing two of them raw measures the
    /// planet's rotation over <c>t</c> — <b>462 m/s</b> at this latitude, 12.9 m per 28 ms frame —
    /// and calls it a difference in the landing. Un-carrying each by its own release offset puts
    /// them all in the frame of the first.
    /// </remarks>
    private static double3 PredictedLanding(double3 positionCci, double3 velocityCci, double sinceFirst)
    {
        double3 ground = PredictedLanding(positionCci, velocityCci);
        return sinceFirst > 0.0 ? Earth.UncarryCci(ground, sinceFirst) : ground;
    }

    private static double3 PredictedLanding(double3 positionCci, double3 velocityCci)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, positionCci, velocityCci, 2.0, 12_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
                                               atmosphericStepSeconds: 0.25, stopOnTheSurface: true));
        return hit.GroundFixedPointCci;
    }

    /// <summary>
    /// Six releases one frame apart, each with the ejection the bus actually gives — which is what
    /// the mod probes. The spread between their predicted landings is what the kick then chases.
    /// </summary>
    [Fact]
    public void SixReleasesOneFrameApartArePredictedToLandTogether()
    {
        double3 v = Release(out double3 from);

        // A 0.5 m/s ejection along the bus's track, as Arsenal.ReentryVehicleMk21 carries.
        double3 eject = Vec.Unit(v) * Arsenal.ReentryVehicleMk21.LaunchSpeed;

        List<double3> withEject = [];
        List<double3> without = [];
        List<double3> fixedAxis = [];

        for (int i = 0; i < 6; i++)
        {
            // Where the bus is when warhead i leaves: the same arc, one 28 ms frame further on.
            double t = i * 0.028;
            Assert.True(Kepler.TryCoast(Mu, from, v, t, out double3 p, out double3 bv));

            // Along the bus's TRACK, which rotates as it coasts...
            withEject.Add(PredictedLanding(p, bv + Vec.Unit(bv) * Arsenal.ReentryVehicleMk21.LaunchSpeed, t));
            // ...and along a FIXED direction, which is what a tube on an attitude-held bus is.
            fixedAxis.Add(PredictedLanding(p, bv + eject, t));
            without.Add(PredictedLanding(p, bv, t));
        }

        double Spread(List<double3> xs)
        {
            double3 mean = xs.Aggregate(Vec.Zero, (a, b) => a + b) / xs.Count;
            return Math.Sqrt(xs.Sum(x => Vec.Len2(x - mean)) / xs.Count);
        }

        double withSpread = Spread(withEject);
        double bareSpread = Spread(without);

        Out.WriteLine("six releases, 28 ms apart on one coasting arc, predicted landings:");
        Out.WriteLine($"   ejected along the TRACK   : {withSpread * 1000.0,8:F2} mm rms");
        Out.WriteLine($"   ejected along a FIXED axis: {Spread(fixedAxis) * 1000.0,8:F2} mm rms");
        Out.WriteLine($"   no ejection at all        : {bareSpread * 1000.0,8:F2} mm rms");
        Out.WriteLine($"\n   flown, the per-warhead probe deviation is 18.1 mm rms, and 87% of it is");
        Out.WriteLine("   not a real difference between the warheads (ACCURACY-PLAN 3ed).");

        // Without an ejection the six ARE one arc, so any spread at all is the predictor
        // disagreeing with itself about the same trajectory sampled at six points.
        Out.WriteLine($"\n   the bare spread is the predictor's own state-to-state scatter, since");
        Out.WriteLine("   six points of one coast are one trajectory and must land in one place.");

        Assert.True(bareSpread < 1.0,
                    $"six samples of a single arc should predict one landing; they spread "
                    + $"{bareSpread:F3} m");
    }

    /// <summary>
    /// And the same question asked of the <em>predictor's own step</em> rather than the release
    /// instant: propagating the state forward before predicting must not change where it lands.
    /// </summary>
    [Fact]
    public void PropagatingBeforePredictingDoesNotMoveTheLanding()
    {
        double3 v = Release(out double3 from);
        double3 direct = PredictedLanding(from, v);

        Out.WriteLine($"{"coast first (s)",18} {"landing moves (mm)",20}");

        double worst = 0.0;

        foreach (double t in new[] { 0.028, 0.056, 0.084, 0.112, 0.140, 1.0, 10.0 })
        {
            Assert.True(Kepler.TryCoast(Mu, from, v, t, out double3 p, out double3 bv));
            double moved = Vec.Len(PredictedLanding(p, bv, t) - direct);
            worst = Math.Max(worst, moved);
            Out.WriteLine($"{t,18:F3} {moved * 1000.0,20:F2}");
        }

        Out.WriteLine("\n   a coast is exact and every row is brought back to one epoch, so all six");
        Out.WriteLine("   are one trajectory and every millimetre here is the predictor disagreeing");
        Out.WriteLine("   with itself about the same arc from a different start.");

        Assert.True(worst < 1.0, $"the landing moved {worst:F3} m for the same arc");
    }
}
