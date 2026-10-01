using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A bar over each part showing what <see cref="PartHealth"/> says is left of it — a debugging aid,
/// on <see cref="Config.DrawPartHealth"/>.
/// </summary>
internal static class PartHealthBars
{
    /// <summary>Every part of a craft this near the camera gets a bar; a damaged one gets one at any range.</summary>
    public const double RangeMetres = 5000.0;

    private const float Width = 28f;
    private const float Height = 4f;

    private static readonly ImColor8 Back = new(0, 0, 0, 160);
    private static readonly ImColor8 Frame = new(255, 255, 255, 90);
    private static readonly ImColor8 Text = new(255, 255, 255, 200);

    public static void Draw()
    {
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

        if (!ImGui.Begin("##KSArmoryPartHealth", flags))
        {
            ImGui.End();
            return;
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        double3 eye = KsaWorld.CameraPositionEcl();

        IReadOnlyList<Vehicle> craft = KsaWorld.Vehicles;
        for (int i = 0; i < craft.Count; i++)
        {
            Vehicle v = craft[i];
            if (!KsaWorld.IsAlive(v)) continue;

            bool near = Vec.Len(KsaWorld.PositionEcl(v) - eye) - KsaWorld.MeanRadius(v) < RangeMetres;

            ReadOnlySpan<Part> parts;
            try
            {
                parts = v.Parts.Parts;
            }
            catch
            {
                continue;
            }

            foreach (Part part in parts)
            {
                double health = PartHealth.World.Of(part);
                if (!near && health >= 1.0) continue;
                if (!KsaWorld.TryPartEgo(v, part, out double3 ego)) continue;
                if (!KsaWorld.TryProjectEgo(ego, out float2 at)) continue;

                Bar(draw, at, health);
            }
        }

        ImGui.End();
    }

    private static void Bar(ImDrawListPtr draw, float2 at, double health)
    {
        float l = at.X - (Width * 0.5f), t = at.Y - Height - 6f;
        float fill = (float)Math.Clamp(health, 0.0, 1.0) * Width;

        // Green whole through yellow to red at nothing.
        byte red = (byte)(255 * Math.Clamp(2.0 * (1.0 - health), 0.0, 1.0));
        byte green = (byte)(255 * Math.Clamp(2.0 * health, 0.0, 1.0));

        draw.AddRectFilled(new float2(l, t), new float2(l + Width, t + Height), Back);
        draw.AddRectFilled(new float2(l, t), new float2(l + fill, t + Height), new ImColor8(red, green, 40, 230));
        draw.AddRect(new float2(l, t), new float2(l + Width, t + Height), Frame);

        if (health < 1.0) draw.AddText(new float2(l + Width + 3f, t - 5f), Text, $"{health:P0}");
    }
}
