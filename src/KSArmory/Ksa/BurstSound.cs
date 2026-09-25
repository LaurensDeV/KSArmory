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
///
/// <para><b>The bang is the front arriving, followed live.</b> Each burst's <see cref="BlastFront"/> is
/// run out against where the camera is now, and when it reaches it the bang and the view's shake go
/// together, as loud and as hard as <see cref="BlastAltitude.PeakPascals"/> puts the pressure there, and
/// nothing under <see cref="BurstHearing.FeltPascals"/>. So a burst 37 km up is heard minutes later, and
/// one at 77 km is not heard at all.</para>
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

    // Bangs due within this long of each other from within this far of each other are one bang:
    // six warheads of a bus land 9 mm apart in the same frame, and six copies of one sound played
    // on top of each other is a louder bang, not six.
    private const double SameBangSeconds = 0.5;
    private const double SameBangMetres = 1000.0;

    // A front still short of the camera after this long is let go: KSA's front law is bisected to an
    // hour, and nothing heard at all is worth following further.
    private const double FollowSeconds = 1800.0;

    // The parameters Core's explosion sounds are mixed on, set exactly as ExplosionSoundSystem sets
    // them. Left unset they read zero: the far report -- the rumble -- is silent at every range, the
    // close crack is at full at every range, and the echo sits at its floor.
    private static readonly KeyHash DistanceHash = KeyHash.Make("Distance".AsSpan());
    private static readonly KeyHash PressureHash = KeyHash.Make("Pressure".AsSpan());
    private static readonly KeyHash IntensityHash = KeyHash.Make("Intensity".AsSpan());

    // One burst's front on its way out, anchored to the ground it went off over, and whether it has yet
    // reached the camera straight from the burst and as the ground's reflection.
    private sealed class Front
    {
        public required Celestial Body;
        public required object? Anchored;
        public required double3 Anchor;
        public required double Height;
        public required AmbientAir BurstAir;
        public required double ChargeKg;
        public required int Seed;
        public double Age;
        public bool Direct;
        public bool Reflected;

        public BlastFront Law => BlastFront.For(ChargeKg, BurstAir, KsaWorld.BodyAirOf(Body).SoundMetresPerSecond, Height);
    }

    private static readonly List<Front> _fronts = [];
    private static int _seeds;

    // Bangs playing, re-placed every frame as ExplosionSoundSystem does, because a sound lasting
    // thirty seconds is heard from wherever the camera has got to since.
    private static readonly List<(IChannel Channel, double3 Anchor, object? Body)> _playing = [];

    private static bool _warned;

    /// <summary>Forgets every front still on its way, for a scene that no longer contains them.</summary>
    public static void Clear()
    {
        _fronts.Clear();

        foreach ((IChannel channel, _, _) in _playing)
        {
            try { channel.Stop(stopImmediate: true); }
            catch { /* Gone with the scene. */ }
        }

        _playing.Clear();
    }

    /// <summary>
    /// Starts following a burst's front out to the camera. Nothing where the air at the burst carries no
    /// front, which is the whole of what makes a burst over the air silent.
    /// </summary>
    public static void Begin(Celestial body, double3 burstEcl, double chargeKg, double burstHeight = 0.0)
    {
        try
        {
            if (!KsaWorld.HasAtmosphere(body)) return;

            AmbientAir air = KsaWorld.AirAt(body, burstEcl);
            double height = double.IsFinite(burstHeight) ? Math.Max(burstHeight, 0.0) : 0.0;
            if (!double.IsFinite(BlastFront.For(chargeKg, air, KsaWorld.BodyAirOf(body).SoundMetresPerSecond, height)
                                     .ArrivalSeconds(1.0))) return;

            // Anchored to the body, because the front is minutes out at worst and a bare ecliptic point
            // is left behind by the planet's 29.8 km/s long before it arrives.
            if (!KsaWorld.TryAnchorToGround(burstEcl, out object? anchored, out double3 anchor)) return;

            foreach (Front f in _fronts)
            {
                if (!ReferenceEquals(f.Anchored, anchored) || f.Age > SameBangSeconds) continue;
                if (Vec.Len(f.Anchor - anchor) > SameBangMetres) continue;

                f.ChargeKg += chargeKg;
                return;
            }

            Front front = new()
            {
                Body = body, Anchored = anchored, Anchor = anchor, Height = height, BurstAir = air,
                ChargeKg = chargeKg, Seed = ++_seeds,
            };
            _fronts.Add(front);

            if (KsaWorld.TryMainCameraPose(out double3 eye, out _))
            {
                double range = Vec.Len(eye - burstEcl);
                Log.Info($"blast front of {MushroomCloud.KilotonsFor(chargeKg):G3} kt in air at {air.PressureRatio:E2} "
                         + $"of sea level's pressure: reaches the camera {Distance.Say(range)} off in "
                         + $"{front.Law.ArrivalSeconds(range):F1} s were it to stay put");
            }
        }
        catch
        {
            // A bang nobody hears is not worth a fault.
        }
    }

    // The two reports of an air burst closer than this are heard as one.
    private const double SecondBangSeconds = 0.15;

    // The reflected report, as a share of the loudness of the direct one: the ground gives back most of
    // what reaches it, and it has come further.
    private const double ReflectedShare = 0.6;

    /// <summary>
    /// Follows every front on the simulated step, and as one reaches the camera plays the bang and shakes
    /// the view together, as loud and as hard as the pressure there: a thing in the world, so it waits
    /// through a pause and hurries under timewarp. Then re-places what is playing against the camera.
    /// </summary>
    public static void Update(double dt)
    {
        if (double.IsFinite(dt) && dt >= 0.0)
        {
            bool eye = KsaWorld.TryMainCameraPose(out double3 eyeEcl, out _);

            for (int i = _fronts.Count - 1; i >= 0; i--)
            {
                Front front = _fronts[i];
                front.Age += dt;

                try
                {
                    if (eye && KsaWorld.TryGroundAnchorEcl(front.Anchored, front.Anchor, out double3 burstEcl, out _))
                    {
                        Follow(front, burstEcl, eyeEcl);
                    }
                }
                catch
                {
                    front.Direct = front.Reflected = true;
                }

                if ((front.Direct && front.Reflected) || front.Age > FollowSeconds) _fronts.RemoveAt(i);
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

    private static void Follow(Front front, double3 burstEcl, double3 eyeEcl)
    {
        BlastFront law = front.Law;
        double radius = law.Radius(front.Age);
        double range = Vec.Len(eyeEcl - burstEcl);
        double3 up = Vec.Unit(burstEcl - front.Body.GetPositionEcl());

        if (!front.Direct && radius >= range)
        {
            front.Direct = true;
            Arrive(front, burstEcl, eyeEcl, range);
        }

        if (front.Reflected) return;
        if (!(front.Height > 0.0)) { front.Reflected = true; return; }

        double3 toEar = eyeEcl - burstEcl;
        double over = Vec.Dot(toEar, up) + front.Height;
        double across = Vec.Len(toEar - (up * Vec.Dot(toEar, up)));
        double image = Vec.Len(eyeEcl - (burstEcl - (up * (2.0 * front.Height))));

        // Inside the Mach stem, or near enough that the two reports run together, there is one bang.
        if (over <= 0.0 || GroundReflection.InReflection(front.Height, across, over) > 0.5
            || (image - range) / law.SoundMetresPerSecond < SecondBangSeconds)
        {
            front.Reflected = true;
            return;
        }

        if (radius < image || !front.Direct) return;

        front.Reflected = true;
        double pascals = BlastAltitude.PeakPascals(front.ChargeKg, front.BurstAir, KsaWorld.AirAt(front.Body, eyeEcl),
                                                   image, 1.0);
        if (BurstHearing.Heard(pascals)) Play(front.Anchored, front.Anchor, front.ChargeKg, ReflectedShare * BurstHearing.Loudness(pascals));
    }

    // The front has reached the camera straight from the burst: the pressure there by G&D's rule, with
    // the ground's own gain where the eye is, and the bang and the shake together if it is felt at all.
    private static void Arrive(Front front, double3 burstEcl, double3 eyeEcl, double range)
    {
        AmbientAir earAir = KsaWorld.AirAt(front.Body, eyeEcl);
        double gain = KsaWorld.GroundReflectionAt(front.Body, burstEcl, front.ChargeKg).GainAt(eyeEcl, front.ChargeKg);
        double pascals = BlastAltitude.PeakPascals(front.ChargeKg, front.BurstAir, earAir, range, gain);

        if (!BurstHearing.Heard(pascals))
        {
            Log.Info($"blast front passed the camera {Distance.Say(range)} from the burst at {pascals:F0} Pa, "
                     + $"{front.Age:F1} s after it -- too weak to hear or feel");
            return;
        }

        double sound = KsaWorld.BodyAirOf(front.Body).SoundMetresPerSecond;
        double phase = BlastAltitude.PositivePhaseSeconds(front.ChargeKg, front.BurstAir, earAir, sound, range, gain);
        double strength = BlastShake.Struck(pascals, phase, front.Seed);
        double loudness = BurstHearing.Loudness(pascals);
        Play(front.Anchored, front.Anchor, front.ChargeKg, loudness);

        Log.Info($"blast front passed the camera {Distance.Say(range)} from the burst {front.Age:F1} s after it, at "
                 + $"{pascals / 1000.0:F2} kPa (ground gain {gain:F2}): heard at {loudness:F2}, "
                 + $"shaking at {strength:F2} for {ViewShake.Seconds(phase):F1} s");
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

    private static void Play(object? body, double3 anchor, double chargeKg, double loudness)
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

            sound.Play(spatial, (float)Math.Clamp(loudness, 0.0, 1.0), out IChannel? channel, startPaused: true);
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
