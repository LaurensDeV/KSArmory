using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The ceiling under the arrival-angle control. Arrival angle is the dominant precision lever and it
/// is bought with propellant, so the number an operator may ask for is a property of their rocket
/// against their target — not a round number on a slider.
/// </summary>
public class ArrivalBudgetTests
{
    private const double EarthMu = 3.986004418e14;
    private const double EarthRadius = 6_371_000.0;

    private static BallisticBody Earth => new(EarthMu, EarthRadius, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 Equator(double longitudeRad, double altitude = 0.0)
        => new((EarthRadius + altitude) * Math.Cos(longitudeRad),
               (EarthRadius + altitude) * Math.Sin(longitudeRad), 0.0);

    private static double Steepest(double availableMetresPerSecond, double separationRad = 0.46)
    {
        double3 pad = Equator(0.0);

        return ArrivalBudget.SteepestAffordableDeg(Earth, pad, Earth.GroundVelocityCci(pad),
                                                   Equator(separationRad), availableMetresPerSecond);
    }

    /// <summary>
    /// The whole point of the control: more in the tanks buys a steeper arrival. If this were flat
    /// the clamp would be a decoration.
    /// </summary>
    [Fact]
    public void MoreDeltaVBuysASteeperArrival()
    {
        double lean = Steepest(7_000.0);
        double fat = Steepest(11_000.0);

        Assert.True(double.IsFinite(lean), "a 7 km/s stack could not be costed at all");
        Assert.True(fat > lean, $"{fat:F1} deg on 11 km/s against {lean:F1} on 7");
    }

    /// <summary>
    /// And it is a real bound rather than a preference: what it returns has to be affordable, and a
    /// step past it has to not be. Anything else is a slider that lies in one direction or the
    /// other.
    /// </summary>
    [Fact]
    public void TheAnswerIsAffordableAndTheNextStepUpIsNot()
    {
        const double Available = 9_000.0;

        double3 pad = Equator(0.0);
        double3 aim = Equator(0.46);
        double3 moving = Earth.GroundVelocityCci(pad);

        double steepest = Steepest(Available);
        Assert.True(steepest > 0.0, "nothing was affordable, so there is no boundary to check");

        Assert.True(Cost(steepest) <= Available,
                    $"{steepest:F1} deg costs {Cost(steepest):F0} m/s of {Available:F0}");

        double past = steepest + 4.0 * ArrivalBudget.ResolutionDeg;
        Assert.True(Cost(past) > Available,
                    $"{past:F1} deg costs {Cost(past):F0} m/s, which is inside the budget");

        double Cost(double floorDeg)
            => BallisticArc.TryCheapest(Earth, pad, moving, aim, out BallisticArc.Solution arc,
                                        1.0, false, double.NaN, floorDeg)
                   ? Vec.Len(arc.RequiredVelocityCci - moving)
                   : double.PositiveInfinity;
    }

    /// <summary>
    /// A stack that cannot fly the shot at all reports <b>zero</b>, not NaN. The two mean different
    /// things to the panel — one is "you are short of propellant", which the reach readout already
    /// says in words, and the other is "nothing has been costed yet".
    /// </summary>
    [Fact]
    public void AStackThatCannotAffordAnyArcReportsZeroRatherThanNothing()
    {
        double answer = Steepest(50.0);

        Assert.True(double.IsFinite(answer), "an unaffordable shot must still be a number");
        Assert.Equal(0.0, answer, 3);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void NothingInTheTanksIsUnknownRatherThanZero(double available)
    {
        Assert.True(double.IsNaN(Steepest(available)));
    }

    /// <summary>
    /// A stack with an implausible amount in it still stops somewhere sensible. Past about eighty
    /// degrees an arc is a vertical drop and the search would otherwise run to the pole.
    /// </summary>
    [Fact]
    public void TheCeilingIsBoundedEvenForAStackThatCanAffordAnything()
    {
        double answer = Steepest(60_000.0);

        Assert.True(answer <= ArrivalBudget.SteepestConsideredDeg,
                    $"answered {answer:F1} deg, past the {ArrivalBudget.SteepestConsideredDeg:F0} considered");
    }
}
