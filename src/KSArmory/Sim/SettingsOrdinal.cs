namespace KSArmory;

/// <summary>
/// The number a craft's weapon is filed under in its settings: its ordinal among the launchers, plus the
/// switched-off family's launchers before it on the craft. Switching a family off then hands no weapon
/// the settings of the one before it, and with nothing switched off it is the ordinal unchanged.
/// </summary>
public static class SettingsOrdinal
{
    public static int Of(int ordinal, IReadOnlyList<string> partIds, int partIndex, Func<string, bool> isSwitchedOff)
    {
        int filed = ordinal;
        for (int i = 0; i < partIndex && i < partIds.Count; i++)
        {
            if (isSwitchedOff(partIds[i])) filed++;
        }

        return filed;
    }
}
