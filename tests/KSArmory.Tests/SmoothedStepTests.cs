using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Evening out the engine's step for a cosmetic ease.
///
/// <para>The numbers here are the measured ones: on a 120 Hz screen at a nominal 60 fps the step
/// beats between 8.33 ms and 25.0 ms, one and three vsync intervals, which advances a raw-stepped
/// transition three times as far on alternate frames.</para>
/// </summary>
public class SmoothedStepTests
{
    private const double Short = 1.0 / 120.0;        // 8.333 ms
    private const double Long = 3.0 / 120.0;         // 25.0 ms
    private const double Mean = (Short + Long) / 2;

    // The display's beat, long enough for the average to settle.
    private static double[] Beat(int frames)
    {
        double[] steps = new double[frames];
        for (int i = 0; i < frames; i++) steps[i] = i % 2 == 0 ? Short : Long;
        return steps;
    }

    /// <summary>
    /// The whole point. Fed the display's beat, the ease advances by very nearly the same amount
    /// every frame; fed straight through, it advances three times as far on every other one.
    /// </summary>
    [Fact]
    public void TheDisplaysBeatDoesNotReachTheEase()
    {
        SmoothedStep clock = new();
        double[] steps = Beat(200);

        double smallest = double.MaxValue;
        double largest = 0.0;

        // Skip the first few while the average settles onto the beat.
        for (int i = 0; i < steps.Length; i++)
        {
            double advance = clock.Next(steps[i]);
            if (i < 40) continue;

            smallest = Math.Min(smallest, advance);
            largest = Math.Max(largest, advance);
        }

        Assert.Equal(3.0, Long / Short, 6);            // what arrives
        Assert.True(largest / smallest < 1.12,          // what the ease sees
                    $"the ease still swings {largest / smallest:F2}:1 across the beat");
    }

    /// <summary>
    /// The clock does not drift: it starts a touch late while the average settles onto the beat,
    /// and loses nothing after that. A per-frame shortfall would stretch the transition in
    /// proportion to its length, which is a tuned number; a one-off is tens of milliseconds on a
    /// 1.2 second ease and imperceptible.
    /// </summary>
    [Fact]
    public void TimeIsLostOnlyWhileTheAverageSettles()
    {
        double Deficit(int frames)
        {
            SmoothedStep clock = new();
            double raw = 0.0;
            double smoothed = 0.0;

            foreach (double step in Beat(frames))
            {
                raw += step;
                smoothed += clock.Next(step);
            }

            return raw - smoothed;
        }

        double over400 = Deficit(400);
        double over1600 = Deficit(1600);

        // A settling transient, and small against the ease it is clocking.
        Assert.InRange(over400, 0.0, 0.1);

        // Four times the frames, the same shortfall: nothing is being lost per frame.
        Assert.Equal(over400, over1600, 6);
    }

    /// <summary>
    /// A paused world stops the ease dead, which is why it runs on the simulated step at all. An
    /// average decaying towards zero would keep the camera creeping after the world stopped.
    /// </summary>
    [Fact]
    public void APausedWorldHoldsItStill()
    {
        SmoothedStep clock = new();

        for (int i = 0; i < 40; i++) clock.Next(Mean);

        for (int i = 0; i < 30; i++) Assert.Equal(0.0, clock.Next(0.0));

        // And what resumes is what was running before, not a crawl back up from nothing.
        Assert.Equal(Mean, clock.Next(Mean), 6);
    }

    /// <summary>
    /// Slow motion is still slow. The step is proportional to simulation speed, so an average of
    /// it is too — a hundredth of the speed must be a hundredth of the advance.
    /// </summary>
    [Fact]
    public void SlowMotionStaysSlow()
    {
        SmoothedStep fast = new();
        SmoothedStep slow = new();

        double lastFast = 0.0;
        double lastSlow = 0.0;

        foreach (double step in Beat(200))
        {
            lastFast = fast.Next(step);
            lastSlow = slow.Next(step * 0.01);
        }

        Assert.Equal(0.01, lastSlow / lastFast, 9);
    }

    /// <summary>A step the engine could not report advances nothing rather than poisoning it.</summary>
    [Fact]
    public void ABadStepIsNotAnAdvance()
    {
        SmoothedStep clock = new();

        for (int i = 0; i < 20; i++) clock.Next(Mean);

        Assert.Equal(0.0, clock.Next(double.NaN));
        Assert.Equal(0.0, clock.Next(-1.0));
        Assert.Equal(Mean, clock.Next(Mean), 6);
    }
}
