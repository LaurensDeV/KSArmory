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

    // How long the echo layer of that bang runs at its own pitch: Core ships four, at 10.6 to
    // 10.8 s. Said in the log beside the pitch, because the length is the part a reader can check
    // by ear and the pitch is not.
    private const double EchoSeconds = 10.7;

    // Anything further than this and the bang is not worth queueing: a minute of silence and then
    // a noise is indistinguishable from a bug.
    private const double FurthestSeconds = 45.0;

    // Bangs due within this long of each other from within this far of each other are one bang:
    // six warheads of a bus land 9 mm apart in the same frame, and six copies of one sound played
    // on top of each other is a louder bang, not six.
    private const double SameBangSeconds = 0.5;
    private const double SameBangMetres = 1000.0;

    // The parameters Core's explosion sounds are mixed on, set exactly as ExplosionSoundSystem sets
    // them. Left unset they read zero: the far report -- the rumble -- is silent at every range, the
    // close crack is at full at every range, and the echo sits at its floor.
    private static readonly KeyHash DistanceHash = KeyHash.Make("Distance".AsSpan());
    private static readonly KeyHash PressureHash = KeyHash.Make("Pressure".AsSpan());
    private static readonly KeyHash IntensityHash = KeyHash.Make("Intensity".AsSpan());

    private static readonly List<(double Due, double3 BurstEcl, object? Body, double ChargeKg)> _pending = [];

    // Bangs playing, re-placed every frame as ExplosionSoundSystem does, because a sound lasting
    // thirty seconds is heard from wherever the camera has got to since.
    private static readonly List<(IChannel Channel, double3 Anchor, object? Body)> _playing = [];

    private static bool _warned;

    /// <summary>Forgets every bang still on its way, for a scene that no longer contains them.</summary>
    public static void Clear()
    {
        _pending.Clear();

        foreach ((IChannel channel, _, _) in _playing)
        {
            try { channel.Stop(stopImmediate: true); }
            catch { /* Gone with the scene. */ }
        }

        _playing.Clear();
    }

    /// <summary>
    /// Queues one, to be heard once the sound has covered the distance. Silent on a body with no
    /// air, which is the whole of what makes that case different.
    ///
    /// <para>An air burst is heard twice from outside its Mach stem: the front straight from the
    /// burst, and the one the ground reflected, which has the further to come. Inside the stem the
    /// two are one front, and one bang.</para>
    /// </summary>
    public static void Begin(Celestial body, double3 burstEcl, double chargeKg, double burstHeight = 0.0)
    {
        try
        {
            if (!KsaWorld.HasAtmosphere(body)) return;
            if (GameAudio.GetAudioCamera() is not { } camera) return;

            double range = Vec.Len(burstEcl - camera.PositionEcl);
            if (!double.IsFinite(range)) return;

            // The body's own speed of sound, which its air sets -- the same one the blast front and the
            // dust ring run at on its ground (BodyAir): 340 m/s in Earth's air, about 206 in a cold thin
            // one. KSA's air is isothermal, so it is one number over the whole path.
            double speed = KsaWorld.BodyAirOf(body).SoundMetresPerSecond;
            if (!(speed > 0.0)) return;

            Queue(burstEcl, chargeKg, range / speed);

            if (!(burstHeight > 0.0)) return;

            double3 up = Vec.Unit(burstEcl - body.GetPositionEcl());
            double3 toEar = camera.PositionEcl - burstEcl;
            double over = Vec.Dot(toEar, up) + burstHeight;
            double across = Vec.Len(toEar - (up * Vec.Dot(toEar, up)));
            if (over <= 0.0 || GroundReflection.InReflection(burstHeight, across, over) > 0.5) return;

            double reflected = Vec.Len(camera.PositionEcl - (burstEcl - (up * (2.0 * burstHeight))));
            if ((reflected - range) / speed < SecondBangSeconds) return;

            Queue(burstEcl, chargeKg * ReflectedShare, reflected / speed);
        }
        catch
        {
            // A bang nobody hears is not worth a fault.
        }
    }

    // Under this the two reports of an air burst are heard as one.
    private const double SecondBangSeconds = 0.15;

    // The reflected report, as a share of the charge that sets how it sounds: the ground gives back
    // most of what reaches it, and it has come further.
    private const double ReflectedShare = 0.6;

    private static void Queue(double3 burstEcl, double chargeKg, double delay)
    {
        try
        {
            if (!double.IsFinite(delay) || delay > FurthestSeconds) return;

            // Anchored to the body, because the bang is still seconds away and a bare ecliptic
            // point is left behind by the planet's 29.8 km/s long before it arrives.
            if (!KsaWorld.TryAnchorToGround(burstEcl, out object? anchored, out double3 anchor)) return;

            for (int i = 0; i < _pending.Count; i++)
            {
                (double due, double3 at, object? onBody, double charge) = _pending[i];
                if (!ReferenceEquals(onBody, anchored) || Math.Abs(due - delay) > SameBangSeconds) continue;
                if (Vec.Len(at - anchor) > SameBangMetres) continue;

                _pending[i] = (due, at, onBody, charge + chargeKg);
                return;
            }

            _pending.Add((delay, anchor, anchored, chargeKg));
        }
        catch
        {
            // A bang nobody hears is not worth a fault.
        }
    }

    /// <summary>
    /// Counts the queue down and plays what has arrived, on the simulated step: a bang is a thing
    /// in the world, so it waits through a pause and hurries under timewarp like everything else.
    /// Then re-places what is playing against the camera.
    /// </summary>
    public static void Update(double dt)
    {
        if (double.IsFinite(dt) && dt >= 0.0)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                (double due, double3 anchor, object? body, double charge) = _pending[i];

                due -= dt;
                if (due > 0.0) { _pending[i] = (due, anchor, body, charge); continue; }

                _pending.RemoveAt(i);
                Play(body, anchor, charge);
            }
        }

        for (int i = _playing.Count - 1; i >= 0; i--)
        {
            (IChannel channel, double3 anchor, object? body) = _playing[i];

            try
            {
                if (!GameAudio.IsStillActive(channel) || !TrySpatial(body, anchor, out SpatialAudio spatial))
                {
                    _playing.RemoveAt(i);
                    continue;
                }

                Apply(channel, spatial);
            }
            catch
            {
                _playing.RemoveAt(i);
            }
        }
    }

    private static bool TrySpatial(object? body, double3 anchor, out SpatialAudio spatial)
    {
        spatial = default;

        if (!KsaWorld.TryGroundAnchorEcl(body, anchor, out double3 atEcl, out double3 velEcl)) return false;
        if (GameAudio.GetAudioCamera() is not { } camera) return false;

        // Ego is a pure translation of Ecl, so a separation is the same vector in both. The ground's
        // own motion is the source velocity: a Doppler shift on a bang that has already travelled
        // for seconds is far below what anyone could place.
        double3 posEgo = atEcl - camera.PositionEcl;
        if (!Vec.IsFinite(posEgo) || !Vec.IsFinite(velEcl)) return false;

        // The pressure at the listener, in atmospheres, which is what the channel's low-pass reads.
        spatial = new SpatialAudio(posEgo, velEcl, PhysicalAtmosphereReference.GetAtmosphericPressure(camera));
        return true;
    }

    private static void Apply(IChannel channel, SpatialAudio spatial)
    {
        channel.SetSpatialAudio(spatial);
        channel.SetParameter(DistanceHash, (float)spatial.Distance());
        channel.SetParameter(PressureHash, (float)spatial.AtmosphericPressure);
        channel.SetParameter(IntensityHash, 1f);
    }

    private static void Play(object? body, double3 anchor, double chargeKg)
    {
        try
        {
            if (!TrySpatial(body, anchor, out SpatialAudio spatial)) return;
            if (ModLibrary.Get<SoundBehavior>(BangId) is not { } sound) return;

            // Started paused, pitched and placed before it is let go, so no part of it is heard at
            // the pitch of a rocket going off or mixed as though it were at the listener. The
            // multiplier reaches every layer KSA's bang is made of -- the crack, the far report and
            // the ten-second echo -- through the multi-channel wrapper.
            double kt = MushroomCloud.KilotonsFor(chargeKg);
            float pitch = (float)MushroomCloud.BangPitch(kt);

            sound.Play(spatial, 1f, out IChannel? channel, startPaused: true);
            if (channel is null) return;

            channel.PitchMultiplier = pitch;
            Apply(channel, spatial);
            channel.SetPaused(false);

            if (channel.SubscribedClusterSound is { } cluster && cluster.IsPaused()) cluster.SetPaused(false);

            _playing.Add((channel, anchor, body));

            Log.Info($"bang: {kt:F2} kt heard {spatial.Distance() / 1000.0:F1} km away at {pitch:F2} pitch "
                     + $"-- KSA's ten-second echo runs about {EchoSeconds / pitch:F0} s");
        }
        catch (Exception e)
        {
            if (_warned) return;

            _warned = true;
            Log.Warn($"the bang could not be played: {e.Message}");
        }
    }
}
