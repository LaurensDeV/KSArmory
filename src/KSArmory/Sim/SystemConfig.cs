namespace KSArmory;

/// <summary>
/// What a sensor needs from whoever owns it: whose side a contact is on, and whether the craft
/// being flown is off limits.
///
/// <para>An interface because a <c>Radar</c> is driven by a weapons system and by an optical
/// director, and neither should inherit the other's settings to get one. It is also the whole of
/// what a sensor asks about policy — everything else it reads is on its own profile.</para>
/// </summary>
public interface ISensorPolicy
{
    IffPolicy Iff { get; }

    bool ProtectControlledVehicle { get; }

    /// <summary>
    /// The set has been told to stop transmitting, so it sees nothing. A passive sensor — an
    /// infrared seeker, an optical head — is unaffected and answers false, because it was never
    /// transmitting to stop.
    /// </summary>
    bool RadarSilent => false;
}

/// <summary>
/// One battery's own settings — what <em>this</em> installation is allowed to do.
///
/// <para>Split from <see cref="Config"/> because these are the only settings that stop making
/// sense when there is more than one battery in the world. Arming a site, telling it to engage
/// on its own, which side it is on, or driving its turret by hand are decisions about that site;
/// the roster of team names and what gets drawn are decisions about the session, and stay
/// shared.</para>
///
/// <para>Nothing here says how a weapon *performs*. Range, guidance, fuse and launcher geometry
/// belong to <see cref="SensorProfile"/>, <see cref="MunitionProfile"/> and
/// <see cref="LauncherProfile"/>, which vary per weapon system rather than per installation —
/// two Pantsirs on opposite sides of the map share a flight model and disagree about whether
/// they are armed.</para>
/// </summary>
public sealed class SystemConfig : ISensorPolicy
{
    // ---- Engagement policy ----------------------------------------------

    /// <summary>
    /// Who this battery will shoot at. Defaults to engaging anything unrecognised, so a world with
    /// no teams assigned engages everything.
    ///
    /// <para>Per battery, because two sites in one world are exactly what taking opposite sides
    /// means. The team <em>names</em> stay on <see cref="Config.TeamNames"/>.</para>
    /// </summary>
    public IffPolicy Iff { get; } = new();

    /// <summary>
    /// Never fire on the vehicle the player is flying.
    ///
    /// <para>Per battery: two sites can sensibly disagree about it, which is the test. Flying into
    /// one range as a target while another site guards you is the case, and a single switch makes
    /// that impossible.</para>
    /// </summary>
    public bool ProtectControlledVehicle = true;

    // Explicit, so the field above can keep the name. A tick box binds to it by reference, which
    // a property cannot offer.
    bool ISensorPolicy.ProtectControlledVehicle => ProtectControlledVehicle;

    /// <summary>
    /// Draw the bomb sight: where a store released now would land, and the arc it would take.
    ///
    /// <para>Per system rather than session-wide, because two aircraft in one world can sensibly
    /// disagree about wanting one — and it costs a few hundred integration steps to solve.</para>
    /// </summary>
    public bool DrawBombSight = true;

    /// <summary>Master arm. Nothing launches while this is false.</summary>
    public bool Armed;

    /// <summary>Engage without asking.</summary>
    public bool AutoEngage;

    /// <summary>
    /// Ride the main view behind this system's rounds.
    ///
    /// <para>Per battery rather than per session: with several sites alive, whose missiles are
    /// worth watching is exactly the sort of thing two of them disagree about. There is one main
    /// view, and the frame hook offers it only to the system the panel is showing, so setting this
    /// on any other does nothing until that system is focused.</para>
    /// </summary>
    public bool ChaseRounds;

    /// <summary>
    /// Which weapons may engage, independently of the master arm.
    ///
    /// <para>Two layers on one mount: without a switch each, whichever reaches further takes
    /// every target and the other can never be seen to work.</para>
    /// </summary>
    public bool MissilesEnabled = true;

    /// <inheritdoc cref="MissilesEnabled"/>
    public bool GunsEnabled = true;

    /// <summary>Rounds committed to a single target before re-evaluating.</summary>
    public int RoundsPerTarget = 2;

    /// <summary>
    /// Point the launcher wherever the mouse is, instead of at what the radar is holding.
    ///
    /// <para>The drives are rate-limited either way, so this aims *towards* the cursor rather
    /// than snapping to it. Auto-engage still decides when to shoot; this only decides where the
    /// launcher looks.</para>
    /// </summary>
    public bool MouseAim;

    /// <summary>
    /// Click the world to shoot at that spot, with no craft and no track involved.
    ///
    /// <para>The operator naming a place rather than the radar naming a target. It is the only way
    /// to engage what the sensor will not hand you — terrain, or anything the threat model rejects
    /// for being too slow to count. Master arm still applies; a designation is an order to shoot,
    /// not permission to.</para>
    ///
    /// <para>Deliberately not persisted, unlike <see cref="MouseAim"/>. Restoring a tool that only
    /// <em>points</em> costs nothing; restoring one that fires means the first click after loading
    /// a save launches a round at whatever the player happened to be looking at.</para>
    /// </summary>
    public bool MouseFire;

    // ---- Turret ---------------------------------------------------------

    /// <summary>
    /// Slew the launcher onto the tracked target. Turning this off parks it facing forward,
    /// which is also the fallback if the engine refuses the transform write.
    /// </summary>
    public bool TurretTracking = true;

    /// <summary>Drive the launcher by hand instead of from the radar.</summary>
    public bool TurretManual;
    public float TurretManualBearingDeg;
    public float TurretManualElevationDeg = 55f;

    /// <summary>Sweep the turret continuously. Purely for watching it work.</summary>
    public bool TurretSpin;

    /// <summary>Stop the search array turning. Only useful for looking at it.</summary>
    public bool SearchRadarStopped;

    /// <summary>
    /// Stop transmitting. A set that is silent cannot be homed on by an anti-radiation round — and
    /// cannot see anything either, which is the whole of the trade.
    ///
    /// <para>It only helps a site that then <em>moves</em>: a round already in the air carries on
    /// to where the emission last came from. Going quiet buys the time to leave, not immunity.</para>
    ///
    /// <para>Per installation rather than session-wide, because two sites on opposite sides of a
    /// map disagreeing about this is exactly the case — one shuts down while the other keeps
    /// painting.</para>
    /// </summary>
    public bool RadarSilent;

    /// <summary>
    /// How far the scope's rim is (m) — the range setting, not the set's reach.
    ///
    /// <para>Deliberately independent of <see cref="SensorProfile.Range"/>: an operator winds a
    /// scope in to read a crowded sector and back out to see what is coming, and neither says
    /// anything about how far the set can actually detect. Contacts past the rim are held on it
    /// rather than dropped.</para>
    /// </summary>
    public float ScopeRangeMetres = 20_000f;

    // ---- Optical head ---------------------------------------------------
    //
    // Nothing. A head is a part in its own right now, with its own OpticConfig: it is crewed per
    // director rather than per weapons system, it finds its own targets, and a craft can carry one
    // with no armament at all. Keeping a launcher's copy of these would be a second place to set
    // the same thing, and the one that did nothing.
}
