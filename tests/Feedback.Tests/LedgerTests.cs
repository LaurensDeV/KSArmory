using KSArmory.Feedback;
using Xunit;

namespace KSArmory.Feedback.Tests;

/// <summary>
/// The two limits, and the difference between them: a duplicate is already dealt with, a spent
/// ceiling is a real report being dropped.
/// </summary>
public class LedgerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private static Ledger New(int perDay = 3, int windowHours = 6)
        => new(perDay, TimeSpan.FromHours(windowHours));

    [Fact]
    public void AFirstReportIsFree()
        => Assert.Equal(Reservation.Free, New().Reserve("abc", Noon, out _));

    [Fact]
    public void TheSameReportAgainIsADuplicate()
    {
        Ledger ledger = New();
        ledger.Reserve("abc", Noon, out _);

        Assert.Equal(Reservation.Duplicate, ledger.Reserve("abc", Noon.AddMinutes(5), out _));
    }

    [Fact]
    public void ADuplicatePointsAtWhatItAlreadyBecame()
    {
        // So the reporter can be shown the issue rather than left wondering whether it worked.
        Ledger ledger = New();
        ledger.Reserve("abc", Noon, out _);
        ledger.Commit("abc", Noon, "https://github.com/x/y/issues/7");

        ledger.Reserve("abc", Noon.AddMinutes(5), out string? existing);

        Assert.Equal("https://github.com/x/y/issues/7", existing);
    }

    [Fact]
    public void PastTheWindowItIsANewReport()
    {
        // A bug that is still happening tomorrow is worth hearing about again.
        Ledger ledger = New(windowHours: 6);
        ledger.Reserve("abc", Noon, out _);

        Assert.Equal(Reservation.Free, ledger.Reserve("abc", Noon.AddHours(7), out _));
    }

    [Fact]
    public void TheCeilingIsReportedRatherThanSwallowed()
    {
        Ledger ledger = New(perDay: 2);
        ledger.Reserve("a", Noon, out _);
        ledger.Reserve("b", Noon, out _);

        Assert.Equal(Reservation.Ceiling, ledger.Reserve("c", Noon, out _));
    }

    [Fact]
    public void ADuplicateDoesNotNeedRoomInTheDay()
    {
        // Checked before the ceiling: a repeat costs nothing to answer and should not be refused
        // because the day is full, which would report the wrong reason to the reporter.
        Ledger ledger = New(perDay: 1);
        ledger.Reserve("a", Noon, out _);

        Assert.Equal(Reservation.Duplicate, ledger.Reserve("a", Noon.AddMinutes(1), out _));
    }

    [Fact]
    public void TheCeilingLiftsAtMidnight()
    {
        Ledger ledger = New(perDay: 1);
        ledger.Reserve("a", Noon, out _);
        Assert.Equal(Reservation.Ceiling, ledger.Reserve("b", Noon, out _));

        Assert.Equal(Reservation.Free, ledger.Reserve("b", Noon.AddDays(1), out _));
    }

    [Fact]
    public void AFailedFilingGivesTheSlotBack()
    {
        // The bug this exists for: counting at reservation and never releasing lets a GitHub
        // outage spend the whole day's ceiling on issues that were never created.
        Ledger ledger = New(perDay: 1);

        ledger.Reserve("a", Noon, out _);
        ledger.Release("a");

        Assert.Equal(0, ledger.FiledToday);
        Assert.Equal(Reservation.Free, ledger.Reserve("b", Noon, out _));
    }

    [Fact]
    public void AReleasedReportIsNotRememberedAsFiled()
    {
        Ledger ledger = New();
        ledger.Reserve("a", Noon, out _);
        ledger.Release("a");

        Assert.Equal(Reservation.Free, ledger.Reserve("a", Noon.AddMinutes(1), out _));
    }

    [Fact]
    public void ReleasingSomethingUnknownDoesNothing()
    {
        // Called on paths that may not hold a reservation; it must not push the count negative.
        Ledger ledger = New();
        ledger.Release("never seen");

        Assert.Equal(0, ledger.FiledToday);
    }
}
