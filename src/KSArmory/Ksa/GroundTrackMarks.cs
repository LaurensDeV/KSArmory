using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// What this mod draws on KSA's ground-track map.
///
/// <para>Today that is a calibration grid and nothing else: a cross at nine round latitudes and
/// longitudes, which must sit on the ground they name at any zoom — 0,-90 on the Galápagos.
/// <b>Not on KSA's drawn grid lines, except unzoomed.</b> That grid is 25 meridians and 13
/// parallels spread evenly across whatever is visible, so zoomed in they fall on arbitrary values;
/// at zoom 1 they happen to land every 15 degrees with every third labelled, which is the only
/// place the two coincide.</para>
///
/// <para>The placement itself is <see cref="GroundTrackWindow.ProjectOnImage"/>, KSA's own
/// function, so a mark cannot disagree with the locations drawn beside it. Only the crop is this
/// mod's arithmetic — see <see cref="MapViewport"/> for why it has to be.</para>
/// </summary>
internal static class GroundTrackMarks
{
    private static readonly ImColor8 Calibration = new(80, 230, 255, 220);
    private static readonly ImColor8 Label = new(80, 230, 255, 160);

    private const float CrossArmPx = 5f;

    // The crossings KSA labels down both edges, and the meridians its grid draws.
    private static readonly double[] Latitudes = [45.0, 0.0, -45.0];
    private static readonly double[] Longitudes = [-90.0, 0.0, 90.0];

    public static void Draw(GroundTrackWindow window, float2 at, float2 size,
                            float2 min, float2 max)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();

        for (int i = 0; i < Latitudes.Length; i++)
        {
            for (int j = 0; j < Longitudes.Length; j++)
            {
                double lat = Latitudes[i];
                double lon = Longitudes[j];

                if (!MapViewport.Shows(MapViewport.ToUv(lon, lat), min, max)) continue;

                float2 point = GroundTrackWindow.ProjectOnImage(at, size, lon, lat, min, max);

                DrawCross(draw, point);
                draw.AddText(new float2(point.X + 7f, point.Y - 6f), Label, $"{lat:F0},{lon:F0}");
            }
        }
    }

    private static void DrawCross(ImDrawListPtr draw, float2 at)
    {
        draw.AddLine(new float2(at.X - CrossArmPx, at.Y), new float2(at.X + CrossArmPx, at.Y),
                     Calibration, 1.5f);
        draw.AddLine(new float2(at.X, at.Y - CrossArmPx), new float2(at.X, at.Y + CrossArmPx),
                     Calibration, 1.5f);
    }
}
