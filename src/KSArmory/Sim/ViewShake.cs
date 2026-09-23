namespace KSArmory;

/// <summary>
/// The view thrown about as a blast front passes the eye: a sharp jolt, then a rattle dying away,
/// as hard as the overpressure there.
///
/// <para>Drawn as the picture moved rather than the camera turned, so it needs no hold on a view
/// something else may be driving and the panel stays still over it. Measured in shares of the
/// screen's height, so it is the same shake at every resolution.</para>
/// </summary>
internal static class ViewShake
{
    // How far the picture is thrown at full strength, and the overpressure that is: five psi,
    // which knocks a person down.
    public const double MostShare = 0.03;
    public const double FullPascals = 34_474.0;

    // Under this the picture moves by less than a pixel at 1080 lines, and nothing is drawn.
    public const double FaintestShare = 0.0008;

    // It turns by half as much, in radians, as it moves in shares of the height.
    private const double RollPerShare = 0.5;

    // The jolt rises over this, rather than the picture jumping in one frame.
    private const double AttackSeconds = 0.03;

    /// <summary>
    /// How hard a front of <paramref name="overpressurePascals"/> shakes the view, from nothing to one.
    /// A square root, because the jolt a person feels grows slower than the pressure.
    /// </summary>
    public static double Strength(double overpressurePascals)
    {
        if (!(overpressurePascals > 0.0)) return 0.0;
        return Math.Min(1.0, Math.Sqrt(overpressurePascals / FullPascals));
    }

    /// <summary>How long it rattles: the front's own push, and a settle after it.</summary>
    public static double Seconds(double positivePhaseSeconds)
    {
        double push = double.IsFinite(positivePhaseSeconds) ? Math.Max(positivePhaseSeconds, 0.0) : 0.0;
        return Math.Min(0.4 + (1.5 * push), 3.0);
    }

    /// <summary>
    /// Where the picture is thrown <paramref name="since"/> seconds after the front arrived: across
    /// and up in shares of the screen's height, and a roll in radians. Nothing before the front and
    /// nothing once it has rattled out. <paramref name="seed"/> picks the rattle, so two bursts do
    /// not shake alike.
    /// </summary>
    public static (double X, double Y, double Roll) At(double strength, double since, double seconds, int seed)
    {
        if (!(strength > 0.0) || !(since >= 0.0) || !(seconds > 0.0) || since > seconds) return (0.0, 0.0, 0.0);

        double envelope = strength * MostShare * Math.Min(1.0, since / AttackSeconds)
                          * Math.Exp(-3.0 * since / seconds);
        if (envelope < FaintestShare) return (0.0, 0.0, 0.0);

        double x = Rattle(since, seed, 0) * envelope;
        double y = Rattle(since, seed, 1) * envelope;
        double roll = Rattle(since, seed, 2) * envelope * RollPerShare;

        return (x, y, roll);
    }

    // Three sines at frequencies that never line up, each with a phase of its own, normalised to
    // at most one.
    private static double Rattle(double t, int seed, int axis)
    {
        double sum = 0.0;
        for (int k = 0; k < 3; k++)
        {
            double hz = 7.0 + (5.3 * k) + (1.7 * axis);
            double phase = Hash(seed, (axis * 3) + k) * 2.0 * Math.PI;
            sum += Math.Sin((2.0 * Math.PI * hz * t) + phase);
        }

        return sum / 3.0;
    }

    private static double Hash(int seed, int n)
    {
        unchecked
        {
            uint h = (uint)((seed * 73_856_093) ^ (n * 19_349_663));
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }
}
