using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="Kepler.TryCoast"/> against <c>Reference/kepler.csv</c>, solved by the change in
/// eccentric or hyperbolic anomaly and checked there against RK4 to 1e-4 m.
/// </summary>
public class KeplerReferenceTests
{
    private const double ToleranceMetres = 1e-3;
    private const double ToleranceMetresPerSecond = 1e-3;

    private static readonly IReadOnlyList<(string Label, double[] Values)> Table = ReferenceTable.Read("kepler.csv");

    public static IEnumerable<object[]> Cases() => Table.Select((row, i) => new object[] { i, row.Label });

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheCoastMatchesTheReference(int index, string label)
    {
        double[] v = Table[index].Values;
        double mu = v[0];
        double3 position = new(v[1], v[2], v[3]);
        double3 velocity = new(v[4], v[5], v[6]);
        double seconds = v[7];
        double3 expectedPosition = new(v[8], v[9], v[10]);
        double3 expectedVelocity = new(v[11], v[12], v[13]);

        Assert.True(Kepler.TryCoast(mu, position, velocity, seconds, out double3 r, out double3 w),
                    $"case {index} ({label}): refused");

        double positionError = Vec.Len(r - expectedPosition);
        double velocityError = Vec.Len(w - expectedVelocity);
        Assert.True(positionError < ToleranceMetres && velocityError < ToleranceMetresPerSecond,
                    $"case {index} ({label}) after {seconds:F0} s: position off by {positionError * 1000:F3} mm, " +
                    $"velocity by {velocityError * 1000:F3} mm/s");
    }
}
