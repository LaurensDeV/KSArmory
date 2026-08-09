using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// Tracers: an emitter riding a shell, moved to it every frame, so the particles left behind mark
/// the path the round actually took.
///
/// <para>Same shape as <see cref="MotorPlume"/> and for the same reason — a mod-simulated round is
/// not something the engine can parent to, so the origin is rewritten each frame instead.</para>
///
/// <para><b>A muzzle-anchored emitter cannot do this.</b> The engine assigns
/// <c>ParticleEmitter.EmitterVelocity</c> only for a vehicle-parented emitter, so from a celestial
/// parent <c>InheritVelocity</c> has nothing to inherit and <c>BubbleOrigin.VelocityBub</c> never
/// reaches spawning: particles launched at the barrel hang there instead of flying down the bore.
/// Directional spawn logic is no help either, being built about a fixed axis of the body frame
/// rather than the turret's. The streak here comes entirely from the emitter moving between
/// frames, which at 1100 m/s is about 18 m.</para>
///
/// <para><b>Only a few shells are traced, and that is how belts are loaded.</b> Roughly one round
/// in five carries a tracer on a real gun; every round tracing reads as a rod of light rather than
/// as gunfire. It also bounds the cost: at 75 rounds a second an emitter each would drain the
/// shared pool in well under a second and leave nothing in the world able to spawn particles.</para>
/// </summary>
internal sealed class TracerTrail
{
    private const string TracerId = "KSArmoryTracer";

    // How many shells carry one at a time. Eight reads as a stream without the pool noticing.
    private const int MaxTracers = 8;

    // Spacing, as a fraction of how long a shell lives. Every emitter is busy for the whole of
    // its shell's flight, so filling all of them at the head of a burst spends the lot in about a
    // tenth of a second: eight tracers leave together and then nothing does for the two seconds
    // they take to expire, which reads as the rounds having all left while the gun is still
    // firing. Adopting one every life/MaxTracers keeps the stream even and the slots busy.
    private const double SpacingOfLife = 1.0;

    // How old a shell may be when an emitter adopts it.
    //
    // An emitter marks where its shell is by being moved there, so handing a freed one to a shell
    // that is already downrange teleports it: the streak stops where the old shell died and
    // resumes beside a newer one, which on screen is a tracer swinging round to wherever the gun
    // is now pointing. Only a shell that has just left the barrel may be adopted, so an emitter
    // starts at the muzzle and follows one shell out.
    //
    // Two frames rather than a tenth of a second. At 1100 m/s a tenth is 110 m, so a tracer
    // adopted at the far end of that window lights up a hundred metres clear of the gun and
    // appears to spawn out of nothing.
    private const double AdoptWithinSeconds = 0.04;

    private sealed class Live
    {
        public required IEffectSource Owner;
        public required Celestial Body;
        public required List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> Handles;
    }

    private readonly Dictionary<IProjectile, Live> _tracing = [];
    private readonly List<IProjectile> _finished = [];
    private readonly List<IProjectile> _candidates = [];

    private static bool _warned;

    /// <summary>Starts, moves and ends the tracer of every shell this battery has in the air.</summary>
    public void Update(IEffectSource battery)
    {
        if (!battery.PlumesEnabled || battery.Platform is not { } platform)
        {
            ReleaseOwnedBy(battery);
            return;
        }

        _candidates.Clear();
        foreach (IProjectile round in battery.Rounds)
        {
            // Negative tube numbers mark the cannon; the magazine owns zero and up.
            if (round.Tube < 0 && round.State == RoundState.Flying) _candidates.Add(round);
        }

        // Keep the ones already lit before taking on new ones. Swapping which shells are traced
        // every frame would strobe rather than draw streaks.
        int lit = 0;
        double newest = double.MaxValue;
        foreach (IProjectile round in _candidates)
        {
            if (!_tracing.ContainsKey(round)) continue;
            if (Follow(round, battery, platform)) lit++;
            newest = Math.Min(newest, round.Age);
        }

        // Newest first. The battery appends rounds as they are fired, so scanning forwards finds
        // the OLDEST shell still inside the age window, which is the one furthest from the muzzle:
        // the tracer then lights up already downrange instead of at the barrel.
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            IProjectile round = _candidates[i];

            if (lit >= MaxTracers) break;
            if (_tracing.ContainsKey(round)) continue;
            if (round.Age > AdoptWithinSeconds) continue;

            // Measured off the youngest shell already traced rather than off a clock: the spacing
            // wanted is between tracers, and their own ages are what that is.
            double spacing = round.Munition.MaxFlightSeconds * SpacingOfLife / MaxTracers;
            if (newest < spacing) break;

            if (!Follow(round, battery, platform)) continue;

            lit++;
            newest = round.Age;
        }

        // Anything holding an emitter that the battery has stopped reporting, or that has stopped
        // flying. Without this a reaped shell keeps its emitter for the rest of the session.
        foreach (KeyValuePair<IProjectile, Live> kv in _tracing)
        {
            if (!ReferenceEquals(kv.Value.Owner, battery)) continue;
            if (kv.Key.State != RoundState.Flying || !battery.Rounds.Contains(kv.Key))
            {
                _finished.Add(kv.Key);
            }
        }

        foreach (IProjectile round in _finished) Release(round);
        _finished.Clear();
    }

    /// <summary>Hands back the emitters of any system the roster has forgotten.</summary>
    public void Sweep(WeaponSystems roster)
    {
        foreach (KeyValuePair<IProjectile, Live> kv in _tracing)
        {
            bool present = false;
            foreach (WeaponSystems.Entry e in roster.All)
            {
                if (ReferenceEquals(e.Battery, kv.Value.Owner)) { present = true; break; }
            }
            if (!present) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) Release(round);
        _finished.Clear();
    }

    // Which system's round this is. The tables are keyed on the round, and Update is called once
    // per system, so without an owner every system's sweep treats every other system's rounds as
    // orphans: with two systems firing, each release the other's the instant it runs, and the
    // shared emitter pool churns once per system per frame.
    private void ReleaseOwnedBy(IEffectSource battery)
    {
        foreach (KeyValuePair<IProjectile, Live> kv in _tracing)
        {
            if (ReferenceEquals(kv.Value.Owner, battery)) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) Release(round);
        _finished.Clear();
    }

    /// <summary>Hands every emitter back. Safe at any time.</summary>
    public void ReleaseAll()
    {
        foreach (Live live in _tracing.Values) Give(live);
        _tracing.Clear();
    }

    private bool Follow(IProjectile round, IEffectSource battery, Vehicle platform)
    {
        if (battery.Launcher is not { } launcher) return false;

        // Built the way the drawn round bodies are, from the launch anchor plus the travel since.
        // PlatformEcl + OffsetFromPlatform is measured from the platform's ANALYTIC position, which
        // on a landed craft is metres from where its parts are actually placed.
        if (!LauncherPart.TryGetBodyEcl(platform, launcher, round.LaunchAnchorPartFrame,
                                        round.TravelSinceLaunch, battery.PlatformEcl,
                                        out double3 ecl))
        {
            return false;
        }

        if (!_tracing.TryGetValue(round, out Live? live))
        {
            if (Acquire(platform) is not { } fresh) return false;

            fresh.Owner = battery;
            live = fresh;
            _tracing[round] = live;
        }

        double3 centre = live.Body.GetPositionEcl();
        double3 positionCcf = (ecl - centre).Transform(live.Body.GetCce2Ccf());
        if (!Vec.IsFinite(positionCcf)) return false;

        var origin = new BubbleOrigin
        {
            Time = Universe.GetElapsedSimTime(),
            Parent = live.Body,
            BubFrame = BubbleFrame.Ccf,
            PositionBub = positionCcf,

            // Zero, and the emitter declares InheritVelocity false to match: the particles are
            // meant to stay where they were dropped. The shell's motion is already in the origin.
            VelocityBub = double3.Zero,
        };

        foreach (var handle in live.Handles)
        {
            if (handle.TryGet() is not { } emitter) continue;

            emitter.Origin = origin;
        }

        return true;
    }

    private static Live? Acquire(Vehicle platform)
    {
        try
        {
            if (platform.Parent is not Celestial body) return null;

            if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(TracerId, out var handles)
                || handles is null || handles.Count == 0)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn($"no free emitters for '{TracerId}'; shells will fly untraced");
                }
                return null;
            }

            foreach (var handle in handles)
            {
                if (handle.TryGet() is not { } emitter) continue;

                emitter.Context.Astronomical = body;
                emitter.Context.Vehicle = null;
                emitter.Context.Part = null;
                body.AddEmitter(handle);
            }

            return new Live { Owner = null!, Body = body, Handles = [.. handles] };
        }
        catch (Exception e)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"tracer failed to start: {e.Message}");
            }
            return null;
        }
    }

    private void Release(IProjectile round)
    {
        if (!_tracing.Remove(round, out Live? live)) return;

        Give(live);
    }

    private static void Give(Live live)
    {
        // Kill() first, and it is what actually stops it. Celestial.RemoveEmitter only drops the
        // handle from that body's list; ParticleSystem.UpdateEmitters walks the whole pool, so a
        // removed emitter keeps being updated. An Endless one never completes its own simulation,
        // so it spawns for the rest of the session and is never returned to the pool -- which is
        // seen as particles frozen where the emitter last was, and eventually as nothing in the
        // world being able to spawn any.
        foreach (var handle in live.Handles)
        {
            try
            {
                if (handle.TryGet() is { } emitter) emitter.Kill();
                live.Body.RemoveEmitter(handle);
            }
            catch { /* A body torn down mid-frame has already taken its emitters with it. */ }
        }
    }
}
