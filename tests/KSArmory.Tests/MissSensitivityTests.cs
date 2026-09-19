using System.Linq;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// How far the impact moves per metre a second left ungained at cutoff, at the two geometries the
/// batch nights are actually scored on.
///
/// <para>Measurement only. Nothing here asserts an improvement — <c>ErrorBudgetTests</c> asks the
/// same question of the 3,459 km deorbit pickup, and this asks it of the ground-launched shots,
/// because a residual in metres a second says nothing until it is converted into metres.</para>
///
/// <para><b>Each arc is reconstructed from three numbers the flight itself logs</b> — cutoff
/// altitude, downrange distance and flight time. The boost's own downrange travel is not logged,
/// which is worth about three degrees of arrival per 200 km, so no arc here is pinned closer than
/// that.</para>
///
/// <para><b>The arc's sensitivity is not the flown one, and the gap is the point.</b> What the
/// nights measure is the miss <em>after</em> the trim and the aim loop have run, so the arc's
/// figure is the miss a shot would have if nothing corrected it. The flown slopes below say how
/// much of that survives.</para>
/// </summary>
public class MissSensitivityTests(ITestOutputHelper Out)
{
    private const double Mu = DeorbitShot.Mu;
    private const double R = DeorbitShot.R;

    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double GroundMetres(double3 a, double3 b) => DeorbitShot.GroundMetres(a, b);

    /// <summary>
    /// One arc a night flies, as the numbers its own log prints.
    /// </summary>
    /// <param name="Residuals">
    /// What the engines actually left ungained over that night's 96 flights, in metres a second.
    /// The whole point of the sensitivity is to multiply by this.
    /// </param>
    private readonly record struct Flown(string Name, double CutoffAltitude, double DownrangeMetres,
                                         double FlightSeconds, Night Residuals);

    /// <summary>The cutoff residual over one night, as <c>CAPTURE cutoff</c> reported it.</summary>
    private readonly record struct Night(string Name, int Flights, double Median, double Mean, double Worst);

    // 2026-09-01-1042 and 2026-09-01-1445, 96 flights each, read off the CAPTURE cutoff lines.
    private static readonly Night Medium = new("2,000 km", 96, 0.26, 0.338, 1.92);
    private static readonly Night Long = new("12,902 km", 96, 0.14, 0.175, 0.41);

    // The eight rockets in one 2,000 km world do not fly one arc: they cut off between 117 and 182
    // km with flight times from 358 to 563 s, in four pairs. At 12,902 km all eight agree.
    private static Flown[] Geometries =>
    [
        new("2,000 km, lowest arc",  117_000.0, 2_000_000.0,  358.0, Medium),
        new("2,000 km, second arc",  142_000.0, 2_000_000.0,  425.0, Medium),
        new("2,000 km, third arc",   160_000.0, 2_000_000.0,  486.0, Medium),
        new("2,000 km, highest arc", 181_000.0, 2_000_000.0,  563.0, Medium),
        new("12,902 km",             157_000.0, 12_902_000.0, 1881.0, Long),
    ];

    /// <summary>
    /// The arcs, as they come out of the reconstruction. The arrival angle is the check: nothing
    /// here was fitted to it, and the nights fly 17.5 degrees.
    /// </summary>
    [Fact]
    public void WhatTheFlownGeometriesAre()
    {
        foreach (Flown g in Geometries)
        {
            Assert.True(TryArc(g, g.DownrangeMetres, out double3 from, out BallisticArc.Solution arc));

            Out.WriteLine($"{g.Name}:");
            Out.WriteLine($"  cutoff {Vec.Len(from) - R:F0} m up, {Vec.Len(arc.RequiredVelocityCci):F0} m/s, "
                          + $"{arc.FlightSeconds:F0} s of flight");
            Out.WriteLine($"  apogee {(arc.ApogeeRadius - R) / 1000.0:F0} km, "
                          + $"arrives at {arc.ArrivalAngleDeg:F1} deg, "
                          + $"{Vec.Len(arc.ArrivalVelocityCci):F0} m/s");
        }
    }

    /// <summary>
    /// The whole point: metres of impact movement per metre a second at cutoff, per axis, and what
    /// the night's own residual is therefore worth.
    /// </summary>
    [Fact]
    public void HowFarOneMetrePerSecondAtCutoffMovesTheImpact()
    {
        foreach (Flown g in Geometries)
        {
            Assert.True(TryArc(g, g.DownrangeMetres, out double3 from, out BallisticArc.Solution arc));

            double isotropic = Sensitivity(from, arc.RequiredVelocityCci, g.Name);

            Night n = g.Residuals;
            Out.WriteLine($"  {n.Name} night, {n.Flights} flights: "
                          + $"median {n.Median:F2} m/s -> {isotropic * n.Median:F0} m, "
                          + $"mean {n.Mean:F3} -> {isotropic * n.Mean:F0} m, "
                          + $"worst {n.Worst:F2} -> {isotropic * n.Worst / 1000.0:F2} km");
            Out.WriteLine("");
        }
    }

    /// <summary>
    /// The reconstruction's one unknown, priced. The boost's downrange travel is not logged, so the
    /// arc is solved 200 km short as well — which is what says how far these numbers can be
    /// trusted.
    /// </summary>
    [Fact]
    public void NotKnowingWhereTheBoostEndedDoesNotMoveTheAnswer()
    {
        foreach (Flown g in Geometries)
        {
            Assert.True(TryArc(g, g.DownrangeMetres, out double3 fullFrom, out BallisticArc.Solution full));
            Assert.True(TryArc(g, g.DownrangeMetres - 200_000.0, out double3 shortFrom,
                               out BallisticArc.Solution shortArc));

            double a = Sensitivity(fullFrom, full.RequiredVelocityCci, null);
            double b = Sensitivity(shortFrom, shortArc.RequiredVelocityCci, null);

            Out.WriteLine($"{g.Name}: {a:F0} m per m/s at the full range, {b:F0} at 200 km short "
                          + $"-- {100.0 * Math.Abs(b - a) / a:F0}% apart, arrival "
                          + $"{full.ArrivalAngleDeg:F1} vs {shortArc.ArrivalAngleDeg:F1} deg");
        }
    }


    /// <summary>
    /// The arc's own sensitivity against the one the nights actually realise.
    ///
    /// <para>The flown figures are a within-craft least squares of the cutoff residual against the
    /// flight's mean miss, over the same two nights — <b>within</b> craft because the eight rockets
    /// in a world fly different arcs at different aim points, and a pooled fit reads that apart as
    /// a relationship between the two. The interval is a bootstrap over crafts and flights.</para>
    ///
    /// <para>Recorded rather than computed: the logs are not in the repository. Re-derive with
    /// <c>tools/shot-report.py</c> after a night that carries the release summary.</para>
    /// </summary>
    [Fact]
    public void TheLoopAbsorbsMostOfWhatTheCutoffLeaves()
    {
        // 2026-09-01-1042 and 2026-09-01-1445, 96 flights each.
        (string night, double slope, double lo, double hi, double medianMiss)[] flown =
        [
            ("2,000 km",  36.0,   18.0,  87.0,  17.0),
            ("12,902 km", -115.0, -939.0, 519.0, 301.0),
        ];

        foreach ((string night, double slope, double lo, double hi, double medianMiss) in flown)
        {
            // Over every arc that night flew, because the eight rockets of a 2,000 km world cut off
            // between 117 and 182 km and the shallowest of them is half again as sensitive as the
            // steepest. One of the four would describe a quarter of the flights.
            double[] each = [.. Geometries.Where(x => x.Residuals.Name == night).Select(ArcSensitivity)];
            Array.Sort(each);
            double arcSensitivity = each[each.Length / 2];

            Flown g = Array.Find(Geometries, x => x.Residuals.Name == night);
            bool resolved = lo > 0.0 || hi < 0.0;

            Out.WriteLine($"{night}: the arcs move {each[0]:F0}-{each[^1]:F0} m per m/s at cutoff, "
                          + $"median {arcSensitivity:F0}; "
                          + $"the night realises {slope:F0} [97%: {lo:F0}, {hi:F0}]"
                          + (resolved ? $" -- {100.0 * slope / arcSensitivity:F0}% of it survives"
                                      : " -- unresolved, so none of it is visible"));
            Out.WriteLine($"    median residual {g.Residuals.Median:F2} m/s explains "
                          + $"{slope * g.Residuals.Median:F0} m of a {medianMiss:F0} m median miss");
        }
    }
    /// <summary>One geometry's sensitivity, with nothing written out.</summary>
    private double ArcSensitivity(Flown g)
    {
        Assert.True(TryArc(g, g.DownrangeMetres, out double3 from, out BallisticArc.Solution arc));
        return Sensitivity(from, arc.RequiredVelocityCci, null);
    }

    /// <summary>
    /// Root mean square over three axes rather than the worst of them: a residual's direction is
    /// not recorded, so the worst axis describes a shot nobody flew.
    /// </summary>
    private double Sensitivity(double3 from, double3 v, string? label)
    {
        double3 prograde = Vec.Unit(v);
        double3 radial = Vec.Unit(from);
        double3 cross = Vec.Unit(Vec.Cross(radial, prograde));

        (string name, double3 axis)[] axes =
            [("prograde", prograde), ("radial", radial), ("cross-track", cross)];

        if (label is not null) Out.WriteLine($"{label}:");

        double sumOfSquares = 0.0;

        foreach ((string name, double3 axis) in axes)
        {
            const double delta = 0.5;
            double perMetre = GroundMetres(DeorbitShot.Land(from, v + axis * delta),
                                           DeorbitShot.Land(from, v - axis * delta)) / (2.0 * delta);

            sumOfSquares += perMetre * perMetre;

            if (label is not null) Out.WriteLine($"  {name,-12}: {perMetre:F0} m per m/s");
        }

        double isotropic = Math.Sqrt(sumOfSquares / axes.Length);
        if (label is not null) Out.WriteLine($"  {"rms",-12}: {isotropic:F0} m per m/s");
        return isotropic;
    }

    /// <summary>
    /// The arc that departs the logged cutoff altitude and arrives the logged distance downrange in
    /// the logged time.
    ///
    /// <para>The aim point is un-carried by the flight time before it goes in, because
    /// <see cref="BallisticArc.TrySolve"/> carries it forward itself — handing in an already-carried
    /// point applies the planet's turn twice.</para>
    /// </summary>
    private static bool TryArc(Flown g, double arcRangeMetres, out double3 from,
                               out BallisticArc.Solution arc)
    {
        from = new double3(R + g.CutoffAltitude, 0, 0);

        double theta = arcRangeMetres / R;
        double3 arrival = new(R * Math.Cos(theta), R * Math.Sin(theta), 0);

        return BallisticArc.TrySolve(Earth, from, Earth.UncarryCci(arrival, g.FlightSeconds),
                                     g.FlightSeconds, out arc);
    }
}
