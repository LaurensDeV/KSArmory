namespace KSArmory;

/// <summary>
/// What a burst went off on or in, which decides what it leaves behind.
///
/// <para>Over land it lifts dirt into a brown column and burns the ground. On the sea it lifts
/// water — a white column, and nothing burned, because there is no ground to stain. Deep enough
/// under the water that its fireball never reaches the surface, it raises no mushroom at all:
/// the column is air rising through air, and there is none down there.</para>
/// </summary>
public enum BurstSetting
{
    Land,
    WaterSurface,
    Underwater,
}

public static class BurstSettings
{
    /// <summary>
    /// Which of the three, from heights against the mean sphere.
    ///
    /// <para><b>Anything unreadable is land</b>, which is what every burst was before this: a
    /// height field that will not answer should not be what turns a column white or makes it
    /// vanish.</para>
    ///
    /// <para>Underwater is judged against the fireball's own radius rather than any depth at all,
    /// because what decides whether a burst makes a column is whether the ball breaks the surface,
    /// and a store stopped at the waterline sits exactly on it.</para>
    /// </summary>
    public static BurstSetting Classify(double burstHeight, double terrainHeight, double seaLevel,
                                        bool hasSea, double fireballRadius)
    {
        if (!hasSea || !double.IsFinite(terrainHeight) || !double.IsFinite(seaLevel)) return BurstSetting.Land;
        if (terrainHeight >= seaLevel) return BurstSetting.Land;
        if (!double.IsFinite(burstHeight)) return BurstSetting.Land;

        return burstHeight < seaLevel - Math.Max(fireballRadius, 0.0)
                   ? BurstSetting.Underwater
                   : BurstSetting.WaterSurface;
    }
}
