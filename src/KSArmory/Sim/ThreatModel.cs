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
    /// What a sensor can tell about a contact besides where it is going: how large it looks and
    /// how far it is standing off the ground.
    ///
    /// <para>A type rather than two more arguments, so <see cref="Unknown"/> can say at a call
    /// site that nothing is known — which is the state every one of the rules reading it treats as
    /// "do not apply".</para>
    /// </summary>
    /// <param name="MeanRadius">The contact's own size (m), which its cross-section comes from.</param>
    /// <param name="HeightAboveSurface">Above the body's mean sphere (m).</param>
    internal readonly record struct ContactSignature(double MeanRadius, double HeightAboveSurface)
    {
        /// <summary>
        /// A contact nothing extra is known about. Deliberately makes every rule that reads it
        /// inert, so a call site that cannot supply the data gets the behaviour that shipped
        /// before the data existed rather than a silently different one.
        /// </summary>
        public static ContactSignature Unknown => new(0.0, double.PositiveInfinity);
    }

    /// <summary>
    /// Tests one contact against the search volume and, if it clears, works out its threat
    /// geometry.
    /// </summary>
    /// <param name="r">Target position relative to the battery (m), in Ecl.</param>
    /// <param name="v">Target velocity relative to the battery (m/s), in Ecl.</param>
    /// <param name="boresight">Unit vector the radar points along, in Ecl.</param>
    /// <returns>
    /// False when the contact is out of range for its size, outside the cone, too slow, sitting in
    /// the Doppler notch, or down in the ground clutter.
    /// </returns>
    public static bool TryAssess(double3 r, double3 v, double3 boresight, SensorProfile sensor,
                                 ContactSignature signature, out Assessment assessment)
    {
        assessment = default;

        double rangeSquared = Vec.Len2(r);
        double reach = DetectionRange(sensor, signature);

        // The lower bound is not paranoia: a contact at zero range has no direction, so the
        // cone test below would normalise a zero vector.
        if (rangeSquared > reach * reach || rangeSquared < 1.0) return false;

        double3 lineOfSight = Vec.Unit(r);
        if (Vec.Dot(lineOfSight, boresight) < Math.Cos(sensor.ConeHalfAngleRad)) return false;

        // Ignore anything drifting with us — docked craft, debris on the same trajectory.
        if (Vec.Len(v) < sensor.MinTargetSpeed) return false;

        double closing = -Vec.Dot(v, lineOfSight);

        // The notch, which cuts both ways on purpose: a target crossing exactly abeam has no
        // radial motion and is rejected along with the clutter. Absolute, because a set cannot
        // tell an opening target from a closing one by how much Doppler it has.
        if (sensor.NotchSpeed > 0f && Math.Abs(closing) < sensor.NotchSpeed) return false;

        // Down in the ground return. Against the mean sphere, so it costs nothing.
        if (sensor.ClutterFloorMetres > 0f
            && signature.HeightAboveSurface < sensor.ClutterFloorMetres)
        {
            return false;
        }

        double tCa = Vec.TimeOfClosestApproach(r, v, sensor.ThreatHorizonSeconds);
        double cpa = Vec.Len(r + v * tCa);
        double range = Math.Sqrt(rangeSquared);

        assessment = new Assessment(
            Range: range,
            ClosingSpeed: closing,
            ClosestApproach: cpa,
            TimeToClosestApproach: tCa,
            // Either it will pass close enough to matter, or it is already inside the bubble.
            //
            // The second half is redundant *today* and deliberately kept. TimeOfClosestApproach
            // clamps to [0, horizon], so the search starts at now and its minimum can never
            // exceed the value at now — which is the range. Hence cpa <= range always, and
            // `range <= ThreatRadius` cannot flip a false to a true.
            //
            // It stays because the invariant belongs to the clamp, not to this rule. Allowing a
            // negative tCa — to model a target that has already passed — is a plausible future
            // change, and it would make this term load-bearing again with nothing to say so.
            // ClosestApproachNeverExceedsCurrentRange pins the invariant meanwhile.
            IsThreat: cpa <= sensor.ThreatRadius || range <= sensor.ThreatRadius);

        return true;
    }

    /// <summary>
    /// How far this set reaches against a contact of this size.
    ///
    /// <para>Its own <see cref="SensorProfile.Range"/> unless the set has been given a reference
    /// cross-section, so a profile that says nothing about size behaves exactly as it did before
    /// there was anything to say.</para>
    /// </summary>
    public static double DetectionRange(SensorProfile sensor, ContactSignature signature)
        => RadarSignature.DetectionRange(sensor.Range,
                                         RadarSignature.CrossSectionFor(signature.MeanRadius),
                                         sensor.ReferenceCrossSectionM2);

    /// <summary>
    /// Whether a contact is physically within the sensor's reach — range and cone only.
    ///
    /// <para>Deliberately excludes the threat and policy filters that <see cref="TryAssess"/>
    /// applies. A command-linked round needs to know whether the launcher can still *see* its
    /// target, which is a question about the radar. Whether the operator wants to shoot at it
    /// is a separate question, and one test answering both cuts the uplink to rounds already
    /// flying at a contact the operator has declined to engage.</para>
    /// </summary>
    public static bool InSensorVolume(double3 r, double3 boresight, SensorProfile sensor,
                                      ContactSignature signature)
    {
        double rangeSquared = Vec.Len2(r);

        // The same reach the detection used. Taking the profile's raw range instead would keep an
        // uplink alive out to 36 km against something the set only sees at 9, so a round would go
        // on being steered at a contact its own launcher had lost.
        double reach = DetectionRange(sensor, signature);

        if (rangeSquared > reach * reach || rangeSquared < 1.0) return false;
        return Vec.Dot(Vec.Unit(r), boresight) >= Math.Cos(sensor.ConeHalfAngleRad);
    }

    /// <summary>
    /// Orders tracks so the most immediate threat comes first. Stable ordering is not required
    /// and not promised; ties between equal priorities may fall either way.
    /// </summary>
    public static void SortByPriority<T>(List<T> tracks) where T : TrackState
        => tracks.Sort(static (a, b) =>
        {
            int byPriority = a.Priority.CompareTo(b.Priority);

            // Range breaks the tie, because there is always a tie: every non-threat has a priority
            // of MaxValue, so a scope holding four of them is comparing four equal keys. List.Sort
            // is not stable, so without a second key their order is whatever the sort happened to
            // do -- and an optical director watches Tracks[0], which then means the instrument
            // picks a different contact with nothing in the world having changed.
            return byPriority != 0 ? byPriority : a.Range.CompareTo(b.Range);
        });

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
    /// Pantsir. Firing at everything detected wastes the magazine on contacts the rounds expire
    /// short of. There is a floor as well as a ceiling: inside about a kilometre the round is
    /// still boosting and cannot be brought round.</para>
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
