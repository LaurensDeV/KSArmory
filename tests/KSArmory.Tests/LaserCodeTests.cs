using Xunit;

namespace KSArmory.Tests;

public class LaserCodeTests
{
    [Theory]
    [InlineData(1111, true)]
    [InlineData(1688, true)]
    [InlineData(1788, true)]
    [InlineData(1110, false)]
    [InlineData(1789, false)]
    [InlineData(1811, false)]
    [InlineData(1191, false)]
    [InlineData(1119, false)]
    [InlineData(1018, false)]
    [InlineData(2111, false)]
    public void OnlyNatoCodesAreValid(int code, bool valid) => Assert.Equal(valid, LaserCode.IsValid(code));

    [Fact]
    public void TheDefaultIsAValidCode() => Assert.True(LaserCode.IsValid(LaserCode.Default));
}
