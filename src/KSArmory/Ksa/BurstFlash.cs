using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The view going white when a burst goes off in front of you, and the glare round the ball.
/// <para>The model only: <see cref="CloudPass"/> writes it into the scene image, because KSA wraps its
/// UI pass in <c>if (DrawUI)</c>, which F2 and <c>ScreenshotCapture</c> both clear.</para>
/// </summary>
internal static class BurstFlash
{
    // Never quite opaque: at a full white the scene is gone and so is any sense of where.
    private const float MostOpaque = 0.97f;

    // How fast the white bleeds off, as the time constant of its recovery: FlashGlare.RecoverySeconds
    // for the eye of the burst doing the blinding, held so the tail keeps it after the ball is spent.
    private static double _fadeSeconds = 0.28;

    // The whiteout, and the brightness it was last driven by. It is the CHANGE that blinds, not
    // the level: an eye adapts while the source is still burning.
    private static double _held;
    private static double _seen;
    private static double _veilViolet;

    /// <summary>Forgets it, for a scene that no longer contains the burst.</summary>
    public static void Reset()
    {
        _held = 0.0;
        _seen = 0.0;
        _veilViolet = 0.0;
        _fadeSeconds = FlashGlare.RecoverySeconds(1.0);
        Whiteout = 0f;
        Glare = 0f;
        Violet = 0f;
        HaloViolet = 0f;
        GlareColour = default;
        SourceIndex = -1;
    }

    /// <summary>How white the view is, in [0, 1]. Read by the pass that writes it.</summary>
    public static float Whiteout { get; private set; }

    /// <summary>
    /// The halo round the ball, in the whiteout's units and held at its level rather than its rise:
    /// scattered light lasts as long as the source is bright.
    /// </summary>
    public static float Glare { get; private set; }

    /// <summary>
    /// How violet the whiteout is, in [0, 1]: the <see cref="FlashGlare.VioletShare"/> of the light
    /// that drove it, held through its recovery.
    /// </summary>
    public static float Violet { get; private set; }

    /// <summary>How violet the ball's light is now, which the halo round it takes as it warms.</summary>
    public static float HaloViolet { get; private set; }

    /// <summary>The ball's own colour, which the halo takes as it cools to orange.</summary>
    public static float3 GlareColour { get; private set; }

    /// <summary>
    /// Which of <see cref="NuclearClouds.TryBurning"/>'s bursts the glare comes from, or -1. An
    /// index rather than a position, so the pass resolves it against the camera it draws with.
    /// </summary>
    public static int SourceIndex { get; private set; } = -1;

    /// <summary>On the simulated step, so the whiteout freezes with a pause and slows with slow motion.</summary>
    public static void Update(double dt)
    {
        try
        {
            if (!KsaWorld.TryMainCameraPose(out double3 eyeEcl, out double3 forwardEcl))
            {
                Whiteout = 0f;
                return;
            }

            // The view's own field, which the sight's magnification narrows to a few degrees.
            double halfField = KsaWorld.MainViewFovDeg() * 0.5;

            double brightest = 0.0;
            double brightestAdapted = 1.0;
            int source = -1;
            double3 colour = default;

            // Over the bursts, not the clouds: a fireball needs no air, and an airless body has no cloud.
            for (int i = 0; i < NuclearClouds.BurningCount; i++)
            {
                if (!NuclearClouds.TryBurning(i, out double3 burstEcl, out MushroomCloud.Flash flash,
                                              out Celestial? body, out double charge)) continue;

                if (flash.Spent) continue;

                double range = Vec.Len(burstEcl - eyeEcl);
                if (!(range > 1.0)) continue;

                // In suns, against what the eye is adapted to, on the eye's own scale: linear in the
                // light it would be a whiteout at 2.4 km and next to nothing at 10.
                double suns = FlashGlare.Suns(MushroomCloud.KilotonsFor(charge), range, flash.Glow,
                                              NuclearClouds.BurningAir(i));
                double adapted = FlashGlare.AdaptedTo(
                    body is not null ? KsaWorld.SunElevationDeg(body, eyeEcl) : double.NaN);

                // How much of that reaches an eye facing where this one faces.
                double offAxisDeg = double.RadiansToDegrees(
                    Vec.AngleBetween(burstEcl - eyeEcl, forwardEcl));

                double seen = FlashGlare.Level(suns * FlashGlare.Reaching(offAxisDeg, halfField), adapted);
                if (seen <= brightest) continue;

                brightest = seen;
                brightestAdapted = adapted;
                source = i;
                colour = flash.Colour;
            }

            // The last burst that gave any, so the recovery tail keeps a direction after the ball
            // is spent.
            if (source >= 0)
            {
                SourceIndex = source;
                _fadeSeconds = FlashGlare.RecoverySeconds(brightestAdapted);
            }

            double violetNow = SourceIndex >= 0
                                   ? FlashGlare.VioletShare(NuclearClouds.BurningAge(SourceIndex))
                                   : 0.0;
            HaloViolet = (float)violetNow;

            // Driven by the rise: an eye adapts while the source burns. Held at the level, the view
            // stayed near white 3.8 s after a 1.9 s burn.
            double rise = Math.Max(0.0, brightest - _seen);
            _seen = brightest;

            // ADDED, not the larger of the two: a flash that arrives over a few frames blinds as much
            // as one that arrives in one. Held at the largest step, a burst reaching full brightness
            // in two frames stops at the first one's 0.6.
            if (dt > 0.0) _held *= Math.Exp(-dt / _fadeSeconds);
            double added = Math.Min(_held + rise, 1.0) - _held;

            // The veil is an afterimage and keeps the colour of what blinded it, mixed by how much of
            // the whiteout each rise put there, not by the ball's age.
            if (added > 0.0)
            {
                _veilViolet = ((_held * _veilViolet) + (added * violetNow)) / (_held + added);
            }

            _held += added;
            Violet = (float)_veilViolet;

            Whiteout = (float)Math.Clamp(_held, 0.0, MostOpaque);
            if (Whiteout <= 0.004f) Whiteout = 0f;

            Glare = (float)Math.Clamp(brightest, 0.0, 1.0);
            if (Glare <= 0.004f) Glare = 0f;
            if (source >= 0) GlareColour = new float3((float)colour.X, (float)colour.Y, (float)colour.Z);
        }
        catch
        {
            Whiteout = 0f;
            Glare = 0f;
        }
    }
}
