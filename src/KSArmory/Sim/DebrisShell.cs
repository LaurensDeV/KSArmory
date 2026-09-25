using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What a burst in air too thin for a mushroom leaves: its own debris, as light rather than as a cloud.
///
/// <para>Above about 30 km nothing condenses and there is no column to raise. The debris is a shell of
/// hot, ionised gas that glows and is transparent: white-hot and filling the ball at first, then
/// cooling through orange to red while it swells into a shell, brightest at its rim, and fading out
/// within a few minutes. Teak's, 3.8 Mt at 77 km, was a red shell seen from Hawaii for half an hour;
/// most of that was the air it lit, which is <see cref="XRayGlow"/>, not the debris.</para>
///
/// <para>Where the air is thin enough, the magnetic field holds the debris: it cannot cross the field
/// lines, so the shell stretches along them. The field is <see cref="Aurora"/>'s dipole on the spin
/// axis. What runs out along the line is drawn as the aurora where it comes down, not as a jet: drawn
/// straight and even, a jet read as a beam.</para>
/// </summary>
internal static class DebrisShell
{
    /// <summary>How long it glows at all (s).</summary>
    public const double LifeSeconds = 180.0;

    /// <summary>
    /// The glow the red afterglow starts from, in the ball's own glow units, and the e-folding time it
    /// fades on: dim beside the ball, about as bright as the lit air round it, and gone in minutes.
    /// </summary>
    public const double AfterglowGlow = 4.0;

    /// <inheritdoc cref="AfterglowGlow"/>
    public const double AfterglowFadeSeconds = 40.0;

    /// <summary>The afterglow's colour: the ball's last, the deep red of cooling gas.</summary>
    public static readonly double3 AfterglowColour = new(0.75, 0.16, 0.05);

    /// <summary>
    /// The ball's surface radiance for one unit of its glow: the albedo its colour was authored
    /// against, which <c>Ksa/Fireball.cs</c> sizes its light from too.
    /// </summary>
    public const double RadiancePerGlow = 0.4535;

    /// <summary>
    /// The air, against sea level, below which the field holds the debris and above which it does not
    /// at all, with a log-linear change between: about 65 km and 30 km on Earth.
    /// </summary>
    public const double FieldHoldsBelow = 1.0e-4;

    /// <inheritdoc cref="FieldHoldsBelow"/>
    public const double FieldLetsGoAbove = 1.0e-2;

    /// <summary>How far the field stretches the shell along itself, and how long that takes (s).</summary>
    public const double MostElongation = 2.5;

    /// <inheritdoc cref="MostElongation"/>
    public const double StretchSeconds = 30.0;

    /// <summary>
    /// The shell at one age: its radiance and colour, how much of the ball it still fills (one while it
    /// is white-hot, none once it is a shell), and how far the field has stretched it.
    /// </summary>
    public readonly record struct Look(double Radiance, double3 Colour, double Fill, double Elongation)
    {
        /// <summary>Whether there is anything to draw.</summary>
        public bool Spent => !(Radiance > 0.0);
    }

    /// <summary>The shell at an age, for a charge (kg), a height over the ground and the air there.</summary>
    public static Look At(double chargeKg, double age, double burstHeight, double airRatio)
    {
        double kt = MushroomCloud.KilotonsFor(chargeKg);
        if (!(kt > 0.0) || !(age >= 0.0) || age >= LifeSeconds) return default;

        MushroomCloud.Flash flash = MushroomCloud.FlashAt(chargeKg, age, burstHeight, airRatio);
        double hot = Math.Max(flash.Glow, 0.0);
        double ending = 1.0 - Smooth((age - (LifeSeconds - 30.0)) / 30.0);
        double after = AfterglowGlow * Math.Exp(-age / AfterglowFadeSeconds) * ending;
        double glow = Math.Max(hot, after);
        if (!(glow > 0.0)) return default;

        double hotShare = hot / (hot + after);
        double3 colour = hot > 0.0 ? Lerp(AfterglowColour, flash.Colour, hotShare) : AfterglowColour;

        double fill = 1.0 - Smooth(age / Math.Max(MushroomCloud.FlashSeconds(kt, airRatio), 1e-6));
        double held = FieldHold(airRatio);
        double elongation = 1.0 + ((MostElongation - 1.0) * held * Smooth(age / StretchSeconds));

        return new Look(RadiancePerGlow * glow, colour, fill, elongation);
    }

    /// <summary>How much the field holds debris in air this thin, in [0, 1].</summary>
    public static double FieldHold(double airRatio)
    {
        if (!double.IsFinite(airRatio) || airRatio <= 0.0) return 1.0;

        double span = Math.Log10(FieldLetsGoAbove) - Math.Log10(FieldHoldsBelow);
        return Math.Clamp((Math.Log10(FieldLetsGoAbove) - Math.Log10(airRatio)) / span, 0.0, 1.0);
    }

    /// <summary>
    /// Which way the field runs at a point, as a unit direction, for a dipole on the spin axis: along
    /// <c>3(a.u)u - a</c>. Horizontal over the equator and vertical at the poles; its sign says nothing.
    /// </summary>
    public static double3 FieldDirection(double3 up, double3 axis)
    {
        double3 u = Vec.Unit(up);
        double3 a = Vec.Unit(axis);
        double3 field = (u * (3.0 * Vec.Dot(a, u))) - a;
        return Vec.Len2(field) > 1.0e-12 ? Vec.Unit(field) : a;
    }

    private static double Smooth(double x)
    {
        double t = Math.Clamp(x, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    private static double3 Lerp(double3 a, double3 b, double t) => a + ((b - a) * t);
}
