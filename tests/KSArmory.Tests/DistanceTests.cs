using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The readout that has to stay legible as the shot improves.
///
/// <para>Every aim readout printed kilometres to one decimal, which was right when a miss was
/// kilometres and is blind now: on 2026-09-07-1824 the log read <c>bias 0.0 km</c> on every flight
/// while the correction closed 4 km down to 20 m. <c>docs/ACCURACY-PLAN.md</c> 3bx.</para>
/// </summary>
public class DistanceTests
{
    [Theory]
    [InlineData(0.0, "0.0 m")]
    [InlineData(8.4, "8.4 m")]
    [InlineData(18.0, "18.0 m")]
    [InlineData(-12.5, "-12.5 m")]
    [InlineData(999.9, "999.9 m")]
    public void AMetreScaleMissIsSaidInMetres(double metres, string said)
        => Assert.Equal(said, Distance.Say(metres));

    [Theory]
    [InlineData(1_000.0, "1.00 km")]
    [InlineData(4_020.0, "4.02 km")]
    [InlineData(90_000.0, "90.00 km")]
    public void AKilometreScaleMissIsSaidInKilometres(double metres, string said)
        => Assert.Equal(said, Distance.Say(metres));

    /// <summary>
    /// The readings the old format destroyed. Each of these printed as "0.0 km" — the whole of what
    /// a ten-metre shot is made of, rendered as nothing.
    /// </summary>
    [Theory]
    [InlineData(8.0)]
    [InlineData(18.0)]
    [InlineData(49.9)]
    public void TheReadingsTheOldFormatPrintedAsZero(double metres)
    {
        Assert.Equal("0.0 km", $"{metres / 1000.0:F1} km");
        Assert.NotEqual("0.0 m", Distance.Say(metres));
    }

    /// <summary>
    /// Absent rather than zero: this readout separates a converged loop from a broken one, and
    /// printing an unreadable state as a number is the failure the type exists to stop.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnreadableDistanceSaysSoRatherThanReadingZero(double metres)
        => Assert.Equal("unknown", Distance.Say(metres));
}
