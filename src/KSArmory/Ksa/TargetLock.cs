using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Shift-click anything to lock an installation onto it: the turret follows, FIRE shoots a craft
/// named this way, and every director fitted beside it watches the same thing.
///
/// <para><b>It designates; it does not take the player's view.</b> The drives follow, and the
/// picture follows because the mount moved.</para>
///
/// <para>Takes the head and the system rather than roles, because it commands them; the roles in
/// <c>Ksa/WeaponSystemRoles.cs</c> are read-only views for consumers.</para>
/// </summary>
internal static class TargetLock
{
    // How close the cursor has to be to a craft, at minimum, in pixels: a craft a few kilometres out
    // is a couple of pixels across.
    private const float MinPickPixels = 24f;

    // The designation mark, in pixels, constant like the bracket in Markers.
    private const float TickIn = 11f;
    private const float TickOut = 18f;
    private const float LineHeight = 15f;

    // Amber, so it does not read as one of the blue-white system brackets or the green active one.
    private static readonly ImColor8 Designated = new(255, 190, 60, 235);
    private static readonly ImColor8 DesignatedHidden = new(255, 190, 60, 90);

    /// <summary>
    /// Reads the click, if there was one, and locks whatever is under it. The weapon is designated
    /// through <paramref name="designate"/>, which reaches every station of it, and every director on
    /// the craft takes it too, since the click locks the installation rather than one part of it.
    /// </summary>
    public static void Update(WeaponSystem? system, IReadOnlyList<OpticalHead> heads,
                              Action<Aimpoint, string>? designate)
    {
        if (!Requested()) return;

        if ((system?.Platform ?? (heads.Count > 0 ? heads[0].Platform : null)) is not { } platform) return;

        // A craft under the pointer wins over the ground behind it, whatever their ranges along the ray.
        if (TryPickCraft(platform) is { } craft)
        {
            Aimpoint at = Aimpoint.OnVehicle(craft, KsaWorld.PositionEcl(craft),
                                             KsaWorld.VelocityEcl(craft), KsaWorld.MeanRadius(craft));

            designate?.Invoke(at, KsaWorld.DisplayName(craft));
            foreach (OpticalHead head in heads) head.Designate(at, KsaWorld.DisplayName(craft));
            return;
        }

        // Anchored to the body rather than to the ecliptic, so it is still there a second later.
        if (KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out double lat, out double lon,
                                          out string body)
            && KsaWorld.TryAnchorToGround(groundEcl, out object? handle, out double3 anchor))
        {
            Aimpoint at = Aimpoint.OnGround(handle!, anchor, groundEcl, Vec.Zero);
            string what = $"{body} {lat:F2}, {lon:F2}";

            designate?.Invoke(at, what);
            foreach (OpticalHead head in heads) head.Designate(at, what);
        }

        // A click on empty sky must not name a point at infinity.
    }

    /// <summary>
    /// Marks what this installation is designated on, with its range and closing speed.
    ///
    /// <para>Drawn for the installation the panel is showing and no other.</para>
    /// </summary>
    public static void Draw(WeaponSystem? system, OpticalHead? head)
    {
        Aimpoint aim = system?.Designation ?? head?.Designation ?? Aimpoint.Nothing;
        if (aim.Kind == AimpointKind.None) return;

        string what = system is not null && system.Designation.Kind != AimpointKind.None
                          ? system.DesignationName
                          : head?.DesignationName ?? "designated";

        if ((system?.Platform ?? head?.Platform) is not { } platform) return;

        // Only from the craft doing the aiming: a system does not follow the player, so flying the
        // target itself would otherwise show it wearing its own reticule.
        if (!ReferenceEquals(platform, KsaWorld.ControlledVehicle)) return;

        double3 platformEcl = system?.PlatformEcl ?? head?.PlatformEcl ?? Vec.Zero;

        // Read live off the craft where there is one: the aimpoint's own copy is a sample, and a
        // mover is metres from it by the time this draws.
        double3 atEcl = aim.PositionEcl;
        double3 targetVel = aim.VelocityEcl;
        if (aim.Handle is Vehicle craft && KsaWorld.IsAlive(craft))
        {
            atEcl = KsaWorld.PositionEcl(craft);
            targetVel = KsaWorld.VelocityEcl(craft);
        }

        if (!Vec.IsFinite(atEcl)) return;
        if (!KsaWorld.TryProjectOrClamp(atEcl, out float2 at, out bool inView)) return;

        // From the launcher, not the camera, which can be kilometres away.
        double3 r = atEcl - platformEcl;
        double range = Vec.Len(r);
        double closing = range > 1e-6
                             ? -Vec.Dot(targetVel - KsaWorld.VelocityEcl(platform), Vec.Unit(r))
                             : 0.0;

        bool blocked = KsaWorld.IsOccluded(KsaWorld.CameraPositionEcl(), atEcl, out _);
        ImColor8 colour = blocked ? DesignatedHidden : Designated;

        if (ScreenOverlay.Begin("##KSArmoryDesignation"))
        {
            ImDrawListPtr draw = ImGui.GetWindowDrawList();

            if (inView)
            {
                DrawReticle(draw, at, colour);
                // Off the system, which only reports a shortfall while its own gun lay drives it.
                string? beyond = system is { GunLayShortMetres: > 1.0 } laid
                                     ? $"beyond reach -- the gun reaches "
                                       + $"{(laid.GunLayRangeMetres - laid.GunLayShortMetres) / 1000.0:F1} km"
                                     : null;

                DrawReadout(draw, at, what, range, closing, colour, beyond);
            }
            else
            {
                // Off screen the ring alone, at the edge: there the position is a clamp, not a place.
                draw.AddCircle(at, TickIn * 0.6f, colour, 12, 2f);
            }
        }

        ImGui.End();
    }

    // A ring with four ticks outside it, open at the diagonals so the target stays visible.
    private static void DrawReticle(ImDrawListPtr draw, float2 at, ImColor8 colour)
    {
        draw.AddCircle(at, TickIn, colour, 24, 1.6f);

        draw.AddLine(new float2(at.X, at.Y - TickIn), new float2(at.X, at.Y - TickOut), colour, 1.6f);
        draw.AddLine(new float2(at.X, at.Y + TickIn), new float2(at.X, at.Y + TickOut), colour, 1.6f);
        draw.AddLine(new float2(at.X - TickIn, at.Y), new float2(at.X - TickOut, at.Y), colour, 1.6f);
        draw.AddLine(new float2(at.X + TickIn, at.Y), new float2(at.X + TickOut, at.Y), colour, 1.6f);
    }

    // Name, range and closing speed, the speed labelled closing or opening rather than signed.
    private static void DrawReadout(ImDrawListPtr draw, float2 at, string what,
                                    double range, double closing, ImColor8 colour, string? warning = null)
    {
        string distance = range >= 1000.0 ? $"{range / 1000.0:F2} km" : $"{range:F0} m";
        string speed = Math.Abs(closing) < 0.5
                           ? "steady"
                           : $"{Math.Abs(closing):F0} m/s {(closing > 0.0 ? "closing" : "opening")}";

        var origin = new float2(at.X + TickOut + 6f, at.Y - TickIn);
        draw.AddText(origin, colour, what);
        draw.AddText(new float2(origin.X, origin.Y + LineHeight), colour, $"{distance}   {speed}");

        if (warning is not null) draw.AddText(new float2(origin.X, origin.Y + (2f * LineHeight)), BeyondReach, warning);
    }

    private static readonly ImColor8 BeyondReach = new(255, 90, 60, 235);

    // Not while a panel window wants the mouse, or every click on the panel also redesignates.
    private static bool Requested()
        => !ImGui.GetIO().WantCaptureMouse
           && ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)
           && ImGui.GetIO().KeyShift;

    // The craft under the pointer, or null. The hull first, so a craft wearing KSA's hover outline
    // is the one locked however far its centre is from the pointer; then a centre within
    // MinPickPixels, for craft too small or far off for the pointer to land on. The installation's
    // own platform is excluded: designating the thing it is bolted to points it at its own mounting.
    private static Vehicle? TryPickCraft(Vehicle platform)
    {
        if (KsaWorld.CraftUnderCursor(exclude: platform) is { } under)
        {
            Log.Info($"lock: {KsaWorld.DisplayName(under)} is under the pointer");
            return under;
        }

        List<Vehicle> craft = [];
        KsaWorld.CollectVehicles(craft);

        List<float2> onScreen = [];
        List<float> radii = [];
        List<Vehicle> kept = [];

        for (int i = 0; i < craft.Count; i++)
        {
            if (ReferenceEquals(craft[i], platform)) continue;
            if (!KsaWorld.TryProjectOrClamp(KsaWorld.PositionEcl(craft[i]), out float2 at,
                                            out bool inView) || !inView)
            {
                continue;
            }

            onScreen.Add(at);
            radii.Add(MinPickPixels);
            kept.Add(craft[i]);
        }

        int best = Picking.NearestWithin(onScreen, radii, ImGui.GetMousePos());

        return best >= 0 ? kept[best] : null;
    }
}
