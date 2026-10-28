using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// Parts burning (<see cref="PartFire"/>): what each fire costs its part on the simulated clock, the
/// part broken off through the engine's failure queue when it runs out, and the flame and dark smoke
/// drawn on it.
///
/// <para>Drawn as a flare is (<see cref="DecoyEffects"/>): a pooled flame re-placed every frame, and a
/// strand of the booster smoke laid from it. <b>On the skin where it was lit</b>, held in the struck
/// sub-part's own frame as a bullet hole is, and thrown out of it: the flame's particles leave with the
/// craft's speed plus a few metres a second outward, so drag and their own buoyancy stream them back and
/// up. Past <see cref="MaxLit"/> a fire keeps its smoke and burns as fast, unseen.</para>
/// </summary>
internal static class PartFires
{
    private const string FlameId = "KSArmoryPartFire";

    /// <summary>Flames drawn at once, the budget the flare cores keep.</summary>
    public const int MaxLit = 12;

    // KSA's own floor for air (PhysicsBubble counts none below it).
    private const double NoAirPascals = 100.0;

    private static readonly float3 SmokeColour = new(0.12f, 0.11f, 0.10f);

    // How fast the flame leaves the hole, against the craft.
    private const double OutOfTheHole = 4.0;

    // Where a fire burns: a point on a sub-part's skin in its own frame, and the way out of the skin there.
    private readonly record struct Anchor(Part SubPart, double3 Local, double3 Outward);

    private static readonly Dictionary<Part, Anchor> _anchors = new(ReferenceEqualityComparer.Instance);

    private sealed class Lit
    {
        public required Celestial Body;
        public required List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> Handles;
    }

    private static readonly Dictionary<Part, Lit> _lit = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Part, PlumeSmoke.Strand> _smoking = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Vehicle, List<Part>> _broken = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Part> _scratch = [];
    private static bool _warned;

    /// <summary>Whether a part could catch: it holds propellant, and there is air at it.</summary>
    public static bool CanBurn(Part part)
    {
        if (!KsaWorld.TryTankOf(part, out Tank? tank) || !KsaWorld.TryTankMassKg(tank, out double kg) || !(kg > 0.0))
            return false;

        return KsaWorld.TryCraftOf(part, out Vehicle? craft) && AirAt(craft) > NoAirPascals;
    }

    /// <summary>
    /// Pins a fire just lit on <paramref name="part"/> to where a ray from the burst meets its skin.
    /// <paramref name="separation"/> is the craft's centre less the burst at the burst's instant, and
    /// <paramref name="towardPart"/> the line from the burst to the part's centre. Without a hit the fire
    /// burns at the part's centre.
    /// </summary>
    public static void Pin(Part part, Vehicle v, double3 separation, double3 towardPart)
    {
        double length = Vec.Len(towardPart);
        if (!(length > 0.0)) return;

        try
        {
            double3 along = towardPart / length;
            double4x4 asmb2Burst = v.GetMatrixAsmb2Ego(separation);
            Ray ray = new() { Origin = double3.Zero, Direction = along };

            if (!part.FullPart.RayCastEgo(in asmb2Burst, ray, out double near, out _, out double3 at, out _,
                                          out _, out _, out Part? struck, out _)
                || struck is null || near < 0.0)
            {
                return;
            }

            double4x4.Invert(struck.MatrixAsmb2Ego(in asmb2Burst), out double4x4 toLocal);
            double3 cameFrom = ray.Origin.Transform(toLocal);
            if (!Vec.IsFinite(at) || !Vec.IsFinite(cameFrom)) return;

            _anchors[part] = new Anchor(struck, at, Vec.Unit(cameFrom - at));
        }
        catch
        {
            // Burns at the part's centre.
        }
    }

    /// <summary>Burns every fire for <paramref name="dt"/> simulated seconds and breaks what runs out.</summary>
    public static void Update(double dt, Config config, bool draw)
    {
        _scratch.Clear();
        foreach (object handle in PartFire.World.Burning) if (handle is Part part) _scratch.Add(part);

        foreach (Part part in _scratch)
        {
            if (!KsaWorld.TryCraftOf(part, out Vehicle? craft)
                || AirAt(craft) <= NoAirPascals
                || !KsaWorld.TryTankOf(part, out Tank? tank)
                || !KsaWorld.TryTankMassKg(tank, out double kg) || !(kg > 0.0))
            {
                PutOut(part);
                continue;
            }

            // Spent by whatever else hit it; that is what breaks it, not the fire.
            if (PartHealth.World.Of(part) <= 0.0)
            {
                PutOut(part);
                continue;
            }

            double cost = PartFire.World.Cost(part, dt);
            if (cost > 0.0 && PartHealth.World.Hit(part, cost, config.DamageScale))
            {
                if (!_broken.TryGetValue(craft, out List<Part>? parts)) _broken[craft] = parts = [];
                parts.Add(part);
                PutOut(part);
                continue;
            }

            if (draw) Draw(part, craft, config);
        }

        if (_broken.Count > 0) Break();
    }

    /// <summary>Puts every fire out and hands its emitters back, for a world being taken away.</summary>
    public static void Clear()
    {
        foreach (Part part in _lit.Keys.ToList()) Release(part);
        _smoking.Clear();
        _anchors.Clear();
        _broken.Clear();
        PartFire.World.Clear();
    }

    private static void Break()
    {
        KsaWorld.WaitForVehicleSolvers();

        foreach ((Vehicle craft, List<Part> parts) in _broken)
        {
            if (!KsaWorld.IsAlive(craft)) continue;

            if (KsaWorld.LosingThatManyPartsIsFatal(parts.Count, KsaWorld.PartCount(craft)))
            {
                Log.Info($"fire destroyed {KsaWorld.DisplayName(craft)}");
                KsaWorld.Destroy(craft, blastSeverity: 50f);
                continue;
            }

            Log.Info($"fire burnt {parts.Count} part(s) off {KsaWorld.DisplayName(craft)}");
            KsaWorld.TryQueuePartFailure(craft, parts);
        }

        _broken.Clear();
    }

    private static void PutOut(Part part)
    {
        PartFire.World.PutOut(part);
        Release(part);
        _smoking.Remove(part);
        _anchors.Remove(part);
    }

    private static void Draw(Part part, Vehicle craft, Config config)
    {
        if (craft.Parent is not Celestial body) return;
        if (!TryFlameEcl(part, craft, out double3 ecl, out double3 outward)) return;
        if (!KsaWorld.TryPartBox(part, out _, out double3 half)) return;

        doubleQuat toCcf = body.GetCce2Ccf();
        double3 positionCcf = (ecl - body.GetPositionEcl()).Transform(toCcf);
        if (!Vec.IsFinite(positionCcf)) return;

        // The flame grows with the part, from a hand-span on a fin to a metre on a 3 m tank.
        double size = Math.Clamp(0.15 * Math.Min(half.X, Math.Min(half.Y, half.Z)), 0.08, 0.5);

        double3 thrown = KsaWorld.VelocityEcl(craft) - KsaWorld.GroundVelocityAt(body, ecl) + (outward * OutOfTheHole);
        double3 thrownCcf = thrown.Transform(toCcf);
        if (!Vec.IsFinite(thrownCcf)) thrownCcf = double3.Zero;

        if (Detonation.ParticlesEnabled && (_lit.ContainsKey(part) || _lit.Count < MaxLit))
        {
            if (!_lit.TryGetValue(part, out Lit? lit) && Acquire(body) is { } fresh) _lit[part] = lit = fresh;

            if (lit is not null)
            {
                var origin = EmitterPool.At(body, positionCcf, thrownCcf);

                // The flame first, then its smoke, which starts three times as wide.
                for (int i = 0; i < lit.Handles.Count; i++)
                {
                    if (lit.Handles[i].TryGet() is not { } emitter) continue;
                    double scale = i == 0 ? 1.0 : 3.0;
                    emitter.Origin = origin;
                    emitter.ParticleInfo.Size = new float2((float)(size * scale), (float)(size * scale * 2.5));
                }
            }
        }

        if (!config.MotorSmoke || !PlumeSmoke.Available) return;

        if (!_smoking.TryGetValue(part, out PlumeSmoke.Strand? strand)) _smoking[part] = strand = new PlumeSmoke.Strand();
        double3 behind = (KsaWorld.GroundVelocityAt(body, ecl) - KsaWorld.VelocityEcl(craft)).Transform(toCcf);
        PlumeSmoke.Lay(strand, body, positionCcf, behind, (float)(size * 2.0), (float)(size * 12.0), 0.6f, SmokeColour);
    }

    // On the skin where it was lit, as drawn, and the way out of the skin there; the part's centre otherwise.
    private static bool TryFlameEcl(Part part, Vehicle craft, out double3 ecl, out double3 outward)
    {
        outward = double3.Zero;

        if (_anchors.TryGetValue(part, out Anchor anchor)
            && KsaWorld.TryDrawnPartPointEcl(anchor.SubPart, anchor.Local, out ecl)
            && KsaWorld.TryDrawnPartPointEcl(anchor.SubPart, anchor.Local + anchor.Outward, out double3 off))
        {
            outward = Vec.Unit(off - ecl);
            return true;
        }

        ecl = default;
        return KsaWorld.TryPartEgo(craft, part, out double3 ego) && KsaWorld.TryEgoToEcl(ego, out ecl);
    }

    private static double AirAt(Vehicle craft)
        => craft.Parent is Celestial body ? KsaWorld.AtmosphericPressureAt(body, KsaWorld.PositionEcl(craft)) : 0.0;

    private static Lit? Acquire(Celestial body)
    {
        try
        {
            if (EmitterPool.Take(FlameId, body) is not { } handles)
            {
                Warn($"no free emitters for '{FlameId}'; fires will burn unseen");
                return null;
            }

            return new Lit { Body = body, Handles = handles };
        }
        catch (Exception e)
        {
            Warn($"a fire could not be drawn: {e.Message}");
            return null;
        }
    }

    private static void Release(Part part)
    {
        if (_lit.Remove(part, out Lit? lit)) EmitterPool.Give(lit.Body, lit.Handles);
    }

    private static void Warn(string message)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn(message);
    }
}
