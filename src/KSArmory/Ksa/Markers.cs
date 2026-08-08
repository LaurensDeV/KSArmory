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

    // Half-width of the bracket, in pixels. Constant: this is an icon, not a bounding box, and
    // sizing it to the craft would make a distant site a sub-pixel dot -- which is the one case
    // it exists for.
    private const float Half = 11f;
    private const float Corner = 4f;

    // Pointer distance, in pixels, that counts as hovering a marker.
    private const float HoverRadius = 18f;

    // Apparent size, in radians, past which a craft needs no marker: you are looking straight at
    // it. Angular rather than a distance, so a big vessel drops its bracket further out than a
    // drone does -- what matters is how much of the view it fills, not how many metres away it is.
    // About 1.7 degrees, which for the Pantsir is a little over a hundred metres.
    private const double FillsTheViewRad = 0.03;

    // How long a label stays up after being called for, and how much of that it spends fading.
    // Long enough to read and find the thing, short enough that pressing it again is easier than
    // remembering to switch it off.
    private const double ShowSeconds = 10.0;
    private const double FadeSeconds = 2.0;

    // Long enough after the pointer leaves a bracket to reach the label it put up. Without it the
    // label is unclickable: moving towards it leaves the bracket's hover radius and it vanishes.
    private const double ReachSeconds = 2.0;

    // Systems whose label is showing, and for how much longer. Reference identity, which is what
    // a Vehicle compares by -- two craft are never equal, so a stale entry cannot shadow a live
    // one. Pruned in Draw as they expire or the craft is destroyed.
    private static readonly Dictionary<Vehicle, double> Showing = [];
    private static readonly List<Vehicle> Expired = [];

    // Systems whose label stays up until it is unlocked. Separate from the timers rather than an
    // infinite one, so a lock survives a (+) press and a (+) press does not disturb a lock.
    private static readonly HashSet<Vehicle> Locked = [];

    /// <summary>Puts a system's label up for a while. Pressing again restarts the clock.</summary>
    public static void Show(Vehicle craft) => Showing[craft] = ShowSeconds;

    public static void Forget(Vehicle craft)
    {
        Showing.Remove(craft);
        Locked.Remove(craft);
    }

    /// <summary>
    /// Drops every label. These are static and hold a <c>Vehicle</c> each, so a locked one keeps a
    /// destroyed craft reachable for the rest of the process unless the panel happens to draw
    /// again and prune it.
    /// </summary>
    public static void Forget()
    {
        Showing.Clear();
        Locked.Clear();
        Expired.Clear();
    }

    private static void KeepUpFor(Vehicle craft, double seconds)
    {
        Showing[craft] = Showing.TryGetValue(craft, out double left) ? Math.Max(left, seconds)
                                                                    : seconds;
    }

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

        // Labelled because it is locked, because it was called for from the panel and has not run
        // out, or because the pointer is on its bracket. Collected here and drawn after this
        // window closes: a label is a window of its own so its lock can be clicked, and ImGui
        // windows do not nest.
        List<(int Index, float2 At, float Alpha)> labels = [];

        for (int i = 0; i < systems.Count; i++)
        {
            (Vehicle craft, WeaponInventory _) = systems[i];
            if (!KsaWorld.IsAlive(craft)) { Showing.Remove(craft); continue; }

            double3 atEcl = KsaWorld.PositionEcl(craft);

            // Close enough to see plainly: no bracket, and no hover target either. A marker over
            // something already filling the view is nothing but something to catch the pointer.
            double range = Vec.Len(atEcl - eye);
            if (range > 1e-6 && KsaWorld.MeanRadius(craft) / range > FillsTheViewRad) continue;

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
            if (dx * dx + dy * dy <= HoverRadius * HoverRadius) KeepUpFor(craft, ReachSeconds);

            if (Locked.Contains(craft))
            {
                labels.Add((i, at, 1f));
            }
            else if (Showing.TryGetValue(craft, out double left))
            {
                labels.Add((i, at, (float)Math.Clamp(left / FadeSeconds, 0.0, 1.0)));
            }
        }

        ImGui.End();

        foreach ((int index, float2 at, float alpha) in labels)
        {
            DrawLabel(main, at, systems[index], eye, alpha);
        }
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
    // A window of its own, not part of the overlay, because the overlay is NoInputs and nothing
    // drawn on it can be clicked. That is the whole reason the lock works.
    private static void DrawLabel(ImGuiViewportPtr main, float2 at,
                                  (Vehicle Craft, WeaponInventory Inventory) system, double3 eyeEcl,
                                  float alpha)
    {
        Vehicle craft = system.Craft;
        double3 atEcl = KsaWorld.PositionEcl(craft);
        string name = KsaWorld.DisplayName(craft);
        string detail = Range(Vec.Len(atEcl - eyeEcl));

        if (KsaWorld.IsOccluded(eyeEcl, atEcl, out string blockedBy))
        {
            detail += blockedBy.Length > 0 ? $"   behind {blockedBy}" : "   no line of sight";
        }

        bool locked = Locked.Contains(craft);

        // Estimated, only to keep the window on screen; AlwaysAutoResize sets the real size.
        float2 nameSize = ImGui.CalcTextSize(name);
        float2 detailSize = ImGui.CalcTextSize(detail);
        float w = Math.Max(nameSize.X, detailSize.X + 34f) + 18f;
        float h = nameSize.Y + detailSize.Y + 18f;

        // Up and to the right of the bracket, clear of it -- then held on screen, because a marker
        // pinned to the edge would otherwise put its own label past it.
        const float Edge = 4f;
        float x = Math.Clamp(at.X + Half + 6f, main.Pos.X + Edge, main.Pos.X + main.Size.X - w - Edge);
        float y = Math.Clamp(at.Y - h - 6f, main.Pos.Y + Edge, main.Pos.Y + main.Size.Y - h - Edge);

        ImGui.SetNextWindowPos(new float2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f * alpha);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
                                       | ImGuiWindowFlags.AlwaysAutoResize
                                       | ImGuiWindowFlags.NoNav
                                       | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.NoBringToFrontOnFocus
                                       | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoMove;

        // Keyed on the craft's Id, which KSA keeps unique, so each label is its own window and
        // they do not fight over one set of state.
        if (ImGui.Begin($"##KSArmoryLabel_{name}", flags))
        {
            // Reaching for the lock takes the pointer off the bracket, which is what put the
            // label up. Without this the label disappears on the way to its own button.
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem
                                      | ImGuiHoveredFlags.ChildWindows))
            {
                KeepUpFor(craft, ReachSeconds);
            }

            ImGui.TextColored(new float4(0.92f, 0.94f, 0.96f, alpha), name);
            ImGui.TextColored(new float4(0.59f, 0.78f, 1.0f, alpha), detail);

            ImGui.SameLine();
            if (ImGui.SmallButton(locked ? "[x]" : "[ ]"))
            {
                if (locked) Locked.Remove(craft);
                else Locked.Add(craft);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(locked ? "Locked up. Click to let it fade."
                                        : "Keep this label up until it is unlocked.");
            }
        }

        ImGui.End();
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



    // Metres up close, kilometres beyond a kilometre. A site 340 m away reading "0.34 km" is
    // harder to act on than the same number in metres.
    private static string Range(double metres)
    {
        if (!double.IsFinite(metres)) return "range unknown";
        return metres < 1000.0 ? $"{metres:F0} m" : $"{metres / 1000.0:F1} km";
    }
}
