using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// KSA's own explosion where a warhead went off.
///
/// <para>Fired through <c>ExplosionSystem.SpawnPreset</c>, which brings the flash, the sound, the
/// volumetric fireball and smoke and their pressure gating with it. <see cref="WarheadExplosion"/>
/// picks the preset, because KSA's size floor swallows every conventional charge.</para>
///
/// <para>Anchored on the <em>celestial</em>, with no vehicle. A proximity burst happens in mid-air,
/// and the obvious host — the target — is the thing about to be destroyed. KSA sets off its own
/// explosion on whatever the burst kills, so this one is the warhead's alone.</para>
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
    /// Sets off the explosion a charge makes, at a point in Ecl. Silently does nothing if it cannot
    /// be placed: this is decoration, and a warhead that killed its target has already done its job.
    /// </summary>
    /// <param name="near">
    /// Any craft close to the burst, used only to find which body it is over. The round's own
    /// target or the firing platform both do.
    /// </param>
    /// <param name="body">
    /// The body the firing system is over, for when no craft near the burst is left to ask.
    /// </param>
    public static void Explode(double3 burstEcl, double chargeKg, Vehicle? near, Celestial? body = null)
    {
        if (WarheadExplosion.PresetFor(chargeKg) is not { } preset) return;

        string why = TryExplode(preset, burstEcl, chargeKg, near, body);
        if (why.Length == 0) return;

        // A refusal is indistinguishable from a wrong frame and a warhead that never went off: all
        // three are "no explosion". Say which, once per reason.
        if (_reported.Add(why)) Log.Warn($"no explosion ({preset}): {why}");
    }

    private static readonly HashSet<string> _reported = [];

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

    // The body to hang the effect on: whatever the craft nearest the burst is bound to, then the
    // body the firing system knows it is over, then the craft being flown's. All are within a
    // physics bubble of the burst, which is the only accuracy this needs. The middle one is a
    // round whose launcher has been destroyed, where the nearest craft is often the one flown.
    /// <summary>Which body to hang an effect on, given something near it.</summary>
    internal static Celestial? BodyFor(Vehicle? near, Celestial? known = null)
    {
        if (KsaWorld.IsAlive(near) && near!.Parent is Celestial body) return body;
        if (known is not null) return known;
        if (KsaWorld.ControlledVehicle?.Parent is Celestial fallback) return fallback;

        return null;
    }
}
