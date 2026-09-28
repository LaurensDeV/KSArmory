using Xunit;

namespace KSArmory.Tests;

public class ChargeTests
{
    [Theory]
    [InlineData(0.05, "0.05 kg")]
    [InlineData(20.0, "20 kg")]
    [InlineData(999.0, "999 kg")]
    [InlineData(1_000.0, "1 t")]
    [InlineData(300_000.0, "300 t")]
    [InlineData(300_000.0 * 1.1, "330 t")]
    [InlineData(340_000_000.0, "340 kt")]
    [InlineData(322_776_832.0, "322.78 kt")]
    [InlineData(1_200_000_000.0, "1.2 Mt")]
    [InlineData(50_000_000_000.0, "50 Mt")]
    [InlineData(double.NaN, "unknown")]
    public void EachChargeIsSaidInTheUnitThatShowsIt(double kg, string expected)
        => Assert.Equal(expected, Charge.Say(kg));
}
