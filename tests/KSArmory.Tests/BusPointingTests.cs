using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Turning the bus so each tube in turn throws along one line.
/// </summary>
public class BusPointingTests(ITestOutputHelper Out)
{
    private static double3[] BusTubes()
    {
        Tube[] tubes = Arsenal.MirvBus.Tubes;
        double3[] axes = new double3[tubes.Length];
        for (int i = 0; i < tubes.Length; i++) axes[i] = Vec.Unit(tubes[i].Direction);
        return axes;
    }

    private static double Degrees(double radians) => radians * 180.0 / Math.PI;

    [Fact]
    public void TheMeanOfTheBusTubesIsThePartsOwnAxis()
    {
        double3 reference = BusPointing.ReferenceAxis(BusTubes());

        Assert.True(Vec.AngleBetween(reference, new double3(1, 0, 0)) < 1e-12,
                    "the cants are meant to cancel in the mean; if they do not, the launcher's "
                    + "axis is not what the aim correction thinks it is");
    }

    [Fact]
    public void EveryTubeLandsOnTheReferenceAfterItsRepoint()
    {
        double3[] axes = BusTubes();
        double3 reference = BusPointing.ReferenceAxis(axes);

        for (int tube = 0; tube < axes.Length; tube++)
        {
            double3 pointed = BusPointing.Repoint(axes[tube], reference) * axes[tube];
            Assert.True(Vec.AngleBetween(pointed, reference) < 1e-9, $"tube {tube + 1} missed the line");
        }
    }

    /// <summary>
    /// The shape the problem has: one cant per tube, and the axis of that turn walking round the
    /// clock. An index or sign error still lands every tube on the line and fails this.
    /// </summary>
    [Fact]
    public void TheRepointIsOneCantAndItsAxisWalksRoundTheClock()
    {
        double3[] axes = BusTubes();
        double3 reference = BusPointing.ReferenceAxis(axes);

        double3[] turnAxes = new double3[axes.Length];

        for (int tube = 0; tube < axes.Length; tube++)
        {
            Assert.Equal(6.0, Degrees(Vec.AngleBetween(axes[tube], reference)), 3);
            turnAxes[tube] = Vec.Unit(Vec.Cross(axes[tube], reference));
        }

        for (int tube = 0; tube < axes.Length; tube++)
        {
            double apart = Degrees(Vec.AngleBetween(turnAxes[tube], turnAxes[(tube + 1) % axes.Length]));
            Out.WriteLine($"tube {tube + 1} -> {(tube + 1) % axes.Length + 1}: {apart:F1} deg apart");
            Assert.Equal(60.0, apart, 1);
        }
    }

    [Fact]
    public void TheCommandIsTurnedRatherThanRebuilt()
    {
        double3[] axes = BusTubes();
        double3 reference = BusPointing.ReferenceAxis(axes);

        double3 held = Vec.Unit(new double3(0.3, -0.9, 0.2));
        double3 roll = Vec.Unit(new double3(0.7, 0.4, 0.1));
        double before = Vec.AngleBetween(held, roll);

        Assert.True(BusPointing.TryAimTube(axes[2], reference, held, roll,
                                           out double3 direction, out double3 turnedRoll));

        Assert.Equal(before, Vec.AngleBetween(direction, turnedRoll), 12);
        Assert.Equal(1.0, Vec.Len(direction), 12);
        Assert.Equal(1.0, Vec.Len(turnedRoll), 12);
        Assert.True(Vec.Len2(Vec.Cross(turnedRoll, direction)) > 0.0);
    }

    /// <summary>
    /// The trap, made a test so nobody simplifies the latch away.
    ///
    /// <para>The rotation has to be built from where the tube points at the bus's <em>nominal</em>
    /// attitude, latched once. Re-measuring it live and rotating the nominal command by that
    /// instead is a loop that never settles: the tube alternates between on the line and a full
    /// cant off it, for ever, and half the time it looks perfect.</para>
    /// </summary>
    [Fact]
    public void TakingTheLiveAxisInsteadOfTheLatchedOneNeverSettles()
    {
        double3[] axes = BusTubes();
        double3 reference = BusPointing.ReferenceAxis(axes);
        double3 nominal = axes[0];

        double worst = 0.0;
        double3 live = nominal;

        for (int i = 0; i < 200; i++)
        {
            live = Vec.Unit(BusPointing.Repoint(live, reference) * nominal);
            if (i > 100) worst = Math.Max(worst, Degrees(Vec.AngleBetween(live, reference)));
        }

        Out.WriteLine($"the live-axis rule is still {worst:F3} deg off the line after 200 cycles");

        Assert.True(worst > 5.0, "the live-axis rule is supposed to never settle; if it does, the "
                                 + "latch is no longer what makes this exact and this test is moot");

        // Where the latched rule puts it, for contrast.
        double3 latched = BusPointing.Repoint(nominal, reference) * nominal;
        Assert.True(Vec.AngleBetween(latched, reference) < 1e-9);
    }
}
