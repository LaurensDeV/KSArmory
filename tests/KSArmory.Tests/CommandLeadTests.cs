using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class CommandLeadTests
{
    private const double Deg = Math.PI / 180.0;
    private const double Frame = 1.0 / 60.0;

    private static double3 InPlane(double angleRad) => new(Math.Cos(angleRad), Math.Sin(angleRad), 0.0);

    private static double AheadOf(double3 led, double angleRad)
    {
        double3 at = InPlane(angleRad);
        return Math.Atan2(at.X * led.Y - at.Y * led.X, Vec.Dot(at, led));
    }

    [Fact]
    public void ASteadyRampIsCarriedAheadByItsRateTimesTheLead()
    {
        var lead = new CommandLead();
        double rate = 13.0 * Deg;
        double3 led = default;
        double t = 0.0;
        for (; t < 5.0; t += Frame) led = lead.Lead(InPlane(rate * t), Frame, 0.9);

        double ahead = AheadOf(led, rate * (t - Frame));
        Assert.InRange(ahead / Deg, 13.0 * 0.9 - 0.2, 13.0 * 0.9 + 0.2);
    }

    [Fact]
    public void AStepInTheCommandIsNotLed()
    {
        var lead = new CommandLead();
        for (double t = 0.0; t < 2.0; t += Frame) lead.Lead(InPlane(10.0 * Deg * t), Frame, 0.9);

        double3 stepped = InPlane(80.0 * Deg);
        double3 led = lead.Lead(stepped, Frame, 0.9);

        Assert.True(Vec.AngleBetween(led, stepped) < 1e-9);
        Assert.Equal(0.0, Vec.Len(lead.RateRadPerSecond));
    }

    [Fact]
    public void NoLeadHandsTheCommandBackUnchanged()
    {
        var lead = new CommandLead();
        double3 last = default, sent = default;
        for (double t = 0.0; t < 2.0; t += Frame)
        {
            sent = InPlane(13.0 * Deg * t);
            last = lead.Lead(sent, Frame, 0.0);
        }
        Assert.True(Vec.AngleBetween(last, sent) < 1e-12);
    }

    // A first-order tracker with KSA's measured time constant: the nose closes on whatever it is handed at 1/0.9 a
    // second, which on a 13 deg/s ramp settles 11.7 deg behind, as the November flights did.
    [Theory]
    [InlineData(0.0, 11.0, 12.5)]
    [InlineData(0.9, -1.0, 1.0)]
    public void ALaggingTrackerHandedTheLedCommandFollowsTheRampItTrailedBehind(double leadSeconds, double low, double high)
    {
        const double tau = 0.9;
        double rate = 13.0 * Deg;
        var lead = new CommandLead();
        double nose = 0.0, wanted = 0.0;
        for (double t = 0.0; t < 15.0; t += Frame)
        {
            wanted = rate * t;
            double3 led = lead.Lead(InPlane(wanted), Frame, leadSeconds);
            nose += (wanted + AheadOf(led, wanted) - nose) * (Frame / tau);
        }
        Assert.InRange((wanted - nose) / Deg, low, high);
    }

    [Fact]
    public void ATurnUnderHalfTheThresholdIsNotLedAndOneOverItIsLedInFull()
    {
        double Ahead(double degPerSecond)
        {
            var lead = new CommandLead();
            double rate = degPerSecond * Deg;
            double3 led = default;
            double t = 0.0;
            for (; t < 5.0; t += Frame) led = lead.Lead(InPlane(rate * t), Frame, 0.9, fromDegPerSecond: 4.0);
            return AheadOf(led, rate * (t - Frame)) / Deg;
        }

        Assert.Equal(0.0, Ahead(1.0), 9);
        Assert.InRange(Ahead(13.0), 13.0 * 0.9 - 0.2, 13.0 * 0.9 + 0.2);
    }
}
