using Brutal.Numerics;

namespace AirDefence;

/// <summary>What a round is shooting at.</summary>
internal enum AimpointKind
{
    /// <summary>A whole craft. Destroying it is the outcome.</summary>
    Vehicle,

    /// <summary>A component of a craft. The craft survives unless the engine says otherwise.</summary>
    Part,

    /// <summary>A fixed position. Nothing to destroy — the round simply arrives.</summary>
    Point,
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
    double Radius)
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

    /// <summary>Whether something in the world has to still exist for this to be valid.</summary>
    public bool NeedsHandle => Kind != AimpointKind.Point;

    /// <summary>This aimpoint as the per-frame sample the flight model consumes.</summary>
    public TargetState ToTargetState() => new(PositionEcl, VelocityEcl, Radius);

    /// <summary>The same aimpoint moved to a new sample, keeping its kind and handle.</summary>
    public Aimpoint Resampled(double3 positionEcl, double3 velocityEcl)
        => this with { PositionEcl = positionEcl, VelocityEcl = velocityEcl };
}
