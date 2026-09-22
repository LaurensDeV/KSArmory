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
/// <para>Atmospheric bursts only, because it reads the standing clouds and a body with no air grows
/// none. What stands in for the fireball there is <see cref="BurstEjecta"/>'s debris shell, which
/// is its own emissive body.</para>
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

    // How fast the white bleeds off, as the time constant of its decay.
    //
    // The pulse and the whiteout are not the same duration. A third of a kilotonne is at peak
    // brightness for about two tenths of a second, and a flash that short reads as one frame gone
    // wrong rather than as a detonation. What lasts is the recovery: an eye or a camera saturated
    // by it comes back long after the thing that saturated it has gone.
    private const double FadeSeconds = 0.45;

    // The brightness being held, which is the peak seen and not what is burning now.
    private static double _held;

    /// <summary>Forgets it, for a scene that no longer contains the burst.</summary>
    public static void Reset()
    {
        _held = 0.0;
        Whiteout = 0f;
    }

    /// <summary>How white the view is, in [0, 1]. Read by the pass that writes it.</summary>
    public static float Whiteout { get; private set; }

    /// <summary>
    /// Advances it. On the simulated step, like everything else this mod advances: the whiteout
    /// freezes with a pause and slows with the panel's slow motion, which is what somebody watching
    /// a burst in slow motion is asking for.
    /// </summary>
    public static void Update(double dt)
    {
        try
        {
            if (!KsaWorld.TryMainCameraPose(out double3 eyeEcl, out _)) { Whiteout = 0f; return; }

            double brightest = 0.0;

            for (int i = 0; i < NuclearClouds.Count; i++)
            {
                if (!NuclearClouds.TryAt(i, out double3 burstEcl, out _, out _, out _, out _, out _,
                                         out MushroomCloud.Flash flash)) continue;

                if (flash.Spent) continue;

                double range = Vec.Len(burstEcl - eyeEcl);
                if (!(range > 1.0)) continue;

                // The ball's angular size squared, which is what decides how much of the eye it
                // fills -- so a small burst close and a big one far are the same answer.
                double solid = (flash.Radius / range) * (flash.Radius / range);

                brightest = Math.Max(brightest, flash.Glow * solid);
            }

            // Held at the peak and bled off, never following the ball down.
            _held = Math.Max(brightest, dt > 0.0 ? _held * Math.Exp(-dt / FadeSeconds) : _held);

            Whiteout = (float)Math.Clamp(_held / Saturates, 0.0, MostOpaque);
            if (Whiteout <= 0.004f) Whiteout = 0f;
        }
        catch
        {
            Whiteout = 0f;
        }
    }
}
