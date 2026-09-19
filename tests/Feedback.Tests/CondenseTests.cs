using KSArmory.Feedback;
using Xunit;

namespace KSArmory.Feedback.Tests;

/// <summary>
/// What a log is reduced to before anything scores it.
///
/// <para>The numbers here come from a real <c>KSArmory.log</c>: 12 KB of it condenses to 25 lines
/// and 1,627 characters. That is the measurement the limits are sized against, and the reason they
/// are several times larger than it.</para>
/// </summary>
public class CondenseTests
{
    [Fact]
    public void ATimestampAndLevelAreNotWorthScoring()
    {
        Guard.Condensed condensed = Guard.Condense("21:28:53.471 INFO  round 1 detonated");

        Assert.Equal(["round # detonated"], condensed.Lines);
    }

    [Fact]
    public void NumbersCollapseSoOneMessageIsOneLine()
    {
        // The same message with different numbers is the same message. Collapsing them is what
        // turns thousands of lines into the handful of things a log actually says.
        Guard.Condensed condensed = Guard.Condense(
            """
            21:28:53.471 INFO  round 1 detonated with the target at 18 m
            21:29:01.004 INFO  round 2 detonated with the target at 7 m
            21:29:11.882 INFO  round 3 detonated with the target at 41 m
            """);

        Assert.Equal(["round # detonated with the target at # m"], condensed.Lines);
    }

    [Fact]
    public void ANameSurvives()
    {
        // The whole reason a log is scored at all. Everything else in the line is ours; this part
        // is whatever the player typed in the editor.
        Guard.Condensed condensed = Guard.Condense("21:28:53.473 INFO  destroyed FlyingSaucer");

        Assert.Equal(["destroyed FlyingSaucer"], condensed.Lines);
    }

    [Fact]
    public void ARealSizedLogIsWellInsideTheLimits()
    {
        // A working battery writes the same dozen messages over and over. If this ever stops
        // holding, logs start being withheld from honest reporters.
        string log = string.Concat(Enumerable.Repeat(
            """
            20:41:19.026 INFO  ready - Pantsir-S1, 12 tubes, safe.
            21:24:44.779 INFO  holding fire: target out of reach (0.1 km, envelope 1.2-20.0 km)
            21:28:53.471 INFO  round 1 detonated with the target at 18 m
            21:28:53.473 INFO  destroyed NewRocket_1
            19:07:51.651 INFO  crewed AA Defence Site_1_2

            """, 200));

        Guard.Condensed condensed = Guard.Condense(log);

        Assert.True(log.Length > 12_000);
        Assert.True(condensed.Whole);
        Assert.True(condensed.Lines.Count <= 8, $"condensed to {condensed.Lines.Count} lines");
    }

    [Fact]
    public void AnEmptyLogIsCompleteRatherThanUnread()
    {
        // Nothing to read is not the same as failing to read something, and conflating them would
        // withhold every log from every report that did not attach one.
        Assert.True(Guard.Condense(null).Whole);
        Assert.True(Guard.Condense("").Whole);
        Assert.Empty(Guard.Condense("").Lines);
    }

    [Fact]
    public void ALogWithNoLineBreaksIsCutIntoReadablePieces()
    {
        // One 12 KB line scored in one pass reads its first 512 tokens and silently ignores the
        // rest, which is indistinguishable from finding it clean.
        Guard.Condensed condensed = Guard.Condense(new string('a', 2_000) + " destroyed something");

        Assert.True(condensed.Lines.Count > 1);
        Assert.All(condensed.Lines, line => Assert.True(line.Length <= 400));
    }

    [Fact]
    public void TooMuchToReadIsReportedRatherThanTruncatedQuietly()
    {
        // The failure this pins: a log long enough to hit the ceiling having its tail dropped and
        // its head reported as the whole thing.
        Guard.Condensed condensed = Guard.Condense(Distinct(3_000, " "));

        Assert.False(condensed.Whole);
    }

    [Fact]
    public void TooManyDistinctLinesIsAlsoUnread()
        => Assert.False(Guard.Condense(Distinct(400, "\n")).Whole);

    /// <summary>
    /// Text that genuinely does not repeat, joined by <paramref name="separator"/>.
    ///
    /// <para>Free of digits on purpose. Condensing collapses those, so the obvious way to write
    /// this — numbering the words — produces text that is <em>identical</em> once condensed and
    /// proves the opposite of what it looks like.</para>
    /// </summary>
    private static string Distinct(int count, string separator)
        => string.Join(separator, Enumerable.Range(0, count).Select(
            i => $"word{(char)('a' + i % 26)}{(char)('a' + i / 26 % 26)}{(char)('a' + i / 676 % 26)}"));

    [Fact]
    public void RepeatsAreNotAGap()
    {
        // A repeat is covered by the copy that was kept, so dropping it loses nothing. Counting it
        // as unread would withhold every log, since repetition is what a log is.
        string log = string.Concat(Enumerable.Repeat("20:00:00.000 INFO  the same thing again\n", 5_000));

        Guard.Condensed condensed = Guard.Condense(log);

        Assert.True(condensed.Whole);
        Assert.Equal(["the same thing again"], condensed.Lines);
    }
}
