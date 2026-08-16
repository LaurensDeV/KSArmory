using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Point at the world and set off a warhead there — a development tool for looking at the effect
/// without flying an engagement to get one.
///
/// <para>It exists because the three reasons for seeing no explosion look identical in game: the
/// asset never loaded, the burst was placed in the wrong frame, or nothing detonated. This removes
/// the third by making a burst something that can be asked for on demand.</para>
/// </summary>
internal sealed class BurstTool
{
    private static readonly float4 MarkerColour = new(1.0f, 0.6f, 0.2f, 0.9f);

    public void Update(Config config)
    {
        if (!config.BurstTool) return;

        // A click on the panel is not a click on the world behind it.
        if (ImGui.GetIO().WantCaptureMouse) return;
        // Shift is the designate gesture, so a shift-click is not a click on the world.
        // Without this, locking a target while this tool is on also sends a round at it.
        if (ImGui.GetIO().KeyShift) return;

        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)) return;

        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _)) return;

        // Lifted off the surface by the radius, so a burst reads as a ball in the air rather than
        // as something half-buried in the ground it was aimed at.
        double3 up = KsaWorld.ControlledVehicle is { } craft
                         ? KsaWorld.LocalUp(craft)
                         : Vec.Unit(groundEcl);

        double chargeKg = ChargeOf(config);

        // The nuclear fireball rather than the chemical one, for a nuclear burst. They disagree by
        // a factor of three at these charges -- the chemical law is a cube root and this is not --
        // and lifting a surface burst by the wrong one makes it an air burst, which is a different
        // weapon: the whole cloud model below assumes the fireball is touching the ground.
        double radius = config.BurstNuclear
                            ? MushroomCloud.PeakFireballRadius(config.BurstYieldKt)
                            : Warhead.FireballRadius(chargeKg);

        double3 at = groundEcl + (up * Math.Max(radius, 2.0));

        Detonation.Show(config.BurstFireball ? Detonation.Fireball : Detonation.Airburst,
                        at, KsaWorld.ControlledVehicle,
                        (float)Warhead.EffectScale(chargeKg));

        // From the GROUND point, not the lifted one. The lift above exists so the ball is not drawn
        // half-buried, and is right for the ball -- but the cloud is built as offsets from whatever
        // it is given, so handing it the lifted point floats the stem, the skirt and the cap by a
        // whole fireball radius. That is 55 m at 0.3 kt and about 900 m at 340 kt, which reads as a
        // mushroom hanging in the air over the crater. The real weapon path passes its true burst
        // point and has never had this.
        //
        // Unconditional: NuclearClouds decides for itself whether a charge is large enough to have
        // made a cloud, so the tool does not need to know and cannot disagree with the real path.
        NuclearClouds.Begin(groundEcl, KsaWorld.ControlledVehicle, chargeKg);

        Log.Info($"burst tool: {(config.BurstFireball ? "fireball" : "airburst")}, "
                 + (config.BurstNuclear
                        ? $"{config.BurstYieldKt:F2} kt"
                        : $"{chargeKg:F2} kg")
                 + $", lethal {Warhead.LethalRadius(chargeKg):F0} m");
    }

    /// <summary>Marks where the next click would put a burst, and how big it would be.</summary>
    public void Draw(Config config)
    {
        if (!config.BurstTool) return;
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
        => config.BurstNuclear ? config.BurstYieldKt * 1.0e6 : config.BurstChargeKg;
}
