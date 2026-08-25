using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What arrival-angle floor the shipped MIRV scenario can satisfy, and what each one costs.
///
/// <para><c>docs/MIRV-NEXT.md</c> item 7g parks <c>arm/arr15</c> behind this: a shot that cannot
/// satisfy its floor reports <see cref="IcbmReach.TooShallow"/> and holds its warheads, so a night
/// flown at an unreachable floor is 48 refusals rather than 48 measurements.</para>
///
/// <para><b>The cost is a difference between two required velocities</b>, which is why nothing here
/// needs the vehicle's own state. Asking what the vehicle must be doing at burnout to fly arc A
/// rather than arc B is answered by the two arcs; costing it against the pick-up velocity instead
/// makes the answer turn on a flight path angle the logs do not record.</para>
///
/// <para>The reference arc is the flown one. Its vacuum angle is <b>3.6 degrees</b> — the flights
/// arrive at 7.1 through the air, and <see cref="BallisticArc.Solution.ArrivalAngleDeg"/> records
/// that drag bends a graze and leaves 10 to 30 degrees alone. So a floor of 15 constrains something
/// the air will not then move, while today's default arrives shallower than it looks.</para>
///
/// <para>The geometry is the flown one: 247 shots across 21 nights logged 3,441-3,461 km from the
/// craft's own position, which is what <see cref="DeorbitShot.RangeMetres"/> already holds.</para>
/// </summary>
public class ScenarioArrivalFloorTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    /// <summary>The pick-up altitude every night's log reports.</summary>
    private const double PickupAltitude = 207_000.0;

    /// <summary>The stack in the shipped save, off its own throttle trace: 12,164 kN against
    /// 571.7 t burning 2.855 t/s, so an exhaust velocity of 4,261 m/s. It burns 85.9 t for the
    /// 655 m/s the flown shot costs, which is what calibrates this.</summary>
    private const double ExhaustVelocity = 4_261.0;
    private const double PickupMassKg = 571_700.0;

    private static double3 From => new(DeorbitShot.R + PickupAltitude, 0, 0);

    private static double3 Downrange(double metres)
        => new(DeorbitShot.R * Math.Cos(metres / DeorbitShot.R),
               DeorbitShot.R * Math.Sin(metres / DeorbitShot.R), 0);

    private readonly record struct Arc(double FlightSeconds, double ArrivalDeg, double3 RequiredCci);

    /// <summary>Every arc that reaches, swept on flight time — the parameter the solver takes.</summary>
    private static List<Arc> Sweep(double rangeMetres, double fromAltitude)
    {
        double3 from = new(DeorbitShot.R + fromAltitude, 0, 0);
        double3 aim = Downrange(rangeMetres);
        List<Arc> arcs = [];

        for (double t = 200.0; t <= 4_000.0; t += 1.0)
        {
            if (!BallisticArc.TrySolve(Earth, from, aim, t, out BallisticArc.Solution s)) continue;
            if (s.LowestRadius < DeorbitShot.R) continue;      // an arc through the planet is not one
            arcs.Add(new Arc(t, s.ArrivalAngleDeg, s.RequiredVelocityCci));
        }

        return arcs;
    }

    private static Arc Nearest(List<Arc> arcs, double arrivalDeg)
        => arcs.MinBy(a => Math.Abs(a.ArrivalDeg - arrivalDeg));

    /// <summary>Propellant to change velocity by this much, off the shipped stack's own numbers.</summary>
    private static double PropellantTonnes(double deltaV)
        => PickupMassKg * (1.0 - Math.Exp(-deltaV / ExhaustVelocity)) / 1000.0;

    /// <summary>
    /// The gate: is a 15 degree floor reachable at the geometry the scenario flies, and what does
    /// it cost over the arc flown today.
    ///
    /// <para>Costs are reported rather than asserted. What a floor is worth is a number to read
    /// before choosing one, and pinning it here would pin the trajectory search's tuning to this
    /// file.</para>
    /// </summary>
    [Fact]
    public void WhatFloorTheShippedScenarioCanSatisfy()
    {
        List<Arc> arcs = Sweep(DeorbitShot.RangeMetres, PickupAltitude);
        Assert.NotEmpty(arcs);

        Arc flown = Nearest(arcs, 3.6);
        Arc steepest = arcs.MaxBy(a => a.ArrivalDeg);

        Out.WriteLine($"scenario: {DeorbitShot.RangeMetres / 1000.0:F0} km from "
                      + $"{PickupAltitude / 1000.0:F0} km burnout");
        Out.WriteLine($"  {arcs.Count} arcs reach, arriving {arcs.Min(a => a.ArrivalDeg):F2} "
                      + $"to {steepest.ArrivalDeg:F2} deg (vacuum, at the mean sphere)");
        Out.WriteLine($"  flown today: {flown.ArrivalDeg:F2} deg vacuum / 7.1 through the air, "
                      + $"{flown.FlightSeconds:F0} s of flight");
        Out.WriteLine("");
        Out.WriteLine("  floor   arrives   flight s   extra over the flown arc   propellant   vs the 85.9 t flown");

        foreach (double floor in new[] { 5.0, 7.0, 10.0, 12.0, 15.0, 20.0, 25.0 })
        {
            List<Arc> ok = arcs.Where(a => a.ArrivalDeg >= floor).ToList();
            if (ok.Count == 0)
            {
                Out.WriteLine($"  {floor,5:F0}   -- none: steepest is {steepest.ArrivalDeg:F2} deg "
                              + "-- TooShallow");
                continue;
            }

            // The floor picks the shallowest arc that satisfies it, which is the cheapest one:
            // every degree past the bound is bought with velocity nobody asked to spend.
            Arc best = ok.MinBy(a => a.ArrivalDeg);
            double extra = Vec.Len(best.RequiredCci - flown.RequiredCci);
            double tonnes = PropellantTonnes(extra);
            Out.WriteLine($"  {floor,5:F0}   {best.ArrivalDeg,7:F2}   {best.FlightSeconds,8:F0}"
                          + $"   {extra,22:F0} m/s   {tonnes,8:F1} t   {tonnes / 85.9,6:F1}x");
        }

        // The gate 7g parks arm/arr15 behind. A 15 degree floor is geometrically reachable at this
        // range, so a night of it is not a night of TooShallow refusals -- what it is instead is a
        // propellant question, and the table above is the ask.
        Assert.Contains(arcs, a => a.ArrivalDeg >= 15.0);
    }

    /// <summary>
    /// What the floor buys, beside what the table above says it costs.
    ///
    /// <para>The geometric half of the precision gain is <c>cot(g)</c>: a round arriving at g
    /// converts a metre of trajectory error into <c>cot(g)</c> of ground, and that is the term
    /// <c>docs/KINETIC-FLOOR.md</c> says no guidance work removes. Nothing here measures the
    /// flown miss — that needs a night — but the ratio is what makes one worth flying.</para>
    /// </summary>
    [Fact]
    public void WhatAFloorBuysAgainstWhatItCosts()
    {
        List<Arc> arcs = Sweep(DeorbitShot.RangeMetres, PickupAltitude);
        Arc flown = Nearest(arcs, 3.6);
        double flownCot = 1.0 / Math.Tan(flown.ArrivalDeg * Math.PI / 180.0);

        Out.WriteLine($"against the flown {flown.ArrivalDeg:F2} deg arc "
                      + $"(cot {flownCot:F1}, and 85.9 t of propellant):");
        Out.WriteLine("  floor   ground per metre of error   precision gain   propellant   net");

        foreach (double floor in new[] { 7.0, 10.0, 12.0, 15.0, 20.0 })
        {
            Arc best = arcs.Where(a => a.ArrivalDeg >= floor).MinBy(a => a.ArrivalDeg);
            double cot = 1.0 / Math.Tan(best.ArrivalDeg * Math.PI / 180.0);
            double gain = flownCot / cot;
            double cost = PropellantTonnes(Vec.Len(best.RequiredCci - flown.RequiredCci)) / 85.9;
            Out.WriteLine($"  {floor,5:F0}   {cot,25:F1}   {gain,14:F1}x   {cost,9:F1}x   "
                          + $"{(gain > cost ? "buys more than it costs" : "costs more than it buys")}");
        }
    }
}
