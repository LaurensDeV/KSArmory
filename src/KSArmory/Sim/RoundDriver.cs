using Brutal.Numerics;
using System;

namespace KSArmory;

/// <summary>
/// The fields a round re-reads inside its own sub-step loop rather than holding at the frame's first
/// sample. The frame's own values, passed to <see cref="RoundDriver.Fly"/> beside these, are the
/// fallback when a lookup is absent or answers nothing.
/// </summary>
/// <param name="GravityAt">
/// The pull at a stated position and a stated time into the frame. Both arguments matter — the time
/// is <em>back-dated</em>, because the body it is differenced from was sampled at the frame's end.
/// </param>
/// <param name="AirDensityAt">The density at a stated position, back-dated the same way.</param>
/// <param name="AirVelocityAt">
/// The motion of the air at a stated position, back-dated the same way: the ground's velocity, which
/// carries the body's spin at that radius and so belongs to the place. Absent, the frame's first sample.
/// </param>
/// <param name="Ground">Where the surface is under the round, or null for a round nothing stops.</param>
/// <param name="ApproachAt">
/// How far along its arrival a round the ground stops has got, given its ecliptic position and
/// velocity. Absent leaves that round's age as the only thing that reaps it.
/// </param>
/// <param name="GroundCentreDriftAt">
/// How far the sampled ground centre has moved by a stated time into the frame, back-dated the same
/// way: the centre rides a body doing ~30 km/s, so holding it drifts against the round.
/// </param>
/// <param name="GroundQueryDriftAt">
/// How far the ground <em>under a stated point</em> has moved by then — the centre's travel plus the
/// body's spin at that radius. A terrain query is a direction in the frame the surface turns in, so
/// it needs the spin term the centre does not; see <see cref="Slug.GroundQueryAtOwnEpoch"/>.
/// </param>
internal readonly record struct RoundFields(
    Func<double3, double, double3>? GravityAt,
    Func<double3, double, double>? AirDensityAt,
    IGroundTest? Ground,
    Func<double, double3>? GroundCentreDriftAt = null,
    Func<double3, double, double3>? GroundQueryDriftAt = null,
    Func<double3, double3, Approach>? ApproachAt = null,
    Func<double3, double, double3>? AirVelocityAt = null)
{
    /// <summary>
    /// Every field held at the frame's first sample. Nothing flies this way: it prices what re-reading a
    /// field is worth, and is named so it cannot be reached by forgetting to pass something.
    /// </summary>
    public static RoundFields Held => default;
}

/// <summary>
/// The one place a round is advanced by one frame. The game and the headless rig both come through
/// here, so they cannot differ about which fields a round re-reads; a flight's configuration is what
/// the caller supplies, never a flag. The contact candidates and the hull test stay with the caller.
/// </summary>
internal static class RoundDriver
{
    /// <param name="gravity">The frame's own pull, used where <see cref="RoundFields.GravityAt"/> cannot answer.</param>
    /// <param name="frameVelocityEcl">The motion of the medium the round measures its airspeed against.</param>
    /// <param name="platformEcl">
    /// The launcher's position <em>this</em> frame. The round's drawn offset is differenced against
    /// it after the step and with no extrapolation — see <c>docs/FRAMES-AND-EPOCHS.md</c>.
    /// </param>
    public static void Fly(IProjectile round, double dt, TargetState? target,
                           double3 gravity, double3 frameVelocityEcl, double3 platformEcl,
                           MunitionProfile munition, double mediumDensityRatio,
                           in RoundFields fields)
    {
        // Assigned even when null: passing nothing is how a caller holds a field.
        if (round is Slug slug)
        {
            slug.GravityAt = fields.GravityAt;
            slug.AirDensityAt = fields.AirDensityAt;
            slug.Ground = fields.Ground;
            slug.GroundCentreDriftAt = fields.GroundCentreDriftAt;
            slug.GroundQueryDriftAt = fields.GroundQueryDriftAt;
            slug.ApproachAt = fields.ApproachAt;
            slug.AirVelocityAt = fields.AirVelocityAt;
        }
        else if (round is Interceptor missile)
        {
            // Gravity is held: guidance dominates. The medium is not: held for the frame, a round
            // low over the sea reads as under it.
            missile.AirDensityAt = fields.AirDensityAt;
            missile.Ground = fields.Ground;
            missile.GroundCentreDriftAt = fields.GroundCentreDriftAt;
        }

        round.Update(dt, target, gravity, frameVelocityEcl, platformEcl, munition, mediumDensityRatio);
    }
}
