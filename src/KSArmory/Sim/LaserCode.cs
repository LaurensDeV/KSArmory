using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A laser's pulse code, which is what lets a weapon tell its designator's spot from another's.
/// NATO's four digits: the first 1, the second 1 to 7, the last two 1 to 8, so 1111 to 1788.
/// </summary>
public static class LaserCode
{
    public const int Default = 1688;

    public static bool IsValid(int code)
    {
        if (code < 1111 || code > 1788) return false;

        int second = code / 100 % 10;
        int third = code / 10 % 10;
        int fourth = code % 10;

        return second is >= 1 and <= 7 && third is >= 1 and <= 8 && fourth is >= 1 and <= 8;
    }
}

/// <summary>
/// Where one designator's laser is landing, for anything homing on its code. A position and the
/// velocity of what it lies on, never a bare coordinate: the spot rides the ground or the craft it
/// is on, and replayed without that velocity it is left behind at the planet's ~30 km/s.
/// </summary>
/// <param name="Designator">The craft lasing, so a weapon can tell spots apart.</param>
public readonly record struct LaserSpot(object Designator, int Code, double3 PositionEcl, double3 VelocityEcl,
                                        double RangeMetres);
