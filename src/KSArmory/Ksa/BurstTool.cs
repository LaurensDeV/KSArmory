using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

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

    // Simulated, because what it is waiting out is the flash, which is on that clock too. Wall
    // clock would uncover the marker mid-explosion under slow motion and never under warp.
    private double _markerHiddenUntil = double.NegativeInfinity;

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
                            ? MushroomCloud.PeakFireballRadius(config.BurstYieldKt, config.BurstHeightMetres)
                            : Warhead.FireballRadius(chargeKg);

        // An air burst goes off where it was asked to; one lower than its own fireball is lifted
        // clear of the ground as a surface burst is.
        double height = config.BurstNuclear ? Math.Max(config.BurstHeightMetres, 0.0) : 0.0;
        double3 burst = groundEcl + (up * height);
        double3 at = groundEcl + (up * Math.Max(Math.Max(radius, 2.0), height));

        Detonation.Explode(at, chargeKg, KsaWorld.ControlledVehicle);

        // From the GROUND point, not the lifted one. The lift above exists so the ball is not drawn
        // half-buried, and is right for the ball -- but the cloud is built as offsets from whatever
        // it is given, so handing it the lifted point floats the stem, the skirt and the cap by a
        // whole fireball radius. That is 55 m at 0.3 kt and about 900 m at 340 kt, which reads as a
        // mushroom hanging in the air over the crater. The real weapon path passes its true burst
        // point and has never had this.
        //
        // Unconditional: NuclearClouds decides for itself whether a charge is large enough to have
        // made a cloud, so the tool does not need to know and cannot disagree with the real path.
        // An air burst's own point, since the cloud measures its height from it.
        NuclearClouds.Begin(burst, KsaWorld.ControlledVehicle, chargeKg);

        // The marker is drawn at the cursor and the burst happens at the cursor, so the one hides
        // the other. Sized off the flash rather than fixed: that is how long there is something to
        // look at, and it keeps a conventional charge's marker back for almost no time at all.
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
        => config.BurstNuclear ? config.BurstYieldKt * 1.0e6 : config.BurstChargeKg;
}
