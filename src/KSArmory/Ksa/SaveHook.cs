using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Which save is open, and that the game just wrote one -- what the settings kept in a save are
/// written into and read back from.
///
/// <para>KSA keeps no record of the loaded save. <c>GameSaves.Selected</c> is the row highlighted in
/// the save list, which the console's load and save, a scenario's load and a save under a new name
/// never touch, so a session started any of those ways wrote its settings to the user folder or to
/// whatever row was highlighted. <c>UncompressedSave.Load</c> is every load and
/// <c>UncompressedSave.Make</c> every save, overwrite and save-as, and each knows its name.</para>
///
/// <para>Both are public, so <see cref="PinTheSignature"/> tracks them in
/// <c>docs/KSA-API-SURFACE.md</c>. A patch that does not apply leaves the old guess and its
/// timestamp watch in place. Nothing in a postfix may throw: they run inside the engine's save
/// and load.</para>
/// </summary>
internal static class SaveHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.save";

    private static Harmony? _harmony;
    private static bool _saved;

    /// <summary>Whether loads and saves are seen.</summary>
    public static bool Installed { get; private set; }

    /// <summary>The save last loaded or written this session, or null before either.</summary>
    public static string? OpenSaveId { get; private set; }

    /// <summary>True once per save the game wrote, to whoever asks first.</summary>
    public static bool ConsumeSaved()
    {
        if (!_saved) return false;

        _saved = false;
        return true;
    }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? load = AccessTools.Method(typeof(UncompressedSave), nameof(UncompressedSave.Load), Type.EmptyTypes);
            MethodInfo? make = AccessTools.Method(typeof(UncompressedSave), nameof(UncompressedSave.Make), [typeof(string)]);

            if (load is null || make is null)
            {
                Log.Warn("saves are not seen: KSA has no UncompressedSave.Load() or Make(string) to hook -- "
                         + "settings follow the save list's selection");
                return;
            }

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(load, postfix: new HarmonyMethod(typeof(SaveHook).GetMethod(
                               nameof(AfterLoad), BindingFlags.NonPublic | BindingFlags.Static)!));
            _harmony.Patch(make, postfix: new HarmonyMethod(typeof(SaveHook).GetMethod(
                               nameof(AfterMake), BindingFlags.NonPublic | BindingFlags.Static)!));

            Installed = true;
            Log.Info("saves are seen, via UncompressedSave.Load and Make");
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook saving; settings follow the save list's selection ({e.Message})");
        }
    }

    public static void Remove()
    {
        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove the save hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
        OpenSaveId = null;
        _saved = false;
    }

    private static void AfterLoad(UncompressedSave __instance)
    {
        try { OpenSaveId = __instance.Id; }
        catch { }
    }

    private static void AfterMake(GameSave? __result)
    {
        try
        {
            if (__result is null) return;

            OpenSaveId = __result.Id;
            _saved = true;
        }
        catch { }
    }

    // Never called. It puts the patched methods in this assembly's metadata, so a KSA change to
    // either is a build error here rather than settings quietly written somewhere else.
    private static void PinTheSignature(UncompressedSave save)
    {
        save.Load();
        _ = UncompressedSave.Make(string.Empty);
    }
}
