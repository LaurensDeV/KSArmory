using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Brackets around what the chased round is flying at, which is otherwise a pixel until the last frames.
/// <para>An ImGui overlay, not world-space lines: those shrink with the target, and the gizmo renderer
/// runs only for the main viewport.</para>
/// </summary>
internal static class ChaseHud
{
    private static readonly ReticleStroke[] _strokes = new ReticleStroke[Reticle.MaxStrokes];

    private static readonly ImColor8 Target = new(255, 90, 60, 235);
    private static readonly ImColor8 Shadow = new(0, 0, 0, 170);

    // From the floor at the moment the view is taken to a good part of the screen at impact. The
    // same root curve the camera closes on, so the two move together.
    private static float BracketHalfSize(double closing, int screenHeight)
    {
        float grown = screenHeight * 0.32f;

        if (!double.IsFinite(closing)) return Reticle.MinBoxHalfSize;

        double t = 1.0 - Math.Sqrt(Math.Clamp(closing, 0.0, 1.0));

        return (float)(Reticle.MinBoxHalfSize + ((grown - Reticle.MinBoxHalfSize) * t));
    }

    public static void Draw(ChaseCamera chase)
    {
        if (chase.Round is not { } round) return;

        // Any aim, a place on the ground included, read off the camera rather than resolved again so
        // the bracket is around the point the camera was aimed at.
        if (chase.Aim is not { } aim) return;

        int viewport = KsaWorld.MainViewportIndex;
        double3 at = aim.PositionEcl;

        if (!KsaWorld.TryProjectIntoViewport(viewport, at, out float2 centre, out _, out int height))
        {
            return;
        }

        double range = Vec.Len(at - round.PositionEcl);

        // Sized by how far through the flight the round is, not by the target's apparent size, so
        // the brackets read as how close it is rather than how big the target is.
        float half = BracketHalfSize(chase.Closing, height);

        // Closed brackets, and no ranging ladder: the range is written beside it in metres, so a
        // scale for judging it by eye is decoration.
        int count = Reticle.Build(centre, half, settled: true, _strokes, ladder: false);
        if (count == 0) return;

        if (ScreenOverlay.Begin("##KSArmoryChaseHud"))
        {
            ImDrawListPtr draw = ImGui.GetWindowDrawList();

            for (int i = 0; i < count; i++)
            {
                // Twice, dark under bright, so it stays readable against sky and terrain alike.
                draw.AddLine(_strokes[i].A + new float2(1f, 1f), _strokes[i].B + new float2(1f, 1f),
                             Shadow, 2.5f);
                draw.AddLine(_strokes[i].A, _strokes[i].B, Target, 1.6f);
            }

            draw.AddText(new float2(centre.X + half + 8f, centre.Y - 6f), Target, $"{range:F0} m");
        }

        ImGui.End();
    }
}
