namespace AirDefence;

/// <summary>
/// The player's settings: what the battery is allowed to do, and what gets drawn.
///
/// <para>Deliberately <em>not</em> the place for how a weapon performs. Range, guidance, fuse
/// and launcher geometry live on <see cref="SensorProfile"/>, <see cref="MunitionProfile"/> and
/// <see cref="LauncherProfile"/>, because those vary per weapon system and this does not. A
/// second launcher on a second craft has its own profiles and shares this.</para>
///
/// <para><see cref="Active"/> points at whichever system the panel is currently tuning, so the
/// sliders keep editing live values by reference.</para>
/// </summary>
public sealed class Config
{
    /// <summary>
    /// The weapon system the panel is showing. Set by the battery when it resolves its
    /// launcher; the profiles are shared instances, so edits apply to every launcher of that
    /// type, which is what you want when tuning.
    /// </summary>
    public LauncherProfile Launcher = Arsenal.PantsirS1;
    public MunitionProfile Munition = Arsenal.Missile57E6;
    public SensorProfile Sensor = Arsenal.SearchRadar1Rs1;

    /// <summary>Points every profile at the system this battery actually has fitted.</summary>
    public void Select(LauncherProfile launcher)
    {
        Launcher = launcher;
        Munition = Arsenal.MunitionNamed(launcher.Munition);
        Sensor = Arsenal.SensorNamed(launcher.Sensor);
    }

    // ---- Engagement policy ----------------------------------------------

    /// <summary>Engage without asking.</summary>
    public bool AutoEngage;

    /// <summary>Master arm. Nothing launches while this is false.</summary>
    public bool Armed;

    /// <summary>Never fire on the vehicle the player is flying.</summary>
    public bool ProtectControlledVehicle = true;

    /// <summary>Rounds committed to a single target before re-evaluating.</summary>
    public int RoundsPerTarget = 2;

    /// <summary>
    /// Require a launcher part before the battery works. Turn this off to run the system on any
    /// craft, which is useful for testing without opening the editor.
    /// </summary>
    public bool RequireLauncherPart = true;

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
    /// This turns it back on without needing a different build, which is what you want from
    /// someone reporting a bug.
    /// </summary>
    public bool VerboseLog;

    // ---- Visuals --------------------------------------------------------

    public bool DrawRadarVolume = true;
    public bool DrawTracks = true;
    public bool DrawMissiles = true;

    /// <summary>
    /// Draw a sphere on each contact. It scales with range so distant targets stay visible,
    /// which up close means a large ball sitting over the craft. Off by default — the line to
    /// the contact already shows where it is.
    /// </summary>
    public bool DrawTrackMarkers;

    /// <summary>
    /// Draw a tracer sphere on each round in flight, on top of the round's own body.
    ///
    /// Off by default now that rounds are real geometry: the sphere is bigger than the missile
    /// and hides it completely. Turn it on to follow a round at a distance where a 3 m body is
    /// a couple of pixels, or to see where the *simulation* thinks a round is when the body
    /// looks wrong.
    /// </summary>
    public bool DrawRoundMarkers;

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
