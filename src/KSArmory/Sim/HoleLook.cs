namespace KSArmory;

/// <summary>
/// How big the mark a shell leaves on a hull is: the hole it punches, and the soot round it.
/// </summary>
internal static class HoleLook
{
    // An HE shell's hole through thin skin, petalled out to a radius of one and a half calibres.
    public const double CoreInCalibres = 1.5;

    // An inert dart's: a rod through thin plate leaves a hole little wider than itself.
    public const double InertCoreInCalibres = 0.6;

    // Soot out to 0.95 m per cube-root kilogram: 0.35 m for the 20 mm's 50 g, 1.4 m for the
    // 5"/54's 3.3 kg. Cube-root, as the charge's every other radius is.
    public const double ScorchPerCubeRootKg = 0.95;

    // The soot never ends inside the petals.
    public const double LeastScorchInCores = 3.0;

    // A round that states no calibre is marked as the smallest the mod ships.
    public const double UnstatedCalibreMm = 20.0;

    /// <summary>The hole's radius and the soot's, in metres.</summary>
    public static (double Core, double Scorch) RadiiFor(double calibreMm, double chargeKg)
    {
        double calibre = double.IsFinite(calibreMm) && calibreMm > 0.0 ? calibreMm : UnstatedCalibreMm;
        double charge = double.IsFinite(chargeKg) && chargeKg > 0.0 ? chargeKg : 0.0;
        double core = (charge > 0.0 ? CoreInCalibres : InertCoreInCalibres) * calibre / 1000.0;

        double scorch = Math.Max(core * LeastScorchInCores, ScorchPerCubeRootKg * Math.Cbrt(charge));

        return (core, scorch);
    }
}
