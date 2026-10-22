using Brutal.ImGuiApi;

namespace KSArmory;

/// <summary>A transparent window over the whole main view, for marks drawn on the world.</summary>
internal static class ScreenOverlay
{
    // NoInputs: the window covers the screen, so anything else would swallow every click in the
    // game. Hovering is worked out from the pointer position instead.
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoDecoration
                                           | ImGuiWindowFlags.NoInputs
                                           | ImGuiWindowFlags.NoNav
                                           | ImGuiWindowFlags.NoFocusOnAppearing
                                           | ImGuiWindowFlags.NoBringToFrontOnFocus
                                           | ImGuiWindowFlags.NoSavedSettings
                                           | ImGuiWindowFlags.NoBackground;

    /// <summary>ImGui's <c>Begin</c>: the caller ends it whatever this returns.</summary>
    public static bool Begin(string id) => Begin(id, out _);

    public static bool Begin(string id, out ImGuiViewportPtr main)
    {
        main = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(main.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(main.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);

        return ImGui.Begin(id, Flags);
    }
}
