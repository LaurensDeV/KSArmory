using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Draws a launcher's parts when the engine has culled it and its rounds are still in the air.
///
/// <para><c>Vehicle.UpdateRenderData</c> skips every part of a craft less than a pixel across from the
/// camera, judged on the craft's own bounding sphere and position. A round's body is a subpart of the
/// launcher, so it goes with it: on a 1440p screen at a 50° field a craft is under a pixel 1.65 km out for
/// every metre it is across, and a chase camera riding a 5"/54 shell 3.7 m behind it lost the shell part-way
/// down a 60 km shot while the mod went on placing it.</para>
///
/// <para>A postfix asking the engine's own question again and drawing exactly when it did not, so the
/// parts are never drawn twice. The rest of a culled launcher is drawn too — at under a pixel, which
/// costs nothing anyone can see.</para>
///
/// <para>The target is <c>public virtual</c>, so <see cref="PinTheSignature"/> puts it in
/// <c>docs/KSA-API-SURFACE.md</c> and a signature change is a build error. A patch that does not apply
/// leaves the engine's cull, which is today's behaviour. <b>Nothing here may throw</b>: it runs inside
/// the engine's render preparation.</para>
/// </summary>
internal static class RoundBodyDrawHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.roundbodies";

    // Whether a craft has rounds of its own in the air. Null while unloaded, which is what stops a stale
    // patch reaching a dead mod.
    private static Func<Vehicle, bool>? _hasRoundsInFlight;

    private static Harmony? _harmony;
    private static bool _complained;
    private static bool _announced;

    /// <summary>Whether a culled launcher's rounds are still drawn.</summary>
    public static bool Installed { get; private set; }

    public static void Install(Func<Vehicle, bool> hasRoundsInFlight)
    {
        _hasRoundsInFlight = hasRoundsInFlight;

        if (Installed) return;

        try
        {
            MethodInfo? target = typeof(Vehicle).GetMethod(
                nameof(Vehicle.UpdateRenderData), [typeof(IViewport), typeof(int)]);

            if (target is null)
            {
                Log.Warn("KSA has no Vehicle.UpdateRenderData(IViewport, int) to hook; a round's body "
                         + "vanishes with its launcher once the launcher is under a pixel across");
                return;
            }

            MethodInfo postfix = typeof(RoundBodyDrawHook).GetMethod(
                nameof(AfterUpdateRenderData), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));

            Installed = true;
            Log.Info("round bodies drawn past their launcher's cull, via Vehicle.UpdateRenderData");
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the round-body draw; a round's body vanishes with its launcher once "
                     + $"the launcher is under a pixel across ({e.Message})");
        }
    }

    public static void Remove()
    {
        _hasRoundsInFlight = null;

        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove the round-body draw hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Parameter names are Harmony's contract with the original: viewport and inFrameIndex.
    private static void AfterUpdateRenderData(Vehicle __instance, IViewport viewport, int inFrameIndex)
    {
        try
        {
            if (_hasRoundsInFlight is not { } hasRounds || viewport.GetCamera() is not { } camera) return;

            // The engine's own test, on the same camera and the same craft: it drew the parts unless this
            // is under a pixel, so drawing them only then never draws them twice.
            double range = camera.GetPositionEgo(__instance).Length();
            double pixels = camera.GetObjectDiameterPixels(2.0 * __instance.MeanRadius, range);
            if (!(pixels < 1.0) || !hasRounds(__instance)) return;

            double4x4 asmb2Ego = __instance.GetMatrixAsmb2Ego(camera);
            __instance.Parts.UpdateRenderData(in asmb2Ego, __instance.IsEditedVehicle, viewport, inFrameIndex);

            if (_announced) return;
            _announced = true;
            Log.Info($"{KsaWorld.DisplayName(__instance)} is {pixels:F2} px across {range / 1000.0:F1} km from "
                     + "the camera, which KSA does not draw; drawing it anyway for the rounds it has in the air");
        }
        catch (Exception e)
        {
            // Stand down rather than throw again next frame from inside the render preparation.
            _hasRoundsInFlight = null;

            if (_complained) return;
            _complained = true;
            Log.Error("the round-body draw hook failed; rounds vanish with a culled launcher again", e);
        }
    }

    // Never called. It puts the patched method in this assembly's metadata, so docs/KSA-API-SURFACE.md
    // tracks it and a KSA signature change is a build error here.
    private static void PinTheSignature(Vehicle vehicle, IViewport viewport, int frameIndex)
        => vehicle.UpdateRenderData(viewport, frameIndex);
}
