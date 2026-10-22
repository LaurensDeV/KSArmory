namespace KSArmory;

/// <summary>Hermite smoothstep, clamped.</summary>
public static class Ramp
{
    public static double Smooth(double x)
    {
        double t = Math.Clamp(x, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    // Edges that meet or cross are a step at edge1 rather than a division by zero.
    public static double Smooth(double edge0, double edge1, double x)
    {
        if (!(edge1 > edge0)) return x >= edge1 ? 1.0 : 0.0;

        return Smooth((x - edge0) / (edge1 - edge0));
    }
}
