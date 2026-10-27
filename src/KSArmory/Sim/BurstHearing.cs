namespace KSArmory;

/// <summary>
/// Whether the blast front is heard and felt where it reaches, and how loud: one gate for the bang and
/// the shake, because they are one pressure wave and arrive together.
///
/// <para>Keyed on the pressure at the ear, from <see cref="BlastAltitude.PeakPascals"/>, not on the air
/// at the burst: 50 Mt at 37 km puts several kilopascals on the ground and is a bang, where a gate on the
/// air at the burst would make it silent.</para>
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

    /// <summary>The yield and range the equipment's pickup was recorded at: ANNIE, 16 kt from 10.7 km.</summary>
    public const double PickupReferenceKt = 16.0;
    public const double PickupReferenceMetres = 10_700.0;

    /// <summary>The faintest pickup played, as a share of the recording's own level.</summary>
    public const double FaintestPickup = 0.05;

    /// <summary>
    /// How loud the equipment's pickup of the flash is, from nothing to one: as recorded at ANNIE's
    /// range scaled by the cube root of the yield, falling as one over the range past it -- a choice,
    /// not a field law -- and nothing fainter than <see cref="FaintestPickup"/>.
    /// </summary>
    public static double PickupLoudness(double yieldKt, double rangeMetres)
    {
        if (!(yieldKt > 0.0) || !(rangeMetres >= 0.0)) return 0.0;

        double reach = PickupReferenceMetres * Math.Cbrt(yieldKt / PickupReferenceKt);
        double loudness = Math.Min(1.0, reach / Math.Max(rangeMetres, 1.0));
        return loudness >= FaintestPickup ? loudness : 0.0;
    }

    /// <summary>
    /// Whether the front reached the ear this step: it was short of the range and now is not. The front
    /// is followed live rather than timed from the burst, so an ear that moves hears it when it arrives.
    /// </summary>
    public static bool Reached(double frontBefore, double frontNow, double rangeMetres)
        => frontBefore < rangeMetres && frontNow >= rangeMetres;
}
