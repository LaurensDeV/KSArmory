using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Brackets around what the chased round is flying at.
///
/// <para>Without them the target is a pixel until the last few frames, which leaves the ride
/// looking like a camera pointed at empty sky. <see cref="Reticle"/> already floors the box at a
/// minimum size for exactly that reason, so this is only the wiring.</para>
///
/// <para>Drawn as an ImGui overlay rather than with world-space lines: a wireframe drawn in the
/// world shrinks with the target, which is the problem being solved, and the gizmo renderer only
/// runs for the main viewport anyway.</para>
/// </summary>
internal static class ChaseHud
{
    private static readonly ReticleStroke[] _strokes = new ReticleStroke[Reticle.MaxStrokes];

    private static readonly ImColor8 Target = new(255, 90, 60, 235);
    private static readonly ImColor8 Shadow = new(0, 0, 0, 170);

    public static void Draw(ChaseCamera chase)
    {
        if (chase.Round is not { } round) return;
        if (round.TargetRef is not Vehicle target || !KsaWorld.IsAlive(target)) return;

        int viewport = KsaWorld.MainViewportIndex;
        double3 at = KsaWorld.PositionEcl(target);

        if (!KsaWorld.TryProjectIntoViewport(viewport, at, out float2 centre, out _, out int height))
        {
            return;
        }

        double range = Vec.Len(at - round.PositionEcl);
        double angular = 2.0 * Math.Atan2(KsaWorld.MeanRadius(target), Math.Max(range, 1.0));

        float half = Reticle.BoxHalfSize(angular, KsaWorld.ViewportFovRad(viewport), height);

        // Closed brackets: the round is committed and there is nothing here still settling.
        int count = Reticle.Build(centre, half, settled: true, _strokes);
        if (count == 0) return;

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

        if (ImGui.Begin("##KSArmoryChaseHud", flags))
        {
            ImDrawListPtr draw = ImGui.GetWindowDrawList();

            for (int i = 0; i < count; i++)
            {
                // Twice, dark under bright, so it stays readable against sky and terrain alike.
                draw.AddLine(_strokes[i].A + new float2(1f, 1f), _strokes[i].B + new float2(1f, 1f),
                             Shadow, 2.5f);
                draw.AddLine(_strokes[i].A, _strokes[i].B, Target, 1.6f);
            }

            // The number that makes the closing legible: without it the last second reads as the
            // camera moving rather than the round arriving.
            draw.AddText(new float2(centre.X + half + 8f, centre.Y - 6f), Target, $"{range:F0} m");
        }

        ImGui.End();
    }
}
