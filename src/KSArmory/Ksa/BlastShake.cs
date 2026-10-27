using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The view shaken as a blast front passes the camera, as hard as the overpressure there. The model
/// only: <see cref="CloudPass"/> moves the picture by <see cref="Offset"/>.
/// <para>On the simulated step. Fronts passing close together add, held to the most one front can throw.</para>
/// </summary>
internal static class BlastShake
{
    // The rattles still running: how hard, how long since the front passed, how long each lasts.
    private static readonly List<(double Strength, double Since, double Seconds, int Seed)> _shakes = [];

    /// <summary>
    /// The picture's offset now: across and up in shares of the screen's height, and a roll in
    /// radians. Zero when nothing is shaking.
    /// </summary>
    public static (double X, double Y, double Roll) Offset { get; private set; }

    public static bool Shaking => Offset != (0.0, 0.0, 0.0);

    public static void Clear()
    {
        _shakes.Clear();
        Offset = (0.0, 0.0, 0.0);
    }

    public static void Update(double dt)
    {
        if (!double.IsFinite(dt) || dt < 0.0) return;

        double x = 0.0, y = 0.0, roll = 0.0;
        for (int i = _shakes.Count - 1; i >= 0; i--)
        {
            (double strength, double since, double seconds, int seed) = _shakes[i];
            since += dt;
            if (since > seconds)
            {
                _shakes.RemoveAt(i);
                continue;
            }

            _shakes[i] = (strength, since, seconds, seed);
            (double sx, double sy, double sr) = ViewShake.At(strength, since, seconds, seed);
            x += sx;
            y += sy;
            roll += sr;
        }

        double most = ViewShake.MostShare;
        Offset = (Math.Clamp(x, -most, most), Math.Clamp(y, -most, most), Math.Clamp(roll, -most, most));
    }

    /// <summary>
    /// Starts a rattle and returns its strength. Called by <see cref="BurstSound"/>, which follows each
    /// front to the camera, so the shake and the bang are one arrival.
    /// </summary>
    public static double Struck(double pascals, double positivePhaseSeconds, int seed)
    {
        double strength = ViewShake.Strength(pascals);
        if (strength > 0.0) _shakes.Add((strength, 0.0, ViewShake.Seconds(positivePhaseSeconds), seed));
        return strength;
    }
}
