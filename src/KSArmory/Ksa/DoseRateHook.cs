using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Adds this mod's bursts to KSA's own dose rate, which is what its Radiation gauge reads. A postfix on
/// <c>Radiation.ComputeDoseRate</c>, called only by that gauge; a patch that does not apply leaves the
/// gauge reading KSA's sources alone.
/// </summary>
internal static class DoseRateHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.doserate";

    private static Harmony? _harmony;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(Radiation), nameof(Radiation.ComputeDoseRate),
                                                    [typeof(IParentBody), typeof(double3), typeof(double3)]);
            if (target is null)
            {
                Log.Warn("radiation: KSA has no Radiation.ComputeDoseRate to hook; its gauge will not show bursts");
                return;
            }

            MethodInfo postfix = typeof(DoseRateHook).GetMethod(nameof(AfterDoseRate),
                                                                BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));

            Installed = true;
            Log.Info("radiation: bursts reach KSA's dose gauge, via Radiation.ComputeDoseRate");
        }
        catch (Exception e)
        {
            Log.Warn($"radiation: could not hook KSA's dose rate; its gauge will not show bursts ({e.Message})");
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
            Log.Warn($"could not remove the dose-rate hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Inside the engine's gauge update: nothing here may throw.
    private static void AfterDoseRate(double3 positionEcl, ref double __result)
    {
        try
        {
            __result += Radioactivity.DoseRateAt(positionEcl);
        }
        catch
        {
        }
    }

    // Never called: puts the patched method in docs/KSA-API-SURFACE.md, so a signature change is a build error.
    private static double PinTheSignature(IParentBody parent)
        => Radiation.ComputeDoseRate(parent, double3.Zero, double3.Zero);
}
