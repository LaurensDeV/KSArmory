using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// How far the sub-step alone moves where a warhead lands.
/// </summary>
/// <remarks>
/// <para>The walk — landing against the warhead's own post-separation prediction — carries a
/// one-signed downrange term of about −10 mm that no covariate available in flight explains
/// (<c>docs/ACCURACY-PLAN.md</c>). The round integrates its fall at
/// <see cref="MunitionProfile.SubStepSeconds"/>, 1 ms on the Mk 21, and the prediction integrates
/// its own way; a sub-step too coarse to have converged leaves exactly such a term.</para>
///
/// <para>This asks the question with the game out of the way: fly the same release state down at
/// a range of sub-steps and see where the answer settles. The gap between 1 ms and the converged
/// landing is the part of the walk the sub-step is responsible for, and it is a <em>bias</em>
/// rather than a scatter — which is what the walk's mean is.</para>
/// </remarks>
public class SubStepConvergenceTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private sealed class Sphere : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R;
            return true;
        }
    }

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

    /// <summary>Fly the real round — not the predictor — to the ground at a stated sub-step.</summary>
    private static double3 FlyToGround(double subStepMs, double frameMs, out double flightSeconds)
        => FlyToGround(subStepMs, frameMs, 0.0, out flightSeconds, out _);

    private static double3 FlyToGround(double subStepMs, double frameMs, double nudge,
                                       out double flightSeconds, out double3 arrivalVelocity)
    {
        double3 v = Release(out double3 from);

        // A different release state, the way two rockets differ: a small along-track change, which
        // is what a trim pass and a release instant actually leave.
        v += Vec.Unit(v) * nudge;

        MunitionProfile warhead = Arsenal.RoundAtSubStep(Arsenal.ReentryVehicleMk21, subStepMs / 1000.0);

        Slug round = new(from, v, null, 1, from, Vec.Zero)
        {
            Munition = warhead,
            ResampleGroundNearImpact = true,
            SecondOrder = true,
            StopOnTheTerrain = true,
        };

        RoundFields fields = new(
            GravityAt: (p, _) => Vec.Unit(-p) * (Mu / Vec.Len2(p)),
            AirDensityAt: (p, _) => DensityAt(p),
            Ground: new Sphere());

        double dt = frameMs / 1000.0;
        double t = 0.0;
        for (int i = 0; i < 2_000_000 && round.State == RoundState.Flying; i++)
        {
            RoundDriver.Fly(round, dt, null, Vec.Unit(-round.PositionEcl) * (Mu / Vec.Len2(round.PositionEcl)),
                            Vec.Zero, Vec.Zero, warhead, 0.0, fields);
            t += dt;
        }

        Assert.True(round.HitGround, $"the round should have reached the ground at {subStepMs} ms");
        flightSeconds = t;
        arrivalVelocity = round.VelocityEcl;
        return round.PositionEcl;
    }

    /// <summary>
    /// The sweep. Each row is the same release flown at a finer sub-step; the landing must settle,
    /// and how far 1 ms sits from where it settles is the number this exists to produce.
    /// </summary>
    [Fact]
    public void TheLandingSettlesAsTheSubStepFalls()
    {
        double[] steps = [4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625];

        List<(double Step, double3 At, double Flight)> rows = [];
        foreach (double s in steps)
        {
            double3 at = FlyToGround(s, 35.0, out double flight);
            rows.Add((s, at, flight));
        }

        double3 finest = rows[^1].At;

        Out.WriteLine($"released at 877 km, arriving ~32 deg, 35 ms frames, flown to a sphere\n");
        Out.WriteLine($"{"sub-step (ms)",14} {"from the finest (mm)",22} {"from 1 ms (mm)",18} {"flight (s)",12}");

        double3 atOne = rows.First(r => r.Step == 1.0).At;

        foreach ((double step, double3 at, double flight) in rows)
        {
            Out.WriteLine($"{step,14:F4} {Vec.Len(at - finest) * 1000.0,22:F3} "
                          + $"{Vec.Len(at - atOne) * 1000.0,18:F3} {flight,12:F3}");
        }

        double oneMsError = Vec.Len(atOne - finest) * 1000.0;
        Out.WriteLine($"\n   1 ms lands {oneMsError:F3} mm from the converged answer.");
        Out.WriteLine($"   The flown walk's down bias is about -10 mm and its scatter about 20 mm.");

        // Halving the step must at least halve the remaining error, or it is not converging.
        double half = Vec.Len(rows.First(r => r.Step == 0.5).At - finest) * 1000.0;
        Out.WriteLine($"   0.5 ms lands {half:F3} mm from it, a factor of {oneMsError / Math.Max(half, 1e-9):F2}.");
    }

    /// <summary>
    /// The same sweep across release states, which is what separates a bias from a scatter: one
    /// state can only show the offset, and the walk has both.
    /// </summary>
    [Fact]
    public void AcrossReleaseStatesTheSubStepIsABiasAndAScatter()
    {
        double[] steps = [4.0, 2.0, 1.0, 0.5, 0.25];
        const double Finest = 0.0625;

        // Twenty release states spread over a couple of m/s, as trim and release instant leave them.
        double[] nudges = [.. Enumerable.Range(0, 20).Select(i => -1.0 + i * 0.1)];

        Dictionary<double, List<(double Down, double Cross)>> rows = [];

        foreach (double nudge in nudges)
        {
            double3 truth = FlyToGround(Finest, 35.0, nudge, out _, out double3 tv);
            Assert.True(ArrivalFrame.TryAt(truth, tv, out ArrivalFrame frame));

            foreach (double s in steps)
            {
                double3 at = FlyToGround(s, 35.0, nudge, out _, out _);
                double3 d = frame.Resolve(at - truth);
                rows.TryAdd(s, []);
                rows[s].Add((d.Y * 1000.0, d.Z * 1000.0));
            }
        }

        Out.WriteLine($"20 release states, each against its own {Finest} ms truth, in the arrival frame\n");
        Out.WriteLine($"{"sub-step (ms)",14} {"down bias (mm)",16} {"down sd (mm)",14} "
                      + $"{"cross bias (mm)",17} {"cross sd (mm)",15}");

        foreach (double s in steps)
        {
            List<(double Down, double Cross)> r = rows[s];
            double md = r.Average(x => x.Down), mc = r.Average(x => x.Cross);
            double sd = Math.Sqrt(r.Sum(x => (x.Down - md) * (x.Down - md)) / r.Count);
            double sc = Math.Sqrt(r.Sum(x => (x.Cross - mc) * (x.Cross - mc)) / r.Count);
            Out.WriteLine($"{s,14:F4} {md,16:F3} {sd,14:F3} {mc,17:F3} {sc,15:F3}");
        }

        Out.WriteLine("\n   Flown, the walk is about -10 mm down with a within-rocket sd of 19.8 mm.");
        Out.WriteLine("   A sub-step term is ONE-SIGNED across states; scatter here would mean the");
        Out.WriteLine("   sub-step also amplifies whatever separates two release states.");
    }

    /// <summary>
    /// And whether the frame the sub-steps divide changes the answer. It must not: the round cuts
    /// each frame into <c>ceil(dt / SubStep)</c> pieces, so the piece is the same size whatever the
    /// frame is — but the cap is <c>max(64, ceil(MaxFaithfulStep / SubStep))</c>, and a night flown
    /// at a different warp is a different frame, so this is measured rather than assumed.
    /// </summary>
    [Fact]
    public void TheFrameTheSubStepsDivideDoesNotChangeTheBias()
    {
        double[] frames = [8.33, 35.0, 70.0, 152.9, 300.0];
        double[] steps = [4.0, 1.0, 0.25];

        double3 truth = FlyToGround(0.0625, 35.0, 0.0, out _, out double3 tv);
        Assert.True(ArrivalFrame.TryAt(truth, tv, out ArrivalFrame frame));

        Out.WriteLine("down bias against the 0.0625 ms answer, in mm\n");
        Out.WriteLine($"{"frame (ms)",12} {"4 ms",14} {"1 ms",14} {"0.25 ms",14}");

        foreach (double f in frames)
        {
            List<string> cells = [];
            foreach (double st in steps)
            {
                double3 at = FlyToGround(st, f, 0.0, out _, out _);
                cells.Add($"{frame.Resolve(at - truth).Y * 1000.0,14:F3}");
            }
            Out.WriteLine($"{f,12:F2}{string.Concat(cells)}");
        }

        Out.WriteLine("\n   The flown night runs at 152.9 ms frames, 8x warp; the declared predictions");
        Out.WriteLine("   (-21.899 / -5.360 / -1.072 mm) were computed at 35 ms.");
    }
}
