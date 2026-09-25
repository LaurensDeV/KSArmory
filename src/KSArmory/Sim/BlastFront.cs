namespace KSArmory;

/// <summary>
/// One burst's blast front, as everything that follows it asks: the charge, the air it went off in, that
/// air's speed of sound, and how much the ground doubles its energy. Built once per burst, so the drawn
/// front, the ring of dust, the loads it carries to each part and the log about when they arrive cannot
/// disagree about where it is.
///
/// <para>The energy is doubled only as far as the burst is on the ground (<see cref="MushroomCloud.GroundCoupling"/>):
/// the ground reflects the half going down only if it is there to meet it. A burst in free air runs out
/// as a sphere, and what its reflection adds near the ground is <see cref="GroundReflection"/>'s.</para>
/// </summary>
internal readonly record struct BlastFront(double ChargeKg, AmbientAir Air, double SoundMetresPerSecond,
                                           double Reflection)
{
    /// <summary>A surface burst in sea-level air: the front <see cref="MushroomCloud.ShockRadius"/> draws.</summary>
    public static BlastFront SeaLevel(double chargeKg)
        => new(chargeKg, AmbientAir.SeaLevel, BlastWave.SoundMetresPerSecond, BlastWave.SurfaceReflection);

    /// <summary>The front of a burst of a charge (kg), in the air at it, this far over the ground (m).</summary>
    public static BlastFront For(double chargeKg, AmbientAir burstAir, double soundMetresPerSecond, double burstHeight)
        => new(chargeKg, burstAir, soundMetresPerSecond,
               1.0 + MushroomCloud.GroundCoupling(MushroomCloud.KilotonsFor(chargeKg), burstHeight));

    /// <summary>The same front, for a charge that grew when another burst joined it.</summary>
    public BlastFront WithCharge(double chargeKg) => this with { ChargeKg = chargeKg };

    /// <summary>How far it has run (m) at an age (s).</summary>
    public double Radius(double age)
        => BlastAltitude.FrontRadius(ChargeKg, age, Air, SoundMetresPerSecond, Reflection);

    /// <summary>When it reaches a distance (s); never, where the air carries none.</summary>
    public double ArrivalSeconds(double distanceMetres)
        => BlastAltitude.FrontArrivalSeconds(ChargeKg, distanceMetres, Air, SoundMetresPerSecond, Reflection);
}
