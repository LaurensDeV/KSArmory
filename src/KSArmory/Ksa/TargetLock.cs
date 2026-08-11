using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Shift-click anything to lock an installation onto it: the turret follows, and a director
/// fitted beside it watches the same thing.
///
/// <para><b>It designates; it does not take the player's view.</b> The instruction goes to the
/// drives, so the picture follows because the mount moved — which is what an operator means by
/// locking on. Driving the camera instead would frame the target while leaving the turret pointing
/// wherever it was, and would do it on a craft with no weapon at all.</para>
///
/// <para><b>Both, from one click, because they are one installation.</b> A Phalanx has a turret
/// and no director and locks anyway; a bare director has no turret and watches anyway. Splitting
/// this into two gestures would make the answer depend on what happens to be fitted.</para>
///
/// <para>Takes the head and the system themselves rather than roles: this <em>commands</em> them,
/// and the roles in <c>Ksa/WeaponSystemRoles.cs</c> are what a consumer reads. Adding a command to
/// the interface the sight and the chase camera read through would hand them one they must never
/// use.</para>
/// </summary>
internal static class TargetLock
{
    // How close the cursor has to be to a craft, at minimum, in pixels. A craft a few kilometres
    // out is a couple of pixels across, and a grace smaller than the pointer itself is a target
    // nobody can hit -- which the operator would put down to the feature rather than their aim.
    private const float MinPickPixels = 24f;

    // The designation mark, in pixels. Constant like the bracket in Markers and for the same
    // reason: it says where a thing is, not how big it is, and scaling it by range makes the
    // distant target it exists for a dot.
    private const float TickIn = 11f;
    private const float TickOut = 18f;
    private const float LineHeight = 15f;

    // Amber, so it does not read as one of the blue-white system brackets or the green active one.
    private static readonly ImColor8 Designated = new(255, 190, 60, 235);
    private static readonly ImColor8 DesignatedHidden = new(255, 190, 60, 90);

    /// <summary>Reads the click, if there was one, and locks whatever is under it.</summary>
    public static void Update(WeaponSystem? system, OpticalHead? head)
    {
        if (!Requested()) return;

        if ((system?.Platform ?? head?.Platform) is not { } platform) return;

        // A craft under the pointer wins over the ground behind it: what an operator means by
        // clicking is the thing they can see, and the ground is what they meant only when there is
        // no thing. Ranging along the ray instead makes a fighter over a hillside unclickable from
        // one side and unmissable from the other.
        if (TryPickCraft(platform) is { } craft)
        {
            Aimpoint at = Aimpoint.OnVehicle(craft, KsaWorld.PositionEcl(craft),
                                             KsaWorld.VelocityEcl(craft), KsaWorld.MeanRadius(craft));

            system?.Designate(at, KsaWorld.DisplayName(craft));
            head?.Designate(at, KsaWorld.DisplayName(craft));
            return;
        }

        // Anchored to the body rather than to the ecliptic, so it is still there a second later.
        // This is the half that lets a static target be watched at all -- a structure the engine
        // does not model is nothing any sensor reports, and an operator can still name the ground
        // it stands on.
        if (KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out double lat, out double lon,
                                          out string body)
            && KsaWorld.TryAnchorToGround(groundEcl, out object? handle, out double3 anchor))
        {
            Aimpoint at = Aimpoint.OnGround(handle!, anchor, groundEcl, Vec.Zero);
            string what = $"{body} {lat:F2}, {lon:F2}";

            system?.Designate(at, what);
            head?.Designate(at, what);
        }

        // Nothing under the cursor designates nothing. A click on empty sky must not name a point
        // at infinity, which the head would then stare at for the rest of the session.
    }

    /// <summary>
    /// Marks what this installation is designated on, with its range and closing speed.
    ///
    /// <para>Drawn for the installation the panel is showing and no other, the same scoping the
    /// gesture itself uses: one mark for the thing being aimed at, rather than one per crewed
    /// system in the world all pointing at their own targets.</para>
    ///
    /// <para>Its own shape rather than the bracket in <c>Markers</c>. That one says "a weapons
    /// system is here" and this one says "that is what it is on", and two meanings sharing a mark
    /// is worse than either.</para>
    /// </summary>
    public static void Draw(WeaponSystem? system, OpticalHead? head)
    {
        Aimpoint aim = system?.Designation ?? head?.Designation ?? Aimpoint.Nothing;
        if (aim.Kind == AimpointKind.None) return;

        string what = system is not null && system.Designation.Kind != AimpointKind.None
                          ? system.DesignationName
                          : head?.DesignationName ?? "designated";

        if ((system?.Platform ?? head?.Platform) is not { } platform) return;

        // Only from the craft doing the aiming. The panel's selection is not that test: a system
        // stays on the craft carrying it and deliberately does not follow the player, so scoping to
        // the shown system alone left the mark up after switching to the target itself -- the thing
        // being aimed at, wearing its own reticule.
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

        // Measured from the launcher, not from the camera: it is the mount's problem, and the
        // camera can be kilometres away from it.
        double3 r = atEcl - platformEcl;
        double range = Vec.Len(r);
        double closing = range > 1e-6
                             ? -Vec.Dot(targetVel - KsaWorld.VelocityEcl(platform), Vec.Unit(r))
                             : 0.0;

        bool blocked = KsaWorld.IsOccluded(KsaWorld.CameraPositionEcl(), atEcl, out _);
        ImColor8 colour = blocked ? DesignatedHidden : Designated;

        ImGuiViewportPtr main = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(main.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(main.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
                                       | ImGuiWindowFlags.NoInputs
                                       | ImGuiWindowFlags.NoNav
                                       | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.NoBringToFrontOnFocus
                                       | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoBackground;

        if (ImGui.Begin("##KSArmoryDesignation", flags))
        {
            ImDrawListPtr draw = ImGui.GetWindowDrawList();

            if (inView)
            {
                DrawReticle(draw, at, colour);
                DrawReadout(draw, at, what, range, closing, colour);
            }
            else
            {
                // Off screen it keeps the ring alone, at the edge. The readout would be a caption
                // on a mark whose position is a clamp rather than a place.
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

    // Name, range and closing speed, the three the track list carries. Signed, and labelled with
    // its direction rather than by the sign alone: "-40 m/s" reads as a speed that is somehow
    // negative, where "opening" says what the target is doing.
    private static void DrawReadout(ImDrawListPtr draw, float2 at, string what,
                                    double range, double closing, ImColor8 colour)
    {
        string distance = range >= 1000.0 ? $"{range / 1000.0:F2} km" : $"{range:F0} m";
        string speed = Math.Abs(closing) < 0.5
                           ? "steady"
                           : $"{Math.Abs(closing):F0} m/s {(closing > 0.0 ? "closing" : "opening")}";

        var origin = new float2(at.X + TickOut + 6f, at.Y - TickIn);
        draw.AddText(origin, colour, what);
        draw.AddText(new float2(origin.X, origin.Y + LineHeight), colour, $"{distance}   {speed}");
    }

    // Shift and the left button, and not while a panel window wants the mouse -- otherwise every
    // click on the panel behind the sight also redesignates.
    private static bool Requested()
        => !ImGui.GetIO().WantCaptureMouse
           && ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)
           && ImGui.GetIO().KeyShift;

    // The craft nearest the cursor on screen, or null. The installation's own platform is
    // excluded: designating the thing it is bolted to points it at its own mounting.
    private static Vehicle? TryPickCraft(Vehicle platform)
    {
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
