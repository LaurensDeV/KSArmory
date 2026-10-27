namespace KSArmory;

/// <summary>
/// What a nuclear burst draws where there is no air.
///
/// <para>No mushroom with nothing to rise through, and KSA raymarches the trail volume only for an
/// atmospheric body. What is left is the flash, the bomb's own mass expanding, and the ground it throws.</para>
///
/// <para><b>The ballistics are the engine's</b>: KSA counts no air below 100 Pa and falls every particle
/// at full local gravity there, so these numbers only say how hard to throw it.</para>
/// </summary>
public static class AirlessBurst
{
    /// <summary>
    /// How far the thrown ground reaches, in fireball radii.
    ///
    /// <para>Chosen for how it reads; the rest follows from
    /// <see cref="MushroomCloud.PeakFireballRadius"/> and the body's gravity.</para>
    /// </summary>
    public const double ReachInFireballs = 4.0;

    /// <summary>
    /// How far out a burst in vacuum marks the ground under it.
    ///
    /// <para><b>Not a blast law</b>: what reaches the ground is unabsorbed radiation, so the radius goes as
    /// the square root of the yield, not the cube root.</para>
    /// </summary>
    public static double ScorchRadius(double chargeKg)
    {
        if (chargeKg < MushroomCloud.ThresholdKg) return 0.0;

        double joules = chargeKg * Charge.JoulesPerKg * RadiatedFraction;
        return Math.Sqrt(joules / (4.0 * Math.PI * ScorchFluence));
    }

    // Most of a burst's energy leaves as X-rays where nothing absorbs them: Glasstone puts it at
    // seventy to eighty per cent above the atmosphere.
    private const double RadiatedFraction = 0.7;

    // The fluence that visibly changes the ground, in J/m². Chosen, not measured: nobody has burned
    // regolith this way. At a megajoule the mark stands just past the ground a 0.3 kt burst throws.
    private const double ScorchFluence = 1.0e6;

    /// <summary>The debris shell, in fireball radii: what the fireball becomes with nothing to hold it in.</summary>
    public const double ShellInFireballs = 2.6;

    /// <summary>
    /// Whether the fireball reaches the ground, which is the whole of what lets it throw any.
    ///
    /// <para>The textbook definition of a surface burst.</para>
    /// </summary>
    public static bool ThrowsEjecta(double chargeKg, double burstAltitudeMetres)
        => chargeKg >= MushroomCloud.ThresholdKg
           && burstAltitudeMetres < MushroomCloud.PeakFireballRadius(MushroomCloud.KilotonsFor(chargeKg));

    /// <summary>How far, how fast and for how long a burst throws the ground under it.</summary>
    public readonly record struct Ejecta(double ReachMetres, double SpeedMetresPerSecond, double FlightSeconds)
    {
        /// <summary>Nothing to throw, or nowhere to throw it.</summary>
        public bool Spent => ReachMetres <= 0.0 || FlightSeconds <= 0.0;
    }

    /// <summary>
    /// The throw, off the body's own gravity.
    ///
    /// <para>Launched at 45°: the reach is <c>v²/g</c> and the flight <c>v·√2/g</c>.</para>
    /// </summary>
    public static Ejecta EjectaAt(double chargeKg, double gravityMetresPerSecond2)
    {
        double reach = ReachInFireballs * MushroomCloud.PeakFireballRadius(MushroomCloud.KilotonsFor(chargeKg));
        if (!(reach > 0.0) || !(gravityMetresPerSecond2 > 0.0)) return default;

        double speed = Math.Sqrt(reach * gravityMetresPerSecond2);
        return new Ejecta(reach, speed, speed * Math.Sqrt(2.0) / gravityMetresPerSecond2);
    }

    /// <summary>
    /// The debris shell's radius.
    ///
    /// <para>The real expansion is far faster than a frame can show, so what is drawn is that it
    /// happened rather than its speed.</para>
    /// </summary>
    public static double ShellRadius(double chargeKg)
        => chargeKg < MushroomCloud.ThresholdKg
               ? 0.0
               : ShellInFireballs * MushroomCloud.PeakFireballRadius(MushroomCloud.KilotonsFor(chargeKg));

    /// <summary>How long the shell takes to get there, which is the vacuum flash's own duration.</summary>
    public static double ShellSeconds(double chargeKg)
        => chargeKg < MushroomCloud.ThresholdKg
               ? 0.0
               : MushroomCloud.VacuumFlashSeconds(MushroomCloud.KilotonsFor(chargeKg));

    /// <summary>
    /// The spread of speeds the ground leaves at, either side of <see cref="EjectaAt"/>'s nominal.
    ///
    /// <para>The emitter's speed and lifespan spreads must both use it, or fast grains vanish in mid-air
    /// and slow ones lie under the ground.</para>
    /// </summary>
    public const double SpeedSpread = 0.25;

    /// <summary>
    /// A ballistic arc launched at 45° peaks at a quarter of the range it covers, which is what
    /// makes the thrown dust a shallow dome rather than a column.
    /// </summary>
    public const double ApexOfTheReach = 0.25;

    /// <summary>How big what a burst draws is, for a camera that has to frame it: half the width
    /// of the whole thing, and the height of its highest part.</summary>
    public readonly record struct Extent(double RadiusMetres, double TopMetres)
    {
        /// <summary>Nothing drawn, so nothing to look at.</summary>
        public bool Empty => !(RadiusMetres > 0.0);
    }

    /// <summary>
    /// What an airless burst fills, as the two numbers a camera needs.
    ///
    /// <para>Two numbers because the dome is four times wider than it is tall.</para>
    /// </summary>
    public static Extent ExtentOf(double chargeKg, double gravityMetresPerSecond2,
                                  double burstAltitudeMetres)
    {
        if (ThrowsEjecta(chargeKg, burstAltitudeMetres))
        {
            Ejecta thrown = EjectaAt(chargeKg, gravityMetresPerSecond2);
            if (!thrown.Spent)
            {
                return new Extent(thrown.ReachMetres, thrown.ReachMetres * ApexOfTheReach);
            }
        }

        // Nothing thrown leaves the shell, which is a sphere: as tall as it is wide.
        double shell = ShellRadius(chargeKg);
        return new Extent(shell, shell);
    }

    /// <summary>
    /// How long a burst leaves something worth watching, which is what holds the chase camera.
    ///
    /// <para>A cloud's rise where there is air, the ejecta's flight on an airless surface, and the shell
    /// alone for a burst with neither.</para>
    /// </summary>
    public static double WatchSeconds(double chargeKg, bool hasAir,
                                      double gravityMetresPerSecond2, double burstAltitudeMetres)
    {
        if (chargeKg < MushroomCloud.ThresholdKg) return 0.0;
        if (hasAir) return MushroomCloud.RiseSeconds;

        return ThrowsEjecta(chargeKg, burstAltitudeMetres)
                   ? EjectaAt(chargeKg, gravityMetresPerSecond2).FlightSeconds
                   : ShellSeconds(chargeKg);
    }
}
