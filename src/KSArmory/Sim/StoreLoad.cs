namespace KSArmory;

/// <summary>
/// How much of a launcher part's mass has left with its rounds. Worked out from what is still
/// aboard every time, never subtracted at each release: a reload, a refused shot handing its round
/// back and a save reload (which refills the magazine and resets the engine's mass) are then all
/// right with nothing to undo.
/// </summary>
public static class StoreLoad
{
    /// <summary>The mass gone with the rounds fired (kg).</summary>
    public static double ShedKg(double perRoundKg, int full, int remaining)
    {
        if (!(perRoundKg > 0.0) || full <= 0) return 0.0;

        return perRoundKg * Math.Clamp(full - remaining, 0, full);
    }

    /// <summary>
    /// The factor on the part's full mass and inertia that leaves it lighter by
    /// <paramref name="shedKg"/>. A factor rather than a new mass, so the mass stays centred where the
    /// part declares it: the MIRV bus's sits on its thruster ring on purpose. Never below a percent,
    /// so a part declared lighter than its stores is not given no mass at all.
    /// </summary>
    public static double Factor(double fullKg, double shedKg)
    {
        if (!(fullKg > 0.0) || !(shedKg > 0.0)) return 1.0;

        return Math.Clamp((fullKg - shedKg) / fullKg, 0.01, 1.0);
    }
}
