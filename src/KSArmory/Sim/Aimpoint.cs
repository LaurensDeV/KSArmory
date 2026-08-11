using Brutal.Numerics;

namespace KSArmory;

/// <summary>What a round is shooting at.</summary>
internal enum AimpointKind
{
    /// <summary>A whole craft. Destroying it is the outcome.</summary>
    Vehicle,

    /// <summary>A component of a craft. The craft survives unless the engine says otherwise.</summary>
    Part,

    /// <summary>A fixed position. Nothing to destroy — the round simply arrives.</summary>
    Point,

    /// <summary>
    /// A place on a body, which moves with it.
    ///
    /// <para>Distinct from <see cref="Point"/> and not a refinement of it. A point on the ground is
    /// only stationary in the body's own frame: in the ecliptic it carries the planet's ~29.8 km/s
    /// of orbital motion plus up to 465 m/s of spin. Held as a <see cref="Point"/> it is left
    /// behind at 180 km per six seconds of flight, and — worse — the round reads a 29.8 km/s
    /// closing velocity that is entirely the frame, so proportional navigation slams it sideways
    /// at full lateral G.</para>
    /// </summary>
    Ground,

    /// <summary>
    /// Nothing at all. An unguided round has no aimpoint to miss: where it lands was settled by
    /// where the launcher was pointing and how fast it was moving when the operator let it go.
    /// </summary>
    None,
}

/// <summary>
/// A target, independent of what kind of thing it is.
///
/// <para>Guidance needs a position, a velocity and a size; nothing about it cares whether those
/// came from a craft, one of its components, or a map coordinate. Keeping the kind separate from
/// the kinematics is what lets a ground-attack round name a structure or a coordinate.</para>
///
/// <para><see cref="Handle"/> is compared by reference and never dereferenced here. The KSA side
/// resolves it back to whatever it is.</para>
/// </summary>
internal readonly record struct Aimpoint(
    AimpointKind Kind,
    object? Handle,
    double3 PositionEcl,
    double3 VelocityEcl,
    double Radius,
    double3 Anchor = default)
{
    /// <summary>Nothing to arrive at. What a bomb is released with.</summary>
    public static readonly Aimpoint Nothing = new(AimpointKind.None, null, default, default, 0.0);

    /// <summary>
    /// Whether this has to be re-read from the world every frame.
    ///
    /// <para>A place on a body does, always. It is only still in that body's <em>own</em> frame:
    /// held as the ecliptic coordinate it was when it was named, it is left behind by ~29.8 km/s
    /// of orbital motion plus up to 465 m/s of spin. Whatever is chasing it — a round, or a sensor
    /// told to watch it — slides off within a second.</para>
    /// </summary>
    public bool NeedsResampling => Kind == AimpointKind.Ground;

    /// <summary>
    /// Whether this still names anything, given whether its handle is still alive.
    ///
    /// <para>A craft that has been destroyed takes its aimpoint with it. Ground and a bare
    /// coordinate do not — neither can die, and something told to watch a valley should find it
    /// there on looking back. That asymmetry is why a designation is an aimpoint rather than a
    /// contact: a contact-shaped one would have to be dropped the moment nothing reported it,
    /// and nothing ever reports a hillside.</para>
    /// </summary>
    public bool Survives(bool handleAlive)
        => Kind switch
        {
            AimpointKind.None => false,
            AimpointKind.Ground => true,
            AimpointKind.Point => true,
            _ => handleAlive,
        };

    /// <summary>A moving craft or component, tracked by handle.</summary>
    public static Aimpoint OnVehicle(object handle, double3 positionEcl, double3 velocityEcl, double radius)
        => new(AimpointKind.Vehicle, handle, positionEcl, velocityEcl, radius);

    /// <inheritdoc cref="OnVehicle"/>
    public static Aimpoint OnPart(object handle, double3 positionEcl, double3 velocityEcl, double radius)
        => new(AimpointKind.Part, handle, positionEcl, velocityEcl, radius);

    /// <summary>
    /// A fixed position. No handle, so it can never be "lost" — a round aimed at a coordinate
    /// keeps its aimpoint until it arrives or expires.
    /// </summary>
    public static Aimpoint AtPoint(double3 positionEcl, double radius = 0.0)
        => new(AimpointKind.Point, null, positionEcl, Vec.Zero, radius);

    /// <summary>
    /// A place on a body. <paramref name="anchor"/> is where it sits in that body's <em>own</em>
    /// frame, which is the only description of it that does not move: the KSA side turns it back
    /// into a position and a velocity every frame, so the round chases the ground rather than a
    /// coordinate the planet is leaving behind.
    /// </summary>
    public static Aimpoint OnGround(object bodyHandle, double3 anchor,
                                    double3 positionEcl, double3 velocityEcl, double radius = 0.0)
        => new(AimpointKind.Ground, bodyHandle, positionEcl, velocityEcl, radius, anchor);

    /// <summary>Whether something in the world has to still exist for this to be valid.</summary>
    public bool NeedsHandle => Kind != AimpointKind.Point;

    /// <summary>This aimpoint as the per-frame sample the flight model consumes.</summary>
    public TargetState ToTargetState() => new(PositionEcl, VelocityEcl, Radius);

    /// <summary>The same aimpoint moved to a new sample, keeping its kind and handle.</summary>
    public Aimpoint Resampled(double3 positionEcl, double3 velocityEcl)
        => this with { PositionEcl = positionEcl, VelocityEcl = velocityEcl };
}
