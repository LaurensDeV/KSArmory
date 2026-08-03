using Brutal.Numerics;

namespace AirDefence;

/// <summary>
/// Decides what counts as a threat and which one to shoot at first.
///
/// <para>Two jobs, both pure arithmetic over relative motion, and both previously stranded
/// inside KSA-facing classes where nothing could test them. The search-volume and CPA maths
/// came out of <c>Ksa/Radar.cs</c>; the ranking and salvo rules out of
/// <c>Ksa/DefenceBattery.cs</c>.</para>
///
/// <para>Everything works in <em>relative</em> position and velocity — target minus battery —
/// so the ecliptic frame's ~1.5e11 m offset and ~29.8 km/s of common motion cancel before any
/// of it runs. See <see cref="DrawAnchor"/> for why that distinction is load-bearing here.</para>
/// </summary>
internal static class ThreatModel
{
    /// <summary>What the radar works out about one contact that clears the search volume.</summary>
    internal readonly record struct Assessment(
        double Range,
        double ClosingSpeed,
        double ClosestApproach,
        double TimeToClosestApproach,
        bool IsThreat);

    /// <summary>
    /// Tests one contact against the search volume and, if it clears, works out its threat
    /// geometry.
    /// </summary>
    /// <param name="r">Target position relative to the battery (m), in Ecl.</param>
    /// <param name="v">Target velocity relative to the battery (m/s), in Ecl.</param>
    /// <param name="boresight">Unit vector the radar points along, in Ecl.</param>
    /// <returns>False when the contact is out of range, outside the cone, or too slow.</returns>
    public static bool TryAssess(double3 r, double3 v, double3 boresight, SensorProfile sensor,
                                 out Assessment assessment)
    {
        assessment = default;

        double rangeSquared = Vec.Len2(r);

        // The lower bound is not paranoia: a contact at zero range has no direction, so the
        // cone test below would normalise a zero vector.
        if (rangeSquared > (double)sensor.Range * sensor.Range || rangeSquared < 1.0) return false;

        double3 lineOfSight = Vec.Unit(r);
        if (Vec.Dot(lineOfSight, boresight) < Math.Cos(sensor.ConeHalfAngleRad)) return false;

        // Ignore anything drifting with us — docked craft, debris on the same trajectory.
        if (Vec.Len(v) < sensor.MinTargetSpeed) return false;

        double tCa = Vec.TimeOfClosestApproach(r, v, sensor.ThreatHorizonSeconds);
        double cpa = Vec.Len(r + v * tCa);
        double range = Math.Sqrt(rangeSquared);

        assessment = new Assessment(
            Range: range,
            ClosingSpeed: -Vec.Dot(v, lineOfSight),
            ClosestApproach: cpa,
            TimeToClosestApproach: tCa,
            // Either it will pass close enough to matter, or it is already inside the bubble.
            //
            // The second half is redundant *today* and deliberately kept. TimeOfClosestApproach
            // clamps to [0, horizon], so the search starts at now and its minimum can never
            // exceed the value at now — which is the range. Hence cpa <= range always, and
            // `range <= ThreatRadius` cannot flip a false to a true. Verified by deleting it
            // and watching all 92 tests still pass.
            //
            // It stays because the invariant belongs to the clamp, not to this rule. Allowing a
            // negative tCa — to model a target that has already passed — is a plausible future
            // change, and it would make this term load-bearing again with nothing to say so.
            // ClosestApproachNeverExceedsCurrentRange pins the invariant meanwhile.
            IsThreat: cpa <= sensor.ThreatRadius || range <= sensor.ThreatRadius);

        return true;
    }

    /// <summary>
    /// Orders tracks so the most immediate threat comes first. Stable ordering is not required
    /// and not promised; ties between equal priorities may fall either way.
    /// </summary>
    public static void SortByPriority<T>(List<T> tracks) where T : TrackState
        => tracks.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));

    /// <summary>
    /// The threat reaching its closest approach soonest, or -1 if nothing qualifies.
    ///
    /// <para>Returns an index rather than the track itself so the caller keeps its own richer
    /// type — the alternative is handing back a <see cref="TrackState"/> the battery would have
    /// to cast to get the vehicle out of.</para>
    ///
    /// <para>Deliberately independent of list order: this is used to aim the turret while the
    /// lock is still settling, and it must not silently depend on <see cref="SortByPriority"/>
    /// having run first.</para>
    /// </summary>
    public static int IndexOfMostUrgent(IReadOnlyList<TrackState> tracks)
    {
        int best = -1;
        for (int i = 0; i < tracks.Count; i++)
        {
            if (!tracks[i].IsThreat) continue;
            if (best < 0 || tracks[i].TimeToClosestApproach < tracks[best].TimeToClosestApproach)
                best = i;
        }
        return best;
    }

    /// <summary>The first track that is a threat, or -1. Meaningful once sorted.</summary>
    public static int IndexOfFirstThreat(IReadOnlyList<TrackState> tracks)
    {
        for (int i = 0; i < tracks.Count; i++)
            if (tracks[i].IsThreat) return i;
        return -1;
    }

    /// <summary>
    /// Whether another round may be committed to this track.
    ///
    /// <para>This is what stops the battery emptying all twelve tubes into the first contact it
    /// sees and having nothing left for the second. It counts rounds already in the air, not
    /// rounds fired, so a miss frees the allocation again when the round expires.</para>
    /// </summary>
    public static bool HasSalvoCapacity(TrackState track, int roundsPerTarget)
        => track.RoundsAssigned < roundsPerTarget;
}
