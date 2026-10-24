using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The bang, arriving when the front does: KSA plays an explosion's sound at the flash, which is right
/// only from inside its blast radius.
/// <para>Each burst's <see cref="BlastFront"/> is followed live against where the camera is now; when it
/// arrives the bang and the shake go together, as loud as <see cref="BlastAltitude.PeakPascals"/> puts the
/// pressure there and nothing under <see cref="BurstHearing.FeltPascals"/>. No air carries no front.</para>
/// </summary>
internal static class BurstSound
{
    // What Core's own nuclear-scale explosion uses, played here on arrival rather than at the flash.
    private const string BangId = "ExplosionBig";

    // The echo layer's length at its own pitch: Core ships four, at 10.6 to 10.8 s. Logged because a
    // length can be checked by ear and a pitch cannot.
    private const double EchoSeconds = 10.7;

    // Bangs this close in time and place are one: six warheads of a bus land in the same frame.
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

    // Re-placed every frame, as ExplosionSoundSystem does: a long sound is heard from where the camera now is.
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

    /// <summary>Starts following a burst's front out to the camera; nothing where the air carries none.</summary>
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

    /// <summary>On the simulated step, so a front waits through a pause and hurries under timewarp.</summary>
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

    // The pressure at the eye by G&D's rule, with the ground's gain there.
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

        // Ego is a pure translation of Ecl, so a separation is the same vector in both.
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

            // Started paused so no part of it is heard before it is pitched and placed. The multiplier
            // reaches every layer of KSA's bang through the multi-channel wrapper.
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
