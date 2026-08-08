namespace KSArmory;

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
public sealed class SystemConfig
{
    // ---- Engagement policy ----------------------------------------------

    /// <summary>
    /// Who this battery will shoot at. Defaults to engaging anything unrecognised, so a world
    /// where nobody has assigned teams behaves as it did before teams existed.
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

    // ---- Optical head ---------------------------------------------------

    /// <summary>
    /// Which of the game's open camera views this battery's optical head drives, or -1 for none.
    ///
    /// <para>An index rather than a flag because KSA opens the views itself — <c>AddViewport</c>
    /// is private, so a mod borrows one the player has opened rather than making its own.</para>
    ///
    /// <para>Per battery because the head is, but a viewport can only serve one at a time: two
    /// batteries pointed at the same index will fight over it, each rewriting the camera every
    /// frame. Whoever is drawn last wins, which looks like the view flickering between two
    /// sights rather than like a setting that needs changing.</para>
    /// </summary>
    public int OpticViewport = -1;
}
