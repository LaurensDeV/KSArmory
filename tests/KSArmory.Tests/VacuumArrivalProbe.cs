using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the release kick's vacuum solve actually costs, measured rather than estimated.
/// </summary>
/// <remarks>
/// <para><see cref="ReleaseFocus"/> builds its sensitivity and its arrival from
/// <see cref="Kepler.TryCoast"/> — gravity only — and propagates it for the flight time of a
/// <em>drag</em> arc. That is real: at the flown release the vacuum arc is 3.7 km from the drag
/// impact and 1,331 m/s faster.</para>
///
/// <para><b>And it very nearly cancels.</b> <c>TryKick</c> flies the offset's own displacement
/// through the <em>same</em> vacuum propagator it builds the columns from, so a uniform error
/// divides out exactly and only the anisotropy of the ratio survives. End to end the solve leaves
/// <b>0.15%</b> of a 0.86 m ring — 1.6 mm — against a flown residue of 0.6%. So the vacuum solve
/// is <b>not</b> the main cause of the flown ring residue, and replacing it buys millimetres rather
/// than the centimetre it looks worth from the 32% sensitivity error alone.
/// <c>docs/ACCURACY-PLAN.md</c> 3eb.</para>
///
/// <para><b>On the constant's drag.</b> On 3.6x the drag the same solve leaves 0.80% of the ring,
/// which is the physical round's flown spread — <see cref="KickThroughTheAirTests"/>, 3ep.</para>
/// </remarks>
public class VacuumArrivalProbe(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    [Fact]
    public void HowFarTheVacuumArrivalIsFromTheRealOne()
    {
        BallisticBody earth = new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);
        // OUT of the equatorial plane, as the flown aim is: an equatorial shot has the ground's
        // velocity nearly parallel to the arrival, so its perpendicular component -- which is the
        // whole of this term -- is nearly zero and the probe understates it.
        double3 from = new(R + 877_000.0, 0, 0);
        double range = 1_500_000.0;
        double lat = -26.485 * Math.PI / 180.0;
        double3 target = new(R * Math.Cos(range / R), R * Math.Sin(range / R) * Math.Cos(lat),
                             R * Math.Sin(range / R) * Math.Sin(lat));
        double3 coasting = new(0, Math.Sqrt(Mu / (R + 877_000.0)) * Math.Cos(lat),
                               Math.Sqrt(Mu / (R + 877_000.0)) * Math.Sin(lat));
        Assert.True(BallisticArc.TryCheapest(earth, from, coasting, target, out BallisticArc.Solution s));
        double3 v = s.RequiredVelocityCci;

        // What the release probe does: a DRAG prediction, and its flight time.
        Assert.True(ImpactPredictor.TryPredict(earth, from, v, 2.0, 12_000.0,
            out ImpactPredictor.Impact hit, null, null,
            new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
            atmosphericStepSeconds: 0.25, stopOnTheSurface: true));

        double T = hit.Seconds;

        // What ReleaseFocus does: a VACUUM coast, for that same drag flight time.
        Assert.True(Kepler.TryCoast(Mu, from, v, T, out double3 vac, out double3 vacVel));

        double3 upAt = Vec.Unit(hit.PointCci);
        double3 gh = earth.GroundVelocityCci(hit.PointCci);
        double3 alongH = Vec.Unit(hit.VelocityCci - upAt * Vec.Dot(hit.VelocityCci, upAt));
        double perp = Vec.Len(gh - alongH * Vec.Dot(gh, alongH) - upAt * Vec.Dot(gh, upAt));
        Out.WriteLine($"flight time {T:F1} s; ground speed at impact {Vec.Len(gh):F0} m/s, "
                      + $"of which {perp:F0} m/s is square to the track");
        Out.WriteLine($"drag impact   |r| {Vec.Len(hit.PointCci) - R,10:F1} m altitude, "
                      + $"{Vec.Len(hit.VelocityCci),7:F0} m/s");
        Out.WriteLine($"vacuum at T   |r| {Vec.Len(vac) - R,10:F1} m altitude, "
                      + $"{Vec.Len(vacVel),7:F0} m/s");
        Out.WriteLine($"they are {Vec.Len(vac - hit.PointCci) / 1000.0:F2} km apart\n");

        // The direction TryCancel nulls against, both ways.
        double3 groundAtVac = earth.GroundVelocityCci(vac);
        double3 groundAtHit = earth.GroundVelocityCci(hit.PointCci);
        double3 alongVac = Vec.Unit(vacVel - groundAtVac);
        double3 alongReal = Vec.Unit(hit.VelocityCci - groundAtHit);

        double deg = Vec.AngleBetween(alongVac, alongReal) * 180.0 / Math.PI;
        Out.WriteLine($"the arrival direction the solve nulls against is {deg:F3} deg from the real one");
        Out.WriteLine($"  -> a 1 m along-arrival leftover lands {1000.0 * Math.Sin(deg * Math.PI / 180.0):F1} mm out");
        Out.WriteLine($"  -> the 0.86 m ring's 1.59 m image lands "
                      + $"{1590.0 * Math.Sin(deg * Math.PI / 180.0):F1} mm out");

        // The OTHER arm: the sensitivity itself. ReleaseFocus builds its columns by nudging the
        // velocity and coasting in VACUUM. What it needs is how the LANDING moves, through air.
        Out.WriteLine("\n== the columns: how far the landing moves per 0.01 m/s of nudge");
        Out.WriteLine($"{"axis",6} {"vacuum at T (m)",18} {"drag landing (m)",18} {"ratio",8}");

        foreach ((string name, double3 nudge) in new[]
                 {
                     ("x", new double3(0.01, 0, 0)), ("y", new double3(0, 0.01, 0)),
                     ("z", new double3(0, 0, 0.01)),
                 })
        {
            Assert.True(Kepler.TryCoast(Mu, from, v + nudge, T, out double3 nv, out _));
            double vacMove = Vec.Len(nv - vac);

            Assert.True(ImpactPredictor.TryPredict(earth, from, v + nudge, 2.0, 12_000.0,
                out ImpactPredictor.Impact nh, null, null,
                new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
                atmosphericStepSeconds: 0.25, stopOnTheSurface: true));
            double dragMove = Vec.Len(nh.GroundFixedPointCci - hit.GroundFixedPointCci);

            Out.WriteLine($"{name,6} {vacMove,18:F3} {dragMove,18:F3} {dragMove / vacMove,8:F3}");
        }

        Out.WriteLine("\n  a ratio away from 1.000 is the sensitivity error the kick is solved with.");

        // But a 32% sensitivity error cannot square with a flown residue of 0.6%, so something is
        // cancelling. TryKick flies the offset's displacement through the SAME vacuum propagator it
        // builds the columns from -- so a uniform error cancels exactly, and only the ANISOTROPY of
        // the ratio survives. End to end is the only thing that settles it.
        Out.WriteLine("\n== end to end: a 0.86 m ring offset, kicked, then flown through air");

        double3 offset = new(0, 0.86, 0);   // square to the release line

        Assert.True(ReleaseFocus.TryKick(earth, from, v, T, offset, out double3 kick));
        Out.WriteLine($"   the solve asks for {Vec.Len(kick) * 1000.0:F3} mm/s");

        // Where an offset round lands with no kick, and with the kick the vacuum solve gave it.
        double3 Land(double3 p0, double3 v0)
        {
            Assert.True(ImpactPredictor.TryPredict(earth, p0, v0, 2.0, 12_000.0,
                out ImpactPredictor.Impact h, null, null,
                new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
                atmosphericStepSeconds: 0.25, stopOnTheSurface: true));
            return h.GroundFixedPointCci;
        }

        double3 mean = Land(from, v);
        double3 bare = Land(from + offset, v);
        double3 kicked = Land(from + offset, v + kick);

        Out.WriteLine($"   unkicked, the ring puts it {Vec.Len(bare - mean):F3} m from the mean");
        Out.WriteLine($"   kicked,   it is {Vec.Len(kicked - mean) * 1000.0:F1} mm from the mean");
        Out.WriteLine($"   -> the vacuum solve leaves "
                      + $"{100.0 * Vec.Len(kicked - mean) / Vec.Len(bare - mean):F2}% of the ring");
        Out.WriteLine("   the flown ring-shift residue is 0.6% (ACCURACY-PLAN 3dg).");

        double left = Vec.Len(kicked - mean) / Vec.Len(bare - mean);

        // The three numbers this exists to hold. The first two are the fault; the third is why it
        // is small anyway, and it is the one that would be missed by reading the code alone.
        Assert.InRange(deg, 0.02, 0.10);            // the arrival direction, degrees
        Assert.InRange(Vec.Len(vacVel) / Vec.Len(hit.VelocityCci), 1.15, 1.35);   // the speed
        Assert.True(left < 0.01,
                    $"the vacuum solve leaves {100.0 * left:F2}% of the ring; it cancels against "
                    + "itself and should stay under 1%, which is what makes it a millimetre term");
    }
}
