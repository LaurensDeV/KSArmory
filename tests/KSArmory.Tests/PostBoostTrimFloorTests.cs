using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// That the post-boost loop stops where the trim stops being able to help, rather than three passes
/// after the last 250 m improvement.
///
/// <para>Measured over three nights: a pass on a reading under 10 m made the next one worse 52-86% of
/// the time, while readings with nothing fired between them agree to 0.2 m — and the flat band both
/// trims past a rocket's best and stops one still closing, 141 → 31 → 19 → 14 m released at 14.
/// <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
/// </summary>
public class PostBoostTrimFloorTests
{
    private const double Step = 0.5;
    private const double Floor = 7.3;

    // The flown shot's measured holding cost. The constant's 26 m/s would end every one of these on
    // payback at the first reading.
    private const double Holding = 0.28;

    private static PostBoostSituation Situation(bool trimSettled, double missMetres, bool inside, double floor)
        => new(TrimSettled: trimSettled,
               ReleaseDirectionCci: new double3(1.0, 0.0, 0.0),
               PredictedMissMetres: missMetres,
               AimHasSettled: false,
               TrimSpentMetresPerSecond: 0.0,
               HoldingCostMetresPerSecond: Holding,
               DecideOnTheReading: true,
               ReleaseInsideTheTrimFloor: inside,
               TrimFloorMetres: floor);

    // The readings in order, each off a flown correction as the engine delivers them: the first
    // straight off the cutoff solution; every later one after the trim has flown, settled on a frame
    // the prediction had not reached, and the reading arrived on the next.
    private static PostBoostAim.Decision Fly(PostBoostAim aim, bool inside, double floor,
                                             params double[] readings)
    {
        PostBoostAim.Decision last = default;

        for (int i = 0; i < readings.Length; i++)
        {
            int before = aim.Cycles;

            if (i > 0)
            {
                for (int k = 0; k < 4; k++) aim.Update(Step, Situation(false, double.NaN, inside, floor));
                aim.Update(Step, Situation(true, double.NaN, inside, floor));
            }

            for (int k = 0; k < 20; k++)
            {
                last = aim.Update(Step, Situation(true, readings[i], inside, floor));
                if (last.MayRelease || aim.Cycles > before) break;
            }

            if (last.MayRelease) return last;
        }

        return last;
    }

    [Fact]
    public void AReadingInsideTheTrimFloorIsReleasedOnRatherThanTrimmedOn()
    {
        PostBoostAim aim = new();
        PostBoostAim.Decision last = Fly(aim, inside: true, Floor, 3_800.0, 150.0, 5.0);

        Assert.True(last.MayRelease, $"a reading inside the trim's floor was trimmed on: {last.Said}");
        Assert.Equal(2, aim.Cycles);
        Assert.Contains("inside the", last.Said);
    }

    [Fact]
    public void PassesStillClosingAtTheMetreScaleKeepCorrecting()
    {
        PostBoostAim aim = new();
        PostBoostAim.Decision last = Fly(aim, inside: true, Floor,
                                         3_800.0, 141.0, 31.0, 19.0, 14.0, 9.0, 6.0);

        Assert.True(last.MayRelease, "the loop never released");
        Assert.Contains("6 m out, inside the", last.Said);
        Assert.Equal(6, aim.Cycles);
    }

    /// <summary>Off is today's rule: the same readings take a third pass on the 5 m one.</summary>
    [Fact]
    public void OffTheFloorIsTodaysRule()
    {
        PostBoostAim aim = new();
        PostBoostAim.Decision last = Fly(aim, inside: false, Floor, 3_800.0, 150.0, 5.0);

        Assert.False(last.MayRelease, $"released with the switch off: {last.Said}");
        Assert.Equal(3, aim.Cycles);
    }

    /// <summary>A floor the arc could not price is no floor, rather than zero or the last one.</summary>
    [Fact]
    public void AFloorThatCouldNotBePricedReleasesNothingOnIt()
    {
        PostBoostAim aim = new();
        PostBoostAim.Decision last = Fly(aim, inside: true, double.NaN, 3_800.0, 150.0, 5.0);

        Assert.False(last.MayRelease, $"released on a floor that was never priced: {last.Said}");
        Assert.Equal(3, aim.Cycles);
    }

    [Fact]
    public void TheStopBandIsTheSettleBandUntilHalfAFrameOfJetsIsLarger()
    {
        Assert.Equal(BusTrim.SettledMetresPerSecond, BusTrim.StopBand(0.56, 0.017));
        Assert.Equal(0.5 * 0.56 * 0.1, BusTrim.StopBand(0.56, 0.1), 12);
        Assert.Equal(BusTrim.SettledMetresPerSecond, BusTrim.StopBand(0.0, 0.1));
        Assert.Equal(BusTrim.SettledMetresPerSecond, BusTrim.StopBand(double.NaN, 0.1));
    }

    /// <summary>
    /// And a bus that pulses stops inside three pulses instead, which is what the release floor is
    /// priced from — so the floor follows the phase down rather than letting go at what a hold could
    /// manage. Pulses coarser than the band buy nothing and must not raise it.
    /// </summary>
    [Fact]
    public void ThePulsingBandIsThreePulsesAndNeverWiderThanTheHoldsOwn()
    {
        Assert.Equal(3.0 * 0.56 * 0.001, BusTrim.StopBand(0.56, 0.017, 0.001), 12);
        Assert.Equal(BusTrim.SettledMetresPerSecond, BusTrim.StopBand(0.56, 0.017, 0.0));
        Assert.Equal(BusTrim.SettledMetresPerSecond, BusTrim.StopBand(0.56, 0.017, 0.5));
        Assert.Equal(BusTrim.SettledMetresPerSecond, BusTrim.StopBand(0.0, 0.017, 0.001));
    }
}
