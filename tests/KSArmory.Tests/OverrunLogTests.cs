using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What gets said when the world takes a step no round can be integrated across.
///
/// <para>The warning it produces is the mod's only report that rounds have fallen behind the
/// world, so it is worth exactly as much as a reader's willingness to believe it. A scene load
/// takes tens of seconds in one step with an empty sky, which is the case that trains a reader to
/// scroll past — see <see cref="OverrunLog"/>.</para>
/// </summary>
public class OverrunLogTests
{
    private const double Load = 48.0;
    private const double Warped = 1.0;

    /// <summary>
    /// The defect. The sky is empty across a scene load, so nothing is behind the world when it
    /// ends and there is nothing to warn about.
    /// </summary>
    [Fact]
    public void AnEmptySkyIsNotWorthAWarning()
    {
        OverrunLog log = new();

        Assert.Equal(OverrunLog.Notice.Idle, log.Observe(Load, anyInFlight: false));
    }

    [Fact]
    public void RoundsInTheAirAre()
    {
        OverrunLog log = new();

        Assert.Equal(OverrunLog.Notice.Lagging, log.Observe(Warped, anyInFlight: true));
    }

    /// <summary>
    /// The half that is easy to lose while fixing the first. Time is discarded whatever is in the
    /// air — the clamp does not ask — so suppressing the line must not suppress the accounting,
    /// or a later real overrun under-reports how much has gone.
    /// </summary>
    [Fact]
    public void TimeIsCountedWhetherOrNotAnythingWasFlying()
    {
        OverrunLog idle = new();
        OverrunLog busy = new();

        idle.Observe(Load, anyInFlight: false);
        busy.Observe(Load, anyInFlight: true);

        Assert.Equal(Load - Interceptor.MaxFaithfulStep, idle.DiscardedSeconds, 9);
        Assert.Equal(idle.DiscardedSeconds, busy.DiscardedSeconds, 9);
        Assert.Equal(1, idle.Frames);
        Assert.Equal(1, busy.Frames);
    }

    /// <summary>
    /// The rate limit exists so the first line of a run is never buried. Sharing one budget
    /// between the two kinds means a scene load spends the report the first real overrun needs,
    /// and the lag then starts silently.
    /// </summary>
    [Fact]
    public void AnEmptySkyDoesNotSpendTheWarningARealOverrunNeeds()
    {
        OverrunLog log = new();

        log.Observe(Load, anyInFlight: false);

        Assert.Equal(OverrunLog.Notice.Lagging, log.Observe(Warped, anyInFlight: true));
    }

    [Fact]
    public void AWarningIsNotRepeatedEveryFrame()
    {
        OverrunLog log = new();

        Assert.Equal(OverrunLog.Notice.Lagging, log.Observe(Warped, anyInFlight: true));
        for (int i = 2; i < OverrunLog.ReportEvery; i++)
        {
            Assert.Equal(OverrunLog.Notice.Silent, log.Observe(Warped, anyInFlight: true));
        }

        Assert.Equal(OverrunLog.Notice.Lagging, log.Observe(Warped, anyInFlight: true));
    }

    /// <summary>
    /// And the quiet kind is limited too. It is recorded rather than dropped, so a sustained warp
    /// with nothing in the air must not fill a verbose log with a line per frame.
    /// </summary>
    [Fact]
    public void SoIsTheQuietOne()
    {
        OverrunLog log = new();

        Assert.Equal(OverrunLog.Notice.Idle, log.Observe(Load, anyInFlight: false));
        for (int i = 2; i < OverrunLog.ReportEvery; i++)
        {
            Assert.Equal(OverrunLog.Notice.Silent, log.Observe(Load, anyInFlight: false));
        }

        Assert.Equal(OverrunLog.Notice.Idle, log.Observe(Load, anyInFlight: false));
    }

    /// <summary>
    /// The total spans both kinds, because the clamp does. A reader asking how much simulated
    /// time this session has thrown away wants all of it, not the half that had a warning.
    /// </summary>
    [Fact]
    public void TheTotalsSpanBothKinds()
    {
        OverrunLog log = new();

        log.Observe(Load, anyInFlight: false);
        log.Observe(Warped, anyInFlight: true);

        Assert.Equal(2, log.Frames);
        Assert.Equal(Load + Warped - 2.0 * Interceptor.MaxFaithfulStep, log.DiscardedSeconds, 9);
    }
}
