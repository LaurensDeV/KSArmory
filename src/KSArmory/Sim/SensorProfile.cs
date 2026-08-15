namespace KSArmory;

/// <summary>Where a sensor's search cone points.</summary>
public enum BoresightMode
{
    /// <summary>
    /// Radially outward from the parent body — the sky, for anything sitting on the ground.
    ///
    /// <para>Independent of vehicle attitude, so a truck on a slope still searches the sky rather
    /// than the hillside — and wrong the moment the platform is not level-ish, since on a
    /// pitched-over booster or in orbit "up" is not where the threats are.</para>
    /// </summary>
    LocalUp,

    /// <summary>
    /// The launcher part's own <c>+X</c>, which is its mounting "up".
    ///
    /// <para>Follows the platform's attitude, so a launcher on a rocket keeps searching the volume
    /// it is mounted to face however the craft is oriented.</para>
    /// </summary>
    PartForward,

    /// <summary>
    /// Wherever the tubes are actually pointing.
    ///
    /// <para>A set slaved to the launcher rather than searching independently. Pair it with a
    /// small <see cref="SensorProfile.ConeDeg"/>: a hemisphere that follows the turret is
    /// <see cref="LocalUp"/> with extra steps.</para>
    /// </summary>
    TurretAxis,
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
    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Maximum detection range (m).</summary>
    public float Range = 36000f;

    /// <summary>
    /// Half-angle of the search cone about the boresight (degrees). With the default
    /// <see cref="BoresightMode.LocalUp"/>, 90 is a full hemisphere down to the horizon — the right
    /// default for a site that has to cover the whole sky. Narrow it to model a directional radar.
    /// </summary>
    public float ConeDeg = 90f;

    /// <summary>
    /// Where the cone points. Defaults to local "up", which is what a ground site wants.
    /// </summary>
    public BoresightMode BoresightSource = BoresightMode.LocalUp;

    /// <summary>
    /// A track counts as a threat if its closest point of approach to the battery falls inside
    /// this radius (m). This is what makes "passing by" targets engageable rather than only
    /// head-on ones.
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
    /// <para>This is what the set <em>is</em>, not what it is doing — whether it is transmitting
    /// right now is <c>SystemConfig.RadarSilent</c>, which is the operator's switch and the only
    /// defence against <see cref="GuidanceMode.AntiRadiation"/> that does not involve moving.</para>
    ///
    /// <para>Defaults to false, so a profile that says nothing about emission is invisible to an
    /// anti-radiation round rather than accidentally becoming a target for one.</para>
    /// </summary>
    public bool Emits;

    // ---- What the set can tell targets apart by ------------------------------
    //
    // Each of the three is off at zero, and zero is the default: with all three at zero the set
    // behaves exactly as it did before any of them existed. They are the substrate for chaff and
    // decoys, which need detection to depend on what a target *is* before either can mean
    // anything -- see docs/AUDIT-2026-08.md.

    /// <summary>
    /// The cross-section <see cref="Range"/> is quoted against (m²). Zero means the set reaches
    /// the same distance whatever it is looking at.
    ///
    /// <para>Once set, a contact's own size scales the range by the **fourth** root of the ratio,
    /// which is <see cref="RadarSignature.DetectionRange"/>. A missile is then seen at a fraction
    /// of the range its launching aircraft is, and a set can be given a reference that makes it
    /// good against aircraft and poor against rounds — which is what separates a search radar from
    /// a fire-control one.</para>
    /// </summary>
    public float ReferenceCrossSectionM2;

    /// <summary>
    /// Returns whose line-of-sight speed is below this are rejected (m/s). Zero means no notch.
    ///
    /// <para>A pulse-Doppler set rejects what is not moving towards or away from it, because that
    /// is what ground clutter does. The cost is real and is the point: a target crossing exactly
    /// abeam has no radial motion and is lost, which is the one geometry
    /// <see cref="ThreatRadius"/> exists to keep engageable. A set with a notch and a set without
    /// are a genuine choice rather than an upgrade.</para>
    ///
    /// <para>Not the same rule as <see cref="MinTargetSpeed"/>, which tests the whole relative
    /// speed and exists to ignore things drifting alongside.</para>
    /// </summary>
    public float NotchSpeed;

    /// <summary>
    /// Contacts less than this above the surface are lost in ground return (m). Zero means none.
    ///
    /// <para>Against the mean sphere, not the height field: a clutter floor is a soft number, and
    /// spending a terrain sample per contact on it would double what
    /// <see cref="TerrainSamples"/> costs to sharpen a figure that is a guess either way.</para>
    ///
    /// <para>A floor of any size makes a short-range air-defence set useless at the job it exists
    /// for, which is why it defaults to none: the Pantsir is built to kill things down in the
    /// clutter.</para>
    /// </summary>
    public float ClutterFloorMetres;

    /// <summary>
    /// Whether the planet blocks this sensor.
    ///
    /// <para>On, because a radar that sees through a world engages things it could never detect,
    /// and every range figure below is meaningless against a target on the far side. Off lets it
    /// see straight through the body, which is only useful for comparing the two.</para>
    /// </summary>
    public bool HorizonMasking = true;

    /// <summary>
    /// Metres of terrain to assume above the mean sphere, for masking only.
    ///
    /// <para>Inflating the sphere is the cheap approximation to a skyline: it costs nothing per
    /// contact and cannot produce a false negative, so it stays in front of
    /// <see cref="TerrainSamples"/> whether or not that is on. Zero is the geometric limb.</para>
    /// </summary>
    public float TerrainMarginMetres;

    /// <summary>
    /// Height-map lookups this set may spend deciding whether a ridge hides one contact.
    ///
    /// <para>Zero is the mean sphere alone, and is the default because <em>the cost has not been
    /// measured in game</em>. Each lookup is a texture fetch with a block decode, and this runs
    /// once per contact per scan — so the honest way to raise it is to raise it and watch the
    /// frame time, which is why it is a number and not a switch.</para>
    ///
    /// <para>The samples are spread across the part of the line that passes below the body's
    /// highest terrain, not across its whole length, so a contact well above the ground costs
    /// nothing whatever this says.</para>
    /// </summary>
    public int TerrainSamples;

    /// <summary>
    /// How far terrain must stand above the line of sight before it counts as blocking (m).
    ///
    /// <para>Both ends of that line routinely sit on the ground, and a coarse height map read a few
    /// hundred metres from a launcher that is standing on a hill will find the hill. Without a
    /// margin a site on any slope is blind along its own ground.</para>
    /// </summary>
    public float TerrainClearanceMetres = 30f;

    public float ConeHalfAngleRad => float.DegreesToRadians(ConeDeg);
}
