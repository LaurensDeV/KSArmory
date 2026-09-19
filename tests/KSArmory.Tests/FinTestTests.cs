using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The built-in-test sweep. It emits a steering command rather than per-blade angles, so what
/// matters is that the command is one a real airframe could be given — and that the blades it
/// produces through the mixer are always a configuration some demand would explain.
/// </summary>
public class FinTestTests
{
    private const double Authority = 29.43;   // 3 g, the B61's declared lateral limit
    private const double Max = 0.20944;       // 12 degrees of blade travel

    private static double[] Blades(double t) =>
    [
        .. Enumerable.Range(0, 4).Select(i =>
            FinMixer.DeflectionRad(FinTest.CommandBodyFrame(t, Authority),
                                   FinMixer.FinRollRad(i, 4, Math.PI / 4.0), Authority, Max))
    ];

    [Fact]
    public void OppositeBladesAlwaysOppose()
    {
        // The property that makes a cruciform turn rather than roll. A per-blade sweep breaks
        // this at almost every instant, which is what made the old one look wrong.
        for (int step = 0; step <= 400; step++)
        {
            double[] d = Blades(step * 0.01);
            Assert.Equal(-d[2], d[0], 9);
            Assert.Equal(-d[3], d[1], 9);
        }
    }

    [Fact]
    public void OnlyOneAxisIsExercisedAtATime()
    {
        // A real control check sweeps pitch, then yaw. Both at once is a diagonal demand, which
        // is legal but reads as drifting rather than as a test.
        for (int step = 0; step <= 400; step++)
        {
            double3 c = FinTest.CommandBodyFrame(step * 0.01, Authority);
            Assert.True(Math.Abs(c.Y) < 1e-12 || Math.Abs(c.Z) < 1e-12,
                        $"both axes commanded at t={step * 0.01}: {c.Y:F4}, {c.Z:F4}");
        }
    }

    [Fact]
    public void TheCommandNeverExceedsTheAirframesAuthority()
    {
        for (int step = 0; step <= 400; step++)
        {
            double3 c = FinTest.CommandBodyFrame(step * 0.01, Authority);
            Assert.InRange(Vec.Len(c), 0.0, Authority + 1e-9);
            Assert.Equal(0.0, c.X);                     // the axial part never steers
        }
    }

    [Fact]
    public void BothAxesAreActuallyReached()
    {
        double pitch = 0.0, yaw = 0.0;
        for (int step = 0; step <= 400; step++)
        {
            double3 c = FinTest.CommandBodyFrame(step * 0.01, Authority);
            pitch = Math.Max(pitch, Math.Abs(c.Y));
            yaw = Math.Max(yaw, Math.Abs(c.Z));
        }
        // Full travel on each, or the check is not exercising the blades to their stops.
        Assert.InRange(pitch, 0.99 * Authority, Authority + 1e-9);
        Assert.InRange(yaw, 0.99 * Authority, Authority + 1e-9);
    }

    [Fact]
    public void TheBladesPassThroughNeutralBetweenAxes()
    {
        // Zero at each half boundary, so the set does not snap from full pitch to full yaw.
        foreach (double t in new[] { 0.0, FinTest.PeriodSeconds / 2.0, FinTest.PeriodSeconds })
            Assert.Equal(0.0, Vec.Len(FinTest.CommandBodyFrame(t, Authority)), 9);
    }

    [Fact]
    public void NoBladeEverReversesWhileItIsMoving()
    {
        // The fault this sweep was rebuilt for. Handing over from pitch to yaw turns the command
        // through a right angle, and blades 1 and 3 sit on the wrong side of it: with the set
        // still moving they reverse while 0 and 2 carry on, and one pair jerking on its own is
        // exactly what reads as broken. A reversal is only allowed at a standstill.
        const double dt = 0.005;
        for (int blade = 0; blade < 4; blade++)
        {
            double roll = FinMixer.FinRollRad(blade, 4, Math.PI / 4.0);
            double Deflect(double t) =>
                FinMixer.DeflectionRad(FinTest.CommandBodyFrame(t, Authority), roll, Authority, Max);

            double prev = Deflect(0.0) - Deflect(-dt);
            for (int step = 1; step * dt <= FinTest.PeriodSeconds; step++)
            {
                double t = step * dt;
                double rate = Deflect(t) - Deflect(t - dt);
                if (prev * rate < 0.0)
                {
                    // Reversing: it must be happening slowly, not at speed.
                    // Measured: eased turns come round at ~0.07 of travel per second, where an
                    // un-eased handover snaps at 2.2 -- a factor of thirty. Half a travel per
                    // second sits between the two and catches the snap without flagging the
                    // sine's own peaks, which are legitimate and slow.
                    double speed = Math.Max(Math.Abs(prev), Math.Abs(rate)) / dt;
                    Assert.True(speed < 0.5 * Max,
                                $"blade {blade} reverses at {speed / Max:F3} of travel per second, t={t:F3}");
                }
                if (Math.Abs(rate) > 1e-15) prev = rate;
            }
        }
    }

    [Fact]
    public void TheSetRestsAtNeutralBetweenAxes()
    {
        // A finite pause, not just an instant of zero -- that is what removes the handover from
        // view entirely rather than merely slowing it down.
        double half = FinTest.PeriodSeconds / 2.0;
        double rest = half * (1.0 - FinTest.SweepFraction);
        Assert.True(rest > 0.15, $"the rest between axes is only {rest:F3} s");
        for (double t = half - rest * 0.9; t < half - 1e-9; t += rest * 0.2)
            Assert.Equal(0.0, Vec.Len(FinTest.CommandBodyFrame(t, Authority)), 9);
    }

    [Fact]
    public void ItRepeatsEveryPeriod()
    {
        double3 a = FinTest.CommandBodyFrame(1.3, Authority);
        double3 b = FinTest.CommandBodyFrame(1.3 + FinTest.PeriodSeconds, Authority);
        Assert.Equal(0.0, Vec.Len(a - b), 9);
    }

    [Fact]
    public void ANonFiniteClockCannotReachAPartTransform()
    {
        // Written straight into a subpart rotation: NaN there makes the body vanish rather than
        // throw, which is the hardest kind of fault to trace.
        Assert.Equal(0.0, Vec.Len(FinTest.CommandBodyFrame(double.NaN, Authority)));
        Assert.Equal(0.0, Vec.Len(FinTest.CommandBodyFrame(double.PositiveInfinity, Authority)));
        Assert.Equal(0.0, Vec.Len(FinTest.CommandBodyFrame(1.0, double.NaN)));
    }

    [Fact]
    public void ARoundWithNoAuthorityIsNotSwept()
    {
        Assert.Equal(0.0, Vec.Len(FinTest.CommandBodyFrame(1.0, 0.0)));
        Assert.Equal(0.0, Vec.Len(FinTest.CommandBodyFrame(1.0, -5.0)));
    }

    [Fact]
    public void ANegativeClockStillSweepsInRange()
    {
        // The accumulator should never go backwards, but a wrapped or reset clock must not throw
        // the blades outside their travel.
        for (int step = 1; step <= 100; step++)
            Assert.InRange(Vec.Len(FinTest.CommandBodyFrame(-step * 0.05, Authority)),
                           0.0, Authority + 1e-9);
    }
}
