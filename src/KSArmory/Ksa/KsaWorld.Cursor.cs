using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

internal static partial class KsaWorld
{
    /// <summary>
    /// Where the cursor sits relative to the middle of the main view, in screen pixels.
    ///
    /// <para>The middle rather than the viewport's origin, because this is what a head being
    /// dragged is measured against: it is at rest when the cursor is in the middle of the picture
    /// it is producing.</para>
    /// </summary>
    /// <param name="halfHeightPx">
    /// Half the view's height, which is how far the cursor can travel before it runs out of
    /// screen. What a command is measured against, so the same drag means the same thing whatever
    /// the window size.
    /// </param>
    public static bool TryCursorFromViewCentre(float deadZonePx, out float2 fromCentre,
                                               out bool commands, out float halfHeightPx)
    {
        fromCentre = default;
        commands = false;
        halfHeightPx = 1f;

        try
        {
            ImGuiViewportPtr main = ImGui.GetMainViewport();
            float2 centre = new(main.Pos.X + main.Size.X * 0.5f, main.Pos.Y + main.Size.Y * 0.5f);

            halfHeightPx = Math.Max(1f, main.Size.Y * 0.5f);
            commands = CursorAim.OutsideDeadZone(ImGui.GetMousePos(), centre, deadZonePx,
                                                 out fromCentre);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reports how the cursor is being turned into a ray, and how far the answer lands from the
    /// pointer once projected back. The round trip is the measurement that matters: it is the
    /// error actually on screen, whatever the intermediate conventions turn out to be.
    /// </summary>
    public static string DescribeCursorRay(double3 solvedEcl)
    {
        try
        {
            float2 cursor = ImGui.GetMousePos();
            ImGuiViewportPtr main = ImGui.GetMainViewport();

            string chosen = "none";
            ReadOnlySpan<IGameViewport> viewports = GameViewports;
            for (int i = 0; i < viewports.Length; i++)
            {
                IGameViewport v = viewports[i];
                if (!v.Visible) continue;
                if (!CursorAim.TryToViewport(cursor, v.Position, v.Width, v.Height, out float2 local))
                {
                    continue;
                }

                Camera? c = v.GetCamera();
                chosen = $"vp{i} pos={v.Position.X:F0},{v.Position.Y:F0} "
                         + $"size={v.Width}x{v.Height} fb={c?.FramebufferSize.X}x{c?.FramebufferSize.Y} "
                         + $"local={local.X:F0},{local.Y:F0}";
                break;
            }

            string back = TryProjectAhead(solvedEcl, out float2 screen)
                              ? $"back={screen.X:F0},{screen.Y:F0} "
                                + $"err={screen.X - cursor.X:F0},{screen.Y - cursor.Y:F0}"
                              : "back=offscreen";

            return $"cursor={cursor.X:F0},{cursor.Y:F0} "
                   + $"mainvp={main.Pos.X:F0},{main.Pos.Y:F0} {main.Size.X:F0}x{main.Size.Y:F0} "
                   + $"{chosen} {back}";
        }
        catch (Exception e)
        {
            return $"(describe failed: {e.Message})";
        }
    }

    public static bool TryCursorRayEcl(out double3 originEcl, out double3 directionEcl)
    {
        originEcl = default;
        directionEcl = default;
        try
        {
            float2 cursor = ImGui.GetMousePos();

            ReadOnlySpan<IGameViewport> viewports = GameViewports;
            for (int i = 0; i < viewports.Length; i++)
            {
                IGameViewport v = viewports[i];
                if (!v.Visible) continue;

                if (v.GetCamera() is not { } camera) continue;

                // Framebuffer pixels, not viewport pixels: ScreenToEgoRay divides by the camera's
                // own framebuffer, and a render or display scale makes those different sizes.
                if (!CursorAim.TryToFramebuffer(cursor, v.Position, v.Width, v.Height,
                                                camera.FramebufferSize.X, camera.FramebufferSize.Y,
                                                out float2 local))
                {
                    continue;
                }

                double3 direction = camera.ScreenToEgoRay(local).Direction;
                if (!CursorAim.IsUsableDirection(direction)) continue;

                originEcl = camera.EgoToEcl(Vec.Zero);
                directionEcl = Vec.Unit(direction);
                return Vec.IsFinite(originEcl);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where the cursor points, as a bearing from <paramref name="mountEcl"/>.
    ///
    /// <para>Takes a mount because a bearing without an origin is not an aim. The camera stands
    /// well away from any launcher on screen, so its direction and the launcher's coincide only
    /// for something at infinity: against sky they agree, and against ground a few tens of metres
    /// off they disagree by tens of degrees. There is deliberately no direction-only form of this
    /// next to it — the two are indistinguishable wherever anyone tests them first.</para>
    ///
    /// <para>The ray and the range are solved once a frame and shared, so several systems on
    /// mouse aim cost one terrain query between them rather than one each.</para>
    /// </summary>
    public static bool TryCursorAimEcl(double3 mountEcl, out double3 directionEcl)
    {
        directionEcl = default;
        SolveCursorAim();

        return _cursorAimValid
               && CursorAim.TryAimFromMount(_cursorAimOrigin, _cursorAimDirection, _cursorAimRange,
                                            mountEcl, out directionEcl);
    }

    /// <summary>
    /// Where the cursor's ray meets the ground, when the ground is the first thing it meets. False
    /// over sky or over a craft, which give a shell nowhere to land.
    /// </summary>
    public static bool TryCursorGroundEcl(out double3 groundEcl)
    {
        SolveCursorAim();
        groundEcl = _cursorAimGround;

        return _cursorAimValid && _cursorAimOnGround && Vec.IsFinite(groundEcl);
    }

    /// <summary>
    /// Drops the frame's cached cursor solve. Called once where the simulation is stepped.
    /// </summary>
    public static void BeginFrame()
    {
        _cursorAimSolved = false;

        // And the draw anchor, which every Draw*Ecl call converts through. Left standing from an
        // earlier frame it is the platform's position then, not now, and the overlay lands beside
        // the craft by whatever the world has moved since: about 500 m per frame near Earth, and
        // unbounded once a frame passes without any pass establishing one. Cleared here so a
        // caller that forgets BeginDraw draws nothing, which is visible, rather than drawing in
        // the wrong place, which is not.
        _anchored = false;
    }

    // Taken as the range when the ray meets no body. Only one thing reads it -- the parallax
    // between the camera and the mount, which is the standoff over this -- and that error is
    // systematic, always toward the camera's side, and shrinks as the view zooms in. At 100 m of
    // standoff, 20 km gives 0.29 degrees and 200 km gives 0.03, under a third of a pixel at
    // 1080p. Nothing else consumes it: the designator resolves its own ground point.
    private const double CursorSkyRange = 200_000.0;

    private static bool _cursorAimSolved;

    private static bool _cursorAimValid;

    private static double3 _cursorAimOrigin;

    private static double3 _cursorAimDirection;

    private static double _cursorAimRange;

    private static bool _cursorAimOnGround;

    private static double3 _cursorAimGround;

    // How far along the ray the nearest craft is, if it meets one.
    //
    // The bounding sphere only rejects; the mesh decides. A craft's sphere is far wider than the
    // craft, so close up it covers a large part of the screen and the aim snaps to it long before
    // the cursor is over anything. Part.RayCastEgo is watertight and per-triangle against the real
    // mesh -- it is what KSA highlights parts with -- so it answers the question the player is
    // actually asking. It is also a walk over every part, which is why the sphere runs first: one
    // dot product skips a craft the ray is nowhere near.
    //
    // The ray is Ego, and the distance it reports is along it from the camera, so it is the range
    // wanted here with no conversion. Ego is a pure translation of Ecl, so the direction is the
    // same in both -- only an origin would need care.
    private static bool TryCursorCraftHit(double3 eye, double3 direction, Vehicle? exclude,
                                          out Vehicle? hitCraft, out double range)
    {
        hitCraft = null;
        range = double.MaxValue;

        try
        {
            if (Program.MainViewport?.GetCamera() is not { } camera) return false;

            Ray ray = new() { Origin = default, Direction = Vec.Unit(direction) };

            CollectVehicles(_pickScratch);

            foreach (Vehicle craft in _pickScratch)
            {
                if (ReferenceEquals(craft, exclude)) continue;

                double radius = MeanRadius(craft);
                if (!(radius > 0.0)) continue;

                double3 centre = PositionEcl(craft);

                // Standing inside one is not aiming at it: from in there the sphere's only hit
                // ahead is its far side, which is behind the operator's own launcher.
                if (Vec.Len(eye - centre) <= radius) continue;
                if (!Picking.TryHitSphere(eye, direction, centre, radius, out double3 onSphere)) continue;

                if (craft.Parts is not { } tree) continue;

                double4x4 asmb2Ego = craft.GetMatrixAsmb2Ego(camera);
                ReadOnlySpan<Part> parts = tree.Parts;

                bool onMesh = false;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (!parts[i].RayCastEgo(in asmb2Ego, ray, out double hit, out _,
                                             out _, out _, out _, out _, out _, out _))
                    {
                        continue;
                    }

                    onMesh = true;
                    if (hit > 0.0 && hit < range)
                    {
                        range = hit;
                        hitCraft = craft;
                    }
                }

                // A kitten has no mesh to hit: its only part is Core's KittenBackPackPart, whose
                // subpart is declared empty, and the body itself is drawn by the character
                // renderer rather than by a part. The engine picks one by its bounding sphere for
                // exactly that reason. Craft that do carry geometry are still held to the
                // triangle, because their sphere stands metres clear of the hull and snapping to
                // it puts the aim beside everything it is pointed at.
                if (!onMesh && !HasPickableMesh(parts))
                {
                    double toSphere = Vec.Len(onSphere - eye);
                    if (toSphere > 0.0 && toSphere < range)
                    {
                        range = toSphere;
                        hitCraft = craft;
                    }
                }
            }

            return range < double.MaxValue;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The nearest craft a ray from <paramref name="eye"/> meets within <paramref name="maxRange"/>,
    /// on its mesh, leaving out <paramref name="exclude"/>. Positions are the simulated ones, so the
    /// answer is in the same frame as the eye a caller derives from them.
    /// </summary>
    public static bool TryRayCraftHit(double3 eye, double3 direction, Vehicle? exclude, double maxRange,
                                      out Vehicle? hitCraft, out double range)
        => TryRayPartHit(eye, direction, exclude, maxRange, out hitCraft, out _, out range);

    /// <summary><see cref="TryRayCraftHit"/>, also naming the part the ray met first.</summary>
    public static bool TryRayPartHit(double3 eye, double3 direction, Vehicle? exclude, double maxRange,
                                     out Vehicle? hitCraft, out Part? hitPart, out double range)
    {
        hitCraft = null;
        hitPart = null;
        range = maxRange;

        try
        {
            Ray ray = new() { Origin = default, Direction = Vec.Unit(direction) };
            CollectVehicles(_pickScratch);

            foreach (Vehicle craft in _pickScratch)
            {
                if (ReferenceEquals(craft, exclude) || craft.Parts is not { } tree) continue;

                double radius = MeanRadius(craft);
                double3 centre = PositionEcl(craft);
                if (!(radius > 0.0) || Vec.Len(centre - eye) - radius > range) continue;
                if (!Picking.TryHitSphere(eye, direction, centre, radius, out _)) continue;

                double4x4 asmb2Ego = craft.GetMatrixAsmb2Ego(centre - eye);
                ReadOnlySpan<Part> parts = tree.Parts;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].RayCastEgo(in asmb2Ego, ray, out double hit, out _,
                                            out _, out _, out _, out _, out _, out _)
                        && hit > 0.0 && hit < range)
                    {
                        range = hit;
                        hitCraft = craft;
                        hitPart = parts[i];
                    }
                }
            }

            return hitCraft is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a craft's own parts stand across a line of sight leaving a point on it -- what a
    /// targeting pod calls MASK. False when the craft could not be cast against, which the caller
    /// reads as "no claim", never as clear.
    /// </summary>
    /// <param name="craftEcl">The craft's position, from the same sample as <paramref name="fromEcl"/>:
    /// the cast is in a frame centred on the eye, so only their difference matters.</param>
    public static bool TryCraftMasks(Vehicle craft, double3 craftEcl, double3 fromEcl, double3 direction,
                                     double reach, out bool masked)
    {
        masked = false;

        try
        {
            if (!IsAlive(craft) || craft.Parts is not { } tree) return false;
            if (!(Vec.Len2(direction) > 0.0) || !(reach > 0.0)) return false;

            double4x4 asmb2Ego = craft.GetMatrixAsmb2Ego(craftEcl - fromEcl);
            Ray ray = new() { Origin = default, Direction = Vec.Unit(direction) };

            ReadOnlySpan<Part> parts = tree.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!parts[i].RayCastEgo(in asmb2Ego, ray, out double hit, out _,
                                         out _, out _, out _, out _, out _, out _))
                {
                    continue;
                }

                // Past a few centimetres: the eye sits just outside the head's own glass.
                if (hit > 0.05 && hit < reach)
                {
                    masked = true;
                    return true;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // The craft being flown is usually the one carrying the launcher, and it sits under the cursor
    // for most of an orbit view. Snapping the aim onto it would whip the turret round to point at
    // its own hull every time the pointer crossed it.
    private static bool TryCursorCraftRange(double3 eye, double3 direction, out double range)
        => TryCursorCraftHit(eye, direction, ControlledVehicle, out _, out range);

    /// <summary>
    /// The craft the cursor is over, judged by its hull rather than by where its centre lands on
    /// screen, or null.
    ///
    /// <para>A craft close enough to fill a patch of the screen is under the pointer wherever on it
    /// the click lands, while its centre can be far outside any fixed grace. The cast is
    /// <c>Part.RayCastEgo</c>, the one KSA highlights parts with, so a craft wearing the engine's
    /// hover outline is the craft this answers. Ground nearer along the ray hides it.</para>
    /// </summary>
    public static Vehicle? CraftUnderCursor(Vehicle? exclude)
    {
        if (!TryCursorRayEcl(out double3 eye, out double3 direction)) return null;

        if (!TryCursorCraftHit(eye, direction, exclude, out Vehicle? craft, out double range))
        {
            return null;
        }

        bool groundFirst = TryCursorGroundPoint(out double3 ground, out _, out _, out _)
                           && Vec.Len(ground - eye) < range;

        return groundFirst ? null : craft;
    }

    // Whether a ray could ever hit this craft. Mirrors what RayCastEgo actually tests -- one level
    // of SubParts, each needing a MeshViewModule -- so the answer cannot disagree with the cast.
    internal static bool HasPickableMesh(ReadOnlySpan<Part> parts)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            ReadOnlySpan<Part> subs = parts[i].SubParts;

            for (int j = 0; j < subs.Length; j++)
            {
                if (subs[j].Modules.HasAny<MeshViewModule>()) return true;
            }
        }

        return false;
    }

    private static readonly List<Vehicle> _pickScratch = [];

    private static void SolveCursorAim()
    {
        if (_cursorAimSolved) return;

        _cursorAimSolved = true;
        _cursorAimValid = false;

        if (!TryCursorRayEcl(out double3 eye, out double3 direction)) return;

        // The terrain-refined hit, not the mean sphere: the sphere is sea level, so over a pad it
        // sits below the ground and puts the aim point past what the pointer is actually on.
        bool onGround = TryCursorGroundPoint(out double3 ground, out _, out _, out _);
        double range = onGround ? Vec.Len(ground - eye) : CursorSkyRange;

        // Whatever the ray meets first, which need not be the planet. Tested only against
        // celestials, a cursor held on a craft resolves to the ground *behind* it and the aim goes
        // through it -- by however far the craft stands off the terrain, which for anything flying
        // is the whole point of aiming at it.
        if (TryCursorCraftRange(eye, direction, out double onCraft) && onCraft < range)
        {
            range = onCraft;
            onGround = false;
        }

        if (!double.IsFinite(range) || range <= 0.0)
        {
            range = CursorSkyRange;
            onGround = false;
        }

        _cursorAimOrigin = eye;
        _cursorAimDirection = direction;
        _cursorAimRange = range;
        _cursorAimGround = ground;
        _cursorAimOnGround = onGround;
        _cursorAimValid = true;
    }
}
