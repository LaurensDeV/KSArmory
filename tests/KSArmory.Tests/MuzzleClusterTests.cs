using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which muzzles share a flash. Averaging every muzzle into one point is right for a rotary cannon
/// and wrong for anything with a sponson either side: the mean of two clusters is the gap between
/// them, which on a Pantsir is the middle of the hull, where there is no gun.
/// </summary>
public class MuzzleClusterTests
{
    private const double R = TubeGeometry.GunFlashClusterMetres;

    /// <summary>A Phalanx's six barrels are one cluster and keep one flash.</summary>
    [Fact]
    public void ARotaryClusterStaysOneGroup()
    {
        // Arsenal.Ciws.GunMuzzles, which span about 0.21 m.
        double3[] muzzles =
        [
            new(-0.19500, 1.98000,  0.00000),
            new(-0.24750, 1.98000,  0.09093),
            new(-0.35250, 1.98000,  0.09093),
            new(-0.40500, 1.98000,  0.00000),
            new(-0.35250, 1.98000, -0.09093),
            new(-0.24750, 1.98000, -0.09093),
        ];

        Span<int> into = stackalloc int[muzzles.Length];
        Assert.Equal(1, TubeGeometry.ClusterMuzzles(muzzles, R, into));
        foreach (int g in into) Assert.Equal(0, g);
    }

    /// <summary>
    /// A Pantsir's four are two sponsons, and must stay two — this is the case the average broke.
    /// </summary>
    [Fact]
    public void TwoSponsonsStayTwoGroups()
    {
        // Arsenal.PantsirS1.GunMuzzles: pairs at Z = -1.94/-1.76 and +1.76/+1.94.
        double3[] muzzles =
        [
            new(1.01144, 2.50340, -1.94000),
            new(1.01144, 2.50340, -1.76000),
            new(1.01144, 2.50340,  1.76000),
            new(1.01144, 2.50340,  1.94000),
        ];

        Span<int> into = stackalloc int[muzzles.Length];
        Assert.Equal(2, TubeGeometry.ClusterMuzzles(muzzles, R, into));

        Assert.Equal(into[0], into[1]);
        Assert.Equal(into[2], into[3]);
        Assert.NotEqual(into[0], into[2]);

        // And the thing that made this visible: the mean of all four is on the centreline.
        double3 mean = (muzzles[0] + muzzles[1] + muzzles[2] + muzzles[3]) / 4.0;
        Assert.Equal(0.0, mean.Z, 6);
        Assert.True(Math.Abs(muzzles[0].Z) > 1.5, "which is nowhere near either sponson");
    }

    /// <summary>A long row of barrels stays one group, because the link is to any member.</summary>
    [Fact]
    public void ARowLinksThroughItsNeighbours()
    {
        double3[] row =
        [
            new(0, 0, 0.0), new(0, 0, 0.5), new(0, 0, 1.0), new(0, 0, 1.5),
        ];

        Span<int> into = stackalloc int[row.Length];

        // End to end is 1.5 m, well past the radius, but each step is 0.5 m and inside it.
        Assert.Equal(1, TubeGeometry.ClusterMuzzles(row, R, into));
    }

    /// <summary>Every muzzle lands in exactly one group, and the indices are dense from zero.</summary>
    [Fact]
    public void EveryMuzzleIsAssignedExactlyOnce()
    {
        double3[] muzzles =
        [
            new(0, 0, 0), new(0, 0, 0.1),
            new(0, 0, 9), new(0, 0, 9.1),
            new(0, 0, 20),
        ];

        Span<int> into = stackalloc int[muzzles.Length];
        int groups = TubeGeometry.ClusterMuzzles(muzzles, R, into);

        Assert.Equal(3, groups);
        foreach (int g in into) Assert.InRange(g, 0, groups - 1);

        for (int g = 0; g < groups; g++)
        {
            int members = 0;
            foreach (int x in into) if (x == g) members++;
            Assert.True(members > 0, $"group {g} has no members, so the indices are not dense");
        }
    }

    /// <summary>A gun with no muzzles is no groups, not one empty one.</summary>
    [Fact]
    public void NoMuzzlesIsNoGroups()
    {
        Span<int> into = stackalloc int[4];
        Assert.Equal(0, TubeGeometry.ClusterMuzzles([], R, into));
        Assert.Equal(0, TubeGeometry.ClusterMuzzles(new double3[2], R, Span<int>.Empty));
    }
}
