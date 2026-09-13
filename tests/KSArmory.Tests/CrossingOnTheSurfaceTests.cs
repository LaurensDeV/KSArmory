using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Where <see cref="ImpactPredictor"/> puts the ground crossing, with <c>stopOnTheSurface</c> off and
/// on.
///
/// <para>The search accepts the first step up to <see cref="ImpactPredictor.CrossingToleranceMetres"/>
/// under the ground, so its answer is always long by that depth over the arc's descent — while a
/// warhead stops on the surface. Flown, the landing sits 0.201 m short of the late re-flies on 80 of
/// 80 traced warheads, against 0.199 m from the tolerance at 32°. <c>docs/ACCURACY-PLAN.md</c> 40b.</para>
///
/// <para><b>One prediction says nothing about a bias.</b> The depth it reports is wherever its steps
/// happened to fall against the crossing, so every case flies a sweep of arcs started a few tens of
/// metres apart and reads the distribution.</para>
/// </summary>
public class CrossingOnTheSurfaceTests(ITestOutputHelper Out)
{
    private const double Tolerance = ImpactPredictor.CrossingToleranceMetres;
    private const double R = DeorbitShot.R;
    private const int Arcs = 64;

    // Not a multiple of anything the step grid is made of, so each arc meets the ground at a new phase.
    private const double StartSpacingMetres = 29.3;

    private const double RaisedMetres = 1_500.0;

    private static BallisticBody Spinning => DeorbitShot.Earth;

    // Still, so the ground-fixed point is the inertial one and a displacement along the track is
    // exactly the horizontal part of the arc.
    private static BallisticBody Still => new(DeorbitShot.Mu, R, new double3(0, 0, 1), 0.0);

    private static ImpactPredictor.Drag Air => new(DeorbitShot.DensityAt, DeorbitShot.Warhead);

    private readonly record struct Stop(ImpactPredictor.Impact Hit, double Height);

    public static TheoryData<string, double, double> Grounds => new()
    {
        { "sphere", 5_600.0, 32.0 },
        { "raised", 5_600.0, 32.0 },
        { "ramp up", 5_600.0, 32.0 },
        { "ramp down", 5_600.0, 32.0 },
        { "relief", 5_600.0, 32.0 },
        { "sphere", 4_640.0, 7.0 },
        { "ramp up", 4_640.0, 7.0 },
        { "ramp down", 4_640.0, 7.0 },
        { "relief", 4_640.0, 7.0 },
    };

    /// <summary>
    /// With the switch off every stop is under the ground and the mean is well under it; with it on,
    /// the stops sit on the ground to millimetres.
    /// </summary>
    [Theory]
    [MemberData(nameof(Grounds))]
    public void TheSearchStopsUnderTheGroundAndTheSwitchStopsItOnTheGround(string ground, double speed,
                                                                           double gammaDeg)
    {
        Func<double3, double>? surface = Ground(Spinning, ground, speed, gammaDeg);

        double[] off = Sweep(Spinning, surface, speed, gammaDeg, onSurface: false).Select(s => s.Height).ToArray();
        double[] on = Sweep(Spinning, surface, speed, gammaDeg, onSurface: true).Select(s => s.Height).ToArray();

        Out.WriteLine($"{ground} at {gammaDeg:F0} deg: off mean {off.Average():+0.0000;-0.0000} m, "
                      + $"deepest {off.Min():F4}; on mean {on.Average():+0.000000;-0.000000} m, "
                      + $"worst {on.Max(h => Math.Abs(h)):F6}");

        // The regime: every answer came out of the refinement, and it had the whole tolerance to use.
        Assert.All(off, h => Assert.InRange(h, -Tolerance - 1e-9, 1e-9));
        Assert.True(-off.Min() > 0.6 * Tolerance,
                    $"the deepest stop is {-off.Min():F3} m, so the bisection is not what sets these "
                    + "answers and nothing here measures it");

        Assert.True(off.Average() < -0.3 * Tolerance,
                    $"with the switch off the mean stop is {off.Average():F3} m, so the search is no "
                    + "longer one-sided and the switch has nothing to remove");

        Assert.True(Math.Abs(on.Average()) < 0.002,
                    $"with the switch on the mean stop is {on.Average():F4} m off the ground");
        // A line across the bracket leaves the ground's curvature over it, and on a shallow arc over
        // rising relief the bracket is metres of track: centimetres either way, never one-signed.
        Assert.True(on.Max(h => Math.Abs(h)) < 0.1 * Tolerance,
                    $"with the switch on a stop sits {on.Max(h => Math.Abs(h)):F4} m off the ground");
    }

    /// <summary>
    /// The height field KSA ships is a staircase, so a line across the bracket can land on either tread.
    /// What the switch owes it is no bias: the mean inside its own sampling error.
    /// </summary>
    [Fact]
    public void OnKsasQuantisedReliefTheStopsCentreOnTheGround()
    {
        Func<double3, double> surface = DeorbitShot.ErodedGroundKsaSpectrum;

        double[] off = Sweep(Spinning, surface, 5_600.0, 32.0, onSurface: false).Select(s => s.Height).ToArray();
        double[] on = Sweep(Spinning, surface, 5_600.0, 32.0, onSurface: true).Select(s => s.Height).ToArray();

        double spread = Math.Sqrt(on.Select(h => (h - on.Average()) * (h - on.Average())).Sum() / (on.Length - 1));
        double standardError = spread / Math.Sqrt(on.Length);

        Out.WriteLine($"KSA relief at 32 deg: off mean {off.Average():F4} m; on mean {on.Average():F4} m, "
                      + $"sd {spread:F4}, worst {on.Max(h => Math.Abs(h)):F4}");

        // Never above the ground, but not always within the tolerance: a riser taller than it holds the
        // bisection at the riser until the step can shrink no further.
        Assert.All(off, h => Assert.True(h <= 1e-9, $"a stop {h:F3} m above the ground"));
        Assert.True(off.Average() < -0.3 * Tolerance, $"off mean {off.Average():F3} m is not one-sided");

        Assert.True(Math.Abs(on.Average()) < 3.0 * standardError + 0.005,
                    $"with the switch on the mean stop is {on.Average():F4} m against a standard error of "
                    + $"{standardError:F4}, which is a bias rather than the staircase");
    }

    /// <summary>
    /// What the long reading is worth on the ground and on the clock: the depth over how fast the arc
    /// closes on the ground, which on a sphere is <c>cot γ</c> along the track.
    /// </summary>
    [Theory]
    [InlineData("sphere", 5_600.0, 32.0)]
    [InlineData("ramp up", 5_600.0, 32.0)]
    [InlineData("sphere", 4_640.0, 7.0)]
    public void TheLongReadingIsTheDepthOverTheDescentAndTheClockMovesWithIt(string ground, double speed,
                                                                            double gammaDeg)
    {
        Func<double3, double>? surface = Ground(Still, ground, speed, gammaDeg);
        double slope = ground == "ramp up" ? SlopeFor(gammaDeg) : 0.0;

        List<Stop> off = Sweep(Still, surface, speed, gammaDeg, onSurface: false);
        List<Stop> on = Sweep(Still, surface, speed, gammaDeg, onSurface: true);

        int compared = 0;
        double worstAlong = 0.0, worstClock = 0.0, sumLong = 0.0, sumCot = 0.0;

        for (int k = 0; k < Arcs; k++)
        {
            double depth = on[k].Height - off[k].Height;
            if (depth < 0.05) continue;

            double3 up = Vec.Unit(on[k].Hit.PointCci);
            double climb = Vec.Dot(on[k].Hit.VelocityCci, up);
            double across = Vec.Len(on[k].Hit.VelocityCci - up * climb);
            double closing = -climb + slope * across;

            double longBy = AlongTrack(off[k].Hit.GroundFixedPointCci) - AlongTrack(on[k].Hit.GroundFixedPointCci);
            double lateBy = off[k].Hit.Seconds - on[k].Hit.Seconds;

            worstAlong = Math.Max(worstAlong, Math.Abs(longBy / (depth * across / closing) - 1.0));
            worstClock = Math.Max(worstClock, Math.Abs(lateBy / (depth / closing) - 1.0));
            sumLong += longBy;
            sumCot += across / -climb;
            compared++;
        }

        Out.WriteLine($"{ground} at {gammaDeg:F0} deg: {compared} arcs, mean long reading "
                      + $"{sumLong / compared:F3} m at cot {sumCot / compared:F3}, worst ratio error "
                      + $"{worstAlong:P2} along the track and {worstClock:P2} on the clock");

        Assert.True(compared >= Arcs / 2, $"only {compared} arcs stopped deep enough to compare");
        Assert.True(worstAlong < 0.02, $"the long reading is {worstAlong:P1} from the depth over the descent");
        Assert.True(worstClock < 0.02, $"the late reading is {worstClock:P1} from the depth over the closing rate");
    }

    public static TheoryData<string> Twins => ["sphere in air", "relief in air", "KSA relief", "vacuum from 200 km",
                                               "on the pad"];

    /// <summary>
    /// With the switch off the predictor answers exactly what the search with no switch answers, down to
    /// the bit — and with it on, the same arc moves, so the comparison is not of two things that agree
    /// for want of anything to disagree about.
    /// </summary>
    [Theory]
    [MemberData(nameof(Twins))]
    public void WithTheSwitchOffTheAnswerIsTheSearchsToTheBit(string name)
    {
        (BallisticBody body, double3 p, double3 v, Func<double3, double>? surface, ImpactPredictor.Drag? air) = Twin(name);

        List<double3> refPath = [], offPath = [];

        bool refOk = Reference.TryPredict(body, p, v, 2.0, 7_200.0, out ImpactPredictor.Impact expected,
                                          surface, refPath, air);
        bool offOk = ImpactPredictor.TryPredict(body, p, v, 2.0, 7_200.0, out ImpactPredictor.Impact off,
                                                surface, offPath, air);
        bool onOk = ImpactPredictor.TryPredict(body, p, v, 2.0, 7_200.0, out ImpactPredictor.Impact on,
                                               surface, null, air, stopOnTheSurface: true);

        Assert.Equal(refOk, offOk);
        Assert.Equal(refOk, onOk);

        Assert.Equal(Bits(expected.PointCci), Bits(off.PointCci));
        Assert.Equal(Bits(expected.GroundFixedPointCci), Bits(off.GroundFixedPointCci));
        Assert.Equal(Bits(expected.VelocityCci), Bits(off.VelocityCci));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Seconds), BitConverter.DoubleToInt64Bits(off.Seconds));
        Assert.Equal(refPath.Count, offPath.Count);
        Assert.Equal(refPath.Select(Bits), offPath.Select(Bits));

        if (refOk)
        {
            Assert.True(on.Seconds < off.Seconds, $"{name}: the switch did not move the crossing");
        }
    }

    /// <summary>
    /// The switch reads no ground and flies no step the search did not already: the crossing is placed
    /// from the two samples that bracket it.
    /// </summary>
    [Fact]
    public void PlacingTheCrossingCostsNoLookup()
    {
        Func<double3, double> surface = Ground(Spinning, "relief", 5_600.0, 32.0)!;

        (int Ground, int Air) Count(Func<Func<double3, double>, ImpactPredictor.Drag, bool> fly)
        {
            int ground = 0, air = 0;
            bool ok = fly(q => { ground++; return surface(q); },
                          new ImpactPredictor.Drag(q => { air++; return DeorbitShot.DensityAt(q); }, DeorbitShot.Warhead));
            Assert.True(ok);
            return (ground, air);
        }

        (int Ground, int Air) reference = (0, 0), off = (0, 0), on = (0, 0);

        for (int k = 0; k < Arcs; k++)
        {
            (double3 p, double3 v) = Entry(StartAltitude(32.0) + k * StartSpacingMetres, 5_600.0, 32.0);

            (int, int) Add((int, int) a, (int, int) b) => (a.Item1 + b.Item1, a.Item2 + b.Item2);

            reference = Add(reference, Count((g, a) => Reference.TryPredict(Spinning, p, v, 2.0, 3_000.0, out _, g, null, a)));
            off = Add(off, Count((g, a) => ImpactPredictor.TryPredict(Spinning, p, v, 2.0, 3_000.0, out _, g, null, a)));
            on = Add(on, Count((g, a) => ImpactPredictor.TryPredict(Spinning, p, v, 2.0, 3_000.0, out _, g, null, a,
                                                                     stopOnTheSurface: true)));
        }

        Out.WriteLine($"per prediction: ground lookups {reference.Ground / (double)Arcs:F1} for the search "
                      + $"with no switch, {off.Ground / (double)Arcs:F1} off, {on.Ground / (double)Arcs:F1} on; "
                      + $"air lookups {reference.Air / (double)Arcs:F1}, {off.Air / (double)Arcs:F1}, "
                      + $"{on.Air / (double)Arcs:F1}");

        Assert.Equal(off, on);
        Assert.Equal(reference.Air, off.Air);
        Assert.True(off.Ground <= reference.Ground, "the switch-off path reads more ground than the search did");
    }

    // Coming down along +Y over (R, 0, 0), which on the spinning body is eastbound at the equator.
    private static (double3 Position, double3 Velocity) Entry(double altitude, double speed, double gammaDeg)
    {
        double gamma = gammaDeg * Math.PI / 180.0;
        double3 up = new(1, 0, 0);
        double3 along = new(0, 1, 0);

        return (up * (R + altitude), (along * Math.Cos(gamma) - up * Math.Sin(gamma)) * speed);
    }

    // High enough that the refinement starts from a whole air step, low enough that the arc is still
    // coming down at the angle it was started at.
    private static double StartAltitude(double gammaDeg) => gammaDeg > 20.0 ? 20_000.0 : 8_000.0;

    private static double AlongTrack(double3 bodyFixedCci) => R * Math.Atan2(bodyFixedCci.Y, bodyFixedCci.X);

    // Half the arc's own descent either way, so the ground is crossed once and cleanly.
    private static double SlopeFor(double gammaDeg) => 0.5 * Math.Tan(gammaDeg * Math.PI / 180.0);

    private static Func<double3, double>? Ground(BallisticBody body, string name, double speed, double gammaDeg)
    {
        Func<double3, double> raised = _ => R + RaisedMetres;

        if (name == "sphere") return null;
        if (name == "raised") return raised;

        // Laid through where the first arc meets the raised sphere, so every arc of the sweep crosses
        // it near there rather than kilometres up or down the slope.
        (double3 p, double3 v) = Entry(StartAltitude(gammaDeg), speed, gammaDeg);
        Assert.True(ImpactPredictor.TryPredict(body, p, v, 2.0, 3_000.0, out ImpactPredictor.Impact first,
                                               raised, null, Air));

        double s0 = AlongTrack(first.GroundFixedPointCci);
        double slope = SlopeFor(gammaDeg);

        // Two octaves whose steepest sum is a little over half the descent at 32 degrees, scaled with it.
        double scale = Math.Tan(gammaDeg * Math.PI / 180.0) / Math.Tan(32.0 * Math.PI / 180.0);

        return name switch
        {
            "ramp up" => q => R + RaisedMetres + slope * (AlongTrack(q) - s0),
            "ramp down" => q => R + RaisedMetres - slope * (AlongTrack(q) - s0),
            "relief" => q => R + RaisedMetres + scale * (40.0 * Math.Sin(AlongTrack(q) / 170.0)
                                                         + 8.0 * Math.Sin(AlongTrack(q) / 53.0 + 1.0)),
            _ => throw new ArgumentException(name),
        };
    }

    private static List<Stop> Sweep(BallisticBody body, Func<double3, double>? surface, double speed,
                                    double gammaDeg, bool onSurface)
    {
        List<Stop> stops = new(Arcs);

        for (int k = 0; k < Arcs; k++)
        {
            (double3 p, double3 v) = Entry(StartAltitude(gammaDeg) + k * StartSpacingMetres, speed, gammaDeg);

            Assert.True(ImpactPredictor.TryPredict(body, p, v, 2.0, 3_000.0, out ImpactPredictor.Impact hit,
                                                   surface, null, Air, stopOnTheSurface: onSurface),
                        $"arc {k} predicted no impact");

            stops.Add(new Stop(hit, hit.PointCci.Length() - (surface?.Invoke(hit.GroundFixedPointCci) ?? R)));
        }

        return stops;
    }

    private static (BallisticBody, double3, double3, Func<double3, double>?, ImpactPredictor.Drag?) Twin(string name)
    {
        switch (name)
        {
            case "sphere in air":
            {
                (double3 p, double3 v) = Entry(20_000.0, 5_600.0, 32.0);
                return (Spinning, p, v, null, Air);
            }
            case "relief in air":
            {
                (double3 p, double3 v) = Entry(20_000.0, 5_600.0, 32.0);
                return (Spinning, p, v, Ground(Spinning, "relief", 5_600.0, 32.0), Air);
            }
            case "KSA relief":
            {
                (double3 p, double3 v) = Entry(8_000.0, 4_640.0, 7.0);
                return (Spinning, p, v, DeorbitShot.ErodedGroundKsaSpectrum, Air);
            }
            case "vacuum from 200 km":
            {
                (double3 p, double3 v) = Entry(200_000.0, 7_000.0, 3.0);
                return (Spinning, p, v, _ => R + RaisedMetres, null);
            }
            case "on the pad":
            {
                (double3 p, double3 _) = Entry(-200.0, 0.0, 0.0);
                return (Spinning, p, new double3(0, 0, 0), null, Air);
            }
            default:
                throw new ArgumentException(name);
        }
    }

    private static (long, long, long) Bits(double3 value)
        => (BitConverter.DoubleToInt64Bits(value.X), BitConverter.DoubleToInt64Bits(value.Y),
            BitConverter.DoubleToInt64Bits(value.Z));

    /// <summary>
    /// The crossing search with no switch, held apart from the predictor so the switch-off path can be
    /// compared with it bit for bit. It is the reference, so it is never edited to agree.
    /// </summary>
    private static class Reference
    {
        private const double CrossingToleranceMetres = 0.25;
        private const double AtmosphericStepSeconds = 0.25;
        private const double MinRefineSeconds = 1e-6;
        private const double NoticeableDensity = 1e-7;
        private const double NeverComesDownMargin = 12_000.0;

        public static bool TryPredict(BallisticBody body, double3 positionCci, double3 velocityCci,
                                      double stepSeconds, double maxSeconds, out ImpactPredictor.Impact impact,
                                      Func<double3, double>? terrainRadiusAt, List<double3>? pathCci,
                                      ImpactPredictor.Drag? drag)
        {
            impact = default;
            pathCci?.Clear();

            if (!body.IsUsable) return false;
            if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;
            if (!(stepSeconds > 0.0) || !(maxSeconds > 0.0)) return false;

            double periapsis = Kepler.PeriapsisRadius(body.Mu, positionCci, velocityCci);
            if (double.IsFinite(periapsis) && periapsis > body.SurfaceRadius + NeverComesDownMargin) return false;

            double3 r = positionCci;
            double3 v = velocityCci;
            double t = 0.0;
            double h = stepSeconds;

            pathCci?.Add(r);

            bool everAboveGround = r.Length() > SurfaceUnder(body, r, t, terrainRadiusAt);

            while (t < maxSeconds)
            {
                if (DensityAt(r, drag) > NoticeableDensity) h = Math.Min(h, AtmosphericStepSeconds);

                Step(body, r, v, h, drag, out double3 rNext, out double3 vNext);
                double tNext = t + h;

                if (!Vec.IsFinite(rNext) || !Vec.IsFinite(vNext)) return false;

                bool below = rNext.Length() <= SurfaceUnder(body, rNext, tNext, terrainRadiusAt);

                if (below && everAboveGround)
                {
                    double depth = SurfaceUnder(body, rNext, tNext, terrainRadiusAt) - rNext.Length();

                    if (depth > CrossingToleranceMetres && h > MinRefineSeconds)
                    {
                        h *= 0.5;
                        continue;
                    }

                    impact = new ImpactPredictor.Impact(rNext, body.UncarryCci(rNext, tNext), vNext, tNext);
                    pathCci?.Add(rNext);
                    return true;
                }

                if (below)
                {
                    if (!everAboveGround && Vec.Dot(rNext, vNext) <= 0.0) return false;
                }
                else
                {
                    everAboveGround = true;
                }

                r = rNext;
                v = vNext;
                t = tNext;
                pathCci?.Add(r);

                if (pathCci is { Count: > 4096 }) pathCci.RemoveAt(pathCci.Count - 1);
            }

            return false;
        }

        private static double SurfaceUnder(BallisticBody body, double3 pointCci, double seconds,
                                           Func<double3, double>? terrainRadiusAt)
        {
            if (terrainRadiusAt is null) return body.SurfaceRadius;
            if (pointCci.Length() > body.SurfaceRadius + NeverComesDownMargin) return body.SurfaceRadius;

            double radius = terrainRadiusAt(body.UncarryCci(pointCci, seconds));
            return double.IsFinite(radius) && radius > 0.0 ? radius : body.SurfaceRadius;
        }

        private static double DensityAt(double3 pointCci, ImpactPredictor.Drag? drag)
        {
            if (drag is not { } air) return 0.0;

            double density = air.DensityRatioAt(pointCci);
            return double.IsFinite(density) && density > 0.0 ? density : 0.0;
        }

        private static double3 Accel(BallisticBody body, double3 r, double3 v, ImpactPredictor.Drag? drag)
        {
            double3 accel = body.GravityCci(r);

            double density = DensityAt(r, drag);
            if (density <= 0.0 || drag is not { } air) return accel;

            return accel - Medium.Drag(v - body.GroundVelocityCci(r), air.Munition, density);
        }

        private static void Step(BallisticBody body, double3 r, double3 v, double h, ImpactPredictor.Drag? drag,
                                 out double3 rNext, out double3 vNext)
        {
            double3 k1v = Accel(body, r, v, drag);
            double3 k1r = v;

            double3 k2v = Accel(body, r + k1r * (h * 0.5), v + k1v * (h * 0.5), drag);
            double3 k2r = v + k1v * (h * 0.5);

            double3 k3v = Accel(body, r + k2r * (h * 0.5), v + k2v * (h * 0.5), drag);
            double3 k3r = v + k2v * (h * 0.5);

            double3 k4v = Accel(body, r + k3r * h, v + k3v * h, drag);
            double3 k4r = v + k3v * h;

            rNext = r + (k1r + k2r * 2.0 + k3r * 2.0 + k4r) * (h / 6.0);
            vNext = v + (k1v + k2v * 2.0 + k3v * 2.0 + k4v) * (h / 6.0);
        }
    }
}
