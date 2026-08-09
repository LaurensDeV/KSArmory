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
