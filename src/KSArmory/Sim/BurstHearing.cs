namespace KSArmory;

/// <summary>
/// Whether the blast front is heard and felt where it reaches, and how loud: one gate for the bang and
/// the shake, because they are one pressure wave and arrive together.
///
/// <para>Keyed on the pressure at the ear, from <see cref="BlastAltitude.PeakPascals"/>, not on the air
/// at the burst: 50 Mt at 37 km puts several kilopascals on the ground and is a bang, where the same
/// air ratio gate made it silent.</para>
/// </summary>
internal static class BurstHearing
{
    /// <summary>The weakest front heard and felt as the burst's (Pa): a boom, at about 128 dB.</summary>
    public const double FeltPascals = 50.0;

    /// <summary>The front at which the bang is at full loudness (Pa).</summary>
    public const double LoudPascals = 1_000.0;

    /// <summary>Whether a front of this peak is heard.</summary>
    public static bool Heard(double pascals) => pascals >= FeltPascals;

    /// <summary>
    /// How loud, from nothing to one: a square root of the pressure, as <see cref="ViewShake.Strength"/>
    /// takes it, and nothing under <see cref="FeltPascals"/>.
    /// </summary>
    public static double Loudness(double pascals)
        => Heard(pascals) ? Math.Min(1.0, Math.Sqrt(pascals / LoudPascals)) : 0.0;

    /// <summary>
    /// Whether the front reached the ear this step: it was short of the range and now is not. The front
    /// is followed live rather than timed from the burst, so an ear that moves hears it when it arrives.
    /// </summary>
    public static bool Reached(double frontBefore, double frontNow, double rangeMetres)
        => frontBefore < rangeMetres && frontNow >= rangeMetres;
}
