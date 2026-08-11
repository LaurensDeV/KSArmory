using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A head that points rather than trains: two degrees of freedom, no axes of its own, and a
/// limited turn rate.
///
/// <para><see cref="Turret"/> cannot do this job. It drives two named angles about two fixed
/// axes, which is what a traverse ring and a trunnion are; an optical head on a gimbal has
/// neither, and expressing its aim as bearing-and-elevation reintroduces a singularity looking
/// straight up — exactly where an air-defence sight spends its time.</para>
///
/// <para>The rate limit is the whole point. Writing the commanded direction straight to the part
/// makes the head snap onto a track the instant the radar has one, which reads as a bug rather
/// than as a sight.</para>
/// </summary>
public sealed class PointingDrive
{
    /// <summary>Where the head is actually looking, in the part's frame.</summary>
    public double3 Direction { get; private set; } = OpticGeometry.RestDirection;

    /// <summary>Angle still to cover (rad). Zero once settled on the command.</summary>
    public double ErrorRad { get; private set; }

    /// <summary>True once the head is within a degree of where it was told to look.</summary>
    public bool OnTarget => ErrorRad < 0.0175;

    /// <summary>
    /// Puts the head at a direction outright, for a caller enforcing a limit the <em>path</em>
    /// has to respect.
    ///
    /// <para>Clamping the command is not enough. This turns by the shortest rotation onto it, and
    /// the shortest way from one bearing to the opposite one at low elevation goes over the top or
    /// under the bottom — so a head whose ends are both legal sweeps through its own mount getting
    /// between them. Re-clamping what it actually reached each step is what keeps it out.</para>
    /// </summary>
    public void Hold(double3 direction)
    {
        double3 held = Vec.Unit(direction);
        if (!Vec.IsFinite(held) || Vec.Len2(held) < 0.5) return;

        Direction = held;
    }

    public void Reset()
    {
        Direction = OpticGeometry.RestDirection;
        ErrorRad = 0.0;
    }

    /// <summary>Turns toward <paramref name="commandPartFrame"/>, by at most the rate allows.</summary>
    public void Update(double dt, double3 commandPartFrame, double rateRadPerSec)
    {
        double3 command = Vec.Unit(commandPartFrame);
        if (!Vec.IsFinite(command) || command.Equals(Vec.Zero)) return;
        if (!(dt > 0.0) || !double.IsFinite(dt)) return;

        double3 current = Vec.Unit(Direction);
        if (!Vec.IsFinite(current) || current.Equals(Vec.Zero)) current = OpticGeometry.RestDirection;

        double dot = Math.Clamp(Vec.Dot(current, command), -1.0, 1.0);
        ErrorRad = Math.Acos(dot);

        double step = Math.Max(0.0, rateRadPerSec) * dt;
        if (ErrorRad <= step || step <= 0.0)
        {
            Direction = ErrorRad <= step ? command : current;
            if (ErrorRad <= step) ErrorRad = 0.0;
            return;
        }

        // Turn about the axis joining the two directions. Antiparallel has no such axis and any
        // perpendicular is equally right, which is a half turn rather than a NaN.
        double3 axis = Vec.Cross(current, command);
        axis = Vec.Len2(axis) < 1e-18 ? Vec.AnyPerpendicular(current) : Vec.Unit(axis);

        Direction = Vec.Unit(doubleQuat.CreateFromAxisAngle(axis, step) * current);
        ErrorRad -= step;
    }
}
