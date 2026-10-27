using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Decides what counts as a threat and which one to shoot at first.
///
/// <para>Everything takes <em>relative</em> position and velocity, target minus system, so the
/// ecliptic's ~1.5e11 m offset and ~29.8 km/s of common motion cancel before any of it runs.</para>
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
    /// What a sensor can tell about a contact besides where it is going. <see cref="Unknown"/> is the
    /// state every rule reading it treats as "do not apply".
    /// </summary>
    /// <param name="MeanRadius">The contact's own size (m), which its cross-section comes from.</param>
    /// <param name="HeightAboveSurface">Above the body's mean sphere (m).</param>
    /// <param name="IsMunition">
    /// A round in flight rather than a craft, held to a closing geometry. False gets the craft's rule.
    /// </param>
    internal readonly record struct ContactSignature(double MeanRadius, double HeightAboveSurface,
                                                     bool IsMunition = false)
    {
        /// <summary>Makes every rule that reads it inert, rather than silently different.</summary>
        public static ContactSignature Unknown => new(0.0, double.PositiveInfinity);
    }

    /// <param name="r">Target position relative to the system (m), in Ecl.</param>
    /// <param name="v">Target velocity relative to the system (m/s), in Ecl.</param>
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

        // A contact at zero range has no direction for the cone test to normalise.
        if (rangeSquared > reach * reach || rangeSquared < 1.0) return false;

        double3 lineOfSight = Vec.Unit(r);
        if (Vec.Dot(lineOfSight, boresight) < Math.Cos(sensor.ConeHalfAngleRad)) return false;

        // Ignore anything drifting with us — docked craft, debris on the same trajectory.
        if (Vec.Len(v) < sensor.MinTargetSpeed) return false;

        double closing = -Vec.Dot(v, lineOfSight);

        // Rejects a target crossing abeam along with the clutter. Absolute: a set cannot tell an
        // opening target from a closing one by how much Doppler it has.
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
            // The range term is redundant while TimeOfClosestApproach clamps tCa at zero, and kept for
            // the day it does not (ClosestApproachNeverExceedsCurrentRange pins the clamp).
            // An opening munition will never be nearer, and one a neighbouring mount fires is born
            // inside the radius; a craft can come back, so the closing rule is the round's alone.
            IsThreat: (cpa <= sensor.ThreatRadius || range <= sensor.ThreatRadius)
                      && (closing > 0.0 || !signature.IsMunition));

        return true;
    }

    /// <summary>
    /// How far this set reaches against a contact of this size.
    ///
    /// <para><see cref="SensorProfile.Range"/> whatever the size, unless the set has a reference
    /// cross-section.</para>
    /// </summary>
    public static double DetectionRange(SensorProfile sensor, ContactSignature signature)
        => RadarSignature.DetectionRange(sensor.Range,
                                         RadarSignature.CrossSectionFor(signature.MeanRadius),
                                         sensor.ReferenceCrossSectionM2);

    /// <summary>
    /// Whether a contact is physically within the sensor's reach — range and cone only.
    ///
    /// <para>Without <see cref="TryAssess"/>'s threat and policy filters: a command-link round asks whether
    /// the launcher can still see its target, and those filters would cut its uplink.</para>
    /// </summary>
    public static bool InSensorVolume(double3 r, double3 boresight, SensorProfile sensor,
                                      ContactSignature signature)
    {
        double rangeSquared = Vec.Len2(r);

        // The reach detection used, not the raw range, or a round is steered at a contact the set lost.
        double reach = DetectionRange(sensor, signature);

        if (rangeSquared > reach * reach || rangeSquared < 1.0) return false;
        return Vec.Dot(Vec.Unit(r), boresight) >= Math.Cos(sensor.ConeHalfAngleRad);
    }

    /// <summary>Orders tracks so the most immediate threat comes first, nearest first among equals.</summary>
    public static void SortByPriority<T>(List<T> tracks) where T : TrackState
        => tracks.Sort(static (a, b) =>
        {
            int byPriority = a.Priority.CompareTo(b.Priority);

            // Every non-threat has priority MaxValue and List.Sort is not stable, so without range a
            // director watching Tracks[0] would switch contacts with nothing in the world changed.
            return byPriority != 0 ? byPriority : a.Range.CompareTo(b.Range);
        });

    /// <summary>
    /// The threat reaching its closest approach soonest, or -1 if nothing qualifies.
    ///
    /// <para>Independent of list order: it aims the turret while the lock settles, and must not depend on
    /// <see cref="SortByPriority"/> having run.</para>
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
    /// <para>The Pantsir's set sees 36 km and its round flies 20; inside the floor, 1.2 km, the round is
    /// still boosting and cannot be brought round.</para>
    /// </summary>
    public static bool InEngagementEnvelope(TrackState track, MunitionProfile munition)
        => track.Range >= munition.MinRange && track.Range <= munition.MaxRange;

    /// <summary>Whether this contact's side may be fired on at all, before any geometry.</summary>
    public static bool MayEngage(TrackState track, IffPolicy iff) => iff.MayEngage(track.Allegiance);

    /// <summary>
    /// Whether another round may be committed to this track.
    ///
    /// <para>Counts rounds in the air, not rounds fired, so a miss frees the allocation when it expires.</para>
    /// </summary>
    public static bool HasSalvoCapacity(TrackState track, int roundsPerTarget)
        => track.RoundsAssigned < roundsPerTarget;
}
