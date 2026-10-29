namespace KSArmory;

/// <summary>
/// Paintball: every hit marks what it struck with paint and harms nothing (<see cref="Config.Paintball"/>).
/// The colour is the shooter's, one hue a team so a craft's coat says who hit it.
/// </summary>
internal static class Paintball
{
    /// <summary>The hue a shooter's paint is, from 0 to 1: the same every time for the same team or craft.</summary>
    public static float HueFor(string key)
    {
        // FNV-1a, so a name gives one hue across sessions, which string.GetHashCode does not.
        uint hash = 2166136261;
        foreach (char c in key)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return (hash % 3600) / 3600f;
    }

    /// <summary>A splat's radius on the hull (m): a hand across for a 20 mm round, metres for a warhead, tens for a nuke.</summary>
    public static double SplatRadius(double chargeKg, double calibreMm)
    {
        double byCalibre = calibreMm > 0.0 ? calibreMm / 1000.0 * 5.0 : 0.0;
        double byCharge = chargeKg > 0.0 ? 0.6 * Math.Cbrt(chargeKg) : 0.0;
        return Math.Clamp(Math.Max(byCalibre, byCharge), 0.08, 30.0);
    }

    /// <summary>A bright, saturated colour of a hue, red green blue from 0 to 1.</summary>
    public static (float R, float G, float B) Colour(float hue)
    {
        float h = (hue - MathF.Floor(hue)) * 6f;
        float x = 1f - MathF.Abs((h % 2f) - 1f);
        (float r, float g, float b) = h switch
        {
            < 1f => (1f, x, 0f),
            < 2f => (x, 1f, 0f),
            < 3f => (0f, 1f, x),
            < 4f => (0f, x, 1f),
            < 5f => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };

        // Saturation 0.85: pure primaries read as a UI colour rather than as paint.
        const float s = 0.85f;
        return ((1f - s) + (s * r), (1f - s) + (s * g), (1f - s) + (s * b));
    }
}
