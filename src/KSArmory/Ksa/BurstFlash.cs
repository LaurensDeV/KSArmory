using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The screen going white for a moment when a burst goes off in front of you.
///
/// <para>The fireball is drawn as a body in the world and lights what is near it, but a nuclear
/// flash is not a bright object — it is more light than the eye or a camera can take, and what a
/// viewer gets is a white field for about a second. Without it a burst two kilometres away is a
/// small bright ball on an otherwise ordinary afternoon.</para>
///
/// <para><b>This is the model, not the drawing.</b> <see cref="CloudPass"/> writes it into the
/// scene image, because a flash has to survive the UI being off: KSA wraps its whole UI pass in
/// <c>if (DrawUI)</c>, and both F2 and <c>ScreenshotCapture</c> clear that. Painted as an ImGui
/// overlay it vanished from every screenshot the harness took and would have vanished for any
/// player who hid the HUD — which is the wrong answer for something that is not HUD.</para>
///
/// <para>Any body. It is read off the bursts still burning rather than off the standing clouds,
/// because a fireball is incandescent gas and needs no air to be one — if anything a vacuum burst
/// is the brighter, having no atmosphere in the way.</para>
/// </summary>
internal static class BurstFlash
{
    // How much apparent brightness counts as a whiteout. Brightness goes as the glow times the
    // square of the ball's angular size, so this is one number for every yield and every range
    // rather than a curve per weapon: a small burst close and a big one far are the same answer.
    //
    // It DIVIDES, so larger is dimmer. At the peak of a 0.3 kt ball seen from the 2.4 km the cloud
    // is watched from the brightness works out near this -- which is also about its blast radius,
    // and being blinded inside the blast radius is the right shape of answer.
    private const double Saturates = 0.20;

    // Never quite opaque: at a full white the scene is gone and so is any sense of where.
    private const float MostOpaque = 0.92f;

    // How fast the white bleeds off, as the time constant of its recovery. Full white for about a
    // third of a second and gone inside a second and a half.
    private const double FadeSeconds = 0.28;

    // The whiteout, and the brightness it was last driven by. It is the CHANGE that blinds, not
    // the level: an eye adapts while the source is still burning.
    private static double _held;
    private static double _seen;

    /// <summary>Forgets it, for a scene that no longer contains the burst.</summary>
    public static void Reset()
    {
        _held = 0.0;
        _seen = 0.0;
        Whiteout = 0f;
        SourceIndex = -1;
    }

    /// <summary>How white the view is, in [0, 1]. Read by the pass that writes it.</summary>
    public static float Whiteout { get; private set; }

    /// <summary>
    /// Which of <see cref="NuclearClouds.TryBurning"/>'s bursts the glare comes from, or -1. An
    /// index rather than a position, so the pass resolves it against the camera it draws with.
    /// </summary>
    public static int SourceIndex { get; private set; } = -1;

    /// <summary>
    /// Advances it. On the simulated step, like everything else this mod advances: the whiteout
    /// freezes with a pause and slows with the panel's slow motion, which is what somebody watching
    /// a burst in slow motion is asking for.
    /// </summary>
    public static void Update(double dt)
    {
        try
        {
            if (!KsaWorld.TryMainCameraPose(out double3 eyeEcl, out double3 forwardEcl))
            {
                Whiteout = 0f;
                return;
            }

            // Half the field, so a burst in frame blinds and one outside it only glares. The
            // view's own rather than a constant: at the sight's magnification the frame is three
            // degrees wide, and a burst twenty degrees off it is genuinely not being looked at.
            double halfField = KsaWorld.MainViewFovDeg() * 0.5;

            double brightest = 0.0;
            int source = -1;

            // Over the BURSTS, not the clouds. A fireball does not need air, and reading the cloud
            // list meant a burst on an airless body blinded nobody -- which is backwards: there is
            // no atmosphere there to attenuate it.
            for (int i = 0; i < NuclearClouds.BurningCount; i++)
            {
                if (!NuclearClouds.TryBurning(i, out double3 burstEcl, out MushroomCloud.Flash flash)) continue;

                if (flash.Spent) continue;

                double range = Vec.Len(burstEcl - eyeEcl);
                if (!(range > 1.0)) continue;

                // The ball's angular size squared, which is what decides how much of the eye it
                // fills -- so a small burst close and a big one far are the same answer.
                double solid = (flash.Radius / range) * (flash.Radius / range);

                // ...and how much of that reaches somebody facing where they are facing. Without
                // it a burst directly BEHIND the camera whited the screen out exactly as one dead
                // ahead did, because the pose was read for its position and its direction thrown
                // away.
                double offAxisDeg = double.RadiansToDegrees(
                    Vec.AngleBetween(burstEcl - eyeEcl, forwardEcl));

                double seen = flash.Glow * solid * FlashGlare.Reaching(offAxisDeg, halfField);
                if (seen <= brightest) continue;

                brightest = seen;
                source = i;
            }

            // The last burst that gave any, so the recovery tail keeps a direction after the ball
            // is spent.
            if (source >= 0) SourceIndex = source;

            // Driven by the RISE and recovering always, rather than held at whatever is burning.
            // Held at the level, the ball keeps the view at full white for the whole of its 1.9 s
            // burn and only then begins a two and a half second decay -- flown, and still near
            // white at 3.8 s, which is a fault rather than a flash. An eye adapts while the source
            // is still there, so what blinds is the change.
            double rise = Math.Max(0.0, brightest - _seen);
            _seen = brightest;

            if (dt > 0.0) _held *= Math.Exp(-dt / FadeSeconds);
            _held = Math.Max(_held, rise);

            Whiteout = (float)Math.Clamp(_held / Saturates, 0.0, MostOpaque);
            if (Whiteout <= 0.004f) Whiteout = 0f;
        }
        catch
        {
            Whiteout = 0f;
        }
    }
}
