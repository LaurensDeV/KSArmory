using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The cloud's shape over time. These pin the ratios that make it read as a mushroom rather than
/// as a plume, which is the whole reason the choreography is here instead of in the drawing.
/// </summary>
public class MushroomCloudTests
{
    private const double Kt = 1.0e6;      // kg of TNT equivalent in a kilotonne

    /// <summary>
    /// The sizes are Glasstone's. Checked against the worked table in docs/NUCLEAR-EFFECT.md so a
    /// change to the laws has to be a deliberate one.
    ///
    /// <para>Ten percent, because the reference is not self-consistent to better than that: its
    /// cloud figures come from the cube-root form below ten kilotonnes and from the Fig 2.16
    /// polynomial above, and the two only agree to about a tenth where they overlap. A tighter
    /// tolerance would be pinning one source's rounding rather than the law.</para>
    /// </summary>
    [Theory]
    [InlineData(0.3, 34.0, 2010.0, 390.0)]
    [InlineData(1.5, 65.0, 3430.0, 700.0)]
    [InlineData(10.0, 138.0, 6460.0, 1410.0)]
    [InlineData(50.0, 263.0, 11040.0, 2350.0)]
    public void TheSizeLawsMatchTheReference(double kt, double fireball, double top, double cap)
    {
        // Within a few percent: the reference table is itself rounded.
        Assert.True(Math.Abs(MushroomCloud.FireballRadius(kt) - fireball) < fireball * 0.10,
                    $"fireball {MushroomCloud.FireballRadius(kt):F0} m against {fireball:F0}");
        Assert.True(Math.Abs(MushroomCloud.CloudTop(kt) - top) < top * 0.10,
                    $"cloud top {MushroomCloud.CloudTop(kt):F0} m against {top:F0}");
        Assert.True(Math.Abs(MushroomCloud.CapRadius(kt) - cap) < cap * 0.10,
                    $"cap {MushroomCloud.CapRadius(kt):F0} m against {cap:F0}");
    }

    /// <summary>
    /// The stem starts later and climbs slower, so it never reaches the cap. A stem that keeps up
    /// draws a column with a ball on it, which is a plume.
    /// </summary>
    [Fact]
    public void TheStemLagsTheCapAndNeverCatchesIt()
    {
        for (double age = 0.5; age < MushroomCloud.RiseSeconds; age += 0.5)
        {
            MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, age);
            Assert.True(s.StemTop < s.CapCentre,
                        $"at {age:F1} s the stem reached {s.StemTop:F0} m against a cap at {s.CapCentre:F0} m");
        }
    }

    /// <summary>The cap is twice the stem's width, which is Glasstone's ratio below 20 kt.</summary>
    [Fact]
    public void TheCapIsWiderThanTheStem()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        Assert.True(s.CapRadius > s.StemRadius * 2.0,
                    $"cap {s.CapRadius:F0} m against stem {s.StemRadius:F0} m");
    }

    /// <summary>
    /// It climbs fast and then eases, rather than rising steadily. A linear climb reads as a lift.
    /// </summary>
    [Fact]
    public void ItRisesQuicklyThenSettles()
    {
        double early = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds * 0.25).CapCentre;
        double late = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds).CapCentre;

        Assert.True(early > late * 0.4,
                    "a quarter of the way through the rise should already be well up");
        Assert.True(early < late * 0.9, "and not yet at the ceiling");
    }

    /// <summary>
    /// The roll decays to a stop: entrained air cools the toroid and kills the circulation as it
    /// nears its ceiling. A cap still spinning at the end reads as a special effect.
    /// </summary>
    [Fact]
    public void TheToroidalRollSlowsAsItReachesTheCeiling()
    {
        double a = MushroomCloud.At(0.3 * Kt, 2.0).Roll;
        double b = MushroomCloud.At(0.3 * Kt, 6.0).Roll;
        double c = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds).Roll;

        // Rates, not differences: the two intervals are different lengths, and comparing raw
        // deltas across them says more about the sampling than about the roll.
        double early = (b - a) / 4.0;
        double late = (c - b) / (MushroomCloud.RiseSeconds - 6.0);

        Assert.True(early > late, $"the roll should be slowing: {early:F4} then {late:F4} rad/s");
    }

    /// <summary>Nothing is drawn before it exists or after it has gone.</summary>
    [Fact]
    public void ItIsSpentOutsideItsLife()
    {
        Assert.True(MushroomCloud.At(0.3 * Kt, -1.0).Spent);
        Assert.True(MushroomCloud.At(0.3 * Kt, MushroomCloud.LifeSeconds + 1.0).Spent);
        Assert.False(MushroomCloud.At(0.3 * Kt, 1.0).Spent);
    }

    /// <summary>A conventional charge grows no cloud at all, however the caller is feeling.</summary>
    [Fact]
    public void AChemicalChargeHasNoCloud()
    {
        Assert.True(Arsenal.BombMk82.ChargeKg < MushroomCloud.ThresholdKg);
        Assert.True(Arsenal.NukeB61.ChargeKg > MushroomCloud.ThresholdKg);
    }

    /// <summary>At the end of its stroke the ring of pens is a ring, evenly spread about the axis.</summary>
    [Fact]
    public void TheCapPointsRingTheAxis()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);

        const int Count = 8;
        var seen = new List<double3>();

        for (int i = 0; i < Count; i++)
        {
            double3 at = MushroomCloud.CapPoint(s, i, Count, 1.0, 1.0, up, east, north);

            Assert.Equal(s.CapRadius, Math.Sqrt((at.X * at.X) + (at.Y * at.Y)), 6);

            foreach (double3 other in seen)
            {
                Assert.True(Vec.Len(at - other) > s.CapRadius * 0.5, "cap emitters should not bunch");
            }

            seen.Add(at);
        }
    }

    /// <summary>
    /// A pen climbs before it spreads. If it flares from the ground the cloud is a cone, not a
    /// column with a head on it.
    /// </summary>
    [Fact]
    public void APenClimbsBeforeItFlares()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);

        double3 early = MushroomCloud.CapPoint(s, 0, 8, 0.3, 1.0, up, east, north);
        double Radial(double3 v) => Math.Sqrt((v.X * v.X) + (v.Y * v.Y));

        Assert.True(early.Z > s.CapCentre * 0.35, "it should be well up the axis by a third of the way");
        Assert.True(Radial(early) < s.CapRadius * 0.05, "and barely off it");
    }

    /// <summary>
    /// The lip curls back down. That overhang is the mushroom's silhouette, and without it the
    /// shape is a tree.
    /// </summary>
    [Fact]
    public void TheCapLipCurlsUnder()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);

        double highest = MushroomCloud.CapPoint(s, 0, 8, 0.78, 1.0, up, east, north).Z;
        double lip = MushroomCloud.CapPoint(s, 0, 8, 1.0, 1.0, up, east, north).Z;

        Assert.True(lip < highest, $"the lip at {lip:F0} m should hang below the crown at {highest:F0} m");
    }

    /// <summary>
    /// A ring of pens has to close into a surface, and that is arithmetic rather than taste: a
    /// capsule of radius R is solid only within 0.55 R, so pens spaced further apart than 1.1 R
    /// leave clear air between them and the cloud reads as ropes.
    ///
    /// <para>This is the rule the first two attempts broke — eight cap pens 300 m apart with 80 m
    /// tubes, then four stem pens at 144 m with 94 m tubes, which is where the pillars came
    /// from.</para>
    /// </summary>
    [Theory]
    [InlineData(20, 0.90)]      // the rim, at CapExpanded
    public void ARingOfPensCloses(int count, double tubeFraction)
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);

        double tube = s.CapTube * tubeFraction;
        double pitch = MushroomCloud.RingPitch(s, count);

        Assert.True(pitch <= 1.1 * tube,
                    $"{count} pens give a {pitch:F0} m pitch against a {tube:F0} m tube; "
                    + $"needs {1.1 * tube:F0} m or less, or it reads as strands");
    }

    /// <summary>
    /// A cap is a dome about as tall as it is wide at these yields, not a plate. The flat anvil
    /// everyone pictures is megaton-scale and mostly later-time spreading.
    /// </summary>
    [Fact]
    public void TheCapIsADomeRatherThanALid()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);

        double crown = MushroomCloud.CapPoint(s, 0, 8, 1.0, 0.55, up, east, north).Z;
        double rim = MushroomCloud.CapPoint(s, 0, 8, 1.0, 1.0, up, east, north).Z;

        Assert.True(crown > rim, $"the crown at {crown:F0} m should stand above the rim at {rim:F0} m");
    }

    /// <summary>
    /// The pens must not wind round the axis. They are trails that keep every position they have
    /// held, so a full turn draws a helix and a ring of them is a spiral staircase rather than a
    /// cloud. This is the shape that shipped once.
    /// </summary>
    [Fact]
    public void APenDoesNotWindAroundTheAxis()
    {
        for (double age = 0.0; age < MushroomCloud.RiseSeconds; age += 0.25)
        {
            MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, age);
            Assert.True(Math.Abs(s.Roll) < 0.6,
                        $"at {age:F1} s the roll is {s.Roll:F2} rad, which starts to wind");
        }
    }
}
