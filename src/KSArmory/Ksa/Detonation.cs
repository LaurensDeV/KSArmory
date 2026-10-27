using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// KSA's own explosion where a warhead went off, through <c>ExplosionSystem.SpawnPreset</c>.
/// <para>Anchored on the celestial with no vehicle: the obvious host, the target, is about to be
/// destroyed, and KSA sets off its own explosion on whatever the burst kills.</para>
/// </summary>
internal static class Detonation
{
    /// <summary>
    /// Whether the game is drawing particles at all. With it off an explosion still flashes and
    /// sounds, and throws no sparks or debris.
    /// </summary>
    public static bool ParticlesEnabled
    {
        get
        {
            try { return GameSettings.Current.Graphics.Particles; }
            catch { return true; }
        }
    }

    /// <summary>
    /// Whether an explosion preset resolves. Checked at load and logged, because KSA drops an
    /// unknown one with a warning in its own log and nothing in this mod's.
    /// </summary>
    public static bool Resolves(string explosionId)
    {
        try
        {
            return ModLibrary.TryGet<ExplosionReference>(explosionId, out var found) && found is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets off the explosion a charge makes, at a point in Ecl. A refusal is logged once per reason
    /// and never thrown: this is decoration.
    /// </summary>
    /// <param name="near">Any craft close to the burst, used only to find which body it is over.</param>
    /// <param name="body">The body the firing system is over, for when no craft near the burst is left.</param>
    /// <param name="named">The munition's own <see cref="MunitionProfile.BurstEffect"/>, if it names one.</param>
    public static void Explode(double3 burstEcl, double chargeKg, Vehicle? near, Celestial? body = null,
                               string? named = null)
    {
        if (Chosen(named, chargeKg) is not { } preset) return;

        string why = TryExplode(preset, burstEcl, chargeKg, near, body);
        if (why.Length == 0) return;

        // In game a refusal looks the same as a wrong frame or no detonation, so say which.
        if (_reported.Add(why)) Log.Warn($"no explosion ({preset}): {why}");
    }

    private static readonly HashSet<string> _reported = [];

    // A named explosion that does not resolve falls back to the charge's own, so a pack's typo costs the
    // look and not the burst; said once, because KSA would otherwise drop it with nothing in this log.
    private static string? Chosen(string? named, double chargeKg)
    {
        if (string.IsNullOrEmpty(named)) return WarheadExplosion.PresetFor(chargeKg);
        if (Resolves(named)) return named;

        if (_reported.Add($"unresolved:{named}"))
        {
            Log.Warn($"explosion '{named}' is not declared by any mod; bursting as the charge's own instead");
        }
        return WarheadExplosion.PresetFor(chargeKg);
    }

    // Once per preset, so a salvo does not bury the engagement it belongs to.
    private static readonly HashSet<string> _described = [];

    // Empty on success, otherwise why not.
    private static string TryExplode(string preset, double3 burstEcl, double chargeKg, Vehicle? near,
                                     Celestial? known)
    {
        if (!Vec.IsFinite(burstEcl)) return "burst position is not finite";

        try
        {
            if (BodyFor(near, known) is not { } body) return "no celestial to hang it on";

            double3 positionCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(positionCcf)) return "burst position does not convert to body-fixed";

            // At rest in the body's frame, so the smoke stands over the ground where the round burst
            // rather than flying on with the target it burst beside.
            ExplosionContext context = new()
            {
                Anchor = new BubbleOrigin
                {
                    Time = Universe.GetElapsedTime(),
                    Parent = body,
                    BubFrame = BubbleFrame.Ccf,
                    PositionBub = positionCcf,
                    VelocityBub = double3.Zero,
                },
                IntensityJ = WarheadExplosion.Joules(chargeKg),
                AmbientPressurePa = KsaWorld.AtmosphericPressureAt(body, burstEcl),
            };

            ExplosionSystem.SpawnPreset(preset, in context);

            if (_described.Add(preset))
            {
                Log.Info($"explosion {preset} for {chargeKg:G3} kg at {context.AmbientPressurePa:F0} Pa "
                         + $"on {body.Id}");
            }

            return string.Empty;
        }
        catch (Exception e)
        {
            return $"{e.GetType().Name}: {e.Message}";
        }
    }

    /// <summary>
    /// The near craft's body, else the firing system's, else the flown craft's: all within a physics
    /// bubble of the burst, which is the only accuracy an effect needs.
    /// </summary>
    internal static Celestial? BodyFor(Vehicle? near, Celestial? known = null)
    {
        if (KsaWorld.IsAlive(near) && near!.Parent is Celestial body) return body;
        if (known is not null) return known;
        if (KsaWorld.ControlledVehicle?.Parent is Celestial fallback) return fallback;

        return null;
    }
}
