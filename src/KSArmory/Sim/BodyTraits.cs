namespace KSArmory;

/// <summary>The light a body's excited air gives, which is its composition's and nothing else's.</summary>
internal enum Airglow
{
    /// <summary>Nothing declared: a dim, uncoloured glow, so no body is given Earth's by default.</summary>
    Neutral,

    /// <summary>Oxygen's green and red, nitrogen's pink and purple.</summary>
    OxygenNitrogen,

    /// <summary>Carbon dioxide: faint, most of it ultraviolet.</summary>
    CarbonDioxide,

    /// <summary>Hydrogen: a pale pinkish red.</summary>
    Hydrogen,
}

/// <summary>
/// What a burst needs to know about a body that KSA does not declare: its magnetic field, what its air
/// is made of, whether water condenses in it, and whether it has a surface. Read from
/// <c>KSArmory/Bodies.xml</c> in any mod (<see cref="BodyReader"/>), keyed by the body's Id, so a custom
/// solar system gains these by adding lines rather than code.
///
/// <para>Every default is the neutral answer rather than Earth's: a body nobody described has no field,
/// so no aurora and no debris held along it, and a dim uncoloured glow.</para>
/// </summary>
/// <param name="FieldTesla">A dipole's strength at the surface equator (T); 0 for none.</param>
/// <param name="FieldTiltDeg">The dipole's tilt from the spin axis (degrees).</param>
/// <param name="FieldAzimuthDeg">Which way it is tilted, in the body's own longitude (degrees).</param>
/// <param name="Airglow">What its excited air looks like.</param>
/// <param name="Condensation">Whether a burst raises white condensation; unset follows the body's
/// weather and ocean.</param>
/// <param name="Gamma">Its air's ratio of specific heats, for the speed of sound.</param>
/// <param name="HasSurface">Whether there is ground to burst on, burn and reflect from.</param>
/// <param name="XRayOpacity">How strongly its air stops a burst's X-rays per kilogram, against
/// nitrogen and oxygen's.</param>
internal sealed record BodyTraits(
    double FieldTesla = 0.0,
    double FieldTiltDeg = 0.0,
    double FieldAzimuthDeg = 0.0,
    Airglow Airglow = Airglow.Neutral,
    bool? Condensation = null,
    double Gamma = 1.4,
    bool HasSurface = true,
    double XRayOpacity = 1.0)
{
    /// <summary>A body nobody described.</summary>
    public static readonly BodyTraits Default = new();

    /// <summary>Whether it has a field to hold a burst's debris and light an aurora.</summary>
    public bool HasField => FieldTesla > 0.0;
}
