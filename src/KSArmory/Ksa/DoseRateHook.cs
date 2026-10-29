using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Adds this mod's bursts to KSA's own dose rates, which its Radiation gauge and its crew dose both read.
/// A postfix on <c>Radiation.ComputeDoseRates</c>; a patch that does not apply leaves both on KSA's sources alone.
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
            MethodInfo? target = AccessTools.Method(typeof(Radiation), nameof(Radiation.ComputeDoseRates),
                                                    [typeof(IParentBody), typeof(double3), typeof(double3), typeof(double)]);
            if (target is null)
            {
                Log.Warn("radiation: KSA has no Radiation.ComputeDoseRates to hook; its gauge and crew will not feel bursts");
                return;
            }

            MethodInfo postfix = typeof(DoseRateHook).GetMethod(nameof(AfterDoseRates),
                                                                BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));

            Installed = true;
            Log.Info("radiation: bursts reach KSA's dose gauge and crew, via Radiation.ComputeDoseRates");

            InstallBeltSplit(_harmony);
        }
        catch (Exception e)
        {
            Log.Warn($"radiation: could not hook KSA's dose rate; its gauge and crew will not feel bursts ({e.Message})");
        }
    }

    // KSA storm-scales whichever belt has the largest L, so a burst's belt above the natural outer one
    // would take that belt's storm. The original sees only the natural belts, and ours are added after.
    private static void InstallBeltSplit(Harmony harmony)
    {
        try
        {
            MethodInfo? target = AccessTools.Method(typeof(Radiation), nameof(Radiation.ComputeBeltDoseRate),
                                                    [typeof(List<RadiationBeltReference>), typeof(double),
                                                     typeof(double), typeof(double), typeof(double)]);
            if (target is null)
            {
                Log.Warn("radiation: KSA has no Radiation.ComputeBeltDoseRate to hook; a burst's belt above the "
                         + "natural ones takes their storm scaling");
                return;
            }

            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
            harmony.Patch(target,
                          prefix: new HarmonyMethod(typeof(DoseRateHook).GetMethod(nameof(BeforeBelts), Private)!),
                          postfix: new HarmonyMethod(typeof(DoseRateHook).GetMethod(nameof(AfterBelts), Private)!));
            Log.Info("radiation: storms scale a body's own outer belt, not a burst's, via Radiation.ComputeBeltDoseRate");
        }
        catch (Exception e)
        {
            Log.Warn($"radiation: could not split a burst's belts from the natural ones ({e.Message})");
        }
    }

    [ThreadStatic] private static bool _ownBelts;

    private static void BeforeBelts(ref List<RadiationBeltReference> belts, ref List<RadiationBeltReference>? __state)
    {
        __state = null;
        try
        {
            if (_ownBelts || !Radioactivity.TrySplitBelts(belts, out List<RadiationBeltReference>? natural,
                                                          out List<RadiationBeltReference>? ours)) return;

            belts = natural;
            __state = ours;
        }
        catch
        {
        }
    }

    // A burst's belts on their own, at a storm scale of one: alone, the largest L is theirs.
    private static void AfterBelts(ref BeltDoseRate __result, double l, double sinLatitude, double mirrorAltitude,
                                   List<RadiationBeltReference>? __state)
    {
        if (__state is null) return;

        _ownBelts = true;
        try
        {
            __result += Radiation.ComputeBeltDoseRate(__state, l, sinLatitude, mirrorAltitude, 1.0);
        }
        catch
        {
        }
        finally
        {
            _ownBelts = false;
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

    // Inside the engine's gauge and crew updates: nothing here may throw. Gamma goes in the cosmic share,
    // the most penetrating one KSA shields: a hull passes about 70% of it.
    private static void AfterDoseRates(double3 positionEcl, ref DoseRates __result)
    {
        try
        {
            double burst = Radioactivity.DoseRateAt(positionEcl);
            if (burst > 0.0) __result = __result with { Cosmic = __result.Cosmic + new SievertsPerSecond(burst) };
        }
        catch
        {
        }
    }

    // Never called: puts the patched method in docs/KSA-API-SURFACE.md, so a signature change is a build error.
    private static DoseRates PinTheSignature(IParentBody parent)
        => Radiation.ComputeDoseRates(parent, double3.Zero, double3.Zero, double.NaN);
}
