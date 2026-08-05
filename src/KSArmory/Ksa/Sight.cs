using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Paints the gunner's sight over the camera window the optical head is driving.
///
/// <para>An ImGui overlay rather than gizmos: gizmos are drawn in the world and would sit *in*
/// the scene at the target's distance, scaling and occluding with it. A sight is on the glass.</para>
/// </summary>
internal static class Sight
{
    private static readonly ImColor8 Reticle = new(90, 255, 120, 235);
    private static readonly ImColor8 Pending = new(255, 200, 60, 200);
    private static readonly ImColor8 Shadow = new(0, 0, 0, 140);

    private static readonly ReticleStroke[] _strokes = new ReticleStroke[KSArmory.Reticle.MaxStrokes];

    public static void Draw(DefenceBattery battery, Config config, BatteryConfig policy)
    {
        if (policy.OpticViewport < 0 || battery.OpticPart is null) return;
        if (battery.Radar.Locked is not { } track) return;

        if (!KsaWorld.TryProjectIntoViewport(policy.OpticViewport, track.PositionEcl,
                                             out float2 centre, out int width, out int height))
        {
            return;
        }

        double angular = 2.0 * Math.Atan2(KsaWorld.MeanRadius(track.Vehicle),
                                          Math.Max(track.Range, 1.0));
        float half = KSArmory.Reticle.BoxHalfSize(angular, KsaWorld.ViewportFovRad(policy.OpticViewport),
                                                  height);

        // Settled means the head is actually on it, not merely that the radar has a lock — the
        // brackets closing is the operator's cue that the sight has caught up.
        bool settled = battery.OpticOnTarget;
        int count = KSArmory.Reticle.Build(centre, half, settled, _strokes);
        if (count == 0) return;

        // A transparent, click-through window over the whole screen. The strokes carry absolute
        // screen coordinates, and ImGui clips a draw list to its own window.
        ImGuiViewportPtr main = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(main.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(main.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
                                       | ImGuiWindowFlags.NoInputs
                                       | ImGuiWindowFlags.NoNav
                                       | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.NoBringToFrontOnFocus
                                       | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoBackground;

        if (ImGui.Begin("##KSArmorySight", flags))
        {
            ImDrawListPtr draw = ImGui.GetWindowDrawList();
            ImColor8 colour = settled ? Reticle : Pending;

            for (int i = 0; i < count; i++)
            {
                // Drawn twice: a dark stroke under a bright one, so the sight stays readable
                // against both sky and terrain without a panel behind it.
                draw.AddLine(_strokes[i].A + new float2(1f, 1f), _strokes[i].B + new float2(1f, 1f),
                             Shadow, 2.5f);
                draw.AddLine(_strokes[i].A, _strokes[i].B, colour, 1.6f);
            }

            string label = $"{track.Range / 1000.0:F2} km   {track.ClosingSpeed:F0} m/s";
            float2 at = new(centre.X - half, centre.Y + half + 6f);
            draw.AddText(at + new float2(1f, 1f), Shadow, label);
            draw.AddText(at, colour, label);

            if (!settled)
            {
                float2 slew = new(centre.X - half, centre.Y - half - 18f);
                draw.AddText(slew + new float2(1f, 1f), Shadow, "SLEWING");
                draw.AddText(slew, colour, "SLEWING");
            }
        }
        ImGui.End();

        _ = width;
    }
}
