using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Says when a save load has replaced the world, which nothing else can tell: StarMap has no load
/// hook and <see cref="KsaWorld.InFlightScene"/> stays true across one. A postfix on
/// <c>Program.OnGameLoaded</c>, called once right after <c>DeserializeSave</c> rebuilds the world.
/// The reasoning is in CLAUDE.md, "Design decisions worth not re-litigating".
/// <para><b>Nothing in the postfix may throw.</b> It runs inside the engine's load path.</para>
/// </summary>
internal static class WorldReloadHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.worldreload";

    private static Harmony? _harmony;

    // Latched rather than acted on: the load runs deep inside the engine. The next step clears it.
    private static bool _pending;

    /// <summary>Whether a save load can be seen at all.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Why it is not installed. Empty when it is, or when nothing has tried.</summary>
    public static string Trouble { get; private set; } = "";

    /// <summary>True once per save load, to whoever asks first; that caller owns the reset.</summary>
    public static bool ConsumeReload()
    {
        if (!_pending) return false;

        _pending = false;
        return true;
    }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(Program), nameof(Program.OnGameLoaded),
                                                    Type.EmptyTypes);

            if (target is null)
            {
                Trouble = "KSA has no Program.OnGameLoaded() to hook";
                Log.Warn($"save loads cannot be seen: {Trouble} -- rounds in the air will outlive "
                         + "a reload");
                return;
            }

            MethodInfo postfix = typeof(WorldReloadHook).GetMethod(
                nameof(AfterGameLoaded), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));

            Installed = true;
            Trouble = "";
            Log.Info("save loads are seen, via Program.OnGameLoaded");
        }
        catch (Exception e)
        {
            Trouble = e.Message;
            Log.Warn($"could not hook save loading; rounds in the air will outlive a reload "
                     + $"({e.Message})");
        }
    }

    public static void Remove()
    {
        _pending = false;

        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove the world-reload hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    private static void AfterGameLoaded()
    {
        try { _pending = true; }
        catch { }
    }

    // Never called: the reference puts Program.OnGameLoaded in docs/KSA-API-SURFACE.md, so a KSA
    // signature change is a build error here.
    private static void PinTheSignature() => Program.OnGameLoaded();
}
