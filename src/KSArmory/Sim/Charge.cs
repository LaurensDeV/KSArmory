namespace KSArmory;

/// <summary>
/// An explosive charge as a reader wants it: kilograms for a shell, tonnes for a bomb, and kilotons
/// and megatons of TNT for a warhead, since a charge spans thirteen orders of magnitude here and one
/// unit prints nine digits at the top of it.
/// </summary>
public static class Charge
{
    public const double KgPerTonne = 1_000.0;
    public const double KgPerKiloton = 1_000_000.0;
    public const double KgPerMegaton = 1_000_000_000.0;

    public static string Say(double kg)
    {
        if (!double.IsFinite(kg)) return "unknown";

        double size = Math.Abs(kg);
        return size >= KgPerMegaton ? $"{kg / KgPerMegaton:0.##} Mt"
             : size >= KgPerKiloton ? $"{kg / KgPerKiloton:0.##} kt"
             : size >= KgPerTonne ? $"{kg / KgPerTonne:0.##} t"
             : $"{kg:0.##} kg";
    }
}
