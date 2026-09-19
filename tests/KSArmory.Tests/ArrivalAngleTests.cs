using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the angle a round arrives at is worth, measured rather than argued about.
///
/// <para>Every ballistic shot this mod has flown arrives at about seven degrees, because the
/// guidance picks the cheapest transfer and the cheapest transfer from orbit is a graze. That is
/// the worst arrival there is for precision on four separate counts, and each of them has a number
/// here. <c>docs/ARRIVAL-ANGLE.md</c> is the account; this file is where the figures in it come
/// from.</para>
///
/// <para><b>Measurement only.</b> Nothing asserts an improvement and nothing changes what the
/// guidance does — the assertions pin facts that were measured, so the whole sweep can be re-run
/// after a change and compared.</para>
///
/// <para><b>The planet sits at the origin and does not move</b>, which <see cref="DeorbitShot"/>
/// says why of. So nothing here can see an epoch fault, and the open item in
/// <c>docs/ICBM-GUIDANCE.md</c> about the ground a round meets is out of reach of this rig — what
/// is measured is the geometry that would multiply such a fault, not the fault.</para>
/// </summary>
public class ArrivalAngleTests(ITestOutputHelper Out)
{
    private const double Mu = DeorbitShot.Mu;
    private const double R = DeorbitShot.R;

    /// <summary>Exhaust velocity of the stage <c>DeorbitTests</c> flies, for the mass ratios.</summary>
    private const double ExhaustVelocity = 3_100.0;

    /// <summary>The cutoff residual a trimmed bus was flown at, per <c>docs/ICBM-GUIDANCE.md</c>.</summary>
    private const double TrimmedResidual = 0.017;

    /// <summary>A residual an untrimmed split or a coarse cutoff leaves, for the pessimistic column.</summary>
    private const double CoarseResidual = 0.5;

    private static BallisticBody Earth => DeorbitShot.Earth;

    // A round that differs from the Mk 21 in nothing but its drag: the only field the predictor's
    // air model reads.
    private static MunitionProfile Sectional(float dragK)
        => new() { Name = "PROBE", DisplayName = "drag probe", DragK = dragK };

    private static ImpactPredictor.Impact Fly(double3 from, double3 v, MunitionProfile? through)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 1.0, 40_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               through is null
                                                   ? null
                                                   : new ImpactPredictor.Drag(DeorbitShot.DensityAt, through)));
        return hit;
    }

    // Degrees below the local horizontal, positive descending.
    private static double Descent(double3 pointCci, double3 velocityCci)
        => Vec.AngleBetween(pointCci, velocityCci) * 180.0 / Math.PI - 90.0;

    // A circular platform, braked purely retrograde: the one-parameter family a deorbit actually
    // has. Everything else about the shot follows from how hard it brakes.
    private static double3 Braked(double altitude, double brake)
        => new(0, Math.Sqrt(Mu / (R + altitude)) - brake, 0);

    private static double GammaInAir(double altitude, double brake)
    {
        double3 from = new(R + altitude, 0, 0);
        double3 v = Braked(altitude, brake);

        if (Kepler.PeriapsisRadius(Mu, from, v) > R) return double.NaN;

        ImpactPredictor.Impact hit = Fly(from, v, DeorbitShot.Warhead);
        return Descent(hit.PointCci, hit.VelocityCci);
    }

    /// <summary>
    /// The shallowest arrival the air permits, and the brake that reaches it.
    ///
    /// <para>Below it the vacuum arc is shallower still and drag bends it back, so the two branches
    /// meet at a floor rather than the angle carrying on down.</para>
    /// </summary>
    private static (double Degrees, double Brake) DragFloor(double altitude)
    {
        double best = double.PositiveInfinity, at = 0.0;

        for (double brake = 120.0; brake < Math.Sqrt(Mu / (R + altitude)); brake += 10.0)
        {
            double gamma = GammaInAir(altitude, brake);
            if (!double.IsNaN(gamma) && gamma < best) { best = gamma; at = brake; }
        }

        return (best, at);
    }

    // The brake that arrives at a stated angle. Bisected above the floor, where the angle is
    // monotonic in the brake; below it the same angle is reached by a second, far longer arc.
    private static double BrakeForArrival(double altitude, double degrees, double floorBrake)
    {
        double lo = floorBrake, hi = Math.Sqrt(Mu / (R + altitude));

        for (int i = 0; i < 48; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (GammaInAir(altitude, mid) < degrees) lo = mid; else hi = mid;
        }

        return 0.5 * (lo + hi);
    }

    /// <summary>How far the impact moves per metre a second at the drop, on three axes.</summary>
    private static (double Prograde, double Radial, double Cross, double Rms)
        Sensitivity(double3 fromCci, double3 velocityCci)
    {
        double3 prograde = Vec.Unit(velocityCci);
        double3 radial = Vec.Unit(fromCci);
        double3 cross = Vec.Unit(Vec.Cross(radial, prograde));

        double Along(double3 axis)
        {
            const double Delta = 0.5;
            double3 plus = Fly(fromCci, velocityCci + axis * Delta, DeorbitShot.Warhead).GroundFixedPointCci;
            double3 minus = Fly(fromCci, velocityCci - axis * Delta, DeorbitShot.Warhead).GroundFixedPointCci;
            return DeorbitShot.GroundMetres(plus, minus) / (2.0 * Delta);
        }

        double pro = Along(prograde), rad = Along(radial), crs = Along(cross);
        return (pro, rad, crs, Math.Sqrt((pro * pro + rad * rad + crs * crs) / 3.0));
    }

    /// <summary>
    /// Seven degrees is the air's answer, not the guidance's.
    ///
    /// <para>The shot in <see cref="DeorbitShot"/> leaves on a 3.6 degree vacuum arc and arrives at
    /// 7.1, because entry bends a graze back up to whatever the round's own sectional density can
    /// hold. So the arrival angle of a cheap deorbit is a property of the warhead, and the five
    /// degrees this budget was asked about is not reachable with a Mk 21 at all.</para>
    /// </summary>
    [Fact]
    public void TheShallowestArrivalBelongsToTheRoundNotTheGuidance()
    {
        foreach (double altitude in new[] { 300_000.0, 400_000.0, 500_000.0 })
        {
            (double degrees, double brake) = DragFloor(altitude);
            Out.WriteLine($"from {altitude / 1000:F0} km: nothing arrives shallower than "
                          + $"{degrees:F2} deg, reached by braking {brake:F0} m/s");

            Assert.InRange(degrees, 6.5, 7.5);
        }

        double3 from = new(R + 400_000.0, 0, 0);

        foreach (float dragK in new[] { 1.5e-4f, 1.5e-5f, 5e-6f, 1.5e-6f, 5e-7f, 1.5e-7f })
        {
            double best = double.PositiveInfinity, keptSpeed = 0.0, atRange = 0.0;

            for (double brake = 130.0; brake < Math.Sqrt(Mu / (R + 400_000.0)); brake += 10.0)
            {
                double3 v = Braked(400_000.0, brake);
                if (Kepler.PeriapsisRadius(Mu, from, v) > R) continue;

                ImpactPredictor.Impact hit = Fly(from, v, Sectional(dragK));
                double gamma = Descent(hit.PointCci, hit.VelocityCci);
                if (gamma >= best) continue;

                best = gamma;
                keptSpeed = Vec.Len(hit.VelocityCci);
                atRange = R * Vec.AngleBetween(from, hit.PointCci);
            }

            double3 dropped = Fly(from, Vec.Zero, Sectional(dragK)).VelocityCci;

            Out.WriteLine($"  DragK {dragK:E1} ({1.5e-5 / dragK,5:F1}x the Mk 21's density): "
                          + $"floor {best,5:F2} deg at {atRange / 1000,6:F0} km keeping {keptSpeed,5:F0} m/s; "
                          + $"a straight drop lands at {Vec.Len(dropped),5:F0} m/s");
        }
    }

    /// <summary>
    /// The table. Arrival angle against everything that decides where a rod lands.
    ///
    /// <para>The family is a circular platform braking retrograde, which is the one degree of
    /// freedom a deorbit has: how hard it brakes sets the arrival angle, the downrange, the flight
    /// time and the speed left, all at once. They cannot be traded against each other.</para>
    /// </summary>
    [Theory]
    [InlineData(300_000.0)]
    [InlineData(400_000.0)]
    [InlineData(500_000.0)]
    public void WhatPrecisionArrivalAngleBuys(double altitude)
    {
        double3 from = new(R + altitude, 0, 0);
        (double floorDegrees, double floorBrake) = DragFloor(altitude);

        Out.WriteLine($"circular {altitude / 1000:F0} km, {Math.Sqrt(Mu / (R + altitude)):F1} m/s; "
                      + $"shallowest arrival {floorDegrees:F2} deg");
        Out.WriteLine("  arrival | brake m/s | downrange | flight |  impact | cot g | dMiss/dV pro/rad/cross/rms"
                      + " | 10% drag | miss @0.017 | miss @0.5");

        double previousRms = double.PositiveInfinity;

        foreach (double degrees in new[] { 5.0, 7.5, 10.0, 15.0, 20.0, 30.0, 45.0, 60.0, 89.0 })
        {
            if (degrees < floorDegrees)
            {
                Out.WriteLine($"  {degrees,5:F1} deg | unreachable: the air will not let this round "
                              + $"arrive shallower than {floorDegrees:F2} deg");
                continue;
            }

            double brake = BrakeForArrival(altitude, degrees, floorBrake);
            double3 v = Braked(altitude, brake);

            ImpactPredictor.Impact vacuum = Fly(from, v, null);
            ImpactPredictor.Impact air = Fly(from, v, DeorbitShot.Warhead);

            double gamma = Descent(air.PointCci, air.VelocityCci);
            double cot = 1.0 / Math.Tan(gamma * Math.PI / 180.0);
            double downrange = R * Vec.AngleBetween(from, air.PointCci);

            (double pro, double rad, double crs, double rms) = Sensitivity(from, v);

            double heavier = DeorbitShot.GroundMetres(air.GroundFixedPointCci,
                                                      Fly(from, v, Sectional(1.65e-5f)).GroundFixedPointCci);

            // What the shot would be worth at each residual, with the surface term beside it: one
            // metre of disagreement about where the ground is, which is what cot(gamma) multiplies.
            double Total(double residual)
                => Math.Sqrt(Math.Pow(residual * rms, 2.0) + heavier * heavier + cot * cot);

            Out.WriteLine($"  {gamma,5:F2} deg | {brake,9:F0} | {downrange / 1000,6:F0} km | "
                          + $"{air.Seconds,5:F0} s | {Vec.Len(air.VelocityCci),5:F0} m/s | {cot,5:F2} | "
                          + $"{pro,6:F0} {rad,6:F0} {crs,5:F0} {rms,6:F0} | {heavier,7:F0} m | "
                          + $"{Total(TrimmedResidual),8:F0} m | {Total(CoarseResidual),8:F0} m "
                          + $"(vacuum arc {Descent(vacuum.PointCci, vacuum.VelocityCci):F2} deg, "
                          + $"drag costs {(R * Vec.AngleBetween(from, vacuum.PointCci) - downrange) / 1000:F1} km)");

            // Steeper is never worse. The whole recommendation rests on this being monotonic:
            // there is no angle past which the arc grows long enough to give the gain back.
            Assert.True(rms < previousRms,
                        $"{gamma:F2} deg is no more sensitive than the arrival before it");
            previousRms = rms;
        }
    }

    /// <summary>
    /// The other way to steepen: hold the range and fly a longer arc, which is what
    /// <c>IcbmConfig.Loft</c> does.
    ///
    /// <para>It works, and it stops paying. Range sets the floor under the sensitivity — a shot
    /// that has to cover 3,459 km has a long lever whatever angle it comes in at — so the gain is
    /// nearly all spent by fifteen degrees and everything past that is kilometres a second for a
    /// few per cent.</para>
    /// </summary>
    [Fact]
    public void WhatSteepeningCostsAtAFixedRange()
    {
        double3 from = new(R + DeorbitShot.PickupAltitude, 0, 0);
        double3 circular = new(0, Math.Sqrt(Mu / (R + DeorbitShot.PickupAltitude)), 0);
        double3 target = new(R * Math.Cos(DeorbitShot.RangeMetres / R),
                             R * Math.Sin(DeorbitShot.RangeMetres / R), 0);

        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target, out BallisticArc.Solution cheapest));

        Out.WriteLine($"{DeorbitShot.RangeMetres / 1000:F0} km from a {DeorbitShot.PickupAltitude / 1000:F0} km "
                      + $"cutoff; cheapest flight {cheapest.CheapestFlightSeconds:F0} s");

        double previousDv = 0.0, previousRms = double.PositiveInfinity;

        foreach (double loft in new[] { 1.0, 1.2, 1.4, 1.6, 1.8, 2.5, 4.0, 6.0, 9.0, 12.0 })
        {
            double flight = cheapest.CheapestFlightSeconds * loft;

            if (!BallisticArc.TrySolve(Earth, from, target, flight, out BallisticArc.Solution arc)
                || arc.LowestRadius < R - 1.0)
            {
                Out.WriteLine($"  loft {loft,5:F2}: no arc above the ground");
                continue;
            }

            double dv = Vec.Len(arc.RequiredVelocityCci - circular);
            ImpactPredictor.Impact air = Fly(from, arc.RequiredVelocityCci, DeorbitShot.Warhead);
            double gamma = Descent(air.PointCci, air.VelocityCci);

            (double _, double _, double _, double rms) = Sensitivity(from, arc.RequiredVelocityCci);

            double bought = double.IsFinite(previousRms) && dv > previousDv
                          ? (previousRms - rms) / ((dv - previousDv) / 1000.0)
                          : double.NaN;

            Out.WriteLine($"  loft {loft,5:F2}: {gamma,5:F2} deg, {dv,6:F0} m/s, apogee "
                          + $"{(arc.ApogeeRadius - R) / 1000,6:F0} km, mass ratio {Math.Exp(dv / ExhaustVelocity),5:F2}, "
                          + $"rms {rms,6:F0} m per m/s"
                          + (double.IsNaN(bought) ? "" : $", the last km/s bought {bought,6:F0}"));

            previousDv = dv;
            previousRms = rms;
        }
    }

    /// <summary>
    /// What a ten per cent error in the drag model is worth, per arrival angle.
    ///
    /// <para>The number that matters most and is easiest to miss: a correction loop can only remove
    /// what its observer can see, and the observer shares the flight model. So whatever the model
    /// has wrong survives the loop — and on a graze it is worth more than the whole flown
    /// miss.</para>
    /// </summary>
    [Fact]
    public void WhatAnErrorInTheDragModelCosts()
    {
        double3 from = new(R + 400_000.0, 0, 0);
        (double _, double floorBrake) = DragFloor(400_000.0);

        foreach (double degrees in new[] { 7.5, 10.0, 15.0, 20.0, 30.0, 45.0, 60.0 })
        {
            double brake = BrakeForArrival(400_000.0, degrees, floorBrake);
            double3 v = Braked(400_000.0, brake);

            double3 nominal = Fly(from, v, DeorbitShot.Warhead).GroundFixedPointCci;

            Out.WriteLine($"  {degrees,5:F1} deg: +10% drag {DeorbitShot.GroundMetres(nominal, Fly(from, v, Sectional(1.65e-5f)).GroundFixedPointCci),7:F0} m, "
                          + $"-10% {DeorbitShot.GroundMetres(nominal, Fly(from, v, Sectional(1.35e-5f)).GroundFixedPointCci),7:F0} m, "
                          + $"no air at all {DeorbitShot.GroundMetres(nominal, Fly(from, v, null).GroundFixedPointCci) / 1000.0,7:F1} km");
        }
    }

    /// <summary>
    /// One metre of disagreement about where the surface is, in metres of ground, and the three
    /// named surface terms in <c>docs/KSA-TERRAIN.md</c> put through it.
    /// </summary>
    [Fact]
    public void WhatTheSurfaceCostsPerArrivalAngle()
    {
        // The height field's own quantum, and the depth the sea clamp is missing over water.
        const double Quantum = 0.2985;
        const double MeanSeaDepth = 3_776.0;

        // One frame of ground track at entry speed and 60 fps, which is the interval the round
        // holds a single terrain sample across.
        const double FrameOfTrack = 30.0;
        const double Slope = 0.05;

        foreach (double degrees in new[] { 7.0, 10.0, 15.0, 20.0, 30.0, 45.0, 60.0, 89.0 })
        {
            double tan = Math.Tan(degrees * Math.PI / 180.0);
            double cot = 1.0 / tan;

            Out.WriteLine($"  {degrees,5:F1} deg: {cot,6:F2} m of ground per m of height | "
                          + $"the field's quantum {Quantum * cot,6:F2} m | "
                          + $"a held terrain sample on a 5% slope {Slope * FrameOfTrack / (tan + Slope),6:F2} m | "
                          + $"the mean sea depth {MeanSeaDepth * cot / 1000.0,6:F1} km");
        }
    }

    /// <summary>
    /// Impact speed against arrival angle, which is the other half of what a rod wants.
    ///
    /// <para>The energy is the platform's orbital velocity, so braking to arrive steeply is
    /// spending the thing that does the damage. Total speed peaks well short of vertical; the
    /// component square to the ground does not.</para>
    /// </summary>
    [Fact]
    public void ImpactSpeedAgainstArrivalAngle()
    {
        double3 from = new(R + 400_000.0, 0, 0);
        double bestSpeed = 0.0, atAngle = 0.0;

        foreach (double brake in new[] { 300.0, 500.0, 800.0, 1100.0, 1400.0, 1700.0, 2000.0, 2400.0,
                                         2800.0, 3400.0, 4200.0, 5200.0, 6400.0,
                                         7672.6 })
        {
            double3 v = Braked(400_000.0, brake);
            if (Kepler.PeriapsisRadius(Mu, from, v) > R) continue;

            ImpactPredictor.Impact hit = Fly(from, v, DeorbitShot.Warhead);
            double gamma = Descent(hit.PointCci, hit.VelocityCci);
            double speed = Vec.Len(hit.VelocityCci);

            if (speed > bestSpeed) { bestSpeed = speed; atAngle = gamma; }

            Out.WriteLine($"  brake {brake,6:F0} m/s -> {R * Vec.AngleBetween(from, hit.PointCci) / 1000,6:F0} km, "
                          + $"{gamma,6:F2} deg, {speed,6:F0} m/s, square to the ground "
                          + $"{speed * Math.Sin(gamma * Math.PI / 180.0),6:F0} m/s, "
                          + $"{0.5 * speed * speed / 1e6,5:F2} MJ/kg");
        }

        Out.WriteLine($"fastest arrival {bestSpeed:F0} m/s at {atAngle:F2} deg");

        // The energy comes from the orbit, so the steep arrivals are the slow ones.
        Assert.InRange(atAngle, 10.0, 20.0);
    }

    /// <summary>
    /// What the two routes to a steep arrival cost in propellant, as the mass ratio each asks of a
    /// stage with the exhaust velocity <c>DeorbitTests</c> flies.
    /// </summary>
    [Fact]
    public void WhatSteepeningCostsInPropellant()
    {
        double3 from = new(R + 400_000.0, 0, 0);
        (double _, double floorBrake) = DragFloor(400_000.0);

        foreach (double degrees in new[] { 7.5, 10.0, 15.0, 20.0, 30.0, 45.0, 60.0, 88.7 })
        {
            double brake = BrakeForArrival(400_000.0, degrees, floorBrake);
            double3 v = Braked(400_000.0, brake);
            ImpactPredictor.Impact hit = Fly(from, v, DeorbitShot.Warhead);
            double ratio = Math.Exp(brake / ExhaustVelocity);

            Out.WriteLine($"  {Descent(hit.PointCci, hit.VelocityCci),5:F2} deg at "
                          + $"{R * Vec.AngleBetween(from, hit.PointCci) / 1000,6:F0} km: brake {brake,6:F0} m/s, "
                          + $"mass ratio {ratio,6:F2}, {100.0 / ratio,5:F1}% of the stack arrives");
        }
    }

    /// <summary>
    /// What the window search picks when the target is fixed, and what <c>Loft</c> does to it.
    ///
    /// <para><see cref="BurnWindow"/> costs every departure across a day and takes the cheapest,
    /// which for anything more than a few hundred kilometres downrange is the graze. Loft does not
    /// override that: it raises the cost of leaving now as well as the cost of waiting, so a shot
    /// that would have been taken steeply from close in is deferred to a cheap flat one instead.
    /// The operator asks for steeper and gets shallower.</para>
    /// </summary>
    [Fact]
    public void TheWindowSearchPrefersTheGrazeAndLoftCanMakeItWorse()
    {
        const double Altitude = 400_000.0;

        double3 from = new(R + Altitude, 0, 0);
        double3 vel = new(0, Math.Sqrt(Mu / (R + Altitude)), 0);

        double steepestWhenAskedFor = double.NaN;
        double steepestAtOne = double.NaN;

        foreach (double aheadDeg in new[] { 5.0, 20.0, 45.0, 90.0 })
        {
            double ahead = aheadDeg * Math.PI / 180.0;
            double3 aim = new(R * Math.Cos(ahead), R * Math.Sin(ahead), 0);

            foreach (double loft in new[] { 1.0, 1.4, 1.8 })
            {
                Assert.True(BurnWindow.TryFind(Earth, from, vel, aim, out BurnWindow.Window window, loft));

                double gamma = Descent(window.Arc.ImpactCciAtArrival, window.Arc.ArrivalVelocityCci);

                Out.WriteLine($"  {R * ahead / 1000,6:F0} km ahead, loft {loft:F1}: "
                              + $"waits {window.WaitSeconds,7:F0} s, costs {window.Cost,7:F0} m/s "
                              + $"against {window.CostIfLeavingNow,8:F0} to leave now, "
                              + $"flight {window.Arc.FlightSeconds,6:F0} s, arrives {gamma,6:F2} deg");

                if (aheadDeg != 5.0) continue;

                if (loft == 1.0) steepestAtOne = gamma;
                if (loft == 1.8) steepestWhenAskedFor = gamma;
            }
        }

        // Close in, the cheapest shot is already the steep one — and asking for the steepest loft
        // the panel offers throws it away for a graze an orbit and a half later.
        Assert.True(steepestAtOne > 30.0, $"a 556 km shot should arrive steeply, not at {steepestAtOne:F2} deg");
        Assert.True(steepestWhenAskedFor < steepestAtOne,
                    "loft is supposed to be able to invert the arrival here");
    }
}
