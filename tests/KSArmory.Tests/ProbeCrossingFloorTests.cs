using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Where a kicked warhead lands on ground that is not level, and how much of that is the release
/// probe's own crossing rather than the round's fall.
/// </summary>
/// <remarks>
/// <para><see cref="WalkFloorTests"/> measures the <em>walk</em> — the round against its own
/// prediction — which <c>docs/ACCURACY-PLAN.md</c> 3ei showed is not accuracy. This measures the
/// landing: the release probe's impact, the miss kick solved from it, the real
/// <see cref="Slug"/> flown through <see cref="RoundDriver"/>, and where it stops against the aim.
/// The chain is <see cref="KickThroughTheAirTests"/>' with a height field under it.</para>
///
/// <para>A planet at the origin and no spin: every term a frame carrier could add is common to the
/// whole comparison, which is one release flown two ways.</para>
/// </remarks>
public class ProbeCrossingFloorTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 0.0);

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    private static MunitionProfile Constant => Arsenal.ReentryVehicleMk21;
    private static MunitionProfile Shape => Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);

    /// <summary>
    /// <see cref="WalkFloorTests"/>' surface: seven octaves from 600 m down to 9 m, read at a
    /// direction packed to <c>float3</c> exactly as <c>Celestial.cs:833</c> reads it.
    /// </summary>
    private static double Surface(double3 point, double slope, bool packed)
    {
        double3 dir = Vec.Unit(point);
        if (packed) dir = new double3((float)dir.X, (float)dir.Y, (float)dir.Z);
        double along = R * Math.Atan2(dir.Y, dir.X);

        double height = 0.0, slopeSum = 0.0;
        double wavelength = 600.0, amplitude = 1.0;
        for (int i = 0; i < 7; i++)
        {
            height += amplitude * Math.Sin(2.0 * Math.PI * along / wavelength + i * 1.7);
            slopeSum += amplitude * 2.0 * Math.PI / wavelength;
            wavelength *= 0.5;
            amplitude *= 0.5;
        }

        return R + height * (slope / slopeSum);
    }

    private sealed class Ground(double slope, bool packed) : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = Surface(positionEcl, slope, packed);
            return true;
        }
    }

    private static double3 Release(out double3 from, double nudge, double range)
    {
        from = new double3(R + 877_000.0, 0, 0);
        double3 target = new(R * Math.Cos(range / R), R * Math.Sin(range / R), 0.0);
        double speed = Math.Sqrt(Mu / (R + 877_000.0));
        double3 coasting = new(0, speed, 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, coasting, target, out BallisticArc.Solution s));
        return s.RequiredVelocityCci + Vec.Unit(s.RequiredVelocityCci) * nudge;
    }

    private static bool TryProbe(MunitionProfile warhead, double3 p, double3 v, double slope, bool packed,
                                 out ImpactPredictor.Impact hit)
        => ImpactPredictor.TryPredict(Earth, p, v, 2.0, 12_000.0, out hit,
                                      q => Surface(q, slope, packed), null,
                                      new ImpactPredictor.Drag(DensityAt, warhead),
                                      atmosphericStepSeconds: 0.25, stopOnTheSurface: true);

    /// <summary>
    /// The prediction's crossing walked onto the terrain along its own arrival, which is what
    /// <see cref="Slug.StopOnTheTerrain"/> already does for the round. Iterated to convergence, so
    /// this is the ceiling on what such a fix could be worth rather than one secant step of it.
    /// </summary>
    private static double3 OnTheTerrain(in ImpactPredictor.Impact hit, double slope, bool packed)
    {
        double3 point = hit.PointCci;
        double3 along = Vec.Unit(hit.VelocityCci);

        for (int i = 0; i < 8; i++)
        {
            double altitude = Vec.Len(point) - Surface(point, slope, packed);
            double sink = -Vec.Dot(along, Vec.Unit(point));
            if (!(sink > 1e-6)) break;

            point += along * (altitude / sink);
        }

        return point;
    }

    /// <summary>How far above the surface under it a point sits, in millimetres.</summary>
    private static double AltitudeMm(double3 point, double slope, bool packed)
        => (Vec.Len(point) - Surface(point, slope, packed)) * 1000.0;

    // The round as IcbmComputer configures a released warhead.
    private static double3 FlyTheRound(MunitionProfile warhead, double3 p, double3 v, double slope, bool packed)
    {
        const double Frame = 0.025;

        Slug round = new(p, v, null, 1, p, Vec.Zero)
        {
            Munition = warhead,
            ResampleGroundNearImpact = true,
            SecondOrder = true,
            DragAtMidpointVelocity = true,
            StopOnTheTerrain = true,
            AirVelocityAtOwnSubStep = true,
            GroundQueryAtOwnEpoch = true,
        };

        RoundFields fields = new(
            GravityAt: (q, _) => Vec.Unit(-q) * (Mu / Vec.Len2(q)),
            AirDensityAt: (q, _) => DensityAt(q),
            Ground: new Ground(slope, packed),
            AirVelocityAt: (_, _) => Vec.Zero);

        for (int i = 0; i < 2_000_000 && round.State == RoundState.Flying; i++)
        {
            RoundDriver.Fly(round, Frame, null,
                            Vec.Unit(-round.PositionEcl) * (Mu / Vec.Len2(round.PositionEcl)),
                            Vec.Zero, Vec.Zero, warhead, 0.0, fields);
        }

        Assert.True(round.HitGround);
        return round.PositionEcl;
    }

    /// <summary>What one release lands at, and what the probe it was kicked off did.</summary>
    private readonly record struct Shot(double Down, double Cross, double ProbeAltitudeMm, double Cot);

    /// <summary>
    /// One release: the probe, an aim a stated miss away from it, the miss kick solved against the
    /// ground, the round flown, and where it stopped against that aim.
    /// </summary>
    private static Shot Fire(MunitionProfile warhead, double nudge, double slope, bool packed, double missMetres,
                             double range, bool refineTheProbe)
    {
        double3 v = Release(out double3 from, nudge, range);

        Assert.True(TryProbe(warhead, from, v, slope, packed, out ImpactPredictor.Impact hit));
        Assert.True(ArrivalFrame.TryAt(hit.PointCci, hit.VelocityCci, out ArrivalFrame frame));

        // The aim loop stops with the predicted impact a little off the target, and the kick's whole
        // job is to give that back. Square to up at the impact, as a miss on the ground is measured.
        double3 aim = hit.GroundFixedPointCci - frame.Downrange * missMetres;

        double3 impact = refineTheProbe ? OnTheTerrain(hit, slope, packed) : hit.GroundFixedPointCci;

        ReleaseFocus.Separation kick = ReleaseFocus.Kick(
            Earth, from, v, hit.Seconds, Vec.Zero, Vec.Zero, focusRing: false, cancelSpin: false,
            new ReleaseFocus.ProbeMiss(impact, aim, q => Surface(q, slope, packed)));

        Assert.Equal(ReleaseFocus.MissOutcome.Cancelled, kick.Miss);

        double3 landed = FlyTheRound(warhead, from, v + kick.KickCci, slope, packed);
        double3 off = frame.Resolve(landed - aim) * 1000.0;

        double gamma = Math.Asin(Math.Clamp(-Vec.Dot(Vec.Unit(hit.VelocityCci), Vec.Unit(hit.PointCci)), -1.0, 1.0));

        return new Shot(off.Y, off.Z, AltitudeMm(hit.PointCci, slope, packed), 1.0 / Math.Tan(gamma));
    }

    private static (double Mean, double Sd, double MeanProbe, double SdProbe, double Corr, double Cot)
        Sample(int seed, int n, MunitionProfile warhead, double slope, bool packed, double missMetres,
               double range, bool refineTheProbe)
    {
        Random rng = new(seed);
        List<double> down = [], probe = [];
        double cot = 0.0;

        for (int i = 0; i < n; i++)
        {
            Shot shot = Fire(warhead, (rng.NextDouble() - 0.5) * 4.0, slope, packed, missMetres, range,
                             refineTheProbe);
            down.Add(shot.Down);
            probe.Add(shot.ProbeAltitudeMm * shot.Cot);
            cot = shot.Cot;
        }

        double m = down.Average(), mp = probe.Average();
        double sd = Math.Sqrt(down.Sum(x => (x - m) * (x - m)) / down.Count);
        double sdp = Math.Sqrt(probe.Sum(x => (x - mp) * (x - mp)) / probe.Count);
        double cov = down.Zip(probe, (a, b) => (a - m) * (b - mp)).Sum() / down.Count;

        return (m, sd, mp, sdp, cov / Math.Max(sd * sdp, 1e-12), cot);
    }

    private void Say(string what, (double Mean, double Sd, double MeanProbe, double SdProbe, double Corr, double Cot) s)
        => Out.WriteLine($"{what,-34} landing {s.Mean,8:F2} +/- {s.Sd,7:F2} mm    "
                         + $"probe's own stop x cot {s.MeanProbe,8:F2} +/- {s.SdProbe,7:F2} mm    r {s.Corr,6:F2}");

    /// <summary>
    /// The site's own ground, at both arrivals the mod has flown. The flown seat with slope 0.122
    /// groups at 5.8-6.4 mm sd downrange against 2.0-2.8 on the flat seats of the same shots.
    /// </summary>
    [Fact]
    public void TheLandingScattersWithTheGroundsSlope()
    {
        const int N = 24;

        Out.WriteLine($"{N} random release states each, 0.2 m of probe miss, 1,500 km\n");

        Dictionary<double, double> scatter = [];

        foreach (double slope in new[] { 0.0, 0.03, 0.122, 0.35 })
        {
            var s = Sample(11, N, Constant, slope, true, 0.2, 1_500_000.0, false);
            Say($"constant, slope {slope:F3}", s);
            scatter[slope] = s.Sd;
            if (slope == 0.122) Assert.True(s.Corr > 0.8, $"the landing follows the probe's own stop at r {s.Corr:F2}");
        }

        Out.WriteLine("");
        Say("constant, 0.122, exact direction", Sample(11, N, Constant, 0.122, false, 0.2, 1_500_000.0, false));
        Say("shape, 0.122, packed", Sample(11, N, Shape, 0.122, true, 0.2, 1_500_000.0, false));
        Say("shape, 0.122, exact direction", Sample(11, N, Shape, 0.122, false, 0.2, 1_500_000.0, false));

        Assert.True(scatter[0.0] < 0.01, $"level ground scatters {scatter[0.0]:F3} mm");
        Assert.True(scatter[0.122] > 2.0, $"the site's own slope scatters only {scatter[0.122]:F3} mm");
    }

    /// <summary>What stopping the prediction on the terrain, rather than on the chord of its own bracket, is worth.</summary>
    [Fact]
    public void StoppingThePredictionOnTheTerrainTakesTheScatterOut()
    {
        const int N = 24;

        Out.WriteLine($"{N} random release states each, 0.2 m of probe miss, 1,500 km\n");

        // Flat first: there the chord IS the surface, so the refinement has to change nothing.
        Say("constant, 0.000, as it ships", Sample(11, N, Constant, 0.0, true, 0.2, 1_500_000.0, false));
        Say("constant, 0.000, probe on the terrain", Sample(11, N, Constant, 0.0, true, 0.2, 1_500_000.0, true));
        Out.WriteLine("");

        foreach (double slope in new[] { 0.122, 0.35 })
        {
            foreach ((string name, MunitionProfile warhead) in new[] { ("constant", Constant), ("shape", Shape) })
            {
                var ships = Sample(11, N, warhead, slope, true, 0.2, 1_500_000.0, false);
                var fixt = Sample(11, N, warhead, slope, true, 0.2, 1_500_000.0, true);
                Say($"{name}, {slope:F3}, as it ships", ships);
                Say($"{name}, {slope:F3}, probe on the terrain", fixt);

                if (name == "constant" && slope == 0.122)
                {
                    Assert.True(fixt.Sd < 0.5 * ships.Sd,
                                $"the refinement left {fixt.Sd:F3} mm of {ships.Sd:F3}");
                    Assert.True(Math.Abs(fixt.Corr) < 0.3,
                                $"the landing still follows the probe's own stop at r {fixt.Corr:F2}");
                }
            }

            Out.WriteLine("");
        }
    }

    /// <summary>And with the miss the kick has to give back, which is what scales the injection.</summary>
    [Fact]
    public void TheScatterDoesNotFollowTheMissBeingCancelled()
    {
        const int N = 16;

        Out.WriteLine($"{N} random release states each, slope 0.122, 1,500 km\n");

        foreach (double miss in new[] { 0.0, 0.2, 2.0 })
        {
            Say($"constant, {miss:F1} m of miss", Sample(11, N, Constant, 0.122, true, miss, 1_500_000.0, false));
        }
    }

    /// <summary>
    /// What is left once the prediction stops on the terrain: the round's own bracket, which is a
    /// sub-step of track rather than the fraction of a metre the prediction refines to.
    /// </summary>
    [Fact]
    public void WhatIsLeftIsTheRoundsOwnSubStep()
    {
        const int N = 16;

        Out.WriteLine($"{N} random release states each, 0.2 m of probe miss, 1,500 km\n");

        foreach (double slope in new[] { 0.122, 0.35 })
        {
            foreach (double ms in new[] { 1.0, 0.25 })
            {
                MunitionProfile warhead = Arsenal.RoundAtSubStep(Constant, ms / 1000.0);
                Say($"constant at {ms:F2} ms, {slope:F3}, as it ships",
                    Sample(11, N, warhead, slope, true, 0.2, 1_500_000.0, false));
                Say($"constant at {ms:F2} ms, {slope:F3}, probe on the terrain",
                    Sample(11, N, warhead, slope, true, 0.2, 1_500_000.0, true));
            }

            Out.WriteLine("");
        }
    }
}
