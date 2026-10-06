using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What <see cref="IcbmConfig.TrimCountsTheCommandInFlight"/> does to the number the trim chooses on.
///
/// <para>Arithmetic only. <see cref="TrimBus"/> applies a command on the step it is chosen, and
/// even made to apply it a frame late — with the pulse phase and 84–117 ms steps — it spends
/// 1.3–2.0 m/s on 0.38 owed at every step, 1x included, so it does not reproduce the flown
/// difference between 1x and 4.47x and cannot say whether the switch helps. That is a flight's
/// question; <c>docs/MIRV-TARGETS.md</c> has it.</para>
/// </summary>
public class TrimLatencyTests
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    // Two runs alike in every frame, the engine applying each command a frame late, differing only
    // in whether the last call counts the hold still in flight. The thrusters have to be measured
    // first: until then the trim knows no acceleration to count with.
    private static (TrimCommand Last, TrimAxes InFlight) Fly(bool countLast, int frames, double step)
    {
        double3 from = new(R + 1_000_000.0, 0, 0);
        double3 v = new(0, Math.Sqrt(Mu / (R + 1_000_000.0)), 0);
        double3 nose = Vec.Unit(v);

        TrimBus bus = new()
        {
            PositionCci = from,
            VelocityCci = v + nose * 2.0,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(from, nose)),
            DownCci = -Vec.Unit(from),
            AxialAcceleration = 0.565,
            LateralAcceleration = 0.565,
        };

        BusTrim trim = new();
        trim.Begin();

        TrimAxes inFlight = TrimAxes.None;
        TrimCommand c = default;

        for (int i = 0; i < frames; i++)
        {
            c = trim.Update(step, new TrimSituation(Earth, bus.PositionCci, bus.VelocityCci, from, v,
                                                    i * step, bus.NoseCci, bus.RightCci, bus.DownCci,
                                                    CountsTheCommandInFlight: countLast && i == frames - 1));
            if (i == frames - 1) break;

            bus.Step(Earth, inFlight, step);
            inFlight = c.Fire;
        }

        return (c, inFlight);
    }

    [Fact]
    public void CountedAHoldStillInFlightIsTakenOffWhatIsLeft()
    {
        const double step = 0.088;

        (TrimCommand uncounted, TrimAxes inFlight) = Fly(countLast: false, 12, step);
        (TrimCommand counted, _) = Fly(countLast: true, 12, step);

        Assert.NotEqual(TrimAxes.None, inFlight);
        Assert.True(uncounted.Acceleration > 0.5, $"measured {uncounted.Acceleration:F3} m/s2");
        Assert.Equal(uncounted.ToGainMetresPerSecond - uncounted.Acceleration * step,
                     counted.ToGainMetresPerSecond, 3);
    }

    [Fact]
    public void BeforeTheThrustersAreMeasuredThereIsNothingToCount()
    {
        (TrimCommand uncounted, _) = Fly(countLast: false, 2, 0.088);
        (TrimCommand counted, _) = Fly(countLast: true, 2, 0.088);

        Assert.Equal(uncounted.ToGainMetresPerSecond, counted.ToGainMetresPerSecond, 9);
    }
}
