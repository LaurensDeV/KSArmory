using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>Where each warhead of a salvo is aimed, once they are not all aimed at one point.</summary>
public class WarheadFootprintTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private static readonly double3 Aim = new(R * 0.6, R * 0.8, 0.0);
    private static readonly double3 Axis = new(0, 0, 1);

    /// <summary>Zero is every flight before this one: all six on the designation.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void WithoutAFootprintEveryWarheadKeepsTheDesignation(double metres)
    {
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(Aim, WarheadFootprint.AimFor(Aim, Axis, metres, i, 6));
        }
    }

    /// <summary>Six points, evenly spaced, at the radius asked for, and all on the surface.</summary>
    [Fact]
    public void SixWarheadsLandOnARingOfTheStatedRadius()
    {
        const double Metres = 3.0;
        double3[] aims = [.. Enumerable.Range(0, 6).Select(i => WarheadFootprint.AimFor(Aim, Axis, Metres, i, 6))];

        foreach (double3 a in aims)
        {
            Assert.Equal(Vec.Len(Aim), Vec.Len(a), 3);                 // still on the surface
            Assert.Equal(Metres, Vec.Len(a - Aim), 3);                 // at the radius asked for
        }

        // Evenly spaced: neighbours are all the same distance apart.
        double first = Vec.Len(aims[1] - aims[0]);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(first, Vec.Len(aims[(i + 1) % 6] - aims[i]), 3);
        }

        Out.WriteLine($"a {Metres} m ring: neighbours {first:F3} m apart, "
                      + $"opposite pair {Vec.Len(aims[3] - aims[0]):F3} m");

        // And they are actually distinct, which is the whole point.
        Assert.Equal(6, aims.Distinct().Count());
    }

    /// <summary>
    /// The cap is what bounds this, and it is worth being able to ask before flying rather than
    /// reading a refusal afterwards.
    /// </summary>
    [Fact]
    public void TheCapBoundsTheFootprintAndSaysSo()
    {
        const double Flight = 345.0;
        double widest = WarheadFootprint.WidestAt(Flight);

        Out.WriteLine($"on a {Flight:F0} s flight the cap allows {widest:F2} m");

        Assert.Equal(ReleaseFocus.MaxMissKickMetresPerSecond * Flight, widest, 9);
        Assert.True(WarheadFootprint.WithinTheCap(widest * 0.99, Flight));
        Assert.False(WarheadFootprint.WithinTheCap(widest * 1.01, Flight));

        // A longer flight buys a wider one, because the ask is offset over time.
        Assert.True(WarheadFootprint.WidestAt(Flight * 2.0) > widest);

        // Against what the warhead actually is: two body lengths, and nothing beside its fireball.
        Out.WriteLine($"   the Mk 21 is {Arsenal.ReentryVehicleMk21.BodyLength:F2} m long and its "
                      + $"fireball {Warhead.FireballRadius(Arsenal.ReentryVehicleMk21.ChargeKg):F0} m");
        Assert.True(widest > Arsenal.ReentryVehicleMk21.BodyLength,
                    "a footprint that cannot clear one body length is not worth the setting");
        Assert.True(widest < Warhead.FireballRadius(Arsenal.ReentryVehicleMk21.ChargeKg),
                    "if the cap ever cleared the fireball this would be a MIRV footprint and the "
                    + "documentation saying it is not would be wrong");
    }
}
