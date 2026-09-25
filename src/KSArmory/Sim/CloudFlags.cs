namespace KSArmory;

/// <summary>
/// The fourth fireball float on a cloud's dispatch, packed: whether the column is spray, whether there
/// are weather clouds to respect, whether it is the frame's first cloud, how dry the air is, and the
/// blast front's radius. <c>KSArmoryCloud.comp</c> decodes exactly this.
///
/// <para>Every term is an integer below 2^24, so the float carries it exactly and a division by 64 is
/// exact too, with no half added to round it: six low bits of flags --
/// 1 spray, 2 no weather, 4 first, and eight times the dryness in eighths up to seven -- and sixty-four
/// times the front's radius in whole metres above them, to <see cref="MostShockMetres"/>. Negated,
/// because a positive value in that float is what makes the shader read a dispatch as a ground mark.</para>
/// </summary>
internal static class CloudFlags
{
    /// <summary>The largest front radius that keeps the float exact (m).</summary>
    public const double MostShockMetres = 262_143.0;

    /// <summary>The dryness steps the flag carries, from none to <see cref="DrySteps"/>.</summary>
    public const int DrySteps = 7;

    /// <summary>The float for one cloud's dispatch. <paramref name="dryness"/> is in [0, 1].</summary>
    public static float Pack(bool water, bool weather, bool first, double shockMetres, double dryness = 0.0)
    {
        double shock = Math.Clamp(Math.Round(shockMetres), 0.0, MostShockMetres);
        double dry = Math.Round(Math.Clamp(double.IsFinite(dryness) ? dryness : 0.0, 0.0, 1.0) * DrySteps);

        return -(float)((water ? 1.0 : 0.0) + (weather ? 0.0 : 2.0) + (first ? 4.0 : 0.0) + (8.0 * dry)
                        + (64.0 * shock));
    }

    /// <summary>The float read back as the shader reads it, for tests.</summary>
    public static (bool Water, bool Weather, bool First, double Dryness, double ShockMetres) Unpack(float packed)
    {
        float flags = -packed;
        float bits = flags % 64.0f;
        float shock = MathF.Floor(flags / 64.0f);

        return (bits % 2.0f > 0.5f, MathF.Floor(bits / 2.0f) % 2.0f < 0.5f, MathF.Floor(bits / 4.0f) % 2.0f > 0.5f,
                MathF.Floor(bits / 8.0f) / DrySteps, shock);
    }
}
