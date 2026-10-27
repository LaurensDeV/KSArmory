using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Click the world to shoot at that spot, with no track: the IFF and liveness gates do not apply, and
/// <see cref="WeaponSystem.FireAt"/> says which gates still run. Per system, not per session.
/// </summary>
internal sealed class Designator
{
    private static readonly float4 MarkerColour = new(1.0f, 0.85f, 0.25f, 0.9f);
    private static readonly float4 RefusedColour = new(0.8f, 0.3f, 0.25f, 0.8f);

    // How big the ring on the ground is drawn, as a fraction of how far away it is. A fixed radius
    // is a dot at range and swallows the screen underfoot.
    private const double MarkerScale = 0.02;
    private const double MarkerMin = 4.0;

    /// <summary>Fires at the ground under the cursor when the tool is on and the world is clicked.</summary>
    public void Update(IManualFire system, SystemConfig policy)
    {
        if (!policy.MouseFire) return;

        // A click on the panel is not a click on the world behind it.
        if (ImGui.GetIO().WantCaptureMouse) return;
        // Shift-click designates; without this it would also send a round.
        if (ImGui.GetIO().KeyShift) return;

        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)) return;

        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _))
        {
            // A gun shoots where it points, and over sky there is no ground to name.
            if (system.TriggerArmament == ArmamentKind.Belt) system.FireBurst();
            return;
        }

        system.FireAt(Lifted(groundEcl, system));
    }

    /// <summary>Marks where a shot would go, so the tool is aimable before it is fired.</summary>
    public void Draw(IManualFire system, SystemConfig policy)
    {
        if (!policy.MouseFire) return;
        if (ImGui.GetIO().WantCaptureMouse) return;
        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _)) return;

        // Its own anchor, rather than whichever was last set: with the shipped defaults there is
        // often none at all, overlays off, no shells in the air, and nothing else drawing.
        if (system.Platform is not { } platform) return;
        if (!KsaWorld.BeginDraw(platform, system.PlatformEcl)) return;

        double3 at = Lifted(groundEcl, system);

        // Coloured by whether the shot would go: loaded and laid, in range, and within the seeker's
        // reach. Asked of the trigger's armament, because a belt is not a magazine.
        bool ready = system.ReadyToFire;
        double range = system.Platform is null ? 0.0 : Vec.Len(at - system.PlatformEcl);
        bool reaches = range <= system.TriggerMunition.MaxRange;

        float4 colour = ready && reaches && system.CanGuideOnto(at) ? MarkerColour : RefusedColour;

        // Painted, its alpha the brightness it keeps over dark ground; the line up to a lifted
        // aim point stays a line.
        GroundRings.Add(groundEcl, Math.Max(MarkerMin, range * MarkerScale), 0.0,
                        new float4(colour.X, colour.Y, colour.Z, 0.05f));
        KsaWorld.DrawLineEcl(groundEcl, at, colour);
    }

    // Off the surface by the round's own fireball, so a burst reads as something in the air rather
    // than half-buried in the ground it was aimed at.
    private static double3 Lifted(double3 groundEcl, IManualFire system)
    {
        double3 up = system.Platform is { } craft ? KsaWorld.LocalUp(craft) : Vec.Unit(groundEcl);

        return groundEcl + up * Math.Max(system.Munition.FireballRadius, 2.0);
    }
}
