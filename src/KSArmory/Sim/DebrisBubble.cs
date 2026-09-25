using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What a burst above the air leaves where a fireball would be: its debris, pushing the planet's field
/// out of the way until the field's pressure stops it. Starfish's, 1.4 Mt at 400 km, reached 1,840 km
/// along the field and 680 km across it in about 1.2 s and collapsed within about 16 s.
///
/// <para>Its size is the equal-volume radius at which the field swept out holds the debris's kinetic
/// energy: <c>R = ∛(3μ₀·f·E / 2πB²)</c>, with the kinetic share <see cref="KineticShare"/> fitted on
/// Starfish rather than taken from a model. So a small yield gives a small bubble on its own, as the cube
/// root of the energy, and a weaker field a larger one. With no field there is nothing to hold it, and
/// the debris runs out ballistically and fades.</para>
///
/// <para>It takes over from <see cref="DebrisShell"/> as the field's pressure outgrows the air's
/// (<see cref="FieldShare"/>): about 150 km on KSA's Earth, and entirely above its air.</para>
/// </summary>
internal static class DebrisBubble
{
    /// <summary>The share of the yield the debris carries as kinetic energy, fitted on Starfish.</summary>
    public const double KineticShare = 0.025;

    /// <summary>How far the field stretches the bubble along itself: Starfish's 1,840 by 680 km.</summary>
    public const double Stretch = 2.7;

    /// <summary>How long it takes to reach its size, and by when it has collapsed (s).</summary>
    public const double GrowSeconds = 1.2;

    /// <inheritdoc cref="GrowSeconds"/>
    public const double CollapseSeconds = 16.0;

    /// <summary>The mass of a device's debris (kg), which with no field sets how fast it runs out.</summary>
    public const double DebrisMassKg = 1000.0;

    /// <summary>How long ballistic debris stays visible, with no field to hold it (s).</summary>
    public const double BallisticSeconds = 4.0;

    /// <summary>The glow the bubble's rim starts from, in the shell's glow units, fading as it collapses.</summary>
    public const double PeakGlow = 10.0;

    private const double Mu0 = 4.0e-7 * Math.PI;

    /// <summary>
    /// The field (T) at a point <paramref name="fromCentre"/> from a body's centre, for a dipole on the
    /// spin axis of <paramref name="surfaceTesla"/> at the surface equator: <c>B₀(R/r)³√(1+3sin²λ)</c>.
    /// </summary>
    public static double FieldTesla(double surfaceTesla, double bodyRadius, double3 fromCentre, double3 axis)
    {
        double r = Vec.Len(fromCentre);
        if (!(surfaceTesla > 0.0) || !(r > 0.0) || !(bodyRadius > 0.0)) return 0.0;

        double sinLat = Vec.Dot(fromCentre / r, Vec.Unit(axis));
        double ratio = bodyRadius / r;
        return surfaceTesla * ratio * ratio * ratio * Math.Sqrt(1.0 + (3.0 * sinLat * sinLat));
    }

    /// <summary>The equal-volume radius (m) the field holds the debris at; infinite with no field.</summary>
    public static double EqualVolumeRadius(double chargeKg, double fieldTesla)
    {
        if (!(fieldTesla > 0.0)) return double.PositiveInfinity;

        double joules = chargeKg * 4.184e6;
        return Math.Cbrt(3.0 * Mu0 * KineticShare * joules / (2.0 * Math.PI * fieldTesla * fieldTesla));
    }

    /// <summary>
    /// How far the field rather than the air decides what the debris does, in [0, 1]: its pressure
    /// <c>B²/2μ₀</c> against the air's. All of it with no air, and on a body with no field all of it too,
    /// since then the debris runs out ballistically wherever the air cannot stop it.
    /// </summary>
    public static double FieldShare(double fieldTesla, double airPascals)
    {
        if (!(airPascals > 0.0)) return 1.0;
        if (!(fieldTesla > 0.0)) return 0.0;

        double field = fieldTesla * fieldTesla / (2.0 * Mu0);
        return field / (field + airPascals);
    }

    /// <summary>
    /// The bubble at an age: its equal-volume radius (m), its rim's radiance and colour. With no field
    /// the radius is the ballistic debris's, running out at the speed its kinetic energy gives it.
    /// </summary>
    public readonly record struct Look(double Radius, double Radiance, double3 Colour, bool Held)
    {
        /// <summary>Whether there is anything to draw.</summary>
        public bool Spent => !(Radiance > 0.0) || !(Radius > 0.0);

        /// <summary>How far across the field it reaches (m): the equal volume, less its stretch.</summary>
        public double Across => Held ? Radius / Math.Cbrt(Stretch) : Radius;
    }

    /// <summary>The bubble at an age for a charge (kg) in a field (T).</summary>
    public static Look At(double chargeKg, double age, double fieldTesla)
    {
        if (chargeKg < MushroomCloud.ThresholdKg || !(age >= 0.0)) return default;

        if (fieldTesla > 0.0)
        {
            if (age >= CollapseSeconds) return default;

            double full = EqualVolumeRadius(chargeKg, fieldTesla);
            double grown = 1.0 - Math.Exp(-3.0 * age / GrowSeconds);
            double collapsing = Smooth((age - (0.5 * CollapseSeconds)) / (0.5 * CollapseSeconds));
            double radius = full * Math.Max(grown, 0.05) * (1.0 - (0.6 * collapsing));
            double glow = PeakGlow * (1.0 - collapsing);
            double3 colour = Lerp(new double3(0.80, 0.90, 1.00), DebrisShell.AfterglowColour,
                                  Smooth(age / (0.5 * CollapseSeconds)));

            return new Look(radius, DebrisShell.RadiancePerGlow * glow, colour, Held: true);
        }

        if (age >= BallisticSeconds) return default;

        double speed = Math.Sqrt(2.0 * KineticShare * chargeKg * 4.184e6 / DebrisMassKg);
        double fade = Math.Exp(-2.0 * age / BallisticSeconds) * (1.0 - Smooth((age - (0.7 * BallisticSeconds))
                                                                            / (0.3 * BallisticSeconds)));
        return new Look(speed * Math.Max(age, 0.05), DebrisShell.RadiancePerGlow * PeakGlow * fade,
                        new double3(0.80, 0.90, 1.00), Held: false);
    }

    private static double Smooth(double x)
    {
        double t = Math.Clamp(x, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    private static double3 Lerp(double3 a, double3 b, double t) => a + ((b - a) * t);
}
