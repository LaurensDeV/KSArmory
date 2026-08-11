using KSArmory.Feedback;
using Xunit;

namespace KSArmory.Feedback.Tests;

/// <summary>
/// Whether a log reaches a public page.
///
/// <para>The judgement is a delegate here, so these pin the policy rather than the model. What the
/// model actually scores is measured in <c>README.md</c> and needs weights that are fetched during
/// the image build.</para>
/// </summary>
public class LogGateTests
{
    private static bool Never(string line) => false;

    [Fact]
    public void AnOrdinaryLogIsPublished()
    {
        Assert.True(LogGate.MayPublish("20:00:00.000 INFO  destroyed NewRocket_1", Never));
    }

    [Fact]
    public void NoLogIsNotAFailure()
    {
        Assert.True(LogGate.MayPublish(null, Never));
        Assert.True(LogGate.MayPublish("", Never));
    }

    [Fact]
    public void OneRefusedLineWithholdsTheWholeLog()
    {
        string log =
            """
            20:00:00.000 INFO  ready - Pantsir-S1, 12 tubes, safe.
            20:00:01.000 INFO  destroyed <the offending name>
            20:00:02.000 INFO  round 1 away
            """;

        Assert.False(LogGate.MayPublish(log, line => line.Contains("offending")));
    }

    [Fact]
    public void EveryLineIsJudged()
    {
        // Not the log as one document. A single abusive line among a dozen dull ones measures
        // insult 0.95 alone and 0.34 in company, so judging them together loses it.
        List<string> judged = [];
        string log =
            """
            20:00:00.000 INFO  first thing
            20:00:01.000 INFO  second thing
            20:00:02.000 INFO  third thing
            """;

        LogGate.MayPublish(log, line => { judged.Add(line); return false; });

        Assert.Equal(["first thing", "second thing", "third thing"], judged);
    }

    [Fact]
    public void JudgingStopsAtTheFirstRefusal()
    {
        // Each line is a model pass, and the answer cannot change once one line has refused.
        int calls = 0;
        string log =
            """
            20:00:00.000 INFO  bad
            20:00:01.000 INFO  one
            20:00:02.000 INFO  two
            20:00:03.000 INFO  three
            """;

        LogGate.MayPublish(log, _ => { calls++; return true; });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void ALogTooLongToReadThroughIsWithheldEvenWhenEveryLineReadWasClean()
    {
        // The one shape of this that fails open: the lines past the ceiling are exactly the ones
        // never scored, so "nothing that was read was bad" is not an argument for publishing them.
        //
        // Digit-free and non-repeating for the reasons CondenseTests.Distinct gives.
        string log = string.Join(" ", Enumerable.Range(0, 3_000).Select(
            i => $"word{(char)('a' + i % 26)}{(char)('a' + i / 26 % 26)}{(char)('a' + i / 676 % 26)}"));

        Assert.False(Guard.Condense(log).Whole);
        Assert.False(LogGate.MayPublish(log, Never));
    }
}
