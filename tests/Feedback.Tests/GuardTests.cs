using KSArmory.Feedback;
using Xunit;

namespace KSArmory.Feedback.Tests;

/// <summary>What a stranger's text is allowed to become before it is rendered on a public page.</summary>
public class GuardTests
{
    [Fact]
    public void WhatCannotBeSeenIsRemoved()
    {
        // A right-to-left override renders as nothing and reverses everything after it, so the
        // text a reviewer reads is not the text that was sent.
        Assert.Equal("safe", Guard.StripInvisible("sa\u202Efe"));
        Assert.Equal("ab", Guard.StripInvisible("a\u200Bb"));
        Assert.Equal("ab", Guard.StripInvisible("a\u0000b"));
    }

    [Fact]
    public void NewlinesAndTabsSurvive()
    {
        // A log stripped of them is unreadable, which would defeat the point of attaching one.
        Assert.Equal("a\nb\tc", Guard.StripInvisible("a\nb\tc"));
    }

    [Theory]
    [InlineData(@"C:\Users\someone\Documents\log", @"C:\Users\<user>\Documents\log")]
    [InlineData("/home/someone/.local/share", "/home/<user>/.local/share")]
    [InlineData("/Users/someone/Library", "/Users/<user>/Library")]
    public void AHomeDirectoryIsNotPublished(string path, string expected)
    {
        // KSA writes its own path into the log. Someone reporting a bug is not choosing to publish
        // their account name.
        Assert.Equal(expected, Guard.ScrubPaths(path));
    }

    [Fact]
    public void TruncationCountsWhatWillBeShown()
    {
        // Truncating before scrubbing would count characters that a later step removes, so the
        // limit would not mean what it says.
        string cleaned = Guard.Clean(@"C:\Users\someone\x " + new string('y', 100), 40);

        Assert.StartsWith(@"C:\Users\<user>\x", cleaned);
        Assert.EndsWith("[truncated]", cleaned);
    }

    [Fact]
    public void TheSameReportHasTheSameFingerprint()
    {
        // What makes one message sent from a thousand addresses one issue rather than a thousand.
        Assert.Equal(
            Guard.Fingerprint("Turret stuck", "it does not move"),
            Guard.Fingerprint("  turret STUCK  ", "It Does Not Move"));

        Assert.NotEqual(
            Guard.Fingerprint("Turret stuck", "it does not move"),
            Guard.Fingerprint("Turret stuck", "it moves too far"));
    }

    [Fact]
    public void TheFieldsCannotBeSlidPastEachOther()
    {
        // Without a separator, ("ab", "c") and ("a", "bc") are the same report.
        Assert.NotEqual(Guard.Fingerprint("ab", "c"), Guard.Fingerprint("a", "bc"));
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaa", true)]
    [InlineData("asdasdasdasdasd", false)]
    [InlineData("the turret does not move at all", false)]
    [InlineData("aaaa", false)]
    public void AKeyboardMashIsRecognised(string text, bool mash)
        => Assert.Equal(mash, Guard.LooksLikeMash(text));

    [Theory]
    [InlineData("the turret does not move when I arm it and the radar has a lock", true)]
    [InlineData("turret stuck", true)]
    [InlineData("", true)]
    [InlineData("Турель не двигается когда я её включаю и радар видит цель уже", false)]
    [InlineData("砲塔がロックしても動きません。レーダーは目標を見つけています。", false)]
    public void EnglishIsJudgedLeniently(string text, bool english)
    {
        // Short text is always accepted: "turret stuck" carries no evidence either way, and
        // refusing a real report for being terse is worse than reading one in Dutch.
        Assert.Equal(english, Guard.LooksEnglish(text));
    }

    [Theory]
    [InlineData("0.10.0", "0.9.0", true)]      // the comparison a string sort gets backwards
    [InlineData("0.9.0", "0.10.0", false)]
    [InlineData("1.0.0", "1.0.0", true)]
    [InlineData("v1.2.3", "1.2.0", true)]
    [InlineData("1.2.3-rc1", "1.2.3", true)]   // a pre-release is close enough to report against
    [InlineData(null, "1.0.0", false)]         // unplaceable is worse than old
    [InlineData("nonsense", "1.0.0", false)]
    [InlineData("anything", null, true)]       // no minimum configured accepts everything
    public void VersionsCompareNumerically(string? version, string? minimum, bool ok)
        => Assert.Equal(ok, Guard.IsAtLeast(version, minimum));
}
