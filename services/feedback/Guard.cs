using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KSArmory.Feedback;

/// <summary>
/// What a stranger's text is allowed to become before it is rendered on a public page.
///
/// <para>Pure functions, no I/O: every rule here is a property of the text, so it can be argued
/// with and tested without a server. The rules exist because an endpoint that files public issues
/// is a way to make the maintainer's account publish whatever a stranger types.</para>
/// </summary>
public static partial class Guard
{
    /// <summary>
    /// Removes what cannot be seen but changes what is read.
    ///
    /// <para>Control characters, bidirectional overrides and zero-width joiners all render as
    /// nothing while reordering or hiding the text around them. Tabs and newlines are kept because
    /// a log without them is unreadable.</para>
    /// </summary>
    public static string StripInvisible(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var clean = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '\n' or '\t')
            {
                clean.Append(c);
                continue;
            }

            // C0 and C1 control ranges, and the DEL character.
            if (c < 0x20 || (c >= 0x7F && c <= 0x9F)) continue;

            // Bidi overrides and isolates: these reverse how the rest of a line reads.
            if (c is >= '\u202A' and <= '\u202E') continue;
            if (c is >= '\u2066' and <= '\u2069') continue;

            // Zero width space, non-joiner, joiner, and the byte order mark.
            if (c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') continue;

            clean.Append(c);
        }

        return clean.ToString();
    }

    /// <summary>
    /// Replaces anything that looks like a home directory with a placeholder.
    ///
    /// <para>A KSA log carries the path it was written to, which on Windows contains the account
    /// name. Someone reporting a bug is not choosing to publish that, so it does not reach a public
    /// issue.</para>
    /// </summary>
    public static string ScrubPaths(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        value = WindowsUsers().Replace(value, @"C:\Users\<user>");
        value = UnixHome().Replace(value, "/home/<user>");
        value = MacHome().Replace(value, "/Users/<user>");
        return value;
    }

    /// <summary>
    /// Everything a report goes through before it is rendered: strip, scrub, then truncate.
    ///
    /// <para>Truncation is last so a limit counts characters that will actually be shown, rather
    /// than ones a later step removes.</para>
    /// </summary>
    public static string Clean(string? value, int limit)
    {
        string text = ScrubPaths(StripInvisible(value)).Trim();
        return text.Length <= limit ? text : text[..limit] + "\n[truncated]";
    }

    /// <summary>
    /// A log reduced to the distinct things it says, so it can be judged in one pass.
    ///
    /// <para>A log is mostly not a log: it is the same handful of messages repeated, and what
    /// varies between them is a number or a name. Dropping the timestamp, the level, the numbers
    /// and every repeat leaves the vocabulary — which is the only part worth scoring, and is what
    /// makes a 12 KB log fit inside the model's 512-token window instead of needing eight passes at
    /// nearly a second each.</para>
    ///
    /// <para>Names survive. That is the point: a craft name is player-authored text that reaches a
    /// public issue through the log, and it is the only part of a log anyone can choose.</para>
    ///
    /// <para>Each line is returned separately because whoever scores them should score them one at
    /// a time. A whole log judged as one document dilutes a single abusive line among the hundred
    /// dull ones around it — measured at insult 0.95 alone against 0.34 in company.</para>
    /// </summary>
    public static Condensed Condense(string? value)
    {
        if (string.IsNullOrEmpty(value)) return new Condensed([], true);

        HashSet<string> seen = [];
        List<string> kept = [];
        int budget = CondenseBudget;
        bool whole = true;

        foreach (string raw in value.Split('\n'))
        {
            string line = Preamble().Replace(raw, "");
            line = Numbers().Replace(line, "#").Trim();

            // A log with no newlines is one enormous line, and one pass over it would read the
            // first 512 tokens and silently ignore the rest. Cutting it keeps every part readable.
            for (int i = 0; i < line.Length; i += CondenseLine)
            {
                if (budget <= 0 || kept.Count >= CondenseLines)
                {
                    whole = false;
                    break;
                }

                string piece = line[i..Math.Min(i + CondenseLine, line.Length)];

                // A repeat is already covered by the copy that was kept, so skipping it loses
                // nothing and is not a gap.
                if (!seen.Add(piece)) continue;

                kept.Add(piece);
                budget -= piece.Length;
            }
        }

        return new Condensed(kept, whole);
    }

    /// <summary>
    /// A log reduced to what is worth scoring, and whether that is all of it.
    ///
    /// <para><see cref="Whole"/> false means the limits cut something off, so nobody can say the
    /// unread part was clean. Whoever asked is expected to treat that as a refusal — a log too
    /// strange to read through is not one to publish unread.</para>
    /// </summary>
    public readonly record struct Condensed(IReadOnlyList<string> Lines, bool Whole);

    // What Condense will read at most. Measured against a real log, 12 KB of it condenses to 25
    // lines and 1,627 characters, so these are several times the headroom it needs — and the
    // headroom is the point, because exceeding them withholds the log. The ceiling exists so a
    // hostile log cannot turn one scan into a minute of model passes.
    private const int CondenseLine = 400;
    private const int CondenseLines = 96;
    private const int CondenseBudget = 8_000;

    /// <summary>
    /// A stable fingerprint of a report, for noticing the same thing arriving repeatedly.
    ///
    /// <para>Rate limiting is per address and a flood is not. This is what makes one message sent
    /// a thousand times from a thousand places still one issue.</para>
    /// </summary>
    public static string Fingerprint(string? summary, string? detail)
    {
        string material = (summary ?? "").Trim().ToLowerInvariant()
                          + "\u0000"
                          + (detail ?? "").Trim().ToLowerInvariant();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..16];
    }

    /// <summary>
    /// Whether text is mostly one repeated character, which is what a keyboard-mash report is.
    ///
    /// <para>Cheap and unambiguous. Anything cleverer becomes a spam filter, and a spam filter that
    /// rejects a real bug report is worse than an occasional junk issue.</para>
    /// </summary>
    public static bool LooksLikeMash(string? value)
    {
        string text = (value ?? "").Trim();
        if (text.Length < 12) return false;

        int distinct = text.ToLowerInvariant().Distinct().Count();
        return distinct <= 2;
    }

    // Function words, not vocabulary. They are the words English cannot avoid, they are short, and
    // they do not overlap much with the other Latin-script languages a report might arrive in.
    private static readonly HashSet<string> EnglishMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "is", "it", "to", "of", "in", "that", "was", "for", "with", "not", "but",
        "have", "has", "this", "when", "then", "there", "does", "doesn't", "don't", "can't",
        "after", "before", "from", "they", "you", "are", "were", "will", "would", "should",
    };

    /// <summary>
    /// Whether text reads as English, so it can be judged by an English classifier and read by
    /// whoever triages it.
    ///
    /// <para>Deliberately lenient. Two independent signals have to agree that it is <em>not</em>
    /// English before it is refused, and short text is always accepted: "turret stuck" carries no
    /// evidence either way, and refusing a real bug report because it was terse would be worse
    /// than reading an occasional one in Dutch.</para>
    /// </summary>
    public static bool LooksEnglish(string? value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0) return true;

        // Script first: this is the reliable half. A body of Cyrillic or CJK is not English
        // whatever its length, and no word-frequency argument is needed.
        int latin = 0, other = 0;
        foreach (char c in text)
        {
            if (!char.IsLetter(c)) continue;

            if (c < 0x0250 || (c >= 0x1E00 && c <= 0x1EFF)) latin++;
            else other++;
        }

        if (latin + other > 0 && other > (latin + other) * 0.3) return false;

        // Then function words, which only mean anything with enough of them to count.
        string[] words = text.Split(
            [' ', '\n', '\t', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '"', '/'],
            StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 8) return true;

        int markers = words.Count(w => EnglishMarkers.Contains(w.Trim('\'')));
        return markers >= 1 + words.Length / 25;
    }

    /// <summary>
    /// Whether a reported version is at least the minimum, so a bug fixed two releases ago is
    /// not filed again.
    ///
    /// <para>Comparison is numeric per component, not lexical: "0.10.0" is newer than "0.9.0" and
    /// a string comparison says the opposite. A missing or unparseable version is <b>not</b>
    /// treated as new enough, because the one thing worse than an old report is one that cannot be
    /// placed at all.</para>
    /// </summary>
    public static bool IsAtLeast(string? version, string? minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum)) return true;
        if (string.IsNullOrWhiteSpace(version)) return false;

        int[] have = Parse(version);
        int[] want = Parse(minimum);
        if (have.Length == 0) return false;

        for (int i = 0; i < Math.Max(have.Length, want.Length); i++)
        {
            int a = i < have.Length ? have[i] : 0;
            int b = i < want.Length ? want[i] : 0;

            if (a != b) return a > b;
        }

        return true;
    }

    // Leading digits of each dotted component, so "1.2.3-rc1" reads as 1.2.3. A pre-release of a
    // version is close enough to it to accept a report against.
    private static int[] Parse(string version)
    {
        List<int> parts = [];
        foreach (string chunk in version.Trim().TrimStart('v', 'V').Split('.'))
        {
            string digits = new([.. chunk.TakeWhile(char.IsAsciiDigit)]);
            if (digits.Length == 0) break;

            parts.Add(int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture));
        }

        return [.. parts];
    }

    // A leading timestamp and level, in the shape Log.cs writes them.
    [GeneratedRegex(@"^\s*\d{1,2}:\d{2}:\d{2}[.,]\d+\s+[A-Za-z]+\s+")]
    private static partial Regex Preamble();

    [GeneratedRegex(@"[-+]?\d[\d.,:]*")]
    private static partial Regex Numbers();

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUsers();

    [GeneratedRegex(@"/home/[^/\s]+")]
    private static partial Regex UnixHome();

    [GeneratedRegex(@"/Users/[^/\s]+")]
    private static partial Regex MacHome();
}
