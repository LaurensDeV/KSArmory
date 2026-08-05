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
    /// Whether a missile may leave the tube. False while the cannon own the engagement, because
    /// the ring is then laid on the gun's ballistic lead rather than on the target.
    ///
    /// <para>The envelopes overlap — 200–4000 m for the cannon against 1200–20000 m for the
    /// missiles — and inside that band the turret can only point at one solution. A missile
    /// launched along the tube in that state leaves ~18° off for a 300 m/s crosser. Proportional
    /// navigation recovers, which is exactly why nothing measured it: the intercepts still
    /// landed, out of a launcher aimed somewhere else.</para>
    ///
    /// <para>The cannon win the overlap rather than the missiles because the lead is only applied
    /// when the guns can actually take the shot, and a missile held for one pass is cheaper than
    /// a missile spent off-axis.</para>
    /// </summary>
    /// <param name="ringIsOnGunLead">
    /// The turret is actually laid on the ballistic lead — the cannon own the engagement *and*
    /// the lead solved. A solve that fails leaves the ring on the target, which the missiles can
    /// use, so this is not the same question as <see cref="GunsHaveTheEngagement"/>.
    /// </param>
    /// <param name="launchAlongTube">Rounds leave along the tube, so the ring's aim is theirs too.</param>
    public static bool MissilesMayFire(bool ringIsOnGunLead, bool launchAlongTube)
        => !(ringIsOnGunLead && launchAlongTube);
}
