namespace KSArmory;

/// <summary>
/// Which of KSA's own explosions a warhead goes off as, and with how much energy.
///
/// <para><b>The size is the preset, not the energy.</b> KSA sizes an explosion by the cube root of
/// its energy over <see cref="KsaReferenceJoules"/>, floored at <see cref="KsaFloorIntensity"/>, and
/// every conventional warhead this mod carries sits under that floor — so handed its energy alone,
/// a 20 mm shell and an anti-radiation missile would go off the same size. The energy still matters
/// above the floor, which is where a nuclear charge grows the conflagration.</para>
/// </summary>
public static class WarheadExplosion
{
    /// <summary>A kilogram of TNT, in joules — what <see cref="MunitionProfile.ChargeKg"/> is measured in.</summary>
    public const double JoulesPerKg = 4.184e6;

    /// <summary>KSA's <c>ExplosionSystem.ReferenceExplosionEnergyJ</c>: the energy a preset is authored at.</summary>
    public const double KsaReferenceJoules = 5.0e10;

    /// <summary>KSA's <c>ExplosionIntensity</c> floor, as a fraction of the reference energy.</summary>
    public const double KsaFloorIntensity = 0.05;

    /// <summary>Sparks, a little smoke and debris, and no fireball.</summary>
    public const string Pop = "PopSmallExplosion";

    /// <summary>A small fireball with a flash, sparks, embers and a shockwave.</summary>
    public const string SmallFire = "SmallFire";

    /// <summary>The large fireball, debris, standing smoke and the big sound.</summary>
    public const string Conflagration = "Explosion_Conflagration";

    /// <summary>
    /// This mod's own, for a charge that grows a mushroom cloud: Core's conflagration without its
    /// smoke volume, because <see cref="MushroomCloud"/> is already drawing that air at a thousand
    /// times the size and Core's is a pale blue lump under it.
    /// </summary>
    public const string NuclearBurst = "KSArmoryNuclearBurst";

    /// <summary>Every preset a warhead can go off as, smallest first.</summary>
    public static readonly string[] Presets = [Pop, SmallFire, Conflagration, NuclearBurst];

    // The fireball volume's peak radius in Core's ExplosionAssets.xml, at the reference energy, and
    // the conflagration stage's own intensity multiplier. PopSmallExplosion has no fireball.
    private const double SmallFireFireballMetres = 10.0;
    private const double ConflagrationFireballMetres = 56.0;
    private const double ConflagrationFireballIntensity = 1.2;

    /// <summary>
    /// The preset a charge goes off as, or null for a round that carries none.
    ///
    /// <para>Chosen by the warhead's own <see cref="Warhead.FireballRadius"/> against each preset's
    /// fireball at KSA's floor, nearest on a log scale, and a pop below half of SmallFire's. That
    /// puts the change-overs at about 0.36 kg and 41 kg.</para>
    /// </summary>
    public static string? PresetFor(double chargeKg)
    {
        if (!double.IsFinite(chargeKg) || chargeKg <= 0.0) return null;

        double fireball = Warhead.FireballRadius(chargeKg);
        double smallFire = SmallFireFireballMetres * Math.Cbrt(KsaFloorIntensity);
        double conflagration = ConflagrationFireballMetres
                               * Math.Cbrt(ConflagrationFireballIntensity * KsaFloorIntensity);

        if (fireball < smallFire * 0.5) return Pop;
        if (fireball < Math.Sqrt(smallFire * conflagration)) return SmallFire;

        // Above the threshold this mod draws the smoke itself, so the preset that carries Core's is
        // the wrong one however well it fits the fireball.
        return chargeKg >= MushroomCloud.ThresholdKg ? NuclearBurst : Conflagration;
    }

    /// <summary>The energy KSA is handed for a charge, in joules.</summary>
    public static float Joules(double chargeKg)
        => double.IsFinite(chargeKg) && chargeKg > 0.0 ? (float)(chargeKg * JoulesPerKg) : 0f;
}
