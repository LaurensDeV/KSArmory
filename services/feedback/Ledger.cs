namespace KSArmory.Feedback;

/// <summary>What the ledger says about a report that has just arrived.</summary>
public enum Reservation
{
    /// <summary>Nothing like it recently and the day has room: file it.</summary>
    Free,

    /// <summary>The same report is already filed. Nothing to do, and nothing lost.</summary>
    Duplicate,

    /// <summary>The day's ceiling is spent. Something <em>is</em> lost, and the reporter is told.</summary>
    Ceiling,
}

/// <summary>
/// How many issues today, and what has been filed lately.
///
/// <para>Two limits with different meanings, and the difference matters to whoever is typing.
/// A duplicate is already dealt with, so answering "received" is true. A spent ceiling is a real
/// report being dropped, so answering "received" would be a lie — that path returns a refusal the
/// reporter can act on.</para>
///
/// <para>Time is a parameter rather than read from the clock, so the rules can be tested without
/// waiting six hours or waiting for midnight.</para>
/// </summary>
public sealed class Ledger(int maxPerDay, TimeSpan duplicateWindow)
{
    private readonly Dictionary<string, Entry> _recent = [];
    private readonly Lock _gate = new();

    private int _filedToday;
    private DateOnly _today;

    /// <summary>Issues filed so far today, for whoever wants to watch the ceiling approach.</summary>
    public int FiledToday
    {
        get { lock (_gate) { return _filedToday; } }
    }

    /// <summary>
    /// Claims a slot for a report, or explains why not.
    ///
    /// <para>A claim is provisional: <see cref="Commit"/> confirms it once the issue exists, and
    /// <see cref="Release"/> gives it back when filing fails. Counting at claim time and never
    /// releasing would let a GitHub outage eat the day's ceiling without filing anything.</para>
    /// </summary>
    public Reservation Reserve(string fingerprint, DateTimeOffset now, out string? existing)
    {
        lock (_gate)
        {
            existing = null;

            DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
            if (today != _today)
            {
                _today = today;
                _filedToday = 0;
            }

            foreach (string stale in _recent.Where(e => now - e.Value.When > duplicateWindow)
                                            .Select(e => e.Key).ToList())
            {
                _recent.Remove(stale);
            }

            if (_recent.TryGetValue(fingerprint, out Entry seen))
            {
                existing = seen.Url;
                return Reservation.Duplicate;
            }

            // Checked after the duplicate test on purpose: a repeat of something already filed
            // costs nothing and should not be refused because the day is full.
            if (_filedToday >= maxPerDay) return Reservation.Ceiling;

            _recent[fingerprint] = new Entry(now, null);
            _filedToday++;
            return Reservation.Free;
        }
    }

    /// <summary>Records where a reservation ended up, so a repeat can be pointed at it.</summary>
    public void Commit(string fingerprint, DateTimeOffset now, string? url)
    {
        lock (_gate) { _recent[fingerprint] = new Entry(now, url); }
    }

    /// <summary>Gives a reservation back when the issue was never created.</summary>
    public void Release(string fingerprint)
    {
        lock (_gate)
        {
            if (!_recent.Remove(fingerprint)) return;

            if (_filedToday > 0) _filedToday--;
        }
    }

    private readonly record struct Entry(DateTimeOffset When, string? Url);
}
