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
    /// at full lateral G. Both were seen in flight.</para>
    /// </summary>
    Ground,
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

    /// <summary>Whether the kinematics have to be re-read from the world every frame.</summary>
    public bool IsResampled => Kind != AimpointKind.Point;

    /// <summary>This aimpoint as the per-frame sample the flight model consumes.</summary>
    public TargetState ToTargetState() => new(PositionEcl, VelocityEcl, Radius);

    /// <summary>The same aimpoint moved to a new sample, keeping its kind and handle.</summary>
    public Aimpoint Resampled(double3 positionEcl, double3 velocityEcl)
        => this with { PositionEcl = positionEcl, VelocityEcl = velocityEcl };
}
