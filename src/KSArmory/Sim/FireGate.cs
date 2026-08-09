using Brutal.Numerics;

namespace KSArmory;

/// <summary>Whether the launcher is pointing where it is about to shoot.</summary>
public static class FireGate
{
    /// <summary>
    /// A launcher with no training gear is always laid — there is nothing that could be pointing
    /// the wrong way. One whose drive the engine has refused is frozen wherever it stopped, and is
    /// not: firing then ejects rounds along a stale tube transform, which guidance recovers from
    /// well enough that nothing but the drawn facing line shows it happened.
    /// </summary>
    /// <param name="aiming">Slewing onto a track, rather than stowed or driven from the panel.</param>
    /// <param name="trains">The profile declares gear that has to be aimed before firing.</param>
    /// <param name="drivesAccepted">The engine still accepts the traverse and elevation writes.</param>
    /// <param name="assembliesResolved">Those subparts were found on the vehicle.</param>
    /// <param name="settled">The drives have held the commanded bearing for the settle time.</param>
    public static bool IsLaid(bool aiming, bool trains, bool drivesAccepted,
                              bool assembliesResolved, bool settled)
        => !aiming || !trains || (drivesAccepted && assembliesResolved && settled);

    /// <summary>
    /// Whether this engagement belongs to the cannon: inside their envelope, with belt left and
    /// switched on.
    ///
    /// <para>Load-bearing beyond choosing a weapon, because the turret lays on the gun's
    /// ballistic lead whenever this is true — see <see cref="MissilesMayFire"/>.</para>
    /// </summary>
    public static bool GunsHaveTheEngagement(bool hasCannon, bool gunsEnabled, bool beltHasRounds,
                                             double range, double gunMinRange, double gunMaxRange)
        => hasCannon && gunsEnabled && beltHasRounds
           && range >= gunMinRange && range <= gunMaxRange;

    /// <summary>
    /// Whether a missile may leave the tube. False whenever the ring is laid on something other
    /// than what the missile is about to be fired at — the gun's ballistic lead, or the operator's
    /// cursor. Rounds leave along the tube, so the ring's aim is the missile's launch heading.
    ///
    /// <para>The envelopes overlap — 200–4000 m for the cannon against 1200–20000 m for the
    /// missiles — and inside that band the turret can only point at one solution. A missile
    /// launched along the tube in that state leaves ~18° off for a 300 m/s crosser. Proportional
    /// navigation recovers well enough that the intercept still lands, so nothing on screen shows
    /// the round leaving a launcher aimed somewhere else.</para>
    ///
    /// <para>The cannon win the overlap rather than the missiles because the lead is only applied
    /// when the guns can actually take the shot, and a missile held for one pass is cheaper than
    /// a missile spent off-axis.</para>
    ///
    /// <para>The same shape covers the operator: with mouse aim on, the ring follows the cursor
    /// while auto-engage commits the round to the radar's lock, which can be anywhere — up to
    /// 180° away. Nothing else catches that. A command-link round is exempt from
    /// <see cref="CanGuideOntoAimpoint"/> by its first line, so the seeker limit never runs.</para>
    /// </summary>
    /// <param name="ringIsElsewhere">
    /// The turret is laid on something the missile is not being fired at. For the cannon that
    /// means the lead actually solved — a solve that fails leaves the ring on the target, which
    /// the missiles can use, so it is not the same question as <see cref="GunsHaveTheEngagement"/>.
    /// </param>
    /// <param name="launchAlongTube">Rounds leave along the tube, so the ring's aim is theirs too.</param>
    public static bool MissilesMayFire(bool ringIsElsewhere, bool launchAlongTube)
        => !(ringIsElsewhere && launchAlongTube);

    /// <summary>
    /// Whether a round leaving along <paramref name="launchDirection"/> can steer onto where it
    /// was sent.
    ///
    /// <para>A seeker only guides while its target is inside its gimbal limit of the round's own
    /// flight path — and outside it there is no steering, so the flight path never changes, so it
    /// never comes back inside. The limit is therefore decided at launch and permanently: a round
    /// released more than <paramref name="seekerFovRad"/> off its aimpoint flies straight on until
    /// it expires. That is invisible in flight, which is why it is a gate rather than a comment.</para>
    ///
    /// <para>Only a launcher whose rounds leave along a fixed tube can get here. One that trains
    /// has already laid the tube on the target, and a command-linked round is steered by the
    /// launcher rather than by anything it can see for itself.</para>
    /// </summary>
    /// <param name="operatorHeld">
    /// The aimpoint is a place someone designated rather than something the round has to find.
    /// It is steered onto like a command-linked round however the round is guided otherwise —
    /// there is nothing a gimbal limit could lose.
    /// </param>
    public static bool CanGuideOntoAimpoint(GuidanceMode guidance, bool operatorHeld,
                                            double seekerFovRad,
                                            double3 launchDirection, double3 toAimpoint)
    {
        // An unguided round is not being steered anywhere, so there is nothing here to refuse:
        // where it goes was settled by the tube. Command link is steered by the launcher, and an
        // operator-held shot has no other way to reach a place the launcher cannot be pointed at.
        if (guidance != GuidanceMode.Seeker || operatorHeld) return true;

        if (!Vec.IsFinite(launchDirection) || !Vec.IsFinite(toAimpoint)) return false;
        if (Vec.Len2(launchDirection) < 1e-12 || Vec.Len2(toAimpoint) < 1e-12) return false;

        return Vec.AngleBetween(toAimpoint, launchDirection) <= seekerFovRad;
    }
}
