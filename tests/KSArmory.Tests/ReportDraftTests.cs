using KSArmory.Sim;
using Xunit;

namespace KSArmory.Tests;

public class ReportDraftTests
{
    [Fact]
    public void AnEmptySummaryIsTheFirstThingSaid()
    {
        // In the order the form is filled in. Complaining about the detail while the summary is
        // still blank is noise.
        Assert.Equal("a one-line summary is needed", ReportDraft.Problem("", new string('x', 9_000)));
    }

    [Theory]
    [InlineData("turret")]
    [InlineData("       ")]
    public void AWordIsNotAReport(string summary)
        => Assert.NotNull(ReportDraft.Problem(summary, "it does not move"));

    [Fact]
    public void AnOrdinaryReportIsAccepted()
        => Assert.Null(ReportDraft.Problem("The turret will not traverse", "It has a lock and stays put."));

    [Fact]
    public void TooLongSaysByHowMuch()
    {
        // A limit someone has already exceeded is only useful with the overshoot attached.
        string? problem = ReportDraft.Problem(new string('x', ReportDraft.MaxSummary + 7), "");

        Assert.Equal("the summary is 7 characters too long", problem);
    }

    [Fact]
    public void SurroundingSpaceIsNotContent()
    {
        // The endpoint trims before it measures, so a draft that looks long enough here and is
        // refused there would be indistinguishable from a server fault.
        Assert.NotNull(ReportDraft.Problem("  turret  ", ""));
    }

    [Fact]
    public void AShortLogIsSentWhole()
    {
        Assert.Equal("one\ntwo\n", ReportDraft.Tail("one\ntwo\n"));
        Assert.Equal("", ReportDraft.Tail(null));
    }

    [Fact]
    public void ALongLogKeepsItsEnd()
    {
        // What went wrong is at the bottom. Keeping the beginning would attach the part written
        // before the session had a problem.
        string log = string.Join("\n", Enumerable.Range(0, 5_000).Select(i => $"line {i} of the log"));

        string tail = ReportDraft.Tail(log, 200);

        Assert.True(tail.Length <= 200);
        Assert.EndsWith("line 4999 of the log", tail);
        Assert.DoesNotContain("line 0 of the log", tail);
    }

    [Fact]
    public void TheCutIsAtALineBoundary()
    {
        // A partial first line reads as a truncated message rather than a truncated file.
        string log = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"{i:0000} a line of roughly this length"));

        string tail = ReportDraft.Tail(log, 300);

        Assert.DoesNotContain("\n", tail[..1]);
        Assert.Matches(@"^\d{4} a line", tail);
    }

    [Fact]
    public void ALogWithNoLineBreaksIsNotThrownAway()
    {
        // Cutting to the first newline would discard everything when there is no newline until
        // the very end, leaving an empty attachment that looks like a log that was never written.
        string log = new string('x', 4_000) + "\nthe end";

        string tail = ReportDraft.Tail(log, 500);

        Assert.Equal(500, tail.Length);
    }

    [Theory]
    [InlineData(ReportKind.Bug, "bug")]
    [InlineData(ReportKind.Idea, "idea")]
    public void TheKindMatchesWhatTheEndpointLabels(ReportKind kind, string wire)
        => Assert.Equal(wire, ReportDraft.Wire(kind));
}
