using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The lock, on the glass: brackets that close over the acquisition and a mark when there is
/// nothing left in the way.
///
/// <para>Separate from <see cref="Markers"/>, which brackets every weapons system in the world.
/// This brackets the one contact the selected weapon is engaging, which is a different question
/// and usually a different object — the target is rarely a weapons system at all.</para>
///
/// <para>An ImGui overlay for the same reason the markers are: a cue belongs on the glass at a
/// constant size, not in the scene where it would shrink with range and be hidden by the terrain
/// it exists to find something behind.</para>
///
/// <para>Not part of <c>DrawOverlays</c>. That is the diagnostic gizmo layer and it is off by
/// default; knowing whether you have a lock is playing the game rather than debugging it.</para>
/// </summary>
internal static class LockCueOverlay
{
    // Dwell building. Deliberately not green: the whole point is that this state is not yet the
    // one worth firing from.
    private static readonly ImColor8 Acquiring = new(255, 210, 120, 170);

    private static readonly ImColor8 Ready = new(90, 255, 120, 230);

    // Locked and refused. The reason is written beside it, because a cue that says "no" without
    // saying why sends the operator back to the panel, which is where this started.
    private static readonly ImColor8 Refused = new(255, 170, 60, 210);

    // Larger than Markers' icon so the two read as different marks when both are on one contact:
    // the system bracket says "there is a weapon there", this says "it is being shot at".
    private const float Half = Reticle.IconHalfSize * 1.5f;

    private const float CaretLength = 16f;
    private const float CaretHalfWidth = 7f;

    public static void Draw(WeaponSystem battery)
    {
        Track? track = battery.Radar.Locked;
        if (track is null || !track.Contact.IsAlive) return;

        bool held = battery.Hold is not null;

        LockPhase phase = LockCue.Phase(hasTrack: true,
                                        locked: battery.Radar.HasFiringSolution,
                                        clearToFire: !held,
                                        held: held);

        if (phase == LockPhase.None) return;

        // The drawn position, never the simulated one. Track.PositionEcl is sampled after the mod
        // steps, while the camera's matrices were built in the viewport pass before it -- so the
        // two belong to instants one step apart, and the step is what changes with warp. Drawing
        // the raw position makes the bracket jump every time the speed changes.
        //
        // TryDrawEgo is the same pairing round bodies and the gunner's sight already use: the
        // anchor's drawn position plus the round's own flight since launch.
        if (!track.Contact.TryDrawEgo(out double3 targetEgo)) return;
        if (!KsaWorld.TryProjectEgoOrClamp(targetEgo, out float2 at, out bool inView)) return;

        ImGuiViewportPtr main = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(main.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(main.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);

        // NoInputs: this covers the screen, and anything else would swallow every click in the
        // game. Same reasoning as Markers.
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
                                       | ImGuiWindowFlags.NoInputs
                                       | ImGuiWindowFlags.NoNav
                                       | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.NoBringToFrontOnFocus
                                       | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoBackground;

        if (!ImGui.Begin("##KSArmoryLockCue", flags))
        {
            ImGui.End();
            return;
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();

        ImColor8 colour = phase switch
        {
            LockPhase.ClearToFire => Ready,
            LockPhase.Held => Refused,
            _ => Acquiring,
        };

        if (inView) DrawBracket(draw, at, track, battery, phase, colour);
        else DrawCaret(draw, at, main, colour);

        ImGui.End();
    }

    private static void DrawBracket(ImDrawListPtr draw, float2 at, Track track,
                                    WeaponSystem battery, LockPhase phase, ImColor8 colour)
    {
        float acquisition = LockCue.Acquisition(track.HeldSeconds, battery.Sensor.LockSeconds);

        Span<ReticleStroke> strokes = stackalloc ReticleStroke[Reticle.MaxStrokes];
        int n = Reticle.Build(at, Half, LockCue.Standoff(acquisition),
                              settled: phase != LockPhase.Acquiring, strokes, ladder: false);

        for (int i = 0; i < n; i++) draw.AddLine(strokes[i].A, strokes[i].B, colour);

        // The one rung that earns a mark of its own: nothing is in the way. Small and central, so
        // it reads as the brackets having arrived rather than as another thing to look at.
        if (phase == LockPhase.ClearToFire)
        {
            float d = 4f;
            draw.AddLine(new float2(at.X - d, at.Y), new float2(at.X, at.Y - d), colour);
            draw.AddLine(new float2(at.X, at.Y - d), new float2(at.X + d, at.Y), colour);
            draw.AddLine(new float2(at.X + d, at.Y), new float2(at.X, at.Y + d), colour);
            draw.AddLine(new float2(at.X, at.Y + d), new float2(at.X - d, at.Y), colour);
        }

        // Answered where the operator is looking rather than on a tab. This is the whole reason
        // fire control names its first refusal instead of returning quietly.
        if (phase == LockPhase.Held && battery.Hold is { } why)
        {
            draw.AddText(new float2(at.X + Half * 1.8f + 4f, at.Y - 6f), colour, why);
        }
    }

    private static void DrawCaret(ImDrawListPtr draw, float2 at, ImGuiViewportPtr main,
                                  ImColor8 colour)
    {
        float2 centre = new(main.Pos.X + main.Size.X * 0.5f, main.Pos.Y + main.Size.Y * 0.5f);
        if (!LockCue.TryCaretDirection(at, centre, out float2 unit)) return;

        // Perpendicular, for the caret's base.
        float2 side = new(-unit.Y, unit.X);

        float2 tip = new(at.X + unit.X * CaretLength, at.Y + unit.Y * CaretLength);
        float2 a = new(at.X + side.X * CaretHalfWidth, at.Y + side.Y * CaretHalfWidth);
        float2 b = new(at.X - side.X * CaretHalfWidth, at.Y - side.Y * CaretHalfWidth);

        draw.AddLine(a, tip, colour);
        draw.AddLine(b, tip, colour);
        draw.AddLine(a, b, colour);
    }
}
