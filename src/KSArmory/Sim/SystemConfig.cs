namespace KSArmory;

/// <summary>
/// What a sensor needs from whoever owns it: whose side a contact is on, and whether the craft
/// being flown is off limits.
///
/// <para>An interface because a <c>Radar</c> is driven by a weapons system and by an optical
/// director, and neither should inherit the other's settings to get one.</para>
/// </summary>
public interface ISensorPolicy
{
    IffPolicy Iff { get; }

    bool ProtectControlledVehicle { get; }

    /// <summary>
    /// The set has been told to stop transmitting, so it sees nothing. A passive sensor answers false.
    /// </summary>
    bool RadarSilent => false;
}

/// <summary>
/// One system's own settings — what <em>this</em> installation is allowed to do.
///
/// <para>Split from <see cref="Config"/> because two sites in one world can sensibly disagree about
/// these. How a weapon performs is on its profiles, not here.</para>
/// </summary>
public sealed class SystemConfig : ISensorPolicy
{
    /// <summary>
    /// Defaults to engaging anything unrecognised, so a world with no teams assigned engages
    /// everything. The team names stay on <see cref="Config.TeamNames"/>.
    /// </summary>
    public IffPolicy Iff { get; } = new();

    /// <summary>
    /// Never fire on the vehicle the player is flying. Per system, so one site can guard the player
    /// while another treats them as a target. About aiming only: a burst or a stray round near that
    /// vehicle still damages it.
    /// </summary>
    public bool ProtectControlledVehicle = true;

    // A field because a tick box binds to it by reference, which a property cannot offer.
    bool ISensorPolicy.ProtectControlledVehicle => ProtectControlledVehicle;

    /// <summary>
    /// Draw the bomb sight: where a store released now would land, and the arc it would take.
    ///
    /// <para>Costs up to <see cref="BombSight.MaxSteps"/> integration steps a solve.</para>
    /// </summary>
    public bool DrawBombSight = true;

    /// <summary>
    /// Engage on its own: pick what its sensors and IFF allow and fire at it. Off, the weapon fires
    /// only when told.
    ///
    /// <para>Off by default, and there is no master arm: FIRE is pressed on purpose, so a safety in
    /// front of it would prevent no accident.</para>
    /// </summary>
    public bool AutoEngage;

    /// <summary>
    /// Ride the main view behind this system's rounds.
    ///
    /// <para>The frame hook offers the one main view only to the system the panel is showing, so on
    /// any other this does nothing until it is focused.</para>
    /// </summary>
    public bool ChaseRounds;

    /// <summary>
    /// Which weapons may engage, independently of auto-engage.
    ///
    /// <para>Two layers on one mount: without a switch each, whichever reaches further takes
    /// every target and the other can never be seen to work.</para>
    /// </summary>
    public bool MissilesEnabled = true;

    /// <inheritdoc cref="MissilesEnabled"/>
    public bool GunsEnabled = true;

    /// <inheritdoc cref="MissilesEnabled"/>
    public bool BeamEnabled = true;

    /// <summary>Rounds committed to a single target before re-evaluating.</summary>
    public int RoundsPerTarget = 2;

    /// <summary>
    /// Point the launcher wherever the mouse is, instead of at what the radar is holding.
    ///
    /// <para>Auto-engage still decides when to shoot.</para>
    /// </summary>
    public bool MouseAim;

    /// <summary>
    /// Click the world to shoot at that spot, with no craft and no track involved.
    ///
    /// <para>Not persisted, unlike <see cref="MouseAim"/>: restored, the first click after loading a
    /// save would fire at whatever the player was looking at.</para>
    /// </summary>
    public bool MouseFire;

    /// <summary>
    /// Slew the launcher onto the tracked target. Turning this off parks it facing forward,
    /// which is also the fallback if the engine refuses the transform write.
    /// </summary>
    public bool TurretTracking = true;

    public bool TurretManual;
    public float TurretManualBearingDeg;
    public float TurretManualElevationDeg = 55f;

    /// <summary>Sweep the turret continuously. Purely for watching it work.</summary>
    public bool TurretSpin;

    /// <summary>Stop the search array turning. Only useful for looking at it.</summary>
    public bool SearchRadarStopped;

    /// <summary>
    /// Stop transmitting: not homed on, and blind. A round already in the air carries on to where the
    /// emission last came from, so it only helps a site that then moves.
    /// </summary>
    public bool RadarSilent;

    /// <summary>
    /// Whether this installation's scope is also open in a window of its own, beside the Radar tab.
    /// </summary>
    public bool ScopeOpen;

    /// <summary>
    /// How far the scope's rim is (m) — one of <see cref="ScopeGeometry.RangeSteps"/> off the set's
    /// reach, and zero for the whole of it. A value off the steps is read as the nearest.
    /// </summary>
    public float ScopeRangeMetres;
}
