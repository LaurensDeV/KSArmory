using Brutal.Numerics;

namespace KSArmory;

/// <summary>Whether the launcher is pointing where it is about to shoot.</summary>
public static class FireGate
{
    /// <summary>
    /// A launcher with no training gear is always laid. One whose drive the engine refused is frozen
    /// and is not: it would eject rounds along a stale tube transform, which only the facing line shows.
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
    /// Whether a burst already begun may go on. The operator's runs to its end wherever the barrels
    /// swing; an automatic one stops once the gun is off its lay or its target is gone.
    /// </summary>
    public static bool BurstMayContinue(bool operatorBurst, bool laid, bool targetGone)
        => operatorBurst || (laid && !targetGone);

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
    /// <para>Where the Pantsir's envelopes overlap the turret points at one solution, and a missile
    /// launched on the gun's lead leaves ~18° off for a 300 m/s crosser; proportional navigation hides it.
    /// The cannon win the overlap: a missile held for one pass is cheaper than one spent off-axis. With
    /// mouse aim on, the ring follows the cursor while auto-engage fires at the lock, up to 180° away.</para>
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
    /// <para>Outside its gimbal limit a seeker does not steer, so the flight path never brings the target
    /// back: a round released more than <paramref name="seekerFovRad"/> off its aimpoint flies straight
    /// on until it expires.</para>
    /// </summary>
    /// <param name="operatorHeld">
    /// The aimpoint is a place someone designated, steered onto like a command-linked round.
    /// </param>
    public static bool CanGuideOntoAimpoint(GuidanceMode guidance, bool operatorHeld,
                                            double seekerFovRad,
                                            double3 launchDirection, double3 toAimpoint)
    {
        // An anti-radiation round homes through the same gimbal as a seeker, so it is gated too.
        if (guidance is not (GuidanceMode.Seeker or GuidanceMode.AntiRadiation) || operatorHeld)
        {
            return true;
        }

        if (!Vec.IsFinite(launchDirection) || !Vec.IsFinite(toAimpoint)) return false;
        if (Vec.Len2(launchDirection) < 1e-12 || Vec.Len2(toAimpoint) < 1e-12) return false;

        return Vec.AngleBetween(toAimpoint, launchDirection) <= seekerFovRad;
    }
}
