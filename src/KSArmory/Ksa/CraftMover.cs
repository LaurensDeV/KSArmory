using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Pick a craft up with one click and set it down with the next — a development tool for laying
/// out a test range without flying anything into place.
///
/// <para>Placement goes through <c>Vehicle.TeleportToLocation</c>, which is how the game moves a
/// craft itself: it builds the kinematic state from the craft's bounding box, so the hull arrives
/// resting on the terrain rather than half inside it. Writing a position directly would put the
/// craft's origin at the point and leave the rest wherever that happens to be.</para>
///
/// <para>The craft does <em>not</em> hover along under the cursor while held. Placement is a
/// buffered engine event, so following the pointer would mean re-initialising the vehicle's
/// kinematic state sixty times a second, with its velocity and orbit rebuilt on every one. What is
/// held is the <em>choice</em>: the craft stays where it is, the ground under the cursor is marked,
/// and the second click moves it once.</para>
/// </summary>
internal sealed class CraftMover
{
    private static readonly float4 HeldColour = new(1.0f, 0.85f, 0.3f, 1f);
    private static readonly float4 TargetColour = new(0.4f, 1.0f, 0.6f, 1f);

    // Pointer distance, in pixels, that counts as clicking a craft.
    private const float PickRadius = 40f;

    // Radius of the ring drawn where the craft would land, in metres. Fixed rather than scaled to
    // the craft: it marks a spot on the ground, and a big vessel would otherwise hide it.
    private const double MarkerRadius = 12.0;

    private readonly List<Vehicle> _craft = [];
    private readonly List<float2> _screen = [];

    private Vehicle? _held;

    /// <summary>The craft waiting to be put down, if any.</summary>
    public Vehicle? Held => _held;

    public void Release() => _held = null;

    /// <summary>
    /// One frame of the tool. Does nothing at all while switched off, including reading the mouse.
    /// </summary>
    public void Update(Config config)
    {
        if (!config.MoveCraftWithMouse)
        {
            _held = null;
            return;
        }

        if (!KsaWorld.IsAlive(_held)) _held = null;

        // The panel and the marker labels are windows; a click meant for one of them is not a
        // click on the world behind it.
        if (ImGui.GetIO().WantCaptureMouse) return;
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)) return;

        if (_held is null) PickUp();
        else PutDown();
    }

    private void PickUp()
    {
        KsaWorld.CollectVehicles(_craft);
        if (_craft.Count == 0) return;

        _screen.Clear();
        for (int i = 0; i < _craft.Count; i++)
        {
            // Off-screen craft get a position that cannot be picked, so the indices stay aligned
            // with _craft and the nearest-on-screen answer means what it says.
            _screen.Add(KsaWorld.TryProjectAhead(KsaWorld.PositionEcl(_craft[i]), out float2 at)
                            ? at
                            : new float2(float.MaxValue, float.MaxValue));
        }

        int pick = Picking.NearestOnScreen(_screen, ImGui.GetMousePos(), PickRadius);
        if (pick < 0) return;

        _held = _craft[pick];
        Log.Info($"holding {KsaWorld.DisplayName(_held)} - click the ground to set it down");
    }

    private void PutDown()
    {
        if (_held is not { } craft) return;

        if (!KsaWorld.TryCursorGroundPoint(out _, out double lat, out double lon, out string body))
        {
            Log.Info("nothing under the cursor to set it down on");
            return;
        }

        if (KsaWorld.TryPlaceOnSurface(craft, body, lat, lon))
        {
            Log.Info($"placed {KsaWorld.DisplayName(craft)} on {body} at "
                     + $"{lat:F4}, {lon:F4}");
        }

        _held = null;
    }

    /// <summary>Shows what is held and where it would go.</summary>
    public void Draw(Config config)
    {
        if (!config.MoveCraftWithMouse || _held is not { } craft) return;
        if (!KsaWorld.IsAlive(craft)) return;

        double3 heldEcl = KsaWorld.PositionEcl(craft);
        if (!KsaWorld.BeginDraw(craft, heldEcl)) return;

        // A ring on the craft being held, so it is obvious which one the next click moves.
        KsaWorld.DrawSphereEcl(heldEcl, (float)Math.Max(KsaWorld.MeanRadius(craft) * 1.4, 5.0),
                               HeldColour);

        if (!KsaWorld.TryCursorGroundPoint(out double3 groundEcl, out _, out _, out _)) return;

        KsaWorld.DrawSphereEcl(groundEcl, (float)MarkerRadius, TargetColour);
        KsaWorld.DrawLineEcl(heldEcl, groundEcl, TargetColour);
    }
}
