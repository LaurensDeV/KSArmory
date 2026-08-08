using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// The flame at the nozzle: one endless emitter per burning round, re-anchored every frame and
/// handed back at burnout.
///
/// <para>Unlike <see cref="Detonation"/>, which fires a burst and forgets it, this holds an
/// emitter open. Emitters come from a pool, so every one taken has to be returned or a few salvos
/// exhaust it and nothing in the world can spawn particles again — <see cref="Release"/> is not
/// tidiness, it is the whole reason this class tracks anything.</para>
///
/// <para>The origin is rewritten each frame rather than parented to the round, because
/// <see cref="BubbleFrame"/> offers only body-centred frames and a mod-simulated round is not
/// something the engine can follow.</para>
/// </summary>
internal sealed class MotorPlume
{
    private const string PlumeId = "KSArmoryMotorPlume";

    private sealed class Live
    {
        public required Celestial Body;
        public required List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> Handles;
    }

    private readonly Dictionary<IProjectile, Live> _burning = [];
    private readonly List<IProjectile> _finished = [];

    private static bool _warned;

    /// <summary>Starts, moves and ends the plume of every round this battery has burning.</summary>
    public void Update(IEffectSource battery)
    {
        if (!battery.PlumesEnabled || battery.Platform is not { } platform)
        {
            ReleaseAll();
            return;
        }

        foreach (IProjectile round in battery.Rounds)
        {
            if (Burning(round)) Follow(round, battery, platform);
            else _finished.Add(round);
        }

        // Anything holding an emitter that the battery has stopped reporting. A round reaped
        // mid-burn never reaches the branch above, and its emitter would never come back.
        foreach (IProjectile round in _burning.Keys)
        {
            if (!battery.Rounds.Contains(round)) _finished.Add(round);
        }

        foreach (IProjectile round in _finished) Release(round);
        _finished.Clear();
    }

    /// <summary>Hands every emitter back. Safe at any time.</summary>
    public void ReleaseAll()
    {
        foreach (Live live in _burning.Values) Give(live);
        _burning.Clear();
    }

    private static bool Burning(IProjectile round)
        => round.State == RoundState.Flying
           && round.Munition.BoostSeconds > 0f
           && round.Age <= round.Munition.BoostSeconds;

    private void Follow(IProjectile round, IEffectSource battery, Vehicle platform)
    {
        if (!_burning.TryGetValue(round, out Live? live))
        {
            if (Acquire(platform) is not { } fresh) return;

            live = fresh;
            _burning[round] = live;
        }

        // Built exactly the way the drawn body is, from the tube anchor and the travel since
        // launch. PlatformEcl + OffsetFromPlatform is the same round measured from the platform's
        // ANALYTIC position, which on a landed craft is metres from where the body is actually
        // placed - enough to swamp the nozzle offset below and leave the flame amidships.
        if (battery.Launcher is not { } launcher) return;

        if (!LauncherPart.TryGetBodyEcl(platform, launcher, round.LaunchAnchorPartFrame,
                                        round.TravelSinceLaunch, battery.PlatformEcl,
                                        out double3 bodyEcl))
        {
            return;
        }

        // At the nozzle, not at the round's centre. A body mesh is modelled about its middle, so
        // anchoring the flame there puts it half a missile too far forward: 1.5 m on the AIM-9J,
        // which reads as the rocket burning from its nose.
        double3 along = Vec.Unit(round.VelocityLocal);
        double3 ecl = Vec.Len2(along) > 0.5
                          ? bodyEcl - (along * (round.Munition.BodyLength * 0.5))
                          : bodyEcl;

        double3 centre = live.Body.GetPositionEcl();

        double3 positionCcf = (ecl - centre).Transform(live.Body.GetCce2Ccf());
        if (!Vec.IsFinite(positionCcf)) return;

        // The round's own flight, not its ecliptic velocity: with InheritVelocity the particles
        // are launched with this, so the 29.8 km/s would throw every one of them off the map.
        double3 velocityCcf = round.VelocityLocal.Transform(live.Body.GetCce2Ccf());

        var origin = new BubbleOrigin
        {
            Time = Universe.GetElapsedSimTime(),
            Parent = live.Body,
            BubFrame = BubbleFrame.Ccf,
            PositionBub = positionCcf,
            VelocityBub = Vec.IsFinite(velocityCcf) ? velocityCcf : double3.Zero,
        };

        foreach (var handle in live.Handles)
        {
            if (handle.TryGet() is not { } emitter) continue;

            emitter.Origin = origin;
        }
    }

    private static Live? Acquire(Vehicle platform)
    {
        try
        {
            if (platform.Parent is not Celestial body) return null;

            if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(PlumeId, out var handles)
                || handles is null || handles.Count == 0)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn($"no free emitters for '{PlumeId}'; rounds will fly without a plume");
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

            return new Live { Body = body, Handles = [.. handles] };
        }
        catch (Exception e)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"motor plume failed to start: {e.Message}");
            }
            return null;
        }
    }

    private void Release(IProjectile round)
    {
        if (!_burning.Remove(round, out Live? live)) return;

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
            catch { /* Already gone, which is where we wanted it. */ }
        }
    }
}
