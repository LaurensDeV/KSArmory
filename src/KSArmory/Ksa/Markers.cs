using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A bracket on every weapons system, labelled with its name, armament and range when the pointer
/// is on it or when it has been pinned from the panel.
///
/// <para>Modelled on the game's own object markers, so a site reads the way a mountain or a
/// vessel already does. An ImGui overlay rather than gizmos: a marker belongs on the glass, at a
/// constant size, rather than in the scene where it would shrink with distance and be occluded by
/// the very terrain it is meant to find something behind.</para>
/// </summary>
internal static class Markers
{
    private static readonly ImColor8 Idle = new(150, 200, 255, 150);
    private static readonly ImColor8 Active = new(90, 255, 120, 220);

    // Behind the planet. Dimmed rather than hidden: where a system is stays worth knowing when
    // you cannot see it -- that is most of what the marker is for -- but it must not read as
    // something you could look at.
    private static readonly ImColor8 Hidden = new(150, 200, 255, 70);
    private static readonly ImColor8 HiddenActive = new(90, 255, 120, 90);
    private static readonly ImColor8 Label = new(235, 240, 245, 255);
    private static readonly ImColor8 Panel = new(18, 20, 24, 225);
    private static readonly ImColor8 PanelEdge = new(90, 100, 115, 200);

    // Half-width of the bracket, in pixels. Constant: this is an icon, not a bounding box, and
    // sizing it to the craft would make a distant site a sub-pixel dot -- which is the one case
    // it exists for.
    private const float Half = 11f;
    private const float Corner = 4f;

    // Pointer distance, in pixels, that counts as hovering a marker.
    private const float HoverRadius = 18f;

    // How long a label stays up after being called for, and how much of that it spends fading.
    // Long enough to read and find the thing, short enough that pressing it again is easier than
    // remembering to switch it off.
    private const double ShowSeconds = 10.0;
    private const double FadeSeconds = 2.0;

    // Systems whose label is showing, and for how much longer. Reference identity, which is what
    // a Vehicle compares by -- two craft are never equal, so a stale entry cannot shadow a live
    // one. Pruned in Draw as they expire or the craft is destroyed.
    private static readonly Dictionary<Vehicle, double> Showing = [];
    private static readonly List<Vehicle> Expired = [];

    /// <summary>Puts a system's label up for a while. Pressing again restarts the clock.</summary>
    public static void Show(Vehicle craft) => Showing[craft] = ShowSeconds;

    public static void Forget(Vehicle craft) => Showing.Remove(craft);

    public static void Draw(IReadOnlyList<(Vehicle Craft, WeaponInventory Inventory)> systems,
                            Vehicle? active, double dt)
    {
        Age(dt);
        if (systems.Count == 0) return;

        ImGuiViewportPtr main = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(main.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(main.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);

        // NoInputs: the overlay covers the screen, so anything else would swallow every click in
        // the game. Hovering is worked out from the pointer position instead.
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
                                       | ImGuiWindowFlags.NoInputs
                                       | ImGuiWindowFlags.NoNav
                                       | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.NoBringToFrontOnFocus
                                       | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoBackground;

        if (!ImGui.Begin("##KSArmoryMarkers", flags))
        {
            ImGui.End();
            return;
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        float2 cursor = ImGui.GetMousePos();
        double3 eye = KsaWorld.CameraPositionEcl();

        // Labelled either because the pointer is on it or because it was called for from the
        // panel and has not run out yet.
        List<(int Index, float2 At, float Alpha)> labels = [];

        // The hovered one is drawn last so its label sits over any neighbouring bracket.
        int hovered = -1;
        float2 hoveredAt = default;

        for (int i = 0; i < systems.Count; i++)
        {
            (Vehicle craft, WeaponInventory _) = systems[i];
            if (!KsaWorld.IsAlive(craft)) { Showing.Remove(craft); continue; }

            double3 atEcl = KsaWorld.PositionEcl(craft);
            if (!KsaWorld.TryProjectOrClamp(atEcl, out float2 at, out bool inView)) continue;

            bool isActive = ReferenceEquals(craft, active);
            bool blocked = KsaWorld.IsOccluded(eye, atEcl, out _);
            ImColor8 colour = (isActive, blocked) switch
            {
                (true, false) => Active,
                (true, true) => HiddenActive,
                (false, false) => Idle,
                (false, true) => Hidden,
            };

            // In view gets the bracket; out of view gets an arrow at the edge pointing at it, so
            // a site can be located from a craft that cannot see it.
            if (inView) DrawBracket(draw, at, colour, blocked);
            else DrawEdgeArrow(draw, at, colour);

            float dx = cursor.X - at.X;
            float dy = cursor.Y - at.Y;
            if (dx * dx + dy * dy <= HoverRadius * HoverRadius)
            {
                hovered = i;
                hoveredAt = at;
            }
            else if (Showing.TryGetValue(craft, out double left))
            {
                labels.Add((i, at, (float)Math.Clamp(left / FadeSeconds, 0.0, 1.0)));
            }
        }

        foreach ((int index, float2 at, float alpha) in labels)
        {
            DrawLabel(draw, main, at, systems[index], eye, alpha);
        }

        if (hovered >= 0) DrawLabel(draw, main, hoveredAt, systems[hovered], eye, 1f);

        ImGui.End();
    }

    // Four corners, not a closed box: the gap is what stops the marker hiding the thing it marks.
    // A blocked one is crossed through, because dimming alone does not survive a bright horizon.
    private static void DrawBracket(ImDrawListPtr draw, float2 at, ImColor8 colour, bool blocked)
    {
        float l = at.X - Half, r = at.X + Half;
        float t = at.Y - Half, b = at.Y + Half;

        draw.AddLine(new float2(l, t), new float2(l + Corner, t), colour);
        draw.AddLine(new float2(l, t), new float2(l, t + Corner), colour);
        draw.AddLine(new float2(r, t), new float2(r - Corner, t), colour);
        draw.AddLine(new float2(r, t), new float2(r, t + Corner), colour);
        draw.AddLine(new float2(l, b), new float2(l + Corner, b), colour);
        draw.AddLine(new float2(l, b), new float2(l, b - Corner), colour);
        draw.AddLine(new float2(r, b), new float2(r - Corner, b), colour);
        draw.AddLine(new float2(r, b), new float2(r, b - Corner), colour);

        if (blocked)
        {
            const float In = 3f;
            draw.AddLine(new float2(l + In, t + In), new float2(r - In, b - In), colour);
            draw.AddLine(new float2(r - In, t + In), new float2(l + In, b - In), colour);
        }
    }

    // A triangle at the screen edge pointing the way to something out of view. Its range comes
    // from the label, which every marker carries.
    private static void DrawEdgeArrow(ImDrawListPtr draw, float2 at, ImColor8 colour)
    {
        ImGuiViewportPtr main = ImGui.GetMainViewport();
        float cx = main.Pos.X + main.Size.X * 0.5f;
        float cy = main.Pos.Y + main.Size.Y * 0.5f;

        float dx = at.X - cx, dy = at.Y - cy;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f) return;
        dx /= len; dy /= len;

        // Perpendicular, for the base of the triangle.
        float px = -dy, py = dx;
        const float Long = 11f, Wide = 7f;

        draw.AddTriangleFilled(new float2(at.X + dx * Long, at.Y + dy * Long),
                               new float2(at.X - dx * 3f + px * Wide, at.Y - dy * 3f + py * Wide),
                               new float2(at.X - dx * 3f - px * Wide, at.Y - dy * 3f - py * Wide),
                               colour);
    }

    // Name and range, and nothing else. What a system is made of is a question the Components tab
    // answers at leisure; a marker is read at a glance while looking for something.
    private static void DrawLabel(ImDrawListPtr draw, ImGuiViewportPtr main, float2 at,
                                  (Vehicle Craft, WeaponInventory Inventory) system, double3 eyeEcl,
                                  float alpha)
    {
        double3 atEcl = KsaWorld.PositionEcl(system.Craft);
        string name = KsaWorld.DisplayName(system.Craft);
        string detail = Range(Vec.Len(atEcl - eyeEcl));

        if (KsaWorld.IsOccluded(eyeEcl, atEcl, out string blockedBy))
        {
            detail += blockedBy.Length > 0 ? $"   behind {blockedBy}" : "   no line of sight";
        }

        float2 nameSize = ImGui.CalcTextSize(name);
        float2 detailSize = ImGui.CalcTextSize(detail);

        const float PadX = 8f, PadY = 6f, Gap = 2f;
        float w = Math.Max(nameSize.X, detailSize.X) + PadX * 2f;
        float h = nameSize.Y + detailSize.Y + Gap + PadY * 2f;

        // Up and to the right of the bracket, clear of it -- then held on screen, because a marker
        // pinned to the edge would otherwise put its own label past it.
        const float Edge = 4f;
        float x = Math.Clamp(at.X + Half + 6f, main.Pos.X + Edge, main.Pos.X + main.Size.X - w - Edge);
        float y = Math.Clamp(at.Y - h - 6f, main.Pos.Y + Edge, main.Pos.Y + main.Size.Y - h - Edge);
        float2 tl = new(x, y);

        draw.AddRectFilled(tl, new float2(tl.X + w, tl.Y + h), Fade(Panel, alpha));
        draw.AddRect(tl, new float2(tl.X + w, tl.Y + h), Fade(PanelEdge, alpha));
        draw.AddText(new float2(tl.X + PadX, tl.Y + PadY), Fade(Label, alpha), name);
        draw.AddText(new float2(tl.X + PadX, tl.Y + PadY + nameSize.Y + Gap), Fade(Idle, alpha), detail);
    }

    // Counts every showing label down, whether or not its system is still on screen -- otherwise
    // one behind the camera never expires and reappears at full strength when you turn back.
    private static void Age(double dt)
    {
        if (Showing.Count == 0 || !double.IsFinite(dt) || dt <= 0.0) return;

        Expired.Clear();
        foreach (Vehicle craft in Showing.Keys)
        {
            double left = Showing[craft] - dt;
            if (left <= 0.0 || !KsaWorld.IsAlive(craft)) Expired.Add(craft);
            else Showing[craft] = left;
        }

        foreach (Vehicle craft in Expired) Showing.Remove(craft);
    }

    private static ImColor8 Fade(ImColor8 colour, float k)
    {
        if (k >= 1f) return colour;

        byte4 rgba = colour.AsByte4();
        return new ImColor8(rgba.X, rgba.Y, rgba.Z, (byte)(rgba.W * Math.Clamp(k, 0f, 1f)));
    }


    // Metres up close, kilometres beyond a kilometre. A site 340 m away reading "0.34 km" is
    // harder to act on than the same number in metres.
    private static string Range(double metres)
    {
        if (!double.IsFinite(metres)) return "range unknown";
        return metres < 1000.0 ? $"{metres:F0} m" : $"{metres / 1000.0:F1} km";
    }
}
