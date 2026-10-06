namespace KSArmory;

/// <summary>
/// Which channel a director's camera window shows. A targeting pod carries a daylight TV camera
/// and a thermal one, and the thermal one is read white-hot or black-hot.
/// </summary>
public enum SensorMode
{
    Colour,
    Tv,
    WhiteHot,
    BlackHot,
}

public static class SensorModes
{
    /// <summary>The next mode round the cycle, the order a pod's mode button steps.</summary>
    public static SensorMode Next(SensorMode mode) => mode switch
    {
        SensorMode.Tv => SensorMode.WhiteHot,
        SensorMode.WhiteHot => SensorMode.BlackHot,
        SensorMode.BlackHot => SensorMode.Colour,
        _ => SensorMode.Tv,
    };

    /// <summary>What a pod's display calls it.</summary>
    public static string Label(SensorMode mode) => mode switch
    {
        SensorMode.Tv => "TV",
        SensorMode.WhiteHot => "WHOT",
        SensorMode.BlackHot => "BHOT",
        _ => "CLR",
    };
}
