using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The free-flight problem, which is the whole of an ICBM's targeting: everything after burnout is
/// a fall, and the fall is what has to arrive.
///
/// <para>Two things are checked separately on purpose. That the transfer solver returns a velocity
/// is worth nothing on its own — the test that matters is <em>flying</em> that velocity with an
/// integrator that shares none of the solver's maths and finding the target under it. A closed form
/// checked against itself agrees with itself.</para>
/// </summary>
public class BallisticArcTests
{
    private const double EarthMu = 3.986004418e14;
    private const double EarthRadius = 6_371_000.0;
    private const double EarthSpin = 7.2921159e-5;

    private static readonly BallisticBody Earth =
        new(EarthMu, EarthRadius, new double3(0, 0, 1), EarthSpin);

    /// <summary>A non-rotating planet of the same size, for isolating what the spin is worth.</summary>
    private static readonly BallisticBody Still =
        new(EarthMu, EarthRadius, new double3(0, 0, 1), 0.0);

    /// <summary>A point on the equator at a given longitude, at burnout altitude.</summary>
    private static double3 Equator(double longitudeRad, double altitude = 0.0)
        => new((EarthRadius + altitude) * Math.Cos(longitudeRad),
               (EarthRadius + altitude) * Math.Sin(longitudeRad), 0.0);

    private static double SurfaceRange(double3 a, double3 b)
        => EarthRadius * Vec.AngleBetween(a, b);

    [Fact]
    public void TheSolvedVelocityFlownForwardArrivesWhereItSaidItWould()
    {
        double3 from = Equator(0.0, 1000.0);
        double3 aim = Equator(0.7848);                       // ~5000 km east

        Assert.True(BallisticArc.TrySolve(Still, from, aim, 1200.0, out BallisticArc.Solution s));

        Assert.True(ImpactPredictor.TryPredict(Still, from, s.RequiredVelocityCci, 2.0, 7200.0,
                                               out ImpactPredictor.Impact hit));

        double miss = Vec.Len(hit.GroundFixedPointCci - aim);
        Assert.True(miss < 2000.0, $"integrated arc missed the solved aim point by {miss:F0} m");
        Assert.True(Math.Abs(hit.Seconds - 1200.0) < 5.0, $"arrived at {hit.Seconds:F1} s, not 1200");
    }

    [Fact]
    public void TheCheapestShotAcrossFiveThousandKilometresArrivesOnTheTarget()
    {
        double3 from = Equator(0.0, 1000.0);
        double3 aim = Equator(0.7848);

        double3 launchFrame = Earth.GroundVelocityCci(from);

        Assert.True(BallisticArc.TryCheapest(Earth, from, launchFrame, aim, out BallisticArc.Solution s));

        Assert.True(ImpactPredictor.TryPredict(Earth, from, s.RequiredVelocityCci, 2.0, 7200.0,
                                               out ImpactPredictor.Impact hit));

        double miss = Vec.Len(hit.GroundFixedPointCci - aim);
        Assert.True(miss < 5000.0, $"missed by {miss / 1000.0:F1} km after {hit.Seconds:F0} s");
    }

    /// <summary>
    /// The one that separates a correct solve from a plausible one. Over a half-hour flight the
    /// aim point is carried thousands of kilometres by the planet's own turn, and a solver that
    /// aims at where it is now lands nowhere near it.
    /// </summary>
    [Fact]
    public void IgnoringThePlanetsRotationMissesByHundredsOfKilometres()
    {
        double3 from = Equator(0.0, 1000.0);
        double3 aim = Equator(1.4);                          // ~8900 km east

        Assert.True(BallisticArc.TryCheapest(Earth, from, Earth.GroundVelocityCci(from), aim,
                                             out BallisticArc.Solution good));

        // The same shot solved as though the target stood still in inertial space, then flown in
        // the world that actually turns.
        Assert.True(Lambert.TrySolve(from, aim, good.FlightSeconds, EarthMu, out Lambert.Transfer naive));

        Assert.True(ImpactPredictor.TryPredict(Earth, from, naive.DepartureVelocityCci, 2.0, 7200.0,
                                               out ImpactPredictor.Impact hit));

        double miss = SurfaceRange(hit.GroundFixedPointCci, aim);
        Assert.True(miss > 200_000.0,
                    $"a solve that ignores the spin should miss badly; it missed by {miss / 1000.0:F0} km");
    }

    [Fact]
    public void ALoftedShotFliesHigherAndCostsMoreThanTheCheapestOne()
    {
        double3 from = Equator(0.0, 1000.0);
        double3 aim = Equator(0.7848);
        double3 frame = Earth.GroundVelocityCci(from);

        Assert.True(BallisticArc.TryCheapest(Earth, from, frame, aim, out BallisticArc.Solution cheap));
        Assert.True(BallisticArc.TryCheapest(Earth, from, frame, aim, out BallisticArc.Solution lofted, loft: 1.6));

        Assert.True(lofted.ApogeeRadius > cheap.ApogeeRadius,
                    $"lofted apogee {lofted.ApogeeRadius:F0} vs cheapest {cheap.ApogeeRadius:F0}");
        Assert.True(Vec.Len(lofted.VelocityToGain(frame)) > Vec.Len(cheap.VelocityToGain(frame)),
                    "the cheapest shot must be the cheapest one");
    }

    /// <summary>
    /// A depressed shot flattens until the arc's low point is inside the planet. That is a line
    /// drawn through the ground, and every other number about it looks entirely reasonable — so it
    /// has to be refused here rather than noticed later.
    /// </summary>
    [Fact]
    public void AShotFlatEnoughToPassThroughThePlanetIsRefused()
    {
        double3 from = Equator(0.0, 1000.0);
        double3 aim = Equator(2.4);                          // most of the way round
        double3 frame = Earth.GroundVelocityCci(from);

        Assert.True(BallisticArc.TryCheapest(Earth, from, frame, aim, out BallisticArc.Solution cheap));
        Assert.True(cheap.LowestRadius >= EarthRadius - 1.0);

        Assert.False(BallisticArc.TryCheapest(Earth, from, frame, aim, out _, loft: 0.25),
                     "a quarter of the cheapest flight time is a chord through the planet");
    }

    [Fact]
    public void TheArcClearsTheSurfaceAllTheWayAlongIt()
    {
        double3 from = Equator(0.0, 1000.0);
        double3 aim = Equator(1.2);

        Assert.True(BallisticArc.TryCheapest(Earth, from, Earth.GroundVelocityCci(from), aim,
                                             out BallisticArc.Solution s));

        List<double3> path = [];
        Assert.True(ImpactPredictor.TryPredict(Earth, from, s.RequiredVelocityCci, 5.0, 7200.0,
                                               out _, pathCci: path));

        // The last sample is the impact itself, which is on the surface by definition.
        for (int i = 1; i < path.Count - 1; i++)
        {
            Assert.True(path[i].Length() > EarthRadius,
                        $"sample {i} of {path.Count} was {EarthRadius - path[i].Length():F0} m underground");
        }
    }

    [Fact]
    public void ADegenerateAntipodalShotIsRefusedRatherThanGuessed()
    {
        double3 from = Equator(0.0, 1000.0);
        double3 antipode = Equator(Math.PI);

        Assert.False(Lambert.TrySolve(from, antipode, 1800.0, EarthMu, out _),
                     "no plane is determined through two antipodal points and the centre");
    }

    /// <summary>
    /// Which way the planet turns, pinned against physics rather than against the rest of this
    /// file. The solve carries the aim point forward and the prediction carries it back, so the two
    /// agree with each other whichever way round the sign is — a flipped convention cancels itself
    /// and every arrival test still passes. What cannot cancel is that a point on the ground moves
    /// <em>east</em>.
    /// </summary>
    [Fact]
    public void TheGroundMovesEastAndTheAimPointIsCarriedWithIt()
    {
        double3 onTheEquator = Equator(0.0);                 // +X, so east is +Y

        double3 groundVelocity = Earth.GroundVelocityCci(onTheEquator);
        Assert.True(groundVelocity.Y > 0.0,
                    $"a point at longitude zero must move east, not {groundVelocity}");
        Assert.True(Math.Abs(groundVelocity.Length() - EarthSpin * EarthRadius) < 1.0);

        double3 carried = Earth.CarryCci(onTheEquator, 600.0);
        Assert.True(carried.Y > 0.0, "ten minutes of rotation must carry it east too");

        Assert.True(Vec.Len(Earth.UncarryCci(carried, 600.0) - onTheEquator) < 1e-6,
                    "carrying back must be the exact inverse");
    }

    /// <summary>
    /// The consequence for a shot, and the reason the sign matters: an eastward launch is chasing
    /// a target that is running away, so the arc must reach beyond where the map says it is.
    /// </summary>
    [Fact]
    public void AnEastwardShotArrivesBeyondWhereTheTargetStartedOut()
    {
        double3 from = Equator(0.0, 1000.0);
        double3 aim = Equator(0.7848);

        Assert.True(BallisticArc.TryCheapest(Earth, from, Earth.GroundVelocityCci(from), aim,
                                             out BallisticArc.Solution s));

        double aimLongitude = Math.Atan2(aim.Y, aim.X);
        double arrivalLongitude = Math.Atan2(s.ImpactCciAtArrival.Y, s.ImpactCciAtArrival.X);

        Assert.True(arrivalLongitude > aimLongitude,
                    "the arc has to arrive east of the aim point, because the aim point moved east");
        Assert.True(EarthRadius * (arrivalLongitude - aimLongitude) > 100_000.0,
                    "over a flight this long the carry is hundreds of kilometres");
    }
}
