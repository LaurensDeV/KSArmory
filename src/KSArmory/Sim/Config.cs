namespace KSArmory;

/// <summary>
/// Settings that belong to the session rather than to any one battery: the roster of team names,
/// what gets drawn, how much is logged.
///
/// <para>What an individual installation is allowed to do lives on <see cref="SystemConfig"/>.
/// The line between them is whether the answer can differ between two launchers in the same
/// world — arming and which side it is on can, the team names themselves cannot.</para>
///
/// <para>Not the place for how a weapon performs. Range, guidance, fuse and launcher geometry
/// live on <see cref="SensorProfile"/>, <see cref="MunitionProfile"/> and
/// <see cref="LauncherProfile"/>, because those vary per weapon system and this does not.</para>
///
/// <para>The panel edits the profiles of whichever system it is showing, by reference, so tuning
/// reaches every system running that same loadout rather than only the one on screen.</para>
/// </summary>
public sealed class Config
{

    // There is deliberately no launcher, round or sensor here. A weapon system belongs to the
    // installation running it, because two sites in one world can be different systems, and a
    // session-wide selection gives every reader whichever installation updated last.
    // Arsenal.LoadoutFor is what pairs the three.

    /// <summary>
    /// Play a rocket motor while a round is boosting.
    ///
    /// <para>Session-wide rather than per battery: it is a preference about the game's sound, and
    /// two sites in one world wanting different answers is not a case anyone has.</para>
    /// </summary>
    public bool MotorSound = true;

    /// <summary>Volume of that motor, before the engine's own distance and pressure falloff.</summary>
    public float MotorVolume = 0.7f;

    /// <summary>The cannon you can hear while they are firing.</summary>
    public bool CannonSound = true;

    /// <summary>Volume of that gun, before the engine's own distance and pressure falloff.</summary>
    public float CannonVolume = 0.8f;

    /// <summary>
    /// The rate the cannon loop was synthesised at, in rounds per minute.
    ///
    /// <para>Playback pitch is the gun's own rate over this, clamped. The CIWS is at this rate, so
    /// it plays the recording untouched; anything else is retuned toward its own cycle without
    /// being transposed so far that it stops sounding like a gun.</para>
    /// </summary>
    public float CannonReferenceRpm = 4500f;

    /// <summary>
    /// Draw a plume at the nozzle while a round burns.
    ///
    /// <para>Held-open emitters come from a shared pool, so this is the one effect that can starve
    /// the rest of the game's particles if a salvo is large enough. Switchable for that reason as
    /// much as for taste.</para>
    /// </summary>
    public bool MotorPlume = true;

    /// <summary>
    /// Show the little floating button that reopens the panel.
    ///
    /// <para>On by default and worth keeping: the menu-bar entry that replaces it works by
    /// appending to KSA's own bar, which is ImGui behaviour rather than a supported hook. If that
    /// ever stops working, a mod with no way to reopen its panel is unusable.</para>
    ///
    /// <para>Suppressed anyway while <b>ModMenu</b> is installed, since its Mods menu is a way back
    /// in that does not rest on that append. This says whether the button is wanted, not whether it
    /// is needed.</para>
    /// </summary>
    public bool FloatingPanelButton = true;

    /// <summary>
    /// Dirty the smoke of a nuclear cloud rather than leaving it white.
    ///
    /// <para>Costs something worth knowing about: the engine carries one trail colour for the whole
    /// world, so while a cloud stands every solid booster's plume is tinted with it. Held only for
    /// as long as a cloud is up.</para>
    /// </summary>
    public bool DirtyNuclearSmoke = true;

    /// <summary>Play a bang when a warhead goes off.</summary>
    public bool BurstSound = true;

    /// <summary>Its volume, scaled again by the size of the charge before it is played.</summary>
    public float BurstVolume = 0.9f;

    /// <summary>Which sound, by <c>ModLibrary</c> Id. Null borrows Core's separation charge.</summary>
    public string? BurstSoundId;

    /// <summary>
    /// Which sound to use, by <c>ModLibrary</c> Id. Null takes Core's engine loop, which resolves
    /// on every install; a mod-supplied Id only resolves once that asset actually ships.
    /// </summary>
    public string? MotorSoundId;

    // ---- Engagement policy ----------------------------------------------

    /// <summary>
    /// Hold the world's timewarp down while rounds are in the air, and give it back when they
    /// land. See <see cref="WarpPolicy"/> for why: past ~19x a round cannot be simulated, and
    /// the alternative to slowing the world is a salvo that misses by kilometres for reasons
    /// nothing on screen explains.
    ///
    /// <para>Off means the mod never touches the speed. Rounds under heavy warp then lag the
    /// world and miss — the behaviour is wrong rather than absent, which is why this defaults
    /// on.</para>
    /// </summary>
    public bool LimitWarpInFlight = true;

    /// <summary>
    /// Substring that marks a craft as belonging to a team, matched against its name.
    ///
    /// <para>KSA has no team field, so a name convention is the only assignment that needs no
    /// extra UI: a craft called "Red Hunter" is on team "Red" if that is listed here. Empty means
    /// no craft is ever classified and everything stays Unknown.</para>
    ///
    /// <para>Session-wide, unlike <see cref="SystemConfig.Iff"/>: a team name labels a craft the
    /// same way whoever is looking at it, and it is which side each battery takes that differs.</para>
    /// </summary>
    public readonly List<string> TeamNames = [];

    /// <summary>
    /// Click the world to set off a warhead there.
    ///
    /// <para>A development tool for looking at the effect without flying an engagement to get one.
    /// Off by default: while it is on, a click on the world is an explosion.</para>
    /// </summary>
    public bool BurstTool;

    /// <summary>Fireball rather than the paler airburst.</summary>
    public bool BurstFireball = true;

    /// <summary>
    /// Explosive charge for a hand-fired burst (kg). The same figure a round carries, so the tool
    /// shows what a warhead of that size actually looks like rather than an arbitrary size.
    /// </summary>
    public float BurstChargeKg = 20f;

    /// <summary>
    /// Make that burst a nuclear one, dialled in kilotons rather than kilograms.
    ///
    /// <para>The same code path either way: a nuclear charge is a very large one, and the cloud
    /// grows itself for anything over <see cref="MushroomCloud.ThresholdKg"/>. What the tick box
    /// changes is which unit the dial is in, because a slider that has to cover 10 g of shell
    /// filling and 340 kt on one scale is useful for neither.</para>
    /// </summary>
    public bool BurstNuclear;

    /// <summary>
    /// Yield for that (kt), spanning the B61's own dial.
    ///
    /// <para>Kilotons rather than kilograms because that is the unit the thing is specified in, and
    /// the conversion is exact: a kilotonne of TNT equivalent is a million kilograms of it.</para>
    /// </summary>
    public float BurstYieldKt = 0.3f;

    /// <summary>
    /// Pick a craft up with one click and set it down with the next.
    ///
    /// <para>A development tool for laying out a test range, and off by default because while it
    /// is on a click on the world moves a vehicle instead of doing whatever it usually does.</para>
    /// </summary>
    public bool MoveCraftWithMouse;


    // ---- Diagnostics ----------------------------------------------------

    /// <summary>
    /// Periodically dump the battery's world view to the log — every loaded vehicle with the
    /// numbers the radar filters on, plus the render-frame state. Off by default; turn it on
    /// in the panel when something is not behaving and the screen is not saying why.
    /// </summary>
    public bool DiagnosticDump;

    /// <summary>Seconds between diagnostic dumps.</summary>
    public float DiagnosticIntervalSeconds = 3f;

    /// <summary>
    /// Log developer detail — spawn maths, per-vehicle dumps, geometry read-backs.
    ///
    /// A release build starts quiet, because that detail runs to hundreds of lines per
    /// engagement and buries the handful of lines that say what the battery actually did.
    /// This turns it back on without needing a different build, which is what a bug report needs.
    /// </summary>
    public bool VerboseLog;

    // ---- Visuals --------------------------------------------------------

    /// <summary>
    /// Draw the world overlay at all — search volume, tracks, round tracers, drive facing.
    ///
    /// <para>Off by default. It is diagnostic drawing: it answers questions about what the mod
    /// thinks, not about what is happening, and a search cone and a facing line around every
    /// crewed system is a lot of geometry to look past. Rounds still have real bodies with this
    /// off — only the tracers go.</para>
    ///
    /// <para>One switch above all the others because there are as many overlays as there are
    /// crewed systems, and four of everything around four craft is not four times as useful.</para>
    /// </summary>
    public bool DrawOverlays;

    /// <summary>
    /// Draw the overlay only for the system the panel is showing, rather than for every one.
    ///
    /// <para>On by default for the same reason. Turn it off to compare two sites at once, which
    /// is the case it exists for.</para>
    /// </summary>
    public bool DrawOverlayForFocusedOnly = true;

    public bool DrawRadarVolume = true;
    public bool DrawTracks = true;
    public bool DrawMissiles = true;

    /// <summary>
    /// Draw a line along where the drives think they are pointing.
    ///
    /// <para>Its own switch rather than riding on the radar volume: it is what separates "the
    /// maths is wrong" from "the engine ignored the write", so it is the one line worth keeping
    /// when everything else is off.</para>
    /// </summary>
    public bool DrawTurretFacing = true;

    /// <summary>
    /// Diagnostic: hold the chase camera still through its transition instead of flying it onto
    /// the round. It still takes the view and still aims, it simply does not travel.
    ///
    /// <para>This is a discriminator, not a setting anyone wants on. The transition jitters on an
    /// airless body and the camera's measured altitude over the ground alternates by ±145 m a
    /// frame while the offset the mod asks for is smooth — so either the camera's travel is
    /// provoking it, or the whole scene is juddering against a camera that rides a
    /// mod-simulated round stepped on a different clock from the world. Freezing the travel
    /// separates the two in one flight.</para>
    /// </summary>
    public bool FreezeChaseTransition;

    /// <summary>
    /// Bracket every weapons system on screen, with an arrow at the edge for one out of view.
    ///
    /// <para>Session-wide rather than per battery: it draws every system in the world, including
    /// the ones no battery is running on.</para>
    /// </summary>
    public bool DrawSystemMarkers = true;

    /// <summary>
    /// Bracket what the selected weapon is engaging, closing the brackets as the lock matures.
    ///
    /// <para>On by default, and deliberately not part of <see cref="DrawOverlays"/>: that is the
    /// diagnostic gizmo layer, and whether you have a lock is playing rather than debugging. It
    /// is also the one piece of fire-control state that has to be readable without looking away
    /// from the target.</para>
    ///
    /// <para>Session-wide, because it is a property of the screen rather than of an installation:
    /// it draws for whichever weapon the trigger is pointed at, and there is only one of those.</para>
    /// </summary>
    public bool DrawLockCue = true;

    /// <summary>
    /// Show a fireball where a warhead goes off.
    ///
    /// <para>Not part of <see cref="DrawOverlays"/>: that is diagnostic drawing that says what the
    /// mod thinks, and this is the engagement itself. Turning the debug lines off should leave the
    /// explosions.</para>
    /// </summary>
    public bool DrawExplosions = true;

    /// <summary>
    /// Draw a sphere on each contact. It scales with range so distant targets stay visible,
    /// which up close means a large ball sitting over the craft. Off by default — the line to
    /// the contact already shows where it is.
    /// </summary>
    public bool DrawTrackMarkers;

    /// <summary>
    /// Draw a tracer sphere on each round in flight, on top of the round's own body.
    ///
    /// Off by default: rounds are real geometry and the sphere is bigger than the missile, so it
    /// hides what it marks. Turn it on to follow a round at a distance where a 3 m body is a
    /// couple of pixels, or to see where the *simulation* thinks a round is when the body looks
    /// wrong.
    /// </summary>
    public bool DrawRoundMarkers;

    /// <summary>
    /// Draw a marker on each tube, green for loaded and grey for spent.
    ///
    /// <para>Off by default: with rounds sitting visibly in their tubes it is redundant.</para>
    /// </summary>
    public bool DrawTubeMarkers;

    /// <summary>
    /// Place a real subpart body on each round in flight.
    ///
    /// <para>On by default. Turning it off falls back to tracer spheres, which take a completely
    /// separate path — subpart transform in the vehicle's frame versus gizmo anchor in Ecl — so
    /// misbehaviour with bodies on and clean flight with them off isolates the fault to the
    /// transform path rather than the simulation.</para>
    /// </summary>
    public bool UseRoundBodies = true;

    /// <summary>
    /// Draw a marker at each threat's predicted closest point of approach — where it will pass
    /// the battery if it holds course. Off by default: with a 40 s horizon the marker can sit
    /// kilometres from anything visible, which reads as a stray dot rather than a prediction.
    /// </summary>
    public bool DrawClosestApproach;

    /// <summary>
    /// How long the drawn search cone is (m). Purely cosmetic — the real detection range is
    /// <see cref="SensorProfile.Range"/>. Drawing 20 km of converging lines is unreadable, so
    /// the cone is shown as a shape near the craft that conveys direction and angle.
    /// </summary>
    public float ConeDisplayMetres = 2500f;
}
