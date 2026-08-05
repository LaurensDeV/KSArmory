using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Point at the world and set off a warhead there — a development tool for looking at the effect
/// without flying an engagement to get one.
///
/// <para>It exists because the three reasons for seeing no explosion look identical in game: the
/// asset never loaded, the burst was placed in the wrong frame, or nothing detonated. This removes
/// the third by making a burst something you can ask for.</para>
/// </summary>
internal sealed class BurstTool
{
    private static readonly float4 MarkerColour = new(1.0f, 0.6f, 0.2f, 0.9f);

    private double3 _lastBurstEcl;
    private bool _haveLast;

    /// <summary>Where the last burst went off, for the panel to report.</summary>
    public bool TryLastBurst(out double3 ecl)
    {
        ecl = _lastBurstEcl;
        return _haveLast;
    }

    public void Update(Config config)
    {
        if (!config.BurstTool) return;

        // A click on the panel is not a click on the world behind it.
        if (ImGui.GetIO().WantCaptureMouse) return;
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)) return;

        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _)) return;

        // Lifted off the surface by the radius, so a burst reads as a ball in the air rather than
        // as something half-buried in the ground it was aimed at.
        double3 up = KsaWorld.ControlledVehicle is { } craft
                         ? KsaWorld.LocalUp(craft)
                         : Vec.Unit(groundEcl);

        double3 at = groundEcl + up * Math.Max(config.BurstScale * 4.0, 2.0);

        _lastBurstEcl = at;
        _haveLast = true;

        Detonation.Show(config.BurstFireball ? Detonation.Fireball : Detonation.Airburst,
                        at, KsaWorld.ControlledVehicle, config.BurstScale);

        Log.Info($"burst tool: {(config.BurstFireball ? "fireball" : "airburst")} "
                 + $"at scale {config.BurstScale:F1}");
    }

    /// <summary>Marks where the next click would put a burst, and how big it would be.</summary>
    public void Draw(Config config)
    {
        if (!config.BurstTool) return;
        if (KsaWorld.ControlledVehicle is not { } anchor) return;
        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _)) return;
        if (!KsaWorld.BeginDraw(anchor, KsaWorld.PositionEcl(anchor))) return;

        KsaWorld.DrawSphereEcl(groundEcl, (float)Math.Max(config.BurstScale * 4.0, 2.0),
                               MarkerColour);
    }
}
