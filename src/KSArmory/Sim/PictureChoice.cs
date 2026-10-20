namespace KSArmory;

/// <summary>
/// One craft's picture from every set searching aboard it: each contact once, and the one to hand a
/// director or a beam that has nothing more specific.
/// </summary>
internal static class PictureChoice
{
    /// <summary>
    /// The union of several sets' tracks, one per handle — the most urgent sighting of it, nearer
    /// breaking a tie — sorted by <see cref="ThreatModel.SortByPriority"/>.
    /// </summary>
    public static void Merge<T>(IReadOnlyList<IReadOnlyList<T>> sets, Func<T, object> handleOf, List<T> into)
        where T : TrackState
    {
        into.Clear();
        var at = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        for (int s = 0; s < sets.Count; s++)
        {
            IReadOnlyList<T> tracks = sets[s];
            for (int i = 0; i < tracks.Count; i++)
            {
                T track = tracks[i];
                object handle = handleOf(track);

                if (!at.TryGetValue(handle, out int index))
                {
                    at[handle] = into.Count;
                    into.Add(track);
                    continue;
                }

                T kept = into[index];
                if (track.Priority < kept.Priority
                    || (track.Priority == kept.Priority && track.Range < kept.Range))
                {
                    into[index] = track;
                }
            }
        }

        ThreatModel.SortByPriority(into);
    }

    /// <summary>
    /// The index in a merged picture to hand out, or -1: a threat first, else the nearest that is not
    /// friendly, held across scans by <see cref="WatchChoice"/>. A friendly is never handed out.
    /// </summary>
    /// <param name="held">The handle handed out last time, or null.</param>
    public static int Choose<T>(IReadOnlyList<T> picture, Func<T, object> handleOf, object? held)
        where T : TrackState
    {
        int fresh = -1;
        int kept = -1;

        for (int i = 0; i < picture.Count; i++)
        {
            T track = picture[i];
            if (track.Allegiance == Allegiance.Friendly) continue;

            if (fresh < 0 || Better(track, picture[fresh])) fresh = i;
            if (held is not null && ReferenceEquals(handleOf(track), held)) kept = i;
        }

        return WatchChoice.Choose((IReadOnlyList<TrackState>)picture, fresh, kept);
    }

    private static bool Better(TrackState a, TrackState b)
    {
        if (a.IsThreat != b.IsThreat) return a.IsThreat;
        return a.Priority != b.Priority ? a.Priority < b.Priority : a.Range < b.Range;
    }
}
