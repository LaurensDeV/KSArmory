using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Says when the world under the mod has been replaced by a save load.
///
/// <para>Nothing else can tell. StarMap's hook list is <c>BeforeMain</c>, <c>ImmediateLoad</c>,
/// <c>AllModsLoaded</c>, <c>Unload</c>, <c>AfterOnFrame</c>, <c>BeforeGui</c> and <c>AfterGui</c> —
/// no load among them — and <see cref="KsaWorld.InFlightScene"/> stays true right across a load,
/// because <c>Universe.LoadSystem</c> runs once at startup and a save only replaces what is inside
/// the system it built. So the mod never leaves the flight scene and never resets.</para>
///
/// <para><b>What that costs is rounds outliving their world.</b> <c>Universe.DeserializeSave</c>
/// calls <c>DestroyAllVehicles</c>, so every craft the mod holds dies on one frame — and a weapon
/// system with rounds in the air answers a dead platform by going loose, which is right for being
/// shot down mid-engagement and wrong for the world being taken away. The loose systems then step
/// the old scene's rounds into the new one until each expires: measured from play as a Pantsir
/// burst logging shell expiries for five seconds after the reload, ending on
/// <c>last round down, system forgotten</c>.</para>
///
/// <para><c>Program.OnGameLoaded</c> is called from exactly one place, immediately after
/// <c>DeserializeSave</c> has rebuilt the world. Later save or earlier, it fires either way — where
/// watching the universe clock for a rewind only catches a load that goes backwards.</para>
///
/// <para><b>Why this is a weaker thing than patching usually is.</b> The target is
/// <c>public static</c> rather than private, so <see cref="PinTheSignature"/> puts it in this
/// assembly's metadata and <c>tools/api-surface.sh</c> tracks it — a KSA change to it is a build
/// error rather than a silent break. And the fallback degrades: a patch that does not apply says so
/// once, and the cost is one reload's worth of stale rounds rather than anything that stops.</para>
///
/// <para><b>Nothing in the postfix may throw.</b> It runs inside the engine's load path.</para>
/// </summary>
internal static class WorldReloadHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.worldreload";

    private static Harmony? _harmony;

    // Latched rather than acted on here: the load runs deep inside the engine, and tearing the
    // mod's state down from under it is the shape of fault this mod already avoids by queueing
    // part failures instead of applying them. The next step reads and clears it.
    private static bool _pending;

    /// <summary>Whether a save load can be seen at all.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Why it is not installed. Empty when it is, or when nothing has tried.</summary>
    public static string Trouble { get; private set; } = "";

    /// <summary>
    /// True once per save load, to whoever asks first. <b>Consuming</b> — the caller that reads it
    /// owns the reset.
    /// </summary>
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
        // Setting a bool cannot fail, but this runs in the engine's load path where an exception
        // is the game rather than a log line, and the guard costs nothing.
        try { _pending = true; }
        catch { }
    }

    // Never called. It exists so the compiler emits a reference to the patched method, which is
    // what puts Program.OnGameLoaded in docs/KSA-API-SURFACE.md and turns a signature change in KSA
    // into a build error here rather than a reload that silently stops being noticed.
    private static void PinTheSignature() => Program.OnGameLoaded();
}
