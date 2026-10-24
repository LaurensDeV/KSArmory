using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Pick a craft up with one click and set it down with the next, to lay out a test range.
/// <para>Placed through <c>Vehicle.TeleportToLocation</c>, which builds the state from the bounding box
/// so the hull rests on the terrain. A held craft stays put until the second click: placement is a
/// buffered engine event, and following the pointer would rebuild its state and orbit every frame.</para>
/// </summary>
internal sealed class CraftMover
{
    private static readonly float4 HoverColour = new(0.6f, 0.8f, 1.0f, 0.8f);
    private static readonly float4 HeldColour = new(1.0f, 0.85f, 0.3f, 1f);
    private static readonly float4 TargetColour = new(0.4f, 1.0f, 0.6f, 1f);

    // Smallest and largest a craft's clickable area may be, in pixels. The floor keeps a distant
    // vessel reachable when it is a couple of pixels across; the ceiling stops one filling the
    // screen from swallowing every click meant for the ground beside it.
    private const float MinPickRadius = 14f;
    private const float MaxPickRadius = 140f;

    // Closer than this to where it already is and the click means "leave it". A vehicle is several
    // metres across, so anything inside this is the same spot as far as anyone aiming is concerned.
    private const double StayPut = 12.0;

    // Radius of the ring drawn where the craft would land, in metres. Fixed rather than scaled to
    // the craft: it marks a spot on the ground, and a big vessel would otherwise hide it.
    private const double MarkerRadius = 12.0;

    private readonly List<Vehicle> _craft = [];
    private readonly List<float2> _screen = [];
    private readonly List<float> _reach = [];

    private Vehicle? _held;
    private Vehicle? _hovered;
    private int _trace;
    private int _aimTrace;

    /// <summary>The craft waiting to be put down, if any.</summary>
    public Vehicle? Held => _held;

    /// <summary>The craft the pointer is over, which the next click would pick up.</summary>
    public Vehicle? Hovered => _hovered;

    public void Release()
    {
        _held = null;
        _hovered = null;
    }

    /// <summary>Does nothing while switched off, not even reading the mouse.</summary>
    public void Update(Config config)
    {
        if (!config.MoveCraftWithMouse)
        {
            Release();
            return;
        }

        if (!KsaWorld.IsAlive(_held)) _held = null;
        if (!KsaWorld.IsAlive(_hovered)) _hovered = null;

        bool wantCapture = ImGui.GetIO().WantCaptureMouse;
        // Shift is the designate gesture, so a shift-click is not a click on the world.
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)
                       && !ImGui.GetIO().KeyShift;
        bool down = ImGui.IsMouseDown(ImGuiMouseButton.Left);

        // Whether a click reaches this hook at all is not obvious -- ImGui, KSA's own input and
        // this mod all read the same mouse -- so it stays traced, quietly.
        if (clicked || down || ++_trace % 120 == 0)
        {
            float2 where = ImGui.GetMousePos();
            Log.Debug(() => $"mover: capture={wantCapture} clicked={clicked} down={down} "
                            + $"cursor={where.X:F0},{where.Y:F0} held={_held is not null}");
        }

        // A click on the panel or a marker label is not a click on the world behind it.
        if (wantCapture)
        {
            _hovered = null;
            return;
        }

        // Every frame, not only on click, so the hover ring shows what a click would take.
        _hovered = UnderCursor();

        if (!clicked) return;

        if (_held is null) PickUp();
        else PutDown();
    }

    // The craft nearest the pointer within reach, or null.
    private Vehicle? UnderCursor()
    {
        KsaWorld.CollectVehicles(_craft);
        if (_craft.Count == 0) return null;

        _screen.Clear();
        _reach.Clear();
        for (int i = 0; i < _craft.Count; i++)
        {
            // Off-screen craft get a position that cannot be picked, so the indices stay aligned
            // with _craft and the nearest answer means what it says.
            double3 atEcl = KsaWorld.PositionEcl(_craft[i]);
            _screen.Add(KsaWorld.TryProjectAhead(atEcl, out float2 at)
                            ? at
                            : new float2(float.MaxValue, float.MaxValue));

            // As big as the craft looks, so pointing anywhere on a vessel picks it rather than
            // only its centre. MeanRadius is a bounding measure, so this is its whole extent.
            float reach = MinPickRadius;
            if (KsaWorld.TryApparentRadiusPixels(atEcl, KsaWorld.MeanRadius(_craft[i]),
                                                 out float pixels))
            {
                reach = Math.Clamp(pixels, MinPickRadius, MaxPickRadius);
            }

            _reach.Add(reach);
        }

        int pick = Picking.NearestWithin(_screen, _reach, ImGui.GetMousePos());
        return pick < 0 ? null : _craft[pick];
    }

    private void PickUp()
    {
        if (_hovered is not { } craft) return;

        _held = craft;
        _hovered = null;
        Log.Info($"holding {KsaWorld.DisplayName(craft)} - click the ground to set it down");
    }


    private void PutDown()
    {
        if (_held is not { } craft) return;

        // Pointing at the held craft means "leave it". Placing re-derives a position from the
        // latitude and longitude and the surface rule, which is not where the craft is: on a
        // launch pad it is metres below, so a click that should change nothing drops it.
        if (ReferenceEquals(_hovered, craft))
        {
            Log.Info($"left {KsaWorld.DisplayName(craft)} where it was");
            _held = null;
            return;
        }

        if (!TryTarget(out double3 target, out double lat, out double lon, out string body))
        {
            Log.Info("nothing under the cursor to set it down on");
            return;
        }

        // Or near enough to it. The pick radius is generous, so the cursor can be a little off the
        // craft and still mean the same thing.
        if (Vec.Len(target - KsaWorld.PositionEcl(craft)) < StayPut)
        {
            Log.Info($"left {KsaWorld.DisplayName(craft)} where it was");
            _held = null;
            return;
        }

        if (KsaWorld.TryPlaceOnSurface(craft, body, lat, lon))
        {
            Log.Info($"placed {KsaWorld.DisplayName(craft)} on {body} at "
                     + $"{lat:F4}, {lon:F4}");
        }

        _held = null;
    }

    // A craft under the pointer answers with its own footing: the ray through its middle meets the
    // ground behind it, a vehicle-height of parallax past it.
    private bool TryTarget(out double3 groundEcl, out double latitudeDeg, out double longitudeDeg,
                           out string bodyName)
    {
        // The hover is already worked out this frame; asking again would project every craft
        // twice more per frame for the same answer.
        if (_hovered is { } over
            && KsaWorld.TryCraftSurfacePoint(over, out groundEcl, out latitudeDeg,
                                             out longitudeDeg, out bodyName))
        {
            return true;
        }

        return KsaWorld.TryCursorGroundPoint(out groundEcl, out latitudeDeg, out longitudeDeg,
                                             out bodyName);
    }

    /// <summary>Shows what the next click would pick up, or what is held and where it would go.</summary>
    public void Draw(Config config)
    {
        if (!config.MoveCraftWithMouse) return;

        if (_held is null)
        {
            if (_hovered is not { } candidate || !KsaWorld.IsAlive(candidate)) return;

            double3 atEcl = KsaWorld.PositionEcl(candidate);
            if (!KsaWorld.BeginDraw(candidate, atEcl)) return;

            DrawFootprint(candidate, atEcl, HoverColour);
            return;
        }

        if (!KsaWorld.IsAlive(_held)) return;

        double3 heldEcl = KsaWorld.PositionEcl(_held);
        if (!KsaWorld.BeginDraw(_held, heldEcl)) return;

        DrawFootprint(_held, heldEcl, HeldColour);

        if (!TryTarget(out double3 groundEcl, out _, out _, out _)) return;

        // The round trip: where the marker lands, projected back, against where the pointer is.
        if (++_aimTrace % 120 == 0) Log.Debug(() => $"aim: {KsaWorld.DescribeCursorRay(groundEcl)}");

        // Where it would land: a ring on the ground, a short stalk so the spot reads against
        // sloping terrain, and the line from the craft to it.
        double3 up = Up(_held, groundEcl);

        KsaWorld.DrawTorusEcl(groundEcl, up, MarkerRadius, MarkerRadius * 0.14, TargetColour);
        KsaWorld.DrawLineEcl(groundEcl, groundEcl + up * (MarkerRadius * 0.8), TargetColour);
        KsaWorld.DrawLineEcl(heldEcl, groundEcl, TargetColour);
    }

    // A ring at the craft's feet rather than a sphere around it. A sphere big enough to read as
    // "this one" is by construction big enough to hide what it is pointing at.
    private static void DrawFootprint(Vehicle craft, double3 atEcl, float4 colour)
    {
        double radius = Math.Max(KsaWorld.MeanRadius(craft) * 1.4, 5.0);
        double3 up = Up(craft, atEcl);

        // Dropped to the craft's base, so it lies on the ground rather than cutting through the
        // middle of the hull.
        KsaWorld.DrawTorusEcl(atEcl - up * (radius / 1.4), up, radius, radius * 0.10, colour);
    }

    private static double3 Up(Vehicle craft, double3 atEcl)
    {
        double3 up = -Vec.Unit(KsaWorld.GravityAt(craft, atEcl));

        return Vec.Len2(up) < 0.5 ? new double3(0, 0, 1) : up;
    }
}
