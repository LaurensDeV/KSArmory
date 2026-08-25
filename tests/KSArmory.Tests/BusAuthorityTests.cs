using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the trim does when the solution it is chasing keeps moving, on a bus with the authority
/// the shipped one actually has.
///
/// <para><b>The shipped bus is not the one <see cref="TrimBus"/> defaults to.</b> Its twenty
/// nozzles sum to 4.000 units of thrust fore and aft and <b>4.243 in every one of the four lateral
/// directions</b>, with the roll torques cancelling exactly — so it translates cleanly on all six.
/// Read off <c>KSArmoryAssets.xml</c> at KSA's 0.5 translation-enrolment threshold, and confirmed
/// in flight: the 2026-08-25 baseline trimmed starboard 294 times and back 292 and converged, which
/// a bus with no lateral authority could not do.</para>
///
/// <para>That matters because <see cref="BusTrim"/> strikes a direction off after
/// <see cref="BusTrim.DirectionStallSeconds"/> of firing without its own component falling by
/// <see cref="BusTrim.ProgressMetresPerSecond"/>. On a dead axis that is the intended reading. On a
/// live one it is a false positive, and the way to get one is a reference that moves as fast as the
/// bus can null it — which is what <c>MaxMetresPerSecond</c>'s own note describes as a runaway
/// between the aim correction and the trim.</para>
/// </summary>
public class BusAuthorityTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    /// <summary>What the thrusters were measured at in flight, 2026-08-25.</summary>
    private const double AxialAccel = 0.551;

    /// <summary>Lateral is 4.243/4.000 of it, off the nozzle sums.</summary>
    private const double LateralAccel = AxialAccel * 4.243 / 4.000;

    private static BallisticArc.Solution Shot(out double3 fromCci, out double3 aimAtEpoch)
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out fromCci, out double3 target);
        aimAtEpoch = target;
        return arc;
    }

    /// <summary>
    /// Runs the trim while the reference velocity drifts, and reports what got struck off.
    /// </summary>
    private (double Seconds, string Said, double ToGain) Run(double driftPerSecond, double errorMs)
    {
        BallisticArc.Solution arc = Shot(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);
        double3 right = Vec.Unit(Vec.Cross(fromCci, nose));
        double3 down = -Vec.Unit(fromCci);

        TrimBus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci + right * errorMs * 0.6 + nose * errorMs * 0.8,
            NoseCci = nose,
            RightCci = right,
            DownCci = down,
            AxialAcceleration = AxialAccel,
            LateralAcceleration = LateralAccel,
        };

        BusTrim trim = new();
        trim.Begin();

        double step = 1.0 / 60.0;
        double elapsed = 0.0;
        double3 reference = arc.RequiredVelocityCci;
        TrimCommand last = default;

        while (elapsed < 120.0)
        {
            last = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                fromCci, reference, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci));

            if (last.Done) break;

            bus.Step(Earth, last.Fire, step);

            // The aim correction re-solving under the trim: the target it is chasing moves. Across
            // the frame rather than along it, so the drift cannot be mistaken for the bus's own
            // axial burn.
            reference += right * driftPerSecond * step;
            elapsed += step;
        }

        return (elapsed, last.Said, last.ToGainMetresPerSecond);
    }

    /// <summary>
    /// A still solution on the real bus converges, and strikes nothing off. The control.
    /// </summary>
    [Fact]
    public void OnTheRealBusAStillSolutionConvergesWithNothingStruckOff()
    {
        (double seconds, string said, double toGain) = Run(driftPerSecond: 0.0, errorMs: 7.3);

        Out.WriteLine($"{seconds:F1} s: {said} ({toGain:F3} m/s left)");

        Assert.DoesNotContain("struck off", said);
        Assert.True(toGain < 0.1, $"did not converge: {toGain:F3} m/s left");
    }

    /// <summary>
    /// And what a moving one does to it, which is the arr15 shot's shape.
    ///
    /// <para>Reported across a sweep rather than asserted at one rate: where the false positive
    /// starts is the number worth having, and pinning it would pin the loop's tuning here.</para>
    /// </summary>
    [Fact]
    public void WhereAMovingSolutionStartsStrikingOffLiveAxes()
    {
        Out.WriteLine($"bus: axial {AxialAccel:F3} m/s2, lateral {LateralAccel:F3} m/s2 "
                      + "(the shipped layout, all six directions live)");
        Out.WriteLine($"stall rule: {BusTrim.ProgressMetresPerSecond:F2} m/s of progress "
                      + $"per {BusTrim.DirectionStallSeconds:F0} s\n");
        Out.WriteLine($"{"drift m/s2":>12}{"vs lateral":>12}{"seconds":>10}{"left m/s":>11}   outcome");

        foreach (double drift in new[] { 0.0, 0.2, 0.4, 0.50, 0.55, 0.58, 0.60, 0.65, 0.8, 1.2 })
        {
            (double seconds, string said, double toGain) = Run(drift, errorMs: 7.3);
            string outcome = said.Contains("struck off") ? said : "converged";
            Out.WriteLine($"{drift,12:F2}{drift / LateralAccel,12:F2}{seconds,10:F1}"
                          + $"{toGain,11:F3}   {outcome}");
        }
    }
}
