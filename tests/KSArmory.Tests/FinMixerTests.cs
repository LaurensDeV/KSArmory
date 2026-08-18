using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// One lateral command resolved into four blade deflections. Drawn only — nothing here feeds the
/// flight model — but it is the thing that makes a steering round look like it is steering, and
/// the mixing has to be right or the set rolls when it should turn.
/// </summary>
public class FinMixerTests
{
    private const double Authority = 30.0;
    private const double MaxRad = 0.35;

    /// <summary>Cruciform, rolled 45 degrees, which is how a store straddles its rack.</summary>
    private static double Roll(int i) => FinMixer.FinRollRad(i, 4, Math.PI / 4);

    [Fact]
    public void OppositeBladesDeflectOppositeWays()
    {
        double3 command = new(0, Authority, 0);

        double a = FinMixer.DeflectionRad(command, Roll(0), Authority, MaxRad);
        double across = FinMixer.DeflectionRad(command, Roll(2), Authority, MaxRad);

        Assert.True(Math.Abs(a) > 1e-6, "the blade contributing to the demand stayed neutral");
        Assert.Equal(-a, across, 9);
    }

    /// <summary>
    /// A blade edge-on to the demand produces nothing, so it is drawn neutral. Deflecting it would
    /// be a roll input the round never asked for.
    /// </summary>
    [Fact]
    public void ABladeEdgeOnToTheDemandStaysNeutral()
    {
        // Its lift acts along its own normal, so the demand square to that does nothing.
        double roll = Roll(0);
        double3 alongTheBlade = new(0, Math.Cos(roll), Math.Sin(roll));

        Assert.Equal(0.0, FinMixer.DeflectionRad(alongTheBlade * Authority, roll, Authority, MaxRad), 9);
    }

    [Fact]
    public void TravelSaturatesRatherThanRunningAway()
    {
        double3 enormous = new(0, Authority * 40.0, 0);

        double d = FinMixer.DeflectionRad(enormous, Roll(0), Authority, MaxRad);

        Assert.True(Math.Abs(d) <= MaxRad + 1e-12, $"deflected {d:F3} rad past a {MaxRad} limit");
    }

    /// <summary>
    /// Demand is normalised by the round's own authority, so a 3 g store at its limit and a 35 g
    /// missile at its limit both show full travel. Without it a bomb's blades never visibly move.
    /// </summary>
    [Fact]
    public void FullTravelMeansFullDemandForThatAirframe()
    {
        double bomb = FinMixer.DeflectionRad(new double3(0, 3 * 9.80665, 0), Roll(0),
                                             3 * 9.80665, MaxRad);
        double missile = FinMixer.DeflectionRad(new double3(0, 35 * 9.80665, 0), Roll(0),
                                                35 * 9.80665, MaxRad);

        Assert.Equal(bomb, missile, 9);
    }

    [Fact]
    public void ARoundWithNoAuthorityOrNoTravelDrawsNeutral()
    {
        double3 command = new(0, Authority, 0);

        Assert.Equal(0.0, FinMixer.DeflectionRad(command, Roll(0), 0.0, MaxRad), 12);
        Assert.Equal(0.0, FinMixer.DeflectionRad(command, Roll(0), Authority, 0.0), 12);
        Assert.Equal(0.0, FinMixer.DeflectionRad(new double3(double.NaN, 0, 0), Roll(0),
                                                 Authority, MaxRad), 12);
    }

    /// <summary>The axial part of a command is thrust, not steering, and no blade answers it.</summary>
    [Fact]
    public void NothingAnswersACommandAlongTheNose()
    {
        double3 alongNose = new(Authority, 0, 0);

        for (int i = 0; i < 4; i++)
            Assert.Equal(0.0, FinMixer.DeflectionRad(alongNose, Roll(i), Authority, MaxRad), 9);
    }
}
