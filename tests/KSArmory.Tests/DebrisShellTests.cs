using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A burst's debris where the air is too thin for a mushroom: a white-hot ball that cools into a red
/// shell and fades within minutes, stretched along the field where the air is thinnest.
/// </summary>
public class DebrisShellTests(ITestOutputHelper output)
{
    private const double Charge = 50.0e9;           // 50 Mt
    private const double Height = 100_000.0;
    private const double AirAt100Km = 3.7e-6;

    [Fact]
    public void ItStartsWhiteHotAndFillingTheBall()
    {
        // Teak's was measured at 0.3 s: its white-hot phase is seconds at this height, not tens of them.
        DebrisShell.Look early = DebrisShell.At(Charge, 0.3, Height, AirAt100Km);

        Assert.False(early.Spent);
        Assert.True(early.Fill > 0.9, $"fill {early.Fill:F2}");
        Assert.True(early.Colour.Z > 0.5, "white-hot has blue in it");
        Assert.True(early.Radiance > 20.0, $"radiance {early.Radiance:F1}");
    }

    [Fact]
    public void ItCoolsIntoARedShellAndIsGoneInMinutes()
    {
        DebrisShell.Look late = DebrisShell.At(Charge, 60.0, Height, AirAt100Km);
        output.WriteLine($"at 60 s: radiance {late.Radiance:F2}, colour {late.Colour.X:F2} {late.Colour.Y:F2} {late.Colour.Z:F2}, "
                         + $"fill {late.Fill:F2}, stretched {late.Elongation:F2}x");

        Assert.Equal(0.0, late.Fill, 6);
        Assert.True(late.Colour.X > 3.0 * late.Colour.Y, "red");
        Assert.True(late.Radiance < DebrisShell.At(Charge, 1.0, Height, AirAt100Km).Radiance / 50.0);

        Assert.True(DebrisShell.At(Charge, DebrisShell.LifeSeconds, Height, AirAt100Km).Spent);
        Assert.True(DebrisShell.At(Charge, DebrisShell.LifeSeconds - 1.0, Height, AirAt100Km).Radiance < 0.05);
    }

    [Fact]
    public void ItFadesWithoutBrighteningOnceTheBallHasCooled()
    {
        double was = double.MaxValue;
        for (double age = 20.0; age < DebrisShell.LifeSeconds; age += 5.0)
        {
            double now = DebrisShell.At(Charge, age, Height, AirAt100Km).Radiance;
            Assert.True(now <= was + 1e-9, $"{now:F3} at {age} s after {was:F3}");
            was = now;
        }
    }

    [Fact]
    public void TheFieldStretchesItOnlyWhereTheAirIsThinEnough()
    {
        Assert.True(DebrisShell.At(Charge, 40.0, 35_000.0, 9.0e-3).Elongation < 1.05);

        DebrisShell.Look high = DebrisShell.At(Charge, 40.0, Height, AirAt100Km);
        Assert.Equal(DebrisShell.MostElongation, high.Elongation, 6);

        Assert.Equal(1.0, DebrisShell.At(Charge, 0.0, Height, AirAt100Km).Elongation, 6);
    }

    [Fact]
    public void TheFieldRunsNorthSouthOverTheEquatorAndStraightDownAtThePole()
    {
        double3 axis = new(0, 0, 1);

        double3 equator = DebrisShell.FieldDirection(new double3(1, 0, 0), axis);
        Assert.Equal(1.0, Math.Abs(equator.Z), 9);

        double3 pole = DebrisShell.FieldDirection(new double3(0, 0, 1), axis);
        Assert.Equal(1.0, Math.Abs(pole.Z), 9);

        // At 45 degrees the dip is atan(2 tan 45) = 63.4 degrees below the horizontal.
        double3 up = Vec.Unit(new double3(1, 0, 1));
        double3 field = DebrisShell.FieldDirection(up, axis);
        double dip = Math.Asin(Math.Abs(Vec.Dot(field, up))) * 180.0 / Math.PI;
        Assert.Equal(63.43, dip, 1);
    }
}
