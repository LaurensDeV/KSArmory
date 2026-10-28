namespace KSArmory;

/// <summary>
/// What a hurt engine can still do, from what is left of its part (<see cref="PartHealth"/>).
///
/// <para>KSA has no partial damage: a part is whole or gone. These are the mod's, written through the
/// engine's own levers -- the chamber pressure a throttle reaches, the gimbal's travel and whether the
/// engine is lit (<c>docs/DAMAGE-PLAN.md</c>, step 2).</para>
/// </summary>
internal static class EngineDamage
{
    /// <summary>At or above this an engine is unhurt as far as anyone flying it can tell.</summary>
    public const double FullAbove = 0.8;

    /// <summary>At or below this an engine stops, and stays stopped.</summary>
    public const double DeadAt = 0.2;

    /// <summary>Below this the gimbal's actuators are gone and it holds where it is centred.</summary>
    public const double GimbalLostBelow = 0.5;

    /// <summary>The share of its chamber pressure an engine can still reach: whole above
    /// <see cref="FullAbove"/>, nothing at <see cref="DeadAt"/>, straight between.</summary>
    public static double ThrustShare(double health)
    {
        if (!double.IsFinite(health) || health >= FullAbove) return 1.0;
        if (health <= DeadAt) return 0.0;

        return (health - DeadAt) / (FullAbove - DeadAt);
    }

    public static bool GimbalLost(double health) => health < GimbalLostBelow;

    public static bool Dead(double health) => health <= DeadAt;
}
