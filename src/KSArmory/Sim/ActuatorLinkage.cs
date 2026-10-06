using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// An actuator between a traverse and the cannon it elevates: a cylinder on a pin fixed to the
/// traverse and a rod on a pin the cannon carries round its trunnion. Both turn with the line
/// between the pins, so the rod slides out of the cylinder as the pins part.
///
/// <para>In the turret's frame, with the pins on a plane square to
/// <see cref="TubeGeometry.ElevationAxis"/>, which is +Z: a turn about it is a change of
/// <c>atan2(y, x)</c>.</para>
/// </summary>
public static class ActuatorLinkage
{
    /// <summary>Where the rod's pin is once the cannon has turned by <paramref name="gunTurnRad"/> about its trunnion.</summary>
    public static double3 RodPin(double3 trunnion, double3 rodPinAtRest, double gunTurnRad)
        => trunnion + doubleQuat.CreateFromAxisAngle(TubeGeometry.ElevationAxis, gunTurnRad)
                      * (rodPinAtRest - trunnion);

    /// <summary>How far the line between the pins has turned from its modelled pose.</summary>
    public static double TurnRad(double3 cylinderPin, double3 rodPinAtRest, double3 rodPin)
    {
        double3 rest = rodPinAtRest - cylinderPin;
        double3 now = rodPin - cylinderPin;
        return Turret.WrapPi(Math.Atan2(now.Y, now.X) - Math.Atan2(rest.Y, rest.X));
    }

    /// <summary>How far apart the pins are, which less the modelled distance is the rod's extension (m).</summary>
    public static double Length(double3 cylinderPin, double3 rodPin) => Vec.Len(rodPin - cylinderPin);
}
