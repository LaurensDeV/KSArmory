using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What a steeper arrival costs the <em>trim</em>, which is the account
/// <see cref="ArrivalBudget.SteepestAffordableDeg"/> does not keep.
///
/// <para><c>docs/ACCURACY-PLAN.md</c> item 5c, answered here and not the way it was asked. The
/// budget prices what the <b>ascent</b> can pay to reach an angle and answers 67 degrees; flown at
/// 54 the trim ran out and the shot lost 5.55x, so the standing reading was that a steep arrival is
/// dear for the bus to correct on. It is not: at a fixed departure the exchange rate falls
/// monotonically as the floor steepens, because a steeper floor is bought with a <em>longer</em>
/// transfer and the rate is set by the transfer time.</para>
///
/// <para>What the floor really runs into is a wall — past some angle no arc satisfying it exists
/// from where the burn leaves the vehicle at all, and the guidance falls back to a short steep one
/// whose aim costs several times as much to move. 3ag has the reading.</para>
/// </summary>
public class TrimAffordableArrivalTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double3 Downrange(double metres)
        => new(DeorbitShot.R * Math.Cos(metres / DeorbitShot.R),
               DeorbitShot.R * Math.Sin(metres / DeorbitShot.R), 0);

    /// <summary>
    /// The rate against flight time alone, with the geometry held still.
    ///
    /// <para>Nothing here flies. <see cref="AimAuthority.TryRate"/> takes the transfer time as a
    /// free parameter, so one departure point and one aim point priced at a range of times is the
    /// controlled experiment: whatever varies is the time and nothing else. The arrival angle rises
    /// with the time and the rate falls, which is the pair that rules out the angle as the driver.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(3_459_000.0)]
    [InlineData(12_902_000.0)]
    public void ALongerTransferIsWhatMakesTheAimCheapToMove(double shotMetres)
    {
        double3 from = new(DeorbitShot.R + 600_000.0, 0, 0);
        double3 aim = Downrange(shotMetres);

        Out.WriteLine($"--- {shotMetres / 1000:F0} km, one departure, time swept ---");
        Out.WriteLine($"{"flight s",10}{"arrival",10}{"m/s per km",13}{"1/t, m/s per km",18}{"ratio",8}");

        double previous = double.PositiveInfinity;
        int priced = 0;

        foreach (double seconds in new[] { 900.0, 1_200.0, 1_800.0, 2_400.0, 3_000.0, 3_600.0, 4_500.0 })
        {
            if (!AimAuthority.TryRate(Earth, from, aim, seconds, out double perMetre)) continue;
            if (!BallisticArc.TrySolve(Earth, from, aim, seconds, out BallisticArc.Solution arc)) continue;

            double perKm = perMetre * 1000.0;
            double naive = 1000.0 / seconds;

            Out.WriteLine($"{seconds,10:F0}{arc.ArrivalAngleDeg,10:F1}{perKm,13:F3}"
                          + $"{naive,18:F3}{perKm / naive,8:F2}");

            Assert.True(perKm < previous,
                        $"{seconds:F0} s priced {perKm:F3} against {previous:F3} at the shorter transfer");

            previous = perKm;
            priced++;
        }

        Assert.True(priced >= 5, $"only {priced} transfers priced");
    }

    /// <summary>
    /// What the budget's own solver sees, swept finely and at no flight cost: the cheapest arc
    /// satisfying each floor, and the transfer time it satisfies it with.
    ///
    /// <para><b>This is the reading item 5c wants.</b>
    /// <see cref="ArrivalBudget.SteepestAffordableDeg"/> makes exactly this call and keeps only the
    /// cost, so the flight time — which is what sets the aim's exchange rate — is computed on every
    /// probe and thrown away. The floor never buys its angle with a shorter transfer, so the trim's
    /// authority only ever grows with it; what ends the table is the arc ceasing to exist.</para>
    /// </summary>
    [Theory]
    [InlineData(12_902_000.0)]
    [InlineData(3_459_000.0)]
    public void AFloorIsBoughtWithALongerTransferUntilNoArcSatisfiesIt(double shotMetres)
    {
        // A post-boost state rather than a pad: the floor is latched during the burn and what it
        // costs is spent afterwards, so this is the departure the trim actually pays from.
        double3 from = new(DeorbitShot.R + 900_000.0, 0, 0);
        double3 velocity = new(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + 900_000.0)), 0);
        double3 aim = Downrange(shotMetres);

        Out.WriteLine($"--- {shotMetres / 1000:F0} km ---");
        Out.WriteLine($"{"floor",7}{"arrival",9}{"flight s",10}{"cost m/s",10}"
                      + $"{"m/s per km",13}{"km per budget",15}");

        double longestSeconds = 0.0, steepestSolved = double.NaN, authorityThere = double.NaN;
        double previousSeconds = 0.0;
        bool everShortened = false;

        for (double floor = 0.0; floor <= 70.0; floor += 5.0)
        {
            if (!BallisticArc.TryCheapest(Earth, from, velocity, aim, out BallisticArc.Solution arc,
                                          1.0, false, double.NaN, floor))
            {
                Out.WriteLine($"{floor,7:F0}{"unsolved",9}");
                continue;
            }

            // Only counted once the floor is actually binding: below the arc's natural arrival it
            // returns the same solution every time, which is neither a lengthening nor a shortening.
            if (floor > 0.0 && arc.ArrivalAngleDeg > floor - 0.5 && arc.FlightSeconds < previousSeconds)
            {
                everShortened = true;
            }

            previousSeconds = arc.FlightSeconds;
            longestSeconds = Math.Max(longestSeconds, arc.FlightSeconds);
            steepestSolved = floor;

            double cost = Vec.Len(arc.RequiredVelocityCci - velocity);
            string rate = "-", authority = "-";

            if (AimAuthority.TryRate(Earth, from, aim, arc.FlightSeconds, out double perMetre))
            {
                double perKm = perMetre * 1000.0;
                authorityThere = PostBoostAim.MaxTrimMetresPerSecond / perKm;
                rate = $"{perKm:F3}";
                authority = $"{authorityThere:F1}";
            }

            Out.WriteLine($"{floor,7:F0}{arc.ArrivalAngleDeg,9:F1}{arc.FlightSeconds,10:F0}"
                          + $"{cost,10:F0}{rate,13}{authority,15}");
        }

        Out.WriteLine($"steepest solved {steepestSolved:F0} deg, longest transfer {longestSeconds:F0} s, "
                      + $"aim authority there {authorityThere:F0} km");

        // The claim: steepening never costs the trim its authority. It runs out of arc first.
        Assert.False(everShortened, "a binding floor was satisfied with a shorter transfer");

        // And the wall is real rather than the sweep running off the end of the table.
        Assert.True(steepestSolved < 70.0,
                    $"every floor to {steepestSolved:F0} deg solved; the wall is past this sweep");
    }
}
