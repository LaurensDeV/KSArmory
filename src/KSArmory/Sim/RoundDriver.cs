using Brutal.Numerics;
using System;

namespace KSArmory;

/// <summary>
/// The fields a round re-reads <em>inside</em> its own sub-step loop, rather than holding at the
/// frame's first sample.
///
/// <para>Each is a lookup rather than a value because the round moves within the frame and these
/// three change materially over that distance: air density falls off on an 8 km scale height,
/// gravity is a field about a centre the round is closing on, and the ground is whatever is
/// underneath at the time. A round handed values instead of lookups flies the whole frame through
/// the conditions it had at the top of it.</para>
///
/// <para>The frame's own values are still passed to <see cref="RoundDriver.Fly"/> beside these:
/// they are what a round falls back to when a lookup is absent or answers with nothing.</para>
/// </summary>
/// <param name="GravityAt">
/// The pull at a stated position and a stated time into the frame. Both arguments matter — the time
/// is <em>back-dated</em>, because the body it is differenced from was sampled at the frame's end.
/// </param>
/// <param name="AirDensityAt">The density at a stated position, back-dated the same way.</param>
/// <param name="Ground">Where the surface is under the round, or null for a round nothing stops.</param>
/// <param name="GroundCentreDriftAt">
/// How far the sampled ground centre has moved by a stated time into the frame, back-dated the same
/// way. The radius keeps for the frame — it is a property of the ground — but the centre is a
/// position on a body doing ~30 km/s, so holding it drifts against the round.
/// </param>
internal readonly record struct RoundFields(
    Func<double3, double, double3>? GravityAt,
    Func<double3, double, double>? AirDensityAt,
    IGroundTest? Ground,
    Func<double, double3>? GroundCentreDriftAt = null)
{
    /// <summary>
    /// No lookups at all: every field held at the frame's first sample for the whole frame.
    ///
    /// <para><b>Nothing flies this way.</b> It is here so a budget can price what re-reading a
    /// field is worth, by flying the same release state both ways — and it is a named thing rather
    /// than an omission precisely so that "the round as the game flies it" cannot be arrived at by
    /// forgetting to pass something.</para>
    /// </summary>
    public static RoundFields Held => default;
}

/// <summary>
/// The one place a round is advanced by one frame.
///
/// <para><b>A seam rather than tidiness.</b> The game and the headless rig both come through here,
/// so neither can hold an opinion of its own about which fields a round re-reads within a frame. A
/// second implementation of this loop agrees with the first only while somebody keeps the two in
/// step by hand, and a rig that has drifted still flies — it just prices every budget against a
/// round nothing flew.</para>
///
/// <para>So a flight's configuration is <em>what the caller supplies</em>, never a flag something
/// else interprets. The round the game flies is spelled by passing the game's lookups, which is
/// the only way to spell it and not a thing anyone can arrive at by accident.</para>
///
/// <para>What deliberately stays with the caller is everything KSA-shaped — the contact candidates
/// and the hull test — because <c>Sim/</c> cannot name those types and the rig has no equivalent of
/// them.</para>
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
        // Assigned rather than assigned-if-present: a caller that means "hold this field" says so
        // by passing nothing for it, and that has to be distinguishable from a caller that simply
        // did not reach this line. Only a Slug reads them — an interceptor's flight is dominated by
        // its own guidance rather than by the field it flies through.
        if (round is Slug slug)
        {
            slug.GravityAt = fields.GravityAt;
            slug.AirDensityAt = fields.AirDensityAt;
            slug.Ground = fields.Ground;
            slug.GroundCentreDriftAt = fields.GroundCentreDriftAt;
        }

        round.Update(dt, target, gravity, frameVelocityEcl, platformEcl, munition, mediumDensityRatio);
    }
}
