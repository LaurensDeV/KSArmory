using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Decides what counts as a threat and which one to shoot at first.
///
/// <para>Two jobs: the search volume and the CPA geometry, then the ranking and the salvo rules.
/// Both are pure arithmetic over relative motion, so they live here rather than beside the
/// sensor and fire control that call them, where nothing under <c>Ksa/</c> can be tested.</para>
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
    /// Whether a contact is physically within the sensor's reach — range and cone only.
    ///
    /// <para>Deliberately excludes the threat and policy filters that <see cref="TryAssess"/>
    /// applies. A command-linked round needs to know whether the launcher can still *see* its
    /// target, which is a question about the radar. Whether the operator wants to shoot at it
    /// is a separate question, and answering them with the same test meant that declining to
    /// engage a contact also cut the uplink to rounds already flying at it.</para>
    /// </summary>
    public static bool InSensorVolume(double3 r, double3 boresight, SensorProfile sensor)
    {
        double rangeSquared = Vec.Len2(r);
        if (rangeSquared > (double)sensor.Range * sensor.Range || rangeSquared < 1.0) return false;
        return Vec.Dot(Vec.Unit(r), boresight) >= Math.Cos(sensor.ConeHalfAngleRad);
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
    /// Whether the target is inside the weapon's reach — not merely detected.
    ///
    /// <para>A search radar sees far further than the round flies: 36 km against 20 km for the
    /// Pantsir. Firing at everything detected wastes the magazine on contacts that expire
    /// short, which is exactly what happened to every long crossing shot. There is a floor as
    /// well as a ceiling: inside about a kilometre the round is still boosting and cannot be
    /// brought round.</para>
    /// </summary>
    public static bool InEngagementEnvelope(TrackState track, MunitionProfile munition)
        => track.Range >= munition.MinRange && track.Range <= munition.MaxRange;

    /// <summary>
    /// Whether this contact may be fired on at all, before any geometry is considered.
    ///
    /// <para>Separate from <see cref="InEngagementEnvelope"/> because the two answer different
    /// questions: this one is about whose side the contact is on, that one about whether the round
    /// can reach it. A friendly inside the envelope must still be refused.</para>
    /// </summary>
    public static bool MayEngage(TrackState track, IffPolicy iff) => iff.MayEngage(track.Allegiance);

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
