using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The bang, arriving when it actually would.
///
/// <para>KSA plays an explosion's sound at the instant of the explosion, which is right for
/// everything it ships: a conventional burst is heard from inside its own blast radius or not at
/// all. A nuclear burst is watched from kilometres away, and the flash and the sound are separated
/// by the whole of the time sound takes to get there — seven seconds from the two and a half
/// kilometres the cloud is watched from. That gap is one of the most recognisable things about a
/// burst, and playing the two together throws it away.</para>
///
/// <para><b>And a burst with no air makes no sound at all.</b> That falls out of the same rule
/// rather than needing one of its own: there is nothing to carry it.</para>
/// </summary>
internal static class BurstSound
{
    // What Core's own nuclear-scale explosion uses. Taken out of the preset so the engine no
    // longer plays it on contact; this plays it when it arrives instead.
    private const string BangId = "ExplosionBig";

    // Dry air at about fifteen degrees, which is Earth's. One number rather than one per body:
    // the speed of sound is set by temperature and composition and this mod carries neither. What
    // it does carry is whether there is air at all, which is the difference that matters.
    private const double MetresPerSecond = 343.0;

    // Anything further than this and the bang is not worth queueing: a minute of silence and then
    // a noise is indistinguishable from a bug.
    private const double FurthestSeconds = 45.0;

    private static readonly List<(double Due, double3 BurstEcl, object? Body)> _pending = [];
    private static bool _warned;

    /// <summary>Forgets every bang still on its way, for a scene that no longer contains them.</summary>
    public static void Clear() => _pending.Clear();

    /// <summary>
    /// Queues one, to be heard once the sound has covered the distance. Silent on a body with no
    /// air, which is the whole of what makes that case different.
    /// </summary>
    public static void Begin(Celestial body, double3 burstEcl)
    {
        try
        {
            if (!KsaWorld.HasAtmosphere(body)) return;
            if (!KsaWorld.TryMainCameraPose(out double3 eyeEcl, out _)) return;

            double range = Vec.Len(burstEcl - eyeEcl);
            if (!double.IsFinite(range)) return;

            double delay = range / MetresPerSecond;
            if (delay > FurthestSeconds) return;

            // Anchored to the body, because the bang is still seconds away and a bare ecliptic
            // point is left behind by the planet's 29.8 km/s long before it arrives.
            if (!KsaWorld.TryAnchorToGround(burstEcl, out object? anchored, out double3 anchor)) return;

            _pending.Add((delay, anchor, anchored));
        }
        catch
        {
            // A bang nobody hears is not worth a fault.
        }
    }

    /// <summary>
    /// Counts them down and plays the ones that have arrived. On the simulated step, like
    /// everything else: the wait freezes with a pause and stretches under slow motion, which is
    /// what somebody watching a burst in slow motion is asking for.
    /// </summary>
    public static void Update(double dt)
    {
        if (_pending.Count == 0 || !double.IsFinite(dt) || dt < 0.0) return;

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            (double due, double3 anchor, object? body) = _pending[i];

            due -= dt;
            if (due > 0.0) { _pending[i] = (due, anchor, body); continue; }

            _pending.RemoveAt(i);
            Play(body, anchor);
        }
    }

    private static void Play(object? body, double3 anchor)
    {
        try
        {
            if (!KsaWorld.TryGroundAnchorEcl(body, anchor, out double3 atEcl, out double3 velEcl)) return;
            if (Program.GetMainCamera() is not { } camera) return;
            if (ModLibrary.Get<SoundBehavior>(BangId) is not { } sound) return;

            // Ego is a pure translation of Ecl, so a separation is the same vector in both.
            double3 posEgo = atEcl - camera.PositionEcl;
            // The ground's own motion, taken as the source velocity. The camera's is not
            // subtracted: Camera exposes no ecliptic velocity, and a Doppler shift on a bang that
            // has already travelled for seconds is far below what anyone could place.
            double3 velEgo = velEcl;
            if (!Vec.IsFinite(posEgo) || !Vec.IsFinite(velEgo)) return;

            sound.Play(new SpatialAudio(posEgo, velEgo, 1f), 1f, out IChannel? _);
        }
        catch (Exception e)
        {
            if (_warned) return;

            _warned = true;
            Log.Warn($"the bang could not be played: {e.Message}");
        }
    }
}
