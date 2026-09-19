namespace KSArmory;

/// <summary>
/// A place on a world, as the thing a ballistic missile is aimed at.
///
/// <para>A latitude and a longitude on a named body, never a position. A ballistic flight lasts
/// half an hour, over which an ecliptic coordinate is left behind by 54 million kilometres of the
/// planet's own travel and 830 km of its spin — so the aim point has to be something that can be
/// re-read from the world every cycle rather than a number written down once. That is the same
/// rule <see cref="AimpointKind.Ground"/> exists for, at a thousand times the flight time.</para>
///
/// <para>The body is named rather than held, so a designation survives a save, a reload and a
/// craft that has not been loaded yet. Resolving the name is the KSA side's job.</para>
/// </summary>
internal readonly record struct AimSite(string BodyName, double LatitudeDeg, double LongitudeDeg, string Label)
{
    /// <summary>No designation. What a computer starts with.</summary>
    public static readonly AimSite None = new("", double.NaN, double.NaN, "");

    public bool IsSet => !string.IsNullOrEmpty(BodyName)
                      && double.IsFinite(LatitudeDeg) && double.IsFinite(LongitudeDeg);

    /// <summary>What to call it when nobody has named it.</summary>
    public string Describe()
    {
        if (!IsSet) return "no target";
        if (!string.IsNullOrEmpty(Label)) return Label;

        char ns = LatitudeDeg >= 0.0 ? 'N' : 'S';
        char ew = LongitudeDeg >= 0.0 ? 'E' : 'W';
        return $"{Math.Abs(LatitudeDeg):F3}{ns} {Math.Abs(LongitudeDeg):F3}{ew}";
    }
}
