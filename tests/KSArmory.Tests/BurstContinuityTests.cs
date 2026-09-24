using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A burst is watched frame by frame, so anything it draws has to change smoothly between frames —
/// including when it goes away. A step shows as something switched on or off, whatever the curve
/// either side of it looks like.
///
/// <para>A pop is judged against the frames beside it rather than against a fixed limit: a change
/// several times both its neighbours' is a step on any curve, bright or dim, fast or slow, so one
/// rule serves every signal without a threshold tuned to each.</para>
/// </summary>
public class BurstContinuityTests
{
    private const double Frame = 1.0 / 60.0;

    // How many times its neighbours' change a frame's must be to read as a step, and the change
    // under which nothing is visible anyway.
    private const double StepOverNeighbours = 4.0;
    private const double Negligible = 1.0e-3;

    public static TheoryData<double> Yields => new() { 0.3, 20.0, 340.0, 1000.0 };

    [Theory]
    [MemberData(nameof(Yields))]
    public void TheFireballNeverChangesInOneFrame(double kt)
    {
        double[] ages = Ages(kt);

        // The glow over everything, removal included: that is what says whether it went out or was
        // taken away. The radius only while it is drawn, because a size nobody can see cannot pop.
        AssertNoPops($"{kt} kt glow", ages, a => MushroomCloud.FlashAt(kt * 1.0e6, a).Glow);
        AssertNoPops($"{kt} kt fireball radius",
                     ages.Where(a => !MushroomCloud.FlashAt(kt * 1.0e6, a).Spent).ToArray(),
                     a => MushroomCloud.FlashAt(kt * 1.0e6, a).Radius);
    }

    [Theory]
    [MemberData(nameof(Yields))]
    public void TheCloudNeverChangesInOneFrame(double kt)
    {
        double chargeKg = kt * 1.0e6;

        // Likewise: the fade through its removal, the shape only while there is a cloud.
        AssertNoPops($"{kt} kt fade", Ages(kt), a => MushroomCloud.At(chargeKg, a).Fade);

        double[] ages = Ages(kt).Where(a => a < MushroomCloud.LifeSeconds).ToArray();

        AssertNoPops($"{kt} kt cap centre", ages, a => MushroomCloud.At(chargeKg, a).CapCentre);
        AssertNoPops($"{kt} kt cap radius", ages, a => MushroomCloud.At(chargeKg, a).CapRadius);
        AssertNoPops($"{kt} kt cap tube", ages, a => MushroomCloud.At(chargeKg, a).CapTube);
        AssertNoPops($"{kt} kt stem top", ages, a => MushroomCloud.At(chargeKg, a).StemTop);
        AssertNoPops($"{kt} kt stem radius", ages, a => MushroomCloud.At(chargeKg, a).StemRadius);
        AssertNoPops($"{kt} kt surge radius", ages, a => MushroomCloud.At(chargeKg, a).SurgeRadius);
    }

    // The whole life at 60 fps and a little past it, so what ends is seen ending. The first frame is
    // left out: the flash has to start somewhere.
    private static double[] Ages(double kt)
    {
        int frames = (int)Math.Ceiling((MushroomCloud.LifeSeconds + 1.0) / Frame);
        return Enumerable.Range(1, frames).Select(i => i * Frame).ToArray();
    }

    private static void AssertNoPops(string what, double[] ages, Func<double, double> signal)
    {
        double[] v = ages.Select(signal).ToArray();
        var pops = new List<string>();

        for (int i = 1; i < v.Length - 1; i++)
        {
            double step = Math.Abs(v[i] - v[i - 1]);
            if (step < Negligible) continue;

            double before = i >= 2 ? Math.Abs(v[i - 1] - v[i - 2]) : step;
            double after = Math.Abs(v[i + 1] - v[i]);

            if (step > StepOverNeighbours * Math.Max(before, after) + Negligible)
            {
                pops.Add($"{ages[i]:F2} s: {v[i - 1]:G4} -> {v[i]:G4}");
            }
        }

        Assert.True(pops.Count == 0, $"{what} steps in one frame at {string.Join("; ", pops.Take(5))}");
    }
}
