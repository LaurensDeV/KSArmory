namespace KSArmory;

/// <summary>
/// Which contact an instrument keeps looking at, scan to scan — <b>the one it already holds unless another
/// is clearly more urgent</b>, because two contacts whose priorities are nearly equal swap places on noise
/// and a director following the head of the list swings between them several times a second.
/// </summary>
internal static class WatchChoice
{
    /// <summary>How much sooner another threat's closest approach must be, as a share of the held one's.</summary>
    public const double SoonerShare = 0.2;

    /// <summary>The least it must be sooner by, in seconds, so two threats arriving together do not trade.</summary>
    public const double SoonerSeconds = 1.0;

    /// <summary>How much nearer a contact that is not a threat must be, as a share of the held one's range.</summary>
    public const double NearerShare = 0.2;

    /// <summary>
    /// The index to watch: <paramref name="fresh"/>, the scan's own pick, or <paramref name="held"/>, the
    /// one watched last scan, each -1 when there is none.
    /// </summary>
    public static int Choose(IReadOnlyList<TrackState> tracks, int fresh, int held)
    {
        if (held < 0 || held >= tracks.Count) return fresh;
        if (fresh < 0 || fresh >= tracks.Count || fresh == held) return held;

        TrackState now = tracks[fresh], kept = tracks[held];

        if (now.IsThreat != kept.IsThreat) return now.IsThreat ? fresh : held;

        if (now.IsThreat)
        {
            double margin = Math.Max(SoonerSeconds, SoonerShare * kept.TimeToClosestApproach);
            return now.TimeToClosestApproach < kept.TimeToClosestApproach - margin ? fresh : held;
        }

        return now.Range < (1.0 - NearerShare) * kept.Range ? fresh : held;
    }
}
