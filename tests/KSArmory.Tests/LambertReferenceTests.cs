using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="Lambert"/> against <c>Reference/lambert.csv</c>, which Izzo's method and Lagrange's
/// time equation agree on to 1e-7 m/s and an independent propagator flies onto the target.
/// </summary>
public class LambertReferenceTests
{
    // A long shot turns a metre a second at burnout into one to two kilometres on the ground.
    private const double ToleranceMetresPerSecond = 1e-3;

    private static readonly IReadOnlyList<(string Label, double[] Values)> Table = ReferenceTable.Read("lambert.csv");

    public static IEnumerable<object[]> Cases() => Table.Select((row, i) => new object[] { i, row.Label });

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheTransferMatchesTheReference(int index, string label)
    {
        double[] v = Table[index].Values;
        double mu = v[0];
        double3 from = new(v[1], v[2], v[3]);
        double3 to = new(v[4], v[5], v[6]);
        double seconds = v[7];
        bool longWay = v[8] != 0.0;
        double3 departure = new(v[9], v[10], v[11]);
        double3 arrival = new(v[12], v[13], v[14]);

        Assert.True(Lambert.TrySolve(from, to, seconds, mu, out Lambert.Transfer t, longWay), $"{label}: refused");

        double departureError = Vec.Len(t.DepartureVelocityCci - departure);
        double arrivalError = Vec.Len(t.ArrivalVelocityCci - arrival);
        Assert.True(departureError < ToleranceMetresPerSecond && arrivalError < ToleranceMetresPerSecond,
                    $"{label} (psi {v[15]:G4}): departure off by {departureError * 1000:F3} mm/s, " +
                    $"arrival by {arrivalError * 1000:F3} mm/s");
    }
}
