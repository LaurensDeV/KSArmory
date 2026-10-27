using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A nuclear burst's two sounds, both from the ANNIE film: the observer's equipment picking up the flash
/// at the instant of the burst, and the bang, arriving when the front does.
/// <para>Each burst's <see cref="BlastFront"/> is followed live against where the camera is now; when it
/// arrives the bang and the shake go together, as loud as <see cref="BlastAltitude.PeakPascals"/> puts the
/// pressure there and nothing under <see cref="BurstHearing.FeltPascals"/>. No air carries no front.</para>
/// </summary>
internal static class BurstSound
{
    private const string BangId = "KSArmoryNuclearBang";

    // Core's own bang, of which only the close crack is played: the recording was made 10.7 km out and
    // the air took its crack off. Found by the spatial data Core gives that layer alone, and told the
    // distance on Core's own "Distance" parameter, which fades it out by a kilometre.
    private const string CoreBangId = "ExplosionBig";
    private const string CrackSpatialId = "ExplosionSpatialSoundData";
    private static readonly KeyHash DistanceHash = KeyHash.Make("Distance".AsSpan());
    private static bool _crackMissing;
    private const string PickupId = "KSArmoryNuclearPickup";

    // The recorded bang's length at its own pitch, logged because a length can be checked by ear and a
    // pitch cannot.
    private const double BangSeconds = 2.95;

    // Bangs this close in time and place are one: six warheads of a bus land in the same frame.
    private const double SameBangSeconds = 0.5;
    private const double SameBangMetres = 1000.0;

    // A front still short of the camera after this long is let go: KSA's front law is bisected to an
    // hour, and nothing heard at all is worth following further.
    private const double FollowSeconds = 1800.0;

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
        _probes.Clear();

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
        Pickup(burstEcl, chargeKg);

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

    // The pickups of bursts this close together in wall time are one: a bus's warheads land in a frame.
    private static long? _lastPickupMs;

    // The equipment reacting to the flash, at the instant of the burst whatever the air: it is the
    // radiation the recorder picked up, not the front.
    private static void Pickup(double3 burstEcl, double chargeKg)
    {
        try
        {
            long now = Environment.TickCount64;
            if (now - _lastPickupMs < (long)(SameBangSeconds * 1000.0)) return;
            if (GameAudio.GetAudioCamera() is not { } camera) return;

            double3 posEgo = burstEcl - camera.PositionEcl;
            double loudness = BurstHearing.PickupLoudness(MushroomCloud.KilotonsFor(chargeKg), Vec.Len(posEgo));
            if (loudness <= 0.0 || !Vec.IsFinite(posEgo)) return;
            if (ModLibrary.Get<SoundBehavior>(PickupId) is not { } sound) return;

            SpatialAudio spatial = new(posEgo, new double3(0.0, 0.0, 0.0),
                                       PhysicalAtmosphereReference.GetAtmosphericPressure(camera));
            sound.Play(spatial, (float)loudness, out IChannel? channel, startPaused: false);
            _lastPickupMs = now;
            if (channel is not null) _probes.Add((channel, now, "pickup"));

            Log.Info($"pickup: the equipment heard the flash {Distance.Say(Vec.Len(posEgo))} away at {loudness:F2}");
        }
        catch (Exception e)
        {
            if (_warned) return;

            _warned = true;
            Log.Warn($"the pickup could not be played: {e.Message}");
        }
    }

    // The two reports of an air burst closer than this are heard as one.
    private const double SecondBangSeconds = 0.15;

    // The reflected report, as a share of the loudness of the direct one: the ground gives back most of
    // what reaches it, and it has come further.
    private const double ReflectedShare = 0.6;

    // Channels to report on once they have been playing a moment: what FMOD made of them, because a
    // sound KSA accepted and nobody hears is otherwise indistinguishable from one that played.
    private static readonly List<(IChannel Channel, long StartedMs, string What)> _probes = [];
    private const long ProbeAfterMs = 250;

    private static void Probe()
    {
        long now = Environment.TickCount64;

        for (int i = _probes.Count - 1; i >= 0; i--)
        {
            (IChannel channel, long started, string what) = _probes[i];
            if (now - started < ProbeAfterMs) continue;

            _probes.RemoveAt(i);

            try
            {
                string line = $"{what} after {now - started} ms: playing {channel.IsPlaying()}, virtual {channel.IsVirtual()}";

                if (channel is ChannelWrapper cw)
                {
                    line += $", volume {cw.SoundVolumeMultiplier:F2} x {cw.VolumeMultiplier:F2} x fade {cw.FadeVolumeMultiplier:F2}"
                            + $", pitch {cw.PitchMultiplier:F2} x sim {cw.SimPitchMultiplier:F2}, lpf {cw.LpfValue:F0} Hz"
                            + $", hpf {cw.HpfValue:F0} Hz, {cw.SpatialAudio.Distance():F0} m"
                            + FmodReport(cw);
                }

                Log.Info(line);
            }
            catch (Exception e)
            {
                Log.Info($"{what}: could not be probed: {e.Message}");
            }
        }
    }

    // Brutal.Fmod is not referenced by the build, so its extension methods are reached by name.
    private static string FmodReport(ChannelWrapper cw)
    {
        try
        {
            object fmod = typeof(ChannelWrapper).GetField("Channel")!.GetValue(cw)!;
            Type? ext = fmod.GetType().Assembly.GetType("Brutal.FmodApi.Fmod");
            if (ext is null) return ", fmod unreadable";

            string Ask(string name, int outs)
            {
                var method = ext.GetMethods().FirstOrDefault(
                    m => m.Name == name && m.GetParameters().Length == outs + 1
                         && m.GetParameters()[0].ParameterType == fmod.GetType());
                if (method is null) return "?";

                object?[] args = new object?[outs + 1];
                args[0] = fmod;
                object? result = method.Invoke(null, args);
                return $"{string.Join("/", args.Skip(1))} ({result})";
            }

            return $", fmod volume {Ask("TryGetVolume", 1)}, audibility {Ask("TryGetAudibility", 1)}"
                   + $", min/max {Ask("TryGet3dMinMaxDistance", 2)}, 3d level {Ask("TryGet3dLevel", 1)}"
                   + $", paused {Ask("TryGetPaused", 1)}";
        }
        catch (Exception e)
        {
            return $", fmod unreadable: {e.Message}";
        }
    }

    /// <summary>On the simulated step, so a front waits through a pause and hurries under timewarp.</summary>
    public static void Update(double dt)
    {
        Probe();

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

        // FMOD hears the velocity against a listener at rest, so it has to be the camera's Ego velocity,
        // as ExplosionSoundSystem gives it: in Ecl a burst passes at the planet's 30 km/s and is Dopplered
        // to nothing wherever it is not mixed as 2D. The camera reports Ego velocity only of a body, so the
        // point's is that plus its own against the body; without a body, no Doppler at all.
        double3 velEgo = body is IVelocity on
                             ? velEcl - on.GetVelocityEcl() + camera.GetVelocityEgo(on)
                             : new double3(0.0, 0.0, 0.0);
        if (!Vec.IsFinite(velEgo)) velEgo = new double3(0.0, 0.0, 0.0);

        // The pressure at the listener, in atmospheres, which is what the channel's low-pass reads.
        spatial = new SpatialAudio(posEgo, velEgo, PhysicalAtmosphereReference.GetAtmosphericPressure(camera));
        return true;
    }

    private static void Apply(IChannel channel, SpatialAudio spatial) => channel.SetSpatialAudio(spatial);

    private static void Play(object? body, double3 anchor, double chargeKg, double loudness)
    {
        try
        {
            if (!TrySpatial(body, anchor, out SpatialAudio spatial)) return;
            if (ModLibrary.Get<SoundBehavior>(BangId) is not { } sound) return;

            // Started paused so no part of it is heard before it is pitched and placed.
            double kt = MushroomCloud.KilotonsFor(chargeKg);
            float pitch = (float)MushroomCloud.BangPitch(kt);

            sound.Play(spatial, (float)Math.Clamp(loudness, 0.0, 1.0), out IChannel? channel, startPaused: true);
            if (channel is null) return;

            channel.PitchMultiplier = pitch;
            Apply(channel, spatial);
            channel.SetPaused(false);

            _playing.Add((channel, anchor, body));
            _probes.Add((channel, Environment.TickCount64, "bang"));

            double crackAt = MushroomCloud.CrackDistance(spatial.Distance(), kt);
            bool cracked = PlayCrack(body, anchor, spatial, loudness, crackAt);

            Log.Info($"bang: {kt:F2} kt heard {spatial.Distance() / 1000.0:F1} km away at {pitch:F2} pitch "
                     + $"-- the recording runs {BangSeconds / pitch:F1} s"
                     + (cracked ? $", Core's crack as from {crackAt:F0} m" : ", no crack"));
        }
        catch (Exception e)
        {
            if (_warned) return;

            _warned = true;
            Log.Warn($"the bang could not be played: {e.Message}");
        }
    }

    private static bool PlayCrack(object? body, double3 anchor, SpatialAudio spatial, double loudness, double crackAt)
    {
        try
        {
            SoundReference? crack = ModLibrary.Get<SoundBehavior>(CoreBangId) is MultiSound core
                                        ? core.Sounds.OfType<SoundReference>()
                                              .FirstOrDefault(l => l.SpatialSoundData?.Id == CrackSpatialId)
                                        : null;
            if (crack is null)
            {
                if (!_crackMissing) Log.Warn($"Core's {CoreBangId} has no close crack layer any more: the bang plays without one");
                _crackMissing = true;
                return false;
            }

            crack.Play(spatial, (float)Math.Clamp(loudness, 0.0, 1.0), out IChannel? channel, startPaused: true);
            if (channel is null) return false;

            channel.SetParameter(DistanceHash, (float)crackAt);
            Apply(channel, spatial);
            channel.SetPaused(false);

            _playing.Add((channel, anchor, body));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
