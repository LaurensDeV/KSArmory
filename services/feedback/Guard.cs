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

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUsers();

    [GeneratedRegex(@"/home/[^/\s]+")]
    private static partial Regex UnixHome();

    [GeneratedRegex(@"/Users/[^/\s]+")]
    private static partial Regex MacHome();
}
