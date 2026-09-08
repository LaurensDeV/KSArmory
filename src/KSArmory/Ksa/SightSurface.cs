using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The window a sight paints on, resolved once so nothing downstream asks again.
///
/// <para><b>Which window is a question, not a constant.</b> A director may drive the view the
/// player flies from or a camera window of its own, and the two want different draw lists,
/// different sizes and different cameras. Reading the main view's for both is invisible while
/// only the main view is used and puts the whole picture on the wrong monitor as soon as it is
/// not.</para>
///
/// <para><b>The main view paints beneath the panel and a camera window paints over its image.</b>
/// The background list renders under every window, which is what a sight on the glass wants of
/// the main view. A camera window is a window: its picture is an opaque <c>Image</c>, so the same
/// list would put the whole reticule behind it. The foreground list of that window's <em>platform
/// viewport</em> is the one that lands on top — and naming the platform viewport is what follows
/// the window onto a second monitor, because ImGui gives a torn-off window one of its own and a
/// list belonging to the wrong one draws on the wrong screen.</para>
/// </summary>
internal readonly struct SightSurface
{
    /// <summary>The list to paint into. Clipped to <see cref="Pos"/>..<see cref="Size"/>.</summary>
    public readonly ImDrawListPtr Draw;

    /// <summary>Top-left of the picture, in the absolute screen pixels a draw list takes.</summary>
    public readonly float2 Pos;

    /// <summary>How large the picture is, for anything sized as a fraction of it.</summary>
    public readonly float2 Size;

    /// <summary>Which camera window this is, for the projections and the field of view.</summary>
    public readonly int Index;

    private readonly bool _clipped;

    // What the last resolution came to, so a change is reported once instead of every frame.
    // Which platform viewport a camera window belongs to is the whole question a second monitor
    // asks, and it is not answerable from a screenshot: a sight missing because the window was
    // never drawn and one missing because the head has no lock look identical.
    private static int _saidIndex = int.MinValue;
    private static uint _saidImGuiId;
    private static bool _saidResolved;

    private SightSurface(ImDrawListPtr draw, float2 pos, float2 size, int index, bool clipped)
    {
        Draw = draw;
        Pos = pos;
        Size = size;
        Index = index;
        _clipped = clipped;
    }

    /// <summary>The middle of the picture, which is where the head is boresighted.</summary>
    public float2 Centre => new(Pos.X + Size.X * 0.5f, Pos.Y + Size.Y * 0.5f);

    /// <summary>
    /// False when there is nothing to paint on — no such window, or one ImGui has not drawn yet,
    /// which is the state a camera window is in for its first frame. Drawing nothing for a frame
    /// is right; falling back to the main view would paint this head's reticule over the picture
    /// the player is flying from.
    /// </summary>
    public static bool TryFor(int index, out SightSurface surface)
    {
        surface = default;
        if (index < 0) return false;

        try
        {
            // The view the player flies from, which is the one ImGui itself calls main. Taken
            // from ImGui rather than from the viewport's own rect because that is what every
            // overlay on it has always used, and the two differ under a render scale.
            if (index == KsaWorld.MainViewportIndex)
            {
                ImGuiViewportPtr main = ImGui.GetMainViewport();
                if (main.IsNull()) return false;

                surface = new SightSurface(ImGui.GetBackgroundDrawList(), main.Pos, main.Size,
                                           index, clipped: false);
                return true;
            }

            if (!KsaWorld.TryViewportPicture(index, out float2 pos, out float2 size,
                                             out uint imGuiId))
            {
                return false;
            }

            ImGuiViewportPtr platform = imGuiId == 0u ? default : ImGui.FindViewportByID(imGuiId);
            bool resolved = imGuiId != 0u && !platform.IsNull();

            Report(index, imGuiId, resolved, pos, size);

            // Nothing to paint on yet, which is where a camera window sits until ImGui has drawn
            // it once. One blank frame, and never the main view's list instead.
            if (!resolved) return false;

            ImDrawListPtr draw = ImGui.GetForegroundDrawList(platform);

            // Confines the sight to the picture. Without it the reference line, which is drawn
            // far past both edges on purpose, runs out across the desktop -- and where this
            // window is docked inside the game window, over whatever else is open there.
            draw.PushClipRect(pos, pos + size, false);

            surface = new SightSurface(draw, pos, size, index, clipped: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Says where the sight is painting, whenever that changes. Debug rather than info: it is one
    // line per window a player opens, and useless until somebody is asking why a second monitor
    // is blank.
    private static void Report(int index, uint imGuiId, bool resolved, float2 pos, float2 size)
    {
        if (index == _saidIndex && imGuiId == _saidImGuiId && resolved == _saidResolved) return;

        _saidIndex = index;
        _saidImGuiId = imGuiId;
        _saidResolved = resolved;

        Log.Debug(() => $"sight: window {index} imgui={imGuiId} resolved={resolved} "
                        + $"at {pos.X:F0},{pos.Y:F0} size {size.X:F0}x{size.Y:F0}");
    }

    /// <summary>Releases the clip. Every path that took a surface must reach this.</summary>
    public void Finish()
    {
        if (!_clipped) return;

        try { Draw.PopClipRect(); }
        catch { /* an unbalanced clip is a cosmetic fault, and throwing here is the frame */ }
    }
}
