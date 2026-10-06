using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How a tracer round is drawn: how long its streak is, how brightly it burns, and where it lies
/// on the screen.
///
/// <para><b>The streak is exposure, not the round.</b> A burning tracer is a point, and what the
/// eye sees is that point smeared over the time it integrates light for, so the length follows the
/// round's speed across the screen in the player's own time. At 1x that is 44 m at 1100 m/s; in slow
/// motion it shortens with the world, and paused it is the burning base alone, where a streak of
/// fixed length reads as a rod hanging in the air.</para>
///
/// <para>A round with no tracer is drawn as the same streak, the round's own width, as a faint grey
/// light with no halo — a small fraction of a tracer — so the stream reads against the ground and the
/// sky alike while the tracers stay the only bright thing in it. A darkening was tried first and was
/// invisible past a hundred metres against grass. It is sunlight off the round, not light of its own,
/// so it fades with the sun (<see cref="Daylight"/>): at night only the tracers are seen.</para>
/// </summary>
public static class TracerLook
{
    /// <summary>How long the eye blends a moving light over (s), in the player's time.</summary>
    public const double PersistenceSeconds = 0.04;

    /// <summary>Past this a streak reads as a laser rather than a round (m).</summary>
    public const double LongestMetres = 60.0;

    /// <summary>The burning base alone, drawn when nothing is moving (m).</summary>
    public const double ShortestMetres = 0.3;

    /// <summary>A tracer lights a little way out of the muzzle, as it is seen to on film (m).</summary>
    public const double FadeInMetres = 25.0;

    /// <summary>How long it takes to go out once it has burned through (s).</summary>
    public const double BurnOutSeconds = 0.3;

    /// <summary>How wide the burning compound looks (m); nearer than it fills a pixel it is drawn this wide.</summary>
    public const double GlowWidthMetres = 0.12;

    /// <summary>The narrowest the core is drawn (px). A thinner light is drawn this wide and dimmer.</summary>
    public const double MinCorePixels = 1.5;

    /// <summary>
    /// The least of its brightness a distant round with no tracer keeps. Its true share is its width
    /// in pixels, which a 20 mm round loses past a hundred metres; this keeps the stream visible to
    /// the end of its flight.
    /// </summary>
    public const double BallFloorShare = 0.5;

    /// <summary>The narrowest a round with no tracer is drawn (px).</summary>
    public const double BallMinPixels = 1.0;

    /// <summary>The sun's elevation (deg) at which a round with no tracer is seen at full brightness.</summary>
    public const double FullDaylightDeg = 10.0;

    /// <summary>The sun's elevation (deg) past which it is not seen at all: the end of civil twilight.</summary>
    public const double DarkDeg = -6.0;

    /// <summary>
    /// How much of its daytime brightness a round with no tracer keeps: all of it with the sun
    /// <see cref="FullDaylightDeg"/> up, none once it is <see cref="DarkDeg"/> down, eased between.
    /// A round with no light of its own is seen by the sunlight off it, and a fixed brightness is
    /// what the exposure raises at night until the rounds read as faint tracers. An unreadable sun
    /// is daylight, so a round is never hidden for want of an answer.
    /// </summary>
    public static double Daylight(double sunElevationDeg)
    {
        if (!double.IsFinite(sunElevationDeg)) return 1.0;

        double t = Math.Clamp((sunElevationDeg - DarkDeg) / (FullDaylightDeg - DarkDeg), 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    /// <summary>
    /// Whether the <paramref name="fedThrough"/>th round through <paramref name="barrel"/> is a
    /// tracer, one in <paramref name="every"/>.
    ///
    /// <para>Counted per barrel, because each barrel has its own belt: counted over the gun, a mount
    /// whose barrels divide by the ratio puts every tracer down the same barrel. Staggered by the
    /// barrel's number, so the barrels' tracers take turns rather than leaving together.</para>
    /// </summary>
    public static bool IsTracer(long fedThrough, int barrel, int every)
        => every > 0 && (fedThrough + barrel) % every == 0;

    /// <summary>
    /// The streak's length behind the round. <paramref name="worldPerPlayerSecond"/> is the
    /// simulation speed, zero while paused; never longer than the round has flown, or a streak on a
    /// round just out of the barrel starts behind the mount.
    /// </summary>
    public static double LengthMetres(double speed, double worldPerPlayerSecond, double flownMetres)
    {
        double exposed = Math.Max(speed, 0.0) * PersistenceSeconds * Math.Max(worldPerPlayerSecond, 0.0);
        double length = Math.Clamp(exposed, ShortestMetres, LongestMetres);
        return Math.Min(length, Math.Max(flownMetres, 0.0));
    }

    /// <summary>How brightly it is burning, zero to one: lighting up out of the muzzle, then burning out.</summary>
    public static double Burn(double ageSeconds, double flownMetres, double burnSeconds)
    {
        if (!(burnSeconds > 0.0) || !(ageSeconds < burnSeconds)) return 0.0;

        double lit = Math.Clamp(flownMetres / FadeInMetres, 0.0, 1.0);
        lit = lit * lit * (3.0 - (2.0 * lit));

        double left = Math.Clamp((burnSeconds - ageSeconds) / BurnOutSeconds, 0.0, 1.0);
        return lit * left;
    }

    /// <summary>
    /// The core's drawn width in pixels, and the share of full brightness it keeps: a light
    /// narrower than <see cref="MinCorePixels"/> is drawn that wide, dimmed by what it lacks, so a
    /// distant tracer thins and fades rather than bloating.
    /// </summary>
    public static (double WidthPixels, double Share) Core(double pixelsPerMetre)
    {
        double physical = GlowWidthMetres * Math.Max(pixelsPerMetre, 0.0);
        if (physical >= MinCorePixels) return (physical, 1.0);

        return (MinCorePixels, physical / MinCorePixels);
    }

    /// <summary>
    /// A round with no tracer: its drawn width in pixels, and the share of its brightness it keeps.
    /// Its width is its calibre, and a round narrower than a pixel is drawn a pixel wide and fainter,
    /// down to <see cref="BallFloorShare"/>.
    /// </summary>
    public static (double WidthPixels, double Share) Ball(double pixelsPerMetre, double calibreMetres)
    {
        double physical = Math.Max(calibreMetres, 0.0) * Math.Max(pixelsPerMetre, 0.0);
        double width = Math.Max(physical, BallMinPixels);
        double share = Math.Clamp(physical / BallMinPixels, BallFloorShare, 1.0);
        return (width, share);
    }

    /// <summary>
    /// Clips a segment given in clip space to the part in front of the camera, so a streak passing
    /// beside the eye keeps the half that is visible instead of folding through infinity.
    /// </summary>
    public static bool TryClipToFront(ref double4 head, ref double4 tail, double nearestW)
    {
        bool headIn = head.W > nearestW;
        bool tailIn = tail.W > nearestW;
        if (!headIn && !tailIn) return false;
        if (headIn && tailIn) return true;

        double t = (nearestW - head.W) / (tail.W - head.W);
        double4 cut = new(head.X + ((tail.X - head.X) * t), head.Y + ((tail.Y - head.Y) * t),
                          head.Z + ((tail.Z - head.Z) * t), nearestW);

        if (headIn) tail = cut;
        else head = cut;
        return true;
    }
}
