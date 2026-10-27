using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The lock, on the glass: brackets that close over the acquisition and a mark when there is
/// nothing left in the way.
///
/// <para><see cref="Markers"/> brackets every weapons system; this brackets the one contact the
/// selected weapon is engaging. Not part of <c>DrawOverlays</c>, which is the diagnostic layer.</para>
/// </summary>
internal static class LockCueOverlay
{
    // Dwell building: not green, because this is not yet the state worth firing from.
    private static readonly ImColor8 Acquiring = new(255, 210, 120, 170);

    private static readonly ImColor8 Ready = new(90, 255, 120, 230);

    // Locked and refused: a different hue family from Acquiring, since the two mean opposite things.
    // Same red as the switcher's GUARDING.
    private static readonly ImColor8 Refused = new(255, 89, 77, 210);

    // Larger than Markers' icon so the two read as different marks on one contact.
    private const float Half = Reticle.IconHalfSize * 1.5f;

    private const float CaretLength = 16f;
    private const float CaretHalfWidth = 7f;

    public static void Draw(WeaponSystem system)
    {
        Track? track = system.Radar.Locked;
        if (track is null || !track.Contact.IsAlive) return;

        // A spent launcher whose rounds are still flying has nothing left to cue; their own marks say what is happening.
        if (system.TriggerArmament == ArmamentKind.Tubes && system.TubeCount > 0 && system.Ammo <= 0
            && system.RoundsInFlight > 0)
        {
            return;
        }

        bool held = system.Hold is not null;

        LockPhase phase = LockCue.Phase(hasTrack: true,
                                        locked: system.Radar.HasFiringSolution,
                                        clearToFire: !held,
                                        held: held);

        if (phase == LockPhase.None) return;

        // The drawn position, never Track.PositionEcl: that is sampled after the mod steps, a step
        // after the camera's matrices, so the bracket would jump whenever the speed changes.
        if (!track.Contact.TryDrawEgo(out double3 targetEgo)) return;
        // The view the player flies from: this is the trigger's cue, not a sight's picture.
        if (!KsaWorld.TryProjectEgoOrClamp(KsaWorld.MainViewportIndex, targetEgo, out float2 at,
                                           out bool inView))
        {
            return;
        }

        if (!ScreenOverlay.Begin("##KSArmoryLockCue", out ImGuiViewportPtr main))
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

        if (inView) DrawBracket(draw, at, track, system, phase, colour);
        else DrawCaret(draw, at, main, colour);

        ImGui.End();
    }

    private static void DrawBracket(ImDrawListPtr draw, float2 at, Track track,
                                    WeaponSystem system, LockPhase phase, ImColor8 colour)
    {
        float acquisition = LockCue.Acquisition(track.HeldSeconds, system.Sensor.LockSeconds);

        Span<ReticleStroke> strokes = stackalloc ReticleStroke[Reticle.MaxStrokes];
        int n = Reticle.Build(at, Half, LockCue.Standoff(acquisition),
                              settled: phase != LockPhase.Acquiring, strokes, ladder: false);

        for (int i = 0; i < n; i++) draw.AddLine(strokes[i].A, strokes[i].B, colour);

        if (phase == LockPhase.ClearToFire)
        {
            float d = 4f;
            draw.AddLine(new float2(at.X - d, at.Y), new float2(at.X, at.Y - d), colour);
            draw.AddLine(new float2(at.X, at.Y - d), new float2(at.X + d, at.Y), colour);
            draw.AddLine(new float2(at.X + d, at.Y), new float2(at.X, at.Y + d), colour);
            draw.AddLine(new float2(at.X, at.Y + d), new float2(at.X - d, at.Y), colour);
        }

        // Under the bracket, not beside it: the chase HUD's range and a designation's readout are
        // both written to the right of the same target.
        if (phase == LockPhase.Held && system.Hold is { } why)
        {
            draw.AddText(new float2(at.X - Half * 1.8f, at.Y + Half * 1.8f + 4f), colour, why);
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
