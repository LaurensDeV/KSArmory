namespace KSArmory;

/// <summary>
/// What a nuclear burst draws where there is no air.
///
/// <para>A mushroom cloud is buoyant, so with nothing to rise through there is none — and KSA
/// raymarches the trail volume only for an atmospheric body, so smoke laid there would not draw
/// even if it were right. What is left is the flash, which <see cref="Fireball"/> already carries
/// anywhere, the bomb's own mass expanding out of the burst, and the ground it throws.</para>
///
/// <para><b>The ballistics are the engine's.</b> KSA counts no air below 100 Pa and falls every
/// particle at full local gravity there, whatever density its stage declares — so ejecta thrown
/// upward arcs and comes back with nothing here integrating it. These numbers only say how hard to
/// throw it.</para>
/// </summary>
public static class AirlessBurst
{
    /// <summary>
    /// How far the thrown ground reaches, in fireball radii.
    ///
    /// <para>The one number here chosen for how it reads. Everything else follows from
    /// <see cref="MushroomCloud.PeakFireballRadius"/> and the body's own gravity, so dialling the
    /// yield moves the whole effect together instead of needing a second constant typed for it. In
    /// vacuum there is no air to bleed the dust's speed, so what bounds the reach is the throw
    /// rather than the drag.</para>
    /// </summary>
    public const double ReachInFireballs = 4.0;

    /// <summary>The debris shell, in fireball radii. Larger than the fireball because it is what
    /// the fireball becomes with nothing to hold it in.</summary>
    public const double ShellInFireballs = 2.6;

    /// <summary>
    /// Whether the fireball reaches the ground, which is the whole of what lets it throw any.
    ///
    /// <para>It is also the textbook definition of a surface burst, so nothing separate decides
    /// this: a burst high enough that its fireball never touches has no crater and no ejecta.</para>
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
    /// <para>Launched at 45°, where a ballistic arc goes furthest for its speed — so the reach is
    /// <c>v²/g</c> and the flight <c>v·√2/g</c>, and both invert cleanly from the reach. Gravity is
    /// in it twice over, which is why the same device throws dust for sixteen seconds on the Moon
    /// and six on a body six times heavier.</para>
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
    /// <para>In vacuum nothing slows the bomb's vaporised mass, so it keeps going instead of
    /// settling into a fireball — which is why there is no cloud after it and why the glow is over
    /// in a fraction of a second. The real expansion is orders of magnitude faster than a frame can
    /// show, so what is drawn is that the event happened rather than the speed it happened at.</para>
    /// </summary>
    public static double ShellRadius(double chargeKg)
        => chargeKg < MushroomCloud.ThresholdKg
               ? 0.0
               : ShellInFireballs * MushroomCloud.PeakFireballRadius(MushroomCloud.KilotonsFor(chargeKg));

    /// <summary>How long the shell takes to get there, which is the flash's own duration.</summary>
    public static double ShellSeconds(double chargeKg)
        => chargeKg < MushroomCloud.ThresholdKg
               ? 0.0
               : MushroomCloud.FlashSeconds(MushroomCloud.KilotonsFor(chargeKg));

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
    /// <para>The dome is far wider than it is tall — a reach across against a quarter of one high —
    /// so a camera framed on its height alone stands close enough to leave most of it off screen.
    /// That is the whole reason this reports two numbers where a cloud needs one.</para>
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
    /// <para>Three answers rather than one, because the thing being watched differs: a cloud's rise
    /// where air lets one stand, the ejecta's flight on an airless surface, and the shell alone for
    /// a burst with neither. Holding for the rise on a body that grows no cloud is what left the
    /// camera on an empty sky for the better part of a minute.</para>
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
