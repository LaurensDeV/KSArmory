namespace KSArmory;

/// <summary>
/// What picture, if any, a set puts in front of the operator.
///
/// <para>Not a flag, because an emitter list is a picture too, and a different one from a search
/// scope.</para>
/// </summary>
public enum ScopePresentation
{
    /// <summary>Nothing to show. A seeker head, a bombsight, a designation set.</summary>
    None,

    /// <summary>A search picture — what the set is sweeping and what it holds.</summary>
    Search,

    /// <summary>Who is radiating, and roughly where. A passive anti-radiation seeker's own list.</summary>
    Emitters,
}

/// <summary>Where a sensor's search cone points.</summary>
public enum BoresightMode
{
    /// <summary>
    /// Radially outward from the parent body — the sky, for anything sitting on the ground.
    ///
    /// <para>Independent of vehicle attitude, so it suits a platform that is roughly level.</para>
    /// </summary>
    LocalUp,

    /// <summary>
    /// The launcher part's own <c>+Y</c> — along the host's long axis, which is where a rail
    /// points and which way a round leaves it.
    ///
    /// <para>The one to pair with a tube, whose declared direction is <c>+Y</c> too.</para>
    /// </summary>
    PartForward,

    /// <summary>
    /// Wherever the tubes are actually pointing.
    ///
    /// <para>Pair it with a small <see cref="SensorProfile.ConeDeg"/>.</para>
    /// </summary>
    TurretAxis,

    /// <summary>
    /// The part's own <c>+X</c> — the outward normal of the face it is bolted to.
    ///
    /// <para>For an instrument looking away from its mount. Perpendicular to
    /// <see cref="PartForward"/>, so wrong for anything on a rail.</para>
    /// </summary>
    MountNormal,
}

/// <summary>
/// What a sensor can see and what it considers worth shooting at.
///
/// Separate from <see cref="MunitionProfile"/> because the two vary independently: a longer
/// -ranged set on the same launcher, or the same set feeding a different round. Separate from
/// <see cref="Config"/> because it belongs to a weapon system, not to the player's preferences.
/// </summary>
public sealed class SensorProfile
{
    /// <summary>
    /// Zero range, so a system not yet resolved sees nothing rather than the first registered set's.
    /// </summary>
    public static readonly SensorProfile None = new()
    {
        Name = "",
        DisplayName = "no sensor",
        Range = 0f,
    };

    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Maximum detection range (m).</summary>
    public float Range = 36000f;

    /// <summary>
    /// Half-angle of the search cone about the boresight (degrees). With
    /// <see cref="BoresightMode.LocalUp"/>, 90 is a hemisphere down to the horizon.
    /// </summary>
    public float ConeDeg = 90f;

    public BoresightMode BoresightSource = BoresightMode.LocalUp;

    /// <summary>
    /// None by default; only a set that genuinely searches declares one.
    ///
    /// <para>EXEMPT: what kind of instrument this is, not something an operator tunes.</para>
    /// </summary>
    public ScopePresentation Scope = ScopePresentation.None;

    /// <summary>
    /// A track counts as a threat if its closest point of approach to the system falls inside
    /// this radius (m), so a crossing track counts as well as a head-on one.
    /// </summary>
    public float ThreatRadius = 8000f;

    /// <summary>Only threats whose CPA lands within this many seconds are engaged.</summary>
    public float ThreatHorizonSeconds = 40f;

    /// <summary>Continuous seconds a track must be held before weapons release.</summary>
    public float LockSeconds = 1.5f;

    /// <summary>Tracks below this relative speed are ignored (m/s), e.g. docked craft.</summary>
    public float MinTargetSpeed = 15f;

    /// <summary>
    /// Whether the set finds its targets by transmitting. A radar does; an infrared seeker, an
    /// optical head and a bomb sight do not, and none of them can be homed on.
    ///
    /// <para>What the set <em>is</em>; whether it is transmitting now is
    /// <c>SystemConfig.RadarSilent</c>. False by default, so a profile that says nothing is not a
    /// <see cref="GuidanceMode.AntiRadiation"/> target.</para>
    /// </summary>
    public bool Emits;

    /// <summary>
    /// Holds only the contact it was handed and searches for nothing. That contact counts whether or
    /// not it is closing, because somebody chose it.
    /// </summary>
    public bool CuedOnly;

    /// <summary>
    /// Finds contacts by their heat (<see cref="InfraredSearch"/>) rather than by range and size;
    /// <see cref="Range"/> is then only a ceiling. Passive, so leave <see cref="Emits"/> false.
    ///
    /// <para>EXEMPT: what kind of instrument this is, as <see cref="Scope"/> is.</para>
    /// </summary>
    public bool Infrared;

    /// <summary>The least irradiance an <see cref="Infrared"/> set detects (W/m²).</summary>
    public float IrThresholdWPerM2 = 6.0e-7f;

    /// <summary>
    /// What the threshold is multiplied by for a contact seen against the ground rather than the sky.
    /// One is no penalty.
    /// </summary>
    public float LookDownPenalty = 1f;

    // Each discrimination field below is off at zero, so a set that says nothing tells no contacts apart.

    /// <summary>
    /// The cross-section <see cref="Range"/> is quoted against (m²). Zero means the set reaches
    /// the same distance whatever it is looking at.
    ///
    /// <para>Once set, a contact's own size scales the range by the fourth root of the ratio
    /// (<see cref="RadarSignature.DetectionRange"/>).</para>
    /// </summary>
    public float ReferenceCrossSectionM2;

    /// <summary>
    /// Returns whose line-of-sight speed is below this are rejected (m/s). Zero means no notch.
    ///
    /// <para>A trade, not an upgrade: a contact crossing exactly abeam has no radial speed and is lost.
    /// <see cref="MinTargetSpeed"/> tests the whole relative speed instead.</para>
    /// </summary>
    public float NotchSpeed;

    /// <summary>
    /// How slowly a target must be closing for chaff beside it to break this set's track, in m/s. Zero
    /// is a set chaff never breaks.
    ///
    /// <para>Its own number rather than <see cref="NotchSpeed"/>: a set can hold a beaming target on its
    /// range gate and still have that gate captured by a bigger return at the same Doppler. See
    /// <see cref="ChaffNotch"/>.</para>
    /// </summary>
    public float ChaffNotchMps;

    /// <summary>Seconds a track chaff broke takes to be found again.</summary>
    public float ChaffReacquireSeconds = 3f;

    /// <summary>
    /// Whether an optical channel holds a track the radar lost, so chaff breaks it once per pass through
    /// the notch rather than on every cartridge.
    /// </summary>
    public bool OpticalBackup;

    /// <summary>
    /// Contacts less than this above the surface are lost in ground return (m). Zero means none.
    ///
    /// <para>Against the mean sphere, not the height field: a terrain sample per contact would double
    /// what <see cref="TerrainSamples"/> costs, for a number that is a guess either way.</para>
    /// </summary>
    public float ClutterFloorMetres;

    /// <summary>
    /// Whether the planet blocks this sensor.
    ///
    /// <para>Off sees straight through the body, which is only useful for comparing the two.</para>
    /// </summary>
    public bool HorizonMasking = true;

    /// <summary>
    /// Metres of terrain to assume above the mean sphere, for masking only.
    ///
    /// <para>Costs nothing per contact and cannot produce a false negative, so it stays in front of
    /// <see cref="TerrainSamples"/> whether or not that is on. Zero is the geometric limb.</para>
    /// </summary>
    public float TerrainMarginMetres;

    /// <summary>
    /// Height-map lookups this set may spend deciding whether a ridge hides one contact.
    ///
    /// <para>Zero, the mean sphere alone, by default because the per-contact, per-scan cost has not
    /// been measured in game. The samples cover only the part of the line below the body's highest
    /// terrain, so a contact well above the ground costs nothing.</para>
    /// </summary>
    public int TerrainSamples;

    /// <summary>
    /// How far terrain must stand above the line of sight before it counts as blocking (m).
    ///
    /// <para>Both ends of the line routinely sit on the ground, so without a margin a site on a slope
    /// is blind along its own ground.</para>
    /// </summary>
    public float TerrainClearanceMetres = 30f;

    public float ConeHalfAngleRad => float.DegreesToRadians(ConeDeg);
}
