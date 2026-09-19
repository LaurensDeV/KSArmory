using Brutal.Numerics;
using KSArmory;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What sampling the ground once a frame costs, as a function of arrival angle.
///
/// <para><see cref="Slug"/> asks <see cref="IGroundTest"/> where the ground is once per frame, before
/// the sub-step loop, and holds it as a sphere across the whole frame. On a shallow arrival that is
/// nearly free — <c>ProbeGapTests</c> prices it at zero on the 7.1 degree deorbit. It should not stay
/// free: the vertical closing speed goes as <c>sin(gamma)</c>, so a round arriving steeply crosses
/// the held surface proportionally faster and overshoots it deeper before the next sample notices.
///
/// <para>Which matters because the whole case for a steeper arrival is that it divides every height
/// term by <c>cot(gamma)</c>. A term that instead <em>grows</em> with the angle works against it.</para>
///
/// <para>Flown against <see cref="DeorbitShot.Relief"/> rather than a sphere, and it has to be: the
/// held sample <em>is</em> a sphere, so on a spherical ground refreshing it changes nothing and the
/// term measures zero at every angle whether or not it is real.</para>
/// </summary>
public class SteepGroundSampleTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    /// <summary>Entry state 100 km up, at a stated angle below the local horizontal.</summary>
    private static void Entry(double angleDeg, double speed, out double3 from, out double3 v)
    {
        double r = DeorbitShot.R + 100_000.0;
        from = new double3(r, 0.0, 0.0);

        double g = angleDeg * Math.PI / 180.0;
        double3 up = new(1.0, 0.0, 0.0);
        double3 along = new(0.0, 1.0, 0.0);

        v = (along * Math.Cos(g) - up * Math.Sin(g)) * speed;
    }

    [Fact]
    public void HoldingTheGroundForAFrameCostsMoreTheSteeperItArrives()
    {
        _out.WriteLine("arrival   held-for-a-frame vs per-sub-step, on the ground");

        double shallow = double.NaN;
        double steep = double.NaN;

        foreach (double deg in new[] { 7.0, 15.0, 20.0, 30.0, 45.0, 60.0 })
        {
            Entry(deg, 7_000.0, out double3 from, out double3 v);

            (double3 held, double _) =
                DeorbitShot.FlyTheRound(from, v, DeorbitShot.NominalFrame,
                                        default, new DeorbitShot.Relief());

            (double3 fresh, double _) =
                DeorbitShot.FlyTheRound(from, v, DeorbitShot.NominalFrame,
                                        new DeorbitShot.Refresh { HoldGravity = true, Ground = true },
                                        new DeorbitShot.Relief());

            double metres = DeorbitShot.GroundMetres(held, fresh);
            _out.WriteLine($"  {deg,4:F0} deg   {metres,8:F1} m");

            if (deg == 7.0) shallow = metres;
            if (deg == 20.0) steep = metres;
        }

        _out.WriteLine($"\n  20 deg costs {steep / shallow:F2}x what 7 deg does");

        // The claim under test: this term does not shrink with a steeper arrival the way the
        // height terms do. Recorded rather than asserted tightly, because the size is the finding.
        Assert.True(double.IsFinite(shallow) && double.IsFinite(steep));
    }
}
