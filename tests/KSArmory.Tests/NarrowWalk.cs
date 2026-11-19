using System.Reflection;

namespace KSArmory.Tests;

/// <summary>
/// The walk as it shipped before the wide walk was turned on, for fixtures whose numbers were taken against it: every
/// wide-walk switch off and the 60 m/s trim budget, except the ones a fixture states itself.
/// </summary>
internal static class NarrowWalk
{
    public static readonly string[] Switches =
    [
        nameof(IcbmConfig.WalkStartsAtCutoff), nameof(IcbmConfig.HopsFlyTheirOwnCeiling),
        nameof(IcbmConfig.PriceSlotsOnTheGateChord), nameof(IcbmConfig.HopsSizeTheirOwnSlots),
        nameof(IcbmConfig.HopsFreeTheirArrival), nameof(IcbmConfig.HopsResolveAfterTheirBurn),
        nameof(IcbmConfig.PriceTheTankStopByStop), nameof(IcbmConfig.FreeHopsMeasureTheirSlots),
        nameof(IcbmConfig.WalkShrinksAfterTheSplit), nameof(IcbmConfig.HopsBurnTheEngine),
        nameof(IcbmConfig.FreeHopsCountTheArrivalTheyMove), nameof(IcbmConfig.EngineHopsClearTheStack),
    ];

    /// <param name="stated">The switches, and <see cref="IcbmConfig.TrimBudgetMetresPerSecond"/>, the fixture set itself.</param>
    public static IcbmConfig Narrowed(this IcbmConfig config, params string[] stated)
    {
        foreach (string name in Switches)
        {
            if (Array.IndexOf(stated, name) >= 0) continue;
            typeof(IcbmConfig).GetField(name, BindingFlags.Public | BindingFlags.Instance)!.SetValue(config, false);
        }

        if (Array.IndexOf(stated, nameof(IcbmConfig.TrimBudgetMetresPerSecond)) < 0)
        {
            config.TrimBudgetMetresPerSecond = PostBoostAim.MaxTrimMetresPerSecond;
        }

        return config;
    }
}
