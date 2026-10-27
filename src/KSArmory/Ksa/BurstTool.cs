using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Click the world to set off a warhead there, to look at the effect without flying an engagement.
/// </summary>
internal sealed class BurstTool
{
    private static readonly float4 MarkerColour = new(1.0f, 0.6f, 0.2f, 0.9f);

    // Simulated, because what it is waiting out is the flash, which is on that clock too. Wall
    // clock would uncover the marker mid-explosion under slow motion and never under warp.
    private double _markerHiddenUntil = double.NegativeInfinity;

    public void Update(Config config)
    {
        if (!config.BurstTool) return;

        // A click on the panel is not a click on the world behind it.
        if (ImGui.GetIO().WantCaptureMouse) return;
        // Shift-click designates; without this it would also set off a burst.
        if (ImGui.GetIO().KeyShift) return;

        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)) return;

        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _)) return;

        // Lifted off the surface by the radius, so a burst reads as a ball in the air rather than
        // as something half-buried in the ground it was aimed at.
        double3 up = KsaWorld.ControlledVehicle is { } craft
                         ? KsaWorld.LocalUp(craft)
                         : Vec.Unit(groundEcl);

        double chargeKg = ChargeOf(config);

        // The nuclear fireball for a nuclear burst: the chemical law is a cube root, three times off at
        // these charges, and lifting a surface burst by it would make it an air burst.
        double radius = config.BurstNuclear
                            ? MushroomCloud.PeakFireballRadius(config.BurstYieldKt, config.BurstHeightMetres)
                            : Warhead.FireballRadius(chargeKg);

        // An air burst goes off where it was asked to; one lower than its own fireball is lifted
        // clear of the ground as a surface burst is.
        double height = config.BurstNuclear ? Math.Max(config.BurstHeightMetres, 0.0) : 0.0;
        double3 burst = groundEcl + (up * height);
        double3 at = groundEcl + (up * Math.Max(Math.Max(radius, 2.0), height));

        Detonation.Explode(at, chargeKg, KsaWorld.ControlledVehicle);

        // The burst point, not the lifted one: the cloud is built as offsets from what it is given, so
        // the lifted point would float it by a fireball radius. NuclearClouds decides whether there is one.
        NuclearClouds.Begin(burst, KsaWorld.ControlledVehicle, chargeKg);

        // The marker would hide the burst under the cursor, so it is held back for the flash.
        _markerHiddenUntil = Universe.GetElapsedTime().Seconds()
                             + MushroomCloud.FlashSeconds(MushroomCloud.KilotonsFor(chargeKg));

        Log.Info($"burst tool: {WarheadExplosion.PresetFor(chargeKg) ?? "no explosion"}, "
                 + (config.BurstNuclear
                        ? $"{config.BurstYieldKt:F2} kt at {height:F0} m"
                        : $"{chargeKg:F2} kg")
                 + $", lethal {Warhead.LethalRadius(chargeKg):F0} m");
    }

    /// <summary>Marks where the next click would put a burst, and how big it would be.</summary>
    public void Draw(Config config)
    {
        if (!config.BurstTool) return;
        if (!config.BurstMarker) return;
        if (Universe.GetElapsedTime().Seconds() < _markerHiddenUntil) return;
        if (KsaWorld.ControlledVehicle is not { } anchor) return;
        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _)) return;
        if (!KsaWorld.BeginDraw(anchor, KsaWorld.PositionEcl(anchor))) return;

        // The lethal radius, not the fireball: the marker is there to say what the burst would
        // destroy, and those are very different numbers.
        KsaWorld.DrawSphereEcl(groundEcl, (float)Warhead.LethalRadius(ChargeOf(config)),
                               MarkerColour);
    }

    /// <summary>What the next click would set off, in kg, whichever unit the panel is dialling.</summary>
    public static double ChargeOf(Config config)
        => config.BurstNuclear ? config.BurstYieldKt * Charge.KgPerKiloton : config.BurstChargeKg;
}
