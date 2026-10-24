using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A bracket on every weapons system, labelled with its name and range when the pointer is on it or
/// when it has been pinned from the panel.
///
/// <para>An ImGui overlay rather than gizmos, so it keeps a constant size and is not occluded by the
/// terrain it is meant to find something behind.</para>
/// </summary>
internal static class Markers
{
    private static readonly ImColor8 Idle = new(150, 200, 255, 150);
    private static readonly ImColor8 Active = new(90, 255, 120, 220);

    // Behind the planet: dimmed rather than hidden, so it does not read as something in view.
    private static readonly ImColor8 Hidden = new(150, 200, 255, 70);
    private static readonly ImColor8 HiddenActive = new(90, 255, 120, 90);

    // Half-width of the bracket, in pixels: an icon, not a bounding box. Shared with the gunner's sight.
    private const float Half = Reticle.IconHalfSize;
    private const float Corner = 4f;

    // Pointer distance, in pixels, that counts as hovering a marker.
    private const float HoverRadius = 18f;

    // Apparent size, in radians, past which a craft is plainly in view and needs no marker; about
    // 1.7 degrees.
    private const double FillsTheViewRad = 0.03;

    // How long a label stays up after being called for, and how much of that it spends fading.
    private const double ShowSeconds = 10.0;
    private const double FadeSeconds = 2.0;

    // Long enough after the pointer leaves a bracket to reach the label it put up. Without it the
    // label is unclickable: moving towards it leaves the bracket's hover radius and it vanishes.
    private const double ReachSeconds = 2.0;

    // Systems whose label is showing, and for how much longer, by reference. Pruned in Draw.
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
    /// Drops every label. Each holds a <c>Vehicle</c> statically, which keeps a destroyed craft
    /// reachable until the next prune.
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

        if (!ScreenOverlay.Begin("##KSArmoryMarkers", out ImGuiViewportPtr main))
        {
            ImGui.End();
            return;
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        float2 cursor = ImGui.GetMousePos();
        double3 eye = KsaWorld.CameraPositionEcl();

        // Drawn after this window closes: a label is a window of its own so its lock can be
        // clicked, and ImGui windows do not nest.
        List<(int Index, float2 At, float Alpha)> labels = [];

        for (int i = 0; i < systems.Count; i++)
        {
            (Vehicle craft, WeaponInventory _) = systems[i];
            if (!KsaWorld.IsAlive(craft)) { Showing.Remove(craft); continue; }

            double3 atEcl = KsaWorld.PositionEcl(craft);

            // Plainly in view: no bracket, and no hover target to catch the pointer.
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

    private static void DrawEdgeArrow(ImDrawListPtr draw, float2 at, ImColor8 colour)
    {
        ImGuiViewportPtr main = ImGui.GetMainViewport();
        float cx = main.Pos.X + main.Size.X * 0.5f;
        float cy = main.Pos.Y + main.Size.Y * 0.5f;

        float dx = at.X - cx, dy = at.Y - cy;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f) return;
        dx /= len; dy /= len;

        float px = -dy, py = dx;
        const float Long = 11f, Wide = 7f;

        draw.AddTriangleFilled(new float2(at.X + dx * Long, at.Y + dy * Long),
                               new float2(at.X - dx * 3f + px * Wide, at.Y - dy * 3f + py * Wide),
                               new float2(at.X - dx * 3f - px * Wide, at.Y - dy * 3f - py * Wide),
                               colour);
    }

    // A window of its own because the overlay is NoInputs, and the lock has to be clickable.
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

        // Up and right of the bracket, held on screen so an edge-pinned marker's label stays visible.
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

        // Keyed on the craft's Id, so each label is its own window; craft from one blueprint share one.
        if (ImGui.Begin($"##KSArmoryLabel_{name}", flags))
        {
            // Reaching for the lock takes the pointer off the bracket that put the label up.
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
    // one behind the camera never expires and reappears at full strength when the view returns.
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



    private static string Range(double metres)
    {
        if (!double.IsFinite(metres)) return "range unknown";
        return metres < 1000.0 ? $"{metres:F0} m" : $"{metres / 1000.0:F1} km";
    }
}
