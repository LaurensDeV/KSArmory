using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What the wind behind a blast front does to a craft: a push along the blast on every part it
/// reaches, summed into one kick and one turn about the craft's centre of mass.
///
/// <para>Per part rather than per craft, because a tall rocket hit side-on is pushed hardest where
/// it is widest and nearest, and that offset from the centre of mass is what tips it. Only the
/// wind pushes: the overpressure itself wraps round a closed body within a few milliseconds and
/// mostly cancels.</para>
/// </summary>
internal static class BlastShove
{
    // A cylinder side-on in cross-flow, which is what most of a craft is to a wind along the ground.
    public const double DragCoefficient = 1.2;

    /// <summary>
    /// What a push computed as though the body stayed put comes to once the body moves with the wind:
    /// under a drag that falls as the square of the speed still between them, a body of naive speed
    /// change <c>x·limit</c> reaches <c>limit·x/(1+x)</c>, so a small push is unchanged and none
    /// ever passes <paramref name="limit"/>, the wind's own speed.
    /// </summary>
    public static double Saturate(double naive, double limit)
    {
        if (!(naive > 0.0)) return 0.0;
        if (!(limit > 0.0)) return naive;

        double x = naive / limit;
        return limit * x / (1.0 + x);
    }

    /// <summary>The face a box presents along <paramref name="direction"/>, in square metres.</summary>
    public static double ProjectedArea(double3 halfExtents, double3 direction)
    {
        double3 d = Vec.Unit(direction);
        return 4.0 * ((Math.Abs(d.X) * halfExtents.Y * halfExtents.Z)
                      + (Math.Abs(d.Y) * halfExtents.X * halfExtents.Z)
                      + (Math.Abs(d.Z) * halfExtents.X * halfExtents.Y));
    }

    /// <summary>
    /// The impulse one part takes and the angular impulse it puts about <paramref name="centreOfMass"/>,
    /// from a wind of <paramref name="windImpulse"/> pascal-seconds (<see cref="BlastWave.WindImpulse"/>)
    /// blowing along <paramref name="direction"/>.
    /// </summary>
    public static (double3 Linear, double3 Angular) OnPart(double3 partCentre, double3 direction, double areaM2,
                                                           double windImpulse, double3 centreOfMass)
    {
        if (!(areaM2 > 0.0) || !(windImpulse > 0.0)) return (Vec.Zero, Vec.Zero);

        double3 linear = Vec.Unit(direction) * (DragCoefficient * areaM2 * windImpulse);
        return (linear, Vec.Cross(partCentre - centreOfMass, linear));
    }
}
