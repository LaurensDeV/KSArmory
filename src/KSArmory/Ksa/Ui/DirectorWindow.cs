using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A director's camera window, drawn by the mod rather than by KSA: the picture KSA renders for
/// the leased viewport, with the pod's own controls laid over it — zoom, sensor mode, tracking.
///
/// <para>KSA's window around the same viewport is taken off with
/// <see cref="KsaWorld.TryHideViewportWindow"/>, and where that cannot be done this draws nothing
/// and KSA's window stays, which is the picture without the controls rather than two of it.</para>
///
/// <para>The picture's place is written back to the viewport each frame, because the sight and the
/// projections read it there (<see cref="SightSurface"/>).</para>
/// </summary>
internal static class DirectorWindow
{
    // See-through, so the picture shows under the controls; the lit one marks what is on.
    private static readonly float4 Idle = new(0.08f, 0.08f, 0.08f, 0.45f);
    private static readonly float4 Hover = new(0.25f, 0.25f, 0.25f, 0.65f);
    private static readonly float4 Pressed = new(0.35f, 0.35f, 0.35f, 0.80f);
    private static readonly float4 Lit = new(0.20f, 0.42f, 0.30f, 0.65f);
    private static readonly float4 Firing = new(0.60f, 0.12f, 0.12f, 0.75f);

    // Bottom-left, because the sight writes its status top-left and its zoom top-right.
    private const float Inset = 8f;

    /// <summary>Draws one head's window. False when KSA's own window has to stay instead.</summary>
    /// <param name="paint">Paints the sight over the picture, under the controls.</param>
    public static bool Draw(OpticalHead head, OpticConfig policy, string title, Action<SightSurface> paint)
    {
        int index = policy.Viewport;
        if (!KsaWorld.TryHideViewportWindow(index)) return false;
        if (!KsaWorld.TryViewportTexture(index, out ImTextureRef texture, out float2 rendered)) return true;

        ImGui.SetNextWindowSize(new float2(540f, 560f), ImGuiCond.FirstUseEver);

        bool open = true;
        // The index in the id rather than the title, so renaming the craft does not move the window.
        if (ImGui.Begin($"{title}###KSArmoryDirector{index}", ref open,
                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                        | ImGuiWindowFlags.NoCollapse))
        {
            float2 at = ImGui.GetCursorScreenPos();
            float2 room = ImGui.GetContentRegionAvail();

            ImGui.SetNextItemAllowOverlap();
            ImGui.Image(texture, in rendered, null, null);

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            {
                float wheel = ImGui.GetIO().MouseWheel;
                if (wheel != 0f) policy.Magnification = SightZoom.Stepped(policy.Magnification, wheel > 0f ? 1 : -1, head.Profile.MaxMagnification);
            }

            // Written before the sight paints: its projections read the picture's place back.
            KsaWorld.PlaceViewportPicture(index, at, ImGui.GetWindowViewport().ID, int2.Zero);
            paint(SightSurface.InWindow(index, at, rendered));

            float rowHeight = ImGui.GetFrameHeight();
            ImGui.SetCursorScreenPos(new float2(at.X + Inset, at.Y + rendered.Y - rowHeight - Inset));
            Controls(head, policy, head.Profile.MaxMagnification);

            // Resized once the drag ends: every size is a new set of render targets.
            int2 wanted = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                        ? int2.Zero
                        : new int2((int)room.X, (int)room.Y);
            KsaWorld.PlaceViewportPicture(index, at, ImGui.GetWindowViewport().ID, wanted);
        }
        ImGui.End();

        if (!open)
        {
            KsaWorld.CloseCameraWindow(index);
            policy.Viewport = -1;
        }

        return true;
    }

    private static void Controls(OpticalHead head, OpticConfig policy, float max)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Idle);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Pressed);

        if (ImGui.Button("-##zoom")) policy.Magnification = SightZoom.Stepped(policy.Magnification, -1, max);
        ImGui.SameLine();
        if (ImGui.Button($"x{SightZoom.Clamp(policy.Magnification):0.#}##level")) policy.Magnification = 1f;
        Tip("Back to x1. The wheel over the picture zooms too.");
        ImGui.SameLine();
        if (ImGui.Button("+##zoom")) policy.Magnification = SightZoom.Stepped(policy.Magnification, 1, max);

        ImGui.SameLine();
        bool thermal = policy.Sensor is SensorMode.WhiteHot or SensorMode.BlackHot;
        if (Toggle($"{SensorModes.Label(policy.Sensor)}##sensor", thermal)) policy.Sensor = SensorModes.Next(policy.Sensor);
        Tip("TV, white-hot, black-hot, colour. The thermal modes read brightness as heat, with the sky "
            + "cold: KSA has no temperature to show.");

        ImGui.SameLine();
        if (Toggle("TRK##track", policy.Tracking)) policy.Tracking = !policy.Tracking;
        Tip("Track: slew onto what the director is holding.");

        if (head.HasLaser)
        {
            ImGui.SameLine();
            if (Toggle("LASER##laser", policy.Lasing, Firing)) policy.Lasing = !policy.Lasing;
            Tip("Measures the distance to whatever the crosshair is on, and marks it for laser-guided "
                + "weapons. It will not fire through your own craft.");
        }

        ImGui.PopStyleColor(3);
    }

    // A button that shows whether its setting is on, in place of a tick box, which reads poorly
    // over a picture.
    private static bool Toggle(string label, bool on) => Toggle(label, on, Lit);

    private static bool Toggle(string label, bool on, float4 tint)
    {
        if (on) ImGui.PushStyleColor(ImGuiCol.Button, tint);
        bool pressed = ImGui.Button(label);
        if (on) ImGui.PopStyleColor();
        return pressed;
    }

    private static void Tip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None)) ImGui.SetTooltip(text);
    }
}
