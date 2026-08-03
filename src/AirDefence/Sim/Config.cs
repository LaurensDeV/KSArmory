namespace AirDefence;

/// <summary>
/// Every tunable in one place. Values are live-editable from the ImGui panel, so
/// these are the defaults rather than constants.
/// </summary>
public sealed class Config
{
    // ---- Launcher -------------------------------------------------------
    /// <summary>
    /// Rounds in the launcher: two pods of six, as a real Pantsir-S1 carries. The original
    /// brief asked for six, which was the count while the launcher was an abstract tube
    /// bundle. Once the vehicle grew twelve visible containers, firing half of them and
    /// leaving the rest permanently loaded read as a bug.
    ///
    /// Must match the number of entries in <c>LauncherPart.TubeOffsetsPartFrame</c>, which
    /// tools/validate-parts.py checks against the mesh.
    /// </summary>
    public const int TubeCount = 12;

    /// <summary>Seconds between consecutive rounds of a salvo.</summary>
    public float SalvoSpacing = 0.45f;

    /// <summary>Rounds committed to a single target before re-evaluating.</summary>
    public int RoundsPerTarget = 2;

    /// <summary>Speed the round leaves the rail at, relative to the platform (m/s).</summary>
    public float LaunchSpeed = 60f;

    /// <summary>Distance ahead of the platform the round is created (m).</summary>
    public float MuzzleOffset = 8f;

    /// <summary>
    /// Eject rounds along the tube they are in, rather than slewing them onto the target as
    /// they leave. Only possible because the pods aim; turn it off to compare.
    /// </summary>
    public bool LaunchAlongTube = true;

    /// <summary>
    /// How much the launch direction is biased toward local "up" rather than straight at the
    /// target. Zero fires flat along the line of sight; 1 splits the difference. A little loft
    /// keeps rounds clear of the launcher and the ground on low shots.
    ///
    /// Unused while <see cref="LaunchAlongTube"/> is on, which is the normal case — the tube's
    /// own elevation is the loft.
    /// </summary>
    public float LaunchLoft = 0.35f;

    /// <summary>Seconds of powered flight after launch.</summary>
    public float BoostSeconds = 2.2f;

    /// <summary>Axial acceleration during boost (m/s^2).</summary>
    public float BoostAccel = 260f;

    /// <summary>Round self-destructs this long after launch.</summary>
    public float MaxFlightSeconds = 22f;

    /// <summary>Seconds to reload an empty launcher. Zero disables auto-reload.</summary>
    public float ReloadSeconds = 12f;

    // ---- Turret ---------------------------------------------------------
    /// <summary>
    /// Slew the turret onto the tracked target. Turning this off parks it facing forward,
    /// which is also the fallback if the engine refuses the transform write.
    /// </summary>
    public bool TurretTracking = true;

    /// <summary>
    /// How fast the turret traverses (degrees/second). A real Pantsir manages the better part
    /// of a revolution a second; this is a little slower so the motion is legible on screen.
    /// </summary>
    public float TurretSlewRateDeg = 70f;

    /// <summary>How fast the pods elevate (degrees/second). Elevation drives are slower.</summary>
    public float TurretElevRateDeg = 45f;

    /// <summary>
    /// Seconds the launcher must be steady on the aim point before it will shoot.
    ///
    /// Guards against launching mid-slew, which put rounds out of tubes that were still
    /// pointing somewhere else. Guidance recovered and the intercept still worked, so the only
    /// symptom was that it looked wrong.
    /// </summary>
    public float TurretSettleSeconds = 0.35f;

    /// <summary>
    /// Drive the turret by hand instead of from the radar.
    ///
    /// This is the diagnostic that separates the two things that can go wrong: with a bearing
    /// you set yourself, the turret either moves or it does not, and neither the radar nor the
    /// threat model is involved in the answer.
    /// </summary>
    public bool TurretManual;

    /// <summary>Bearing the manual override holds, in degrees off the vehicle's nose.</summary>
    public float TurretManualBearingDeg;

    /// <summary>Elevation the manual override holds, in degrees above the horizon.</summary>
    public float TurretManualElevationDeg = 55f;

    /// <summary>Sweep the turret continuously. Purely for watching it work.</summary>
    public bool TurretSpin;

    /// <summary>
    /// How fast the search array turns, in revolutions per minute.
    ///
    /// It is a *search* set: it never stops and never aims, unlike the tracking array on the
    /// turret front. Real rotating search radars sit in the teens to low tens of rpm.
    /// </summary>
    public float SearchRadarRpm = 20f;

    /// <summary>Stop the search array turning. Only useful for looking at it.</summary>
    public bool SearchRadarStopped;

    public double TurretSlewRateRad => float.DegreesToRadians(TurretSlewRateDeg);
    public double TurretElevRateRad => float.DegreesToRadians(TurretElevRateDeg);

    // ---- Radar ----------------------------------------------------------
    /// <summary>Maximum detection range (m).</summary>
    public float RadarRange = 20000f;

    /// <summary>
    /// Half-angle of the search cone about the boresight (degrees). Measured off local "up",
    /// so 90 is a full hemisphere down to the horizon — the right default for a site that has
    /// to cover the whole sky. Narrow it to model a directional radar.
    /// </summary>
    public float RadarConeDeg = 90f;

    /// <summary>
    /// A track counts as a threat if its closest point of approach to the battery
    /// falls inside this radius (m). This is what makes "passing by" targets engageable
    /// rather than only head-on ones.
    ///
    /// Sized as a sensible fraction of <see cref="RadarRange"/>: a system that can see 20 km
    /// but only reacts to things passing within 2.5 km spends most of its time watching
    /// threats it has decided to ignore.
    /// </summary>
    public float ThreatRadius = 5000f;

    /// <summary>Only threats whose CPA lands within this many seconds are engaged.</summary>
    public float ThreatHorizonSeconds = 40f;

    /// <summary>Continuous seconds a track must be held before weapons release.</summary>
    public float LockSeconds = 0.8f;

    /// <summary>Tracks below this relative speed are ignored (m/s), e.g. docked craft.</summary>
    public float MinTargetSpeed = 15f;

    // ---- Guidance -------------------------------------------------------
    /// <summary>Proportional-navigation constant. 3-5 is the classic range.</summary>
    public float NavConstant = 4f;

    /// <summary>Lateral acceleration limit (g). Airframes cap out; ours does too.</summary>
    public float MaxLateralG = 30f;

    /// <summary>Seeker gimbal limit, half-angle off the round's velocity vector (degrees).</summary>
    public float SeekerFovDeg = 55f;

    /// <summary>Fraction of local gravity the autopilot compensates for.</summary>
    public float GravityCompensation = 1f;

    /// <summary>Quadratic drag coefficient, k in a = -k*|v|*v. Zero for vacuum-like flight.</summary>
    public float DragK = 4.0e-5f;

    // ---- Warhead --------------------------------------------------------
    /// <summary>Proximity fuse trigger radius (m).</summary>
    public float FuseRadius = 22f;

    /// <summary>Fuse stays safe for this long after launch, so we never kill the platform.</summary>
    public float FuseArmSeconds = 0.6f;

    /// <summary>Radius inside which a detonation is unconditionally lethal (m).</summary>
    public float LethalRadius = 30f;

    /// <summary>Radius at which blast effect falls to zero (m).</summary>
    public float BlastRadius = 90f;

    // ---- Behaviour ------------------------------------------------------
    /// <summary>Engage without asking.</summary>
    public bool AutoEngage;

    /// <summary>Master arm. Nothing launches while this is false.</summary>
    public bool Armed;

    /// <summary>Never fire on the vehicle the player is flying.</summary>
    public bool ProtectControlledVehicle = true;

    /// <summary>
    /// Periodically dump the battery's world view to the log — every loaded vehicle with the
    /// numbers the radar filters on, plus the render-frame state. Off by default; turn it on
    /// in the panel when something is not behaving and the screen is not saying why.
    /// </summary>
    public bool DiagnosticDump;

    /// <summary>Seconds between diagnostic dumps.</summary>
    public float DiagnosticIntervalSeconds = 3f;

    /// <summary>
    /// Require the launcher part to be fitted before the battery works. Turn this off to run
    /// the system on any craft, which is useful for testing without opening the editor.
    /// </summary>
    public bool RequireLauncherPart = true;

    // ---- Visuals --------------------------------------------------------
    public bool DrawRadarVolume = true;
    public bool DrawTracks = true;

    /// <summary>
    /// Draw a sphere on each contact. It scales with range so distant targets stay visible,
    /// which up close means a large ball sitting over the craft. Off by default — the line to
    /// the contact already shows where it is.
    /// </summary>
    public bool DrawTrackMarkers;
    public bool DrawMissiles = true;

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
    /// <see cref="RadarRange"/>. Drawing 20 km of converging lines is unreadable, so the cone
    /// is shown as a shape near the craft that conveys direction and angle.
    /// </summary>
    public float ConeDisplayMetres = 2500f;

    public float ConeHalfAngleRad => float.DegreesToRadians(RadarConeDeg);
    public float SeekerFovRad => float.DegreesToRadians(SeekerFovDeg);
    public double MaxLateralAccel => MaxLateralG * 9.80665;
}
