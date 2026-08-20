using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Six warheads off one aim, and what the tube cant does to them.
///
/// <para>A bus's tubes are canted six degrees at six clock positions, so each warhead is ejected on
/// its own vector. There is one aim for all six, so no aim correction can remove it — the bus has
/// to turn between releases and put each tube in turn on the same line.</para>
/// </summary>
public class MirvSpreadTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    /// <summary>A deorbit from 200 km arriving about 2,700 km downrange — the flown shot.</summary>
    private static BallisticArc.Solution Deorbit(out double3 from)
    {
        from = new double3(R + 200_000.0, 0, 0);
        double range = 2_700_000.0;
        double3 target = new(R * Math.Cos(range / R), R * Math.Sin(range / R), 0);
        double3 circular = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);

        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target, out BallisticArc.Solution s));
        return s;
    }

    /// <summary>
    /// How far apart the six land, given the bus's attitude and whether each tube is turned onto
    /// the mean before it fires.
    /// </summary>
    private static double SpreadMetres(double3 fromCci, double3 velocityCci, doubleQuat busAttitude,
                                       bool repoint, ITestOutputHelper? log = null)
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        Tube[] tubes = CantedRing.Tubes;

        double3[] axes = new double3[tubes.Length];
        for (int i = 0; i < tubes.Length; i++) axes[i] = Vec.Unit(busAttitude * tubes[i].Direction);

        double3 reference = ReleasePointing.ReferenceAxis(axes);
        double3[] landed = new double3[tubes.Length];

        for (int tube = 0; tube < tubes.Length; tube++)
        {
            // Through the function under test, not by asserting the answer: a re-pointed bus throws
            // along the reference because it has been turned there, which is the claim.
            double3 thrown = repoint
                ? Vec.Unit(ReleasePointing.Repoint(axes[tube], reference) * axes[tube])
                : axes[tube];

            Assert.True(ImpactPredictor.TryPredict(Earth, fromCci,
                                                   velocityCci + (thrown * warhead.LaunchSpeed),
                                                   2.0, 20_000.0, out ImpactPredictor.Impact hit,
                                                   null, null,
                                                   new ImpactPredictor.Drag(DensityAt, warhead)),
                        $"tube {tube + 1} never came down");

            landed[tube] = hit.GroundFixedPointCci;
        }

        double worst = 0.0;
        for (int a = 0; a < landed.Length; a++)
        {
            for (int b = a + 1; b < landed.Length; b++)
            {
                worst = Math.Max(worst, R * Vec.AngleBetween(landed[a], landed[b]));
            }
        }

        log?.WriteLine($"  {(repoint ? "re-pointed" : "as canted ")}: {worst:F0} m across the six");
        return worst;
    }

    /// <summary>The bus holds its cutoff line, which on a deorbit is retrograde.</summary>
    private static doubleQuat Attitude(double3 velocityCci, double rollTurns)
    {
        double3 nose = -Vec.Unit(velocityCci);
        doubleQuat onto = Vec.RotationFromTo(new double3(1, 0, 0), nose);
        return doubleQuat.CreateFromAxisAngle(nose, rollTurns * 2.0 * Math.PI) * onto;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void SixWarheadsOffOneAimSpreadOverAKilometre(double roll)
    {
        BallisticArc.Solution arc = Deorbit(out double3 from);
        double spread = SpreadMetres(from, arc.RequiredVelocityCci,
                                     Attitude(arc.RequiredVelocityCci, roll), false, Out);

        Assert.True(spread > 800.0,
                    $"the cant is supposed to scatter them; it only spread {spread:F0} m, so this "
                    + "geometry no longer exercises the thing being fixed");
    }

    /// <summary>
    /// And turning the bus so each tube fires along the mean brings them onto one impact. The
    /// answer must not depend on the bus's roll — the cant is about the bus's own axis.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void RepointingBringsThemOntoOneImpact(double roll)
    {
        BallisticArc.Solution arc = Deorbit(out double3 from);
        doubleQuat attitude = Attitude(arc.RequiredVelocityCci, roll);

        double canted = SpreadMetres(from, arc.RequiredVelocityCci, attitude, false, Out);
        double pointed = SpreadMetres(from, arc.RequiredVelocityCci, attitude, true, Out);

        Assert.True(pointed < 50.0, $"re-pointing left {pointed:F0} m of spread, from {canted:F0}");
    }
}
