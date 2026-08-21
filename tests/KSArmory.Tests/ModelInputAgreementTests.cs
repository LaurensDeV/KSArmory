using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether the round and the <see cref="ImpactPredictor"/> that predicts it are handed the same
/// world — as opposed to <c>ProbeGapTests</c>, which hands them one world on purpose and prices
/// what the two <em>flight models</em> then do differently.
///
/// <para>In flight the two take their gravity, their air, their air's motion and their ground from
/// different code paths. Four of those five agree exactly, by construction rather than by luck, and
/// the reasons are in <c>docs/MIRV-NEXT.md</c>. The fifth is here because it is the one term that
/// is <b>identically zero in every headless rig and never zero in the game</b>: a round is
/// integrated in <c>Ecl</c> about a planet KSA is moving along its own orbit, and the prediction is
/// integrated in <c>Cci</c> about a planet at rest.</para>
///
/// <para><b>Measurement only.</b> Nothing here proposes a change.</para>
/// </summary>
public class ModelInputAgreementTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;
    private static MunitionProfile Warhead => DeorbitShot.Warhead;

    /// <summary>
    /// KSA's own solar mass times its own gravitational constant — <c>AstronomicalTemplate.M_SUN</c>
    /// and <c>IParentBody.Mu</c>, which is <c>Mass * 6.6743E-11</c>.
    /// </summary>
    private const double SolarMu = 1.989e30 * 6.6743e-11;

    /// <summary>Earth's semi-major axis from the shipped <c>Astronomicals.xml</c>.</summary>
    private const double EarthOrbitRadiusMetres = 1.495396277103892e11;

    /// <summary>
    /// How hard KSA's Earth falls toward its Sun, which is the acceleration the round's frame has
    /// and the prediction's has not.
    ///
    /// <para>Eccentricity 0.0166 swings it +4.3%/-3.3% across the year; the tide across a planet's
    /// radius is 0.009% of it, so the field is uniform over everything a round can reach and the
    /// whole term is a rigid translation of the round's trajectory.</para>
    /// </summary>
    private const double PlanetFall = SolarMu / (EarthOrbitRadiusMetres * EarthOrbitRadiusMetres);

    /// <summary>The release state <c>ProbeGapTests</c> flies, so the two budgets are comparable.</summary>
    private static void ReleaseState(out double3 fromCci, out double3 velocityCci)
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out fromCci, out double3 _);

        velocityCci = arc.RequiredVelocityCci
                      + Vec.Unit(arc.RequiredVelocityCci) * Warhead.LaunchSpeed;
    }

    /// <summary>The arrival's own axes: local up, the ground track, and square to both.</summary>
    private static void ArrivalFrame(double3 fromCci, double3 velocityCci,
                                     out double3 up, out double3 along, out double3 cross)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));

        up = Vec.Unit(hit.PointCci);
        along = Vec.Unit(hit.VelocityCci - up * Vec.Dot(hit.VelocityCci, up));
        cross = Vec.Cross(up, along);
    }

    /// <summary>How far past the reference a point lies along the track, signed.</summary>
    private static double Downrange(double3 referenceCci, double3 pointCci, double3 alongCci)
    {
        double metres = DeorbitShot.GroundMetres(referenceCci, pointCci);
        return Vec.Dot(pointCci - referenceCci, alongCci) >= 0.0 ? metres : -metres;
    }

    private static double3 Fly(double3 fromCci, double3 velocityCci, double3 bodyAccelCci)
        => DeorbitShot.FlyTheRoundAsWarped(fromCci, velocityCci, DeorbitShot.ScenarioWarp,
                                           default, null, bodyAccelCci).GroundFixed;

    /// <summary>
    /// The term itself, resolved onto the arrival's own axes.
    ///
    /// <para>The response is linear in the acceleration — pinned by
    /// <see cref="TheShiftIsLinearInTheBodysAccelerationSoItScalesAsTheSquareOfTheFlight"/> — so
    /// three numbers are the whole sensitivity and the worst case over every direction the Sun
    /// could lie in is the length of the vector they make.</para>
    /// </summary>
    [Fact]
    public void WhatThePlanetsOwnFallTowardTheSunMovesTheImpactBy()
    {
        ReleaseState(out double3 from, out double3 v);
        ArrivalFrame(from, v, out double3 up, out double3 along, out double3 cross);

        double3 still = Fly(from, v, Vec.Zero);

        (string What, double3 Dir)[] axes =
        [
            ("radially outward", up),
            ("along the track", along),
            ("across the track", cross),
        ];

        Out.WriteLine($"KSA's Earth falls at {PlanetFall * 1000.0:F3} mm/s^2, which over the "
                      + $"{DeorbitShot.NominalFrame:F4} s frames of a "
                      + $"{Seconds(from, v):F0} s coast is "
                      + $"{0.5 * PlanetFall * Seconds(from, v) * Seconds(from, v):F0} m of drift");
        Out.WriteLine("the Sun lying...            impact moves");

        double sum2 = 0.0;
        foreach ((string what, double3 dir) in axes)
        {
            // The Sun pulls the planet one way, so the round is left behind the other.
            double moved = Downrange(still, Fly(from, v, dir * PlanetFall), along);
            sum2 += moved * moved;

            Out.WriteLine($"  {what,-24}{moved,8:F0} m downrange");
        }

        Out.WriteLine($"  {"worst over any direction",-24}{Math.Sqrt(sum2),8:F0} m");
    }

    /// <summary>
    /// That the shift really is proportional to the acceleration, so the figure above can be scaled
    /// to a flight of any length rather than re-flown for each.
    ///
    /// <para>The drift is <c>a*T^2/2</c>, so doubling the acceleration is the same lever as
    /// multiplying the coast by the square root of two — which is what makes this the one term in
    /// the budget that grows without bound as the range does.</para>
    /// </summary>
    [Fact]
    public void TheShiftIsLinearInTheBodysAccelerationSoItScalesAsTheSquareOfTheFlight()
    {
        ReleaseState(out double3 from, out double3 v);
        ArrivalFrame(from, v, out double3 up, out double3 along, out double3 _);

        double3 still = Fly(from, v, Vec.Zero);

        double one = Downrange(still, Fly(from, v, up * PlanetFall), along);
        double two = Downrange(still, Fly(from, v, up * (2.0 * PlanetFall)), along);

        Out.WriteLine($"radially, at 1x {one:F0} m and at 2x {two:F0} m — {two / one:F3}x");

        Assert.InRange(two / one, 1.9, 2.1);
    }

    /// <summary>
    /// The same term against range, which is the only thing about it a shot can choose.
    ///
    /// <para>Reported as the drift itself rather than as impact, because the direction the Sun
    /// happens to lie in decides how much of it lands downrange and that is not a property of the
    /// weapon. What is a property of the weapon is that it is quadratic: a shot twice as long
    /// carries four times the term.</para>
    /// </summary>
    [Fact]
    public void HowTheTermGrowsWithTheCoast()
    {
        Out.WriteLine("coast    drift");

        foreach (double seconds in new[] { 100.0, 300.0, 497.0, 800.0, 1200.0, 1800.0 })
        {
            Out.WriteLine($"{seconds,5:F0} s{0.5 * PlanetFall * seconds * seconds,9:F0} m");
        }

        Out.WriteLine($"({Warhead.MaxFlightSeconds:F0} s is {Warhead.DisplayName}'s "
                      + "MaxFlightSeconds, so the last row is the weapon's own limit)");
    }

    private static double Seconds(double3 fromCci, double3 velocityCci)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));
        return hit.Seconds;
    }
}
