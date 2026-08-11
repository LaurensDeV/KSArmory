using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Point at the world and shoot at that spot — an operator naming a place instead of the radar
/// naming a craft.
///
/// <para>This is the only way to engage something the sensor cannot supply: terrain, a spot
/// ahead of a target, or anything the threat model rejects for being too slow or too cold. A
/// designation carries no allegiance and no track, so the IFF and liveness gates are not skipped
/// so much as inapplicable — <see cref="WeaponSystem.FireAt"/> says which gates still run.</para>
///
/// <para>Per battery rather than per session, like every other tool that acts on one
/// installation: two sites in the same world are aimed by different people at different things.</para>
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
    public void Update(IManualFire battery, SystemConfig policy)
    {
        if (!policy.MouseFire) return;

        // A click on the panel is not a click on the world behind it.
        if (ImGui.GetIO().WantCaptureMouse) return;
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)) return;

        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _))
        {
            // Nothing under the cursor is not a reason to hold fire. A gun shoots where it is
            // pointing rather than at a named place, and the sky is where most of its targets are:
            // requiring a ground hit would leave the CIWS unable to fire at anything above the
            // horizon, which is the one thing a CIWS is for.
            if (battery.Profile.TubeCount == 0) battery.FireBurst();
            return;
        }

        battery.FireAt(Lifted(groundEcl, battery));
    }

    /// <summary>Marks where a shot would go, so the tool is aimable before it is fired.</summary>
    public void Draw(IManualFire battery, SystemConfig policy)
    {
        if (!policy.MouseFire) return;
        if (ImGui.GetIO().WantCaptureMouse) return;
        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _)) return;

        // Its own anchor, rather than whichever was last set: with the shipped defaults there is
        // often none at all, overlays off, no shells in the air, and nothing else drawing.
        if (battery.Platform is not { } platform) return;
        if (!KsaWorld.BeginDraw(platform, battery.PlatformEcl)) return;

        double3 at = Lifted(groundEcl, battery);

        // Coloured by whether the shot would be taken, because armed, loaded, in range and within
        // the seeker's reach are four separate refusals that all look like a click doing nothing.
        // The last is the least obvious: a fixed launcher can only shoot where it points.
        //
        // Asked of whichever weapon the launcher carries. Reading the magazine leaves a gun-only
        // mount marked refused forever, since its magazine is empty by construction.
        bool ready = battery.ReadyToFire;
        double range = battery.Platform is null ? 0.0 : Vec.Len(at - battery.PlatformEcl);
        bool reaches = range <= battery.Munition.MaxRange;

        float4 colour = ready && reaches && battery.CanGuideOnto(at) ? MarkerColour : RefusedColour;

        double3 up = battery.Platform is { } craft ? KsaWorld.LocalUp(craft) : Vec.Unit(groundEcl);

        KsaWorld.DrawCircleEcl(groundEcl, up, Math.Max(MarkerMin, range * MarkerScale), colour);
        KsaWorld.DrawLineEcl(groundEcl, at, colour);
    }

    // Off the surface by the round's own fireball, so a burst reads as something in the air rather
    // than half-buried in the ground it was aimed at.
    private static double3 Lifted(double3 groundEcl, IManualFire battery)
    {
        double3 up = battery.Platform is { } craft ? KsaWorld.LocalUp(craft) : Vec.Unit(groundEcl);

        return groundEcl + up * Math.Max(battery.Munition.FireballRadius, 2.0);
    }
}
