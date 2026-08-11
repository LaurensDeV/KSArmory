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
