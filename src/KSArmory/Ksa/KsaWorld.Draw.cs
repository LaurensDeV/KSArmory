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
    /// A point in the world as the render camera sees it, differenced in double -- what the cloud
    /// pass places the burst with, so anything drawn in the cloud lands where the cloud is.
    ///
    /// <para><b>Not <see cref="TryEclToEgo"/></b>, which converts through the overlay's draw anchor:
    /// that is set only on a frame some overlay draws and cleared at the start of every frame, so a
    /// caller with no overlay of its own gets nothing on most frames -- and the fireball would go
    /// undrawn, and unlit, whenever no weapon overlay happened to be on screen.</para>
    /// </summary>
    public static bool TryEclToCameraEgo(double3 ecl, out double3 ego)
    {
        ego = default;
        try
        {
            if ((Program.GetRenderCamera() ?? Program.GetMainCamera()) is not { } camera) return false;

            ego = ecl - camera.PositionEcl;
            return Vec.IsFinite(ego);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The Ecl position that renders at a given Ego position.
    ///
    /// <para>The inverse of the conversion everything else does, and it exists for one case:
    /// handing a point to a system that takes Ecl — the particle emitters — when what is known is
    /// where something is <em>drawn</em>. A vehicle's drawn position and its
    /// <c>GetPositionEcl</c> are not the same place.</para>
    /// </summary>
    public static bool TryEgoToEcl(double3 ego, out double3 ecl)
    {
        ecl = default;
        try
        {
            if (Program.GetMainCamera() is not { } camera) return false;

            ecl = camera.EgoToEcl(ego);
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    // The overlay is drawn relative to an anchor vehicle rather than by converting absolute
    // positions. See BeginDraw for why.
    private static DrawAnchor _anchor;

    private static bool _anchored;

    /// <summary>
    /// Establishes the frame for this draw pass. Call once before any Draw*Ecl call.
    ///
    /// <para>Naive conversion — <c>camera.EclToEgo(v.GetPositionEcl())</c> — puts the overlay in
    /// the wrong place. <see cref="Vehicle.GetPositionEcl"/> returns a value computed in
    /// <c>UpdatePerFrameData</c> from <c>Orbit.StateVectors</c>, i.e. the analytic on-rails
    /// position. A landed craft is held where physics puts it, not where its degenerate Kepler
    /// orbit says, and the two differ by enough to be obvious on screen.</para>
    ///
    /// <para>KSA renders vehicles via <c>camera.GetPositionEgo(vehicle)</c>, which returns
    /// <c>-PositionCce</c> for the followed craft and uses <c>KinematicStates.PositionPhys</c>
    /// for others in the same bubble — the physics position in both cases. Anchoring to that and
    /// adding Ecl offsets (exact, since Ego is a pure translation of Ecl) puts the overlay
    /// exactly where the game draws the craft.</para>
    /// </summary>
    /// <param name="anchorEcl">
    /// The anchor's Ecl position **captured at the same instant as everything else being
    /// drawn** — not re-read here. Ecliptic positions near Earth sweep past at ~29.8 km/s, so
    /// a reference taken one frame later than the geometry it is differenced against is about
    /// 500 m stale at 60 fps, and the whole overlay lands that far from the craft.
    /// </param>
    public static bool BeginDraw(Vehicle anchor, double3 anchorEcl)
    {
        _anchored = false;
        if (!IsAlive(anchor)) return false;

        try
        {
            // The camera the frame will actually be rendered with, not the main viewport's.
            Camera camera = Program.GetRenderCamera() ?? Program.GetMainCamera();
            if (camera is null) return false;

            // See DrawAnchor for why these are sampled at different instants, and why
            // collapsing them into one puts the whole overlay beside the craft.
            //
            // GetPositionEgo, not EclToEgo. Its branching on what the camera follows is the
            // engine answering correctly per case — exact for the followed craft, physics-based
            // for others in its bubble — and it is the same call KSA renders vehicles with.
            // EclToEgo instead measures against camera.PositionEcl, which only agrees with the
            // rendered scene when the followed craft's analytic and physics positions coincide.
            // That holds for a landed launcher and fails once the camera follows something in
            // flight, which is when anything drawn to a vehicle stops lining up.
            _anchor = new DrawAnchor(camera.GetPositionEgo(anchor), anchorEcl);

            if (!_anchor.IsValid) return false;

            _anchored = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The anchor's position in the render frame, straight from the engine. Drawing here involves
    /// no arithmetic of the mod's own, so it isolates "is the anchor right" from "is the Ecl
    /// offset maths right".
    /// </summary>
    public static double3 AnchorEgo => _anchor.Ego;

    public static bool HasAnchor => _anchored;

    /// <summary>
    /// A point into the main camera's Ego by that camera alone, not the draw anchor: the inverse of
    /// what <see cref="TryProjectEgoOrClamp"/> undoes to re-base a point into another window.
    /// </summary>
    public static bool TryMainCameraEgo(double3 posEcl, out double3 posEgo)
    {
        posEgo = Vec.Zero;
        try
        {
            if (Program.GetMainCamera() is not { } main) return false;
            posEgo = main.EclToEgo(posEcl);
            return Vec.IsFinite(posEgo);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Converts an Ecl position into the anchored Ego frame.</summary>
    public static bool TryEclToEgo(double3 posEcl, out double3 posEgo)
    {
        if (_anchored)
        {
            posEgo = _anchor.ToEgo(posEcl);
            return true;
        }

        posEgo = Vec.Zero;
        return false;
    }

    /// <summary>
    /// Ego position of a vehicle, straight from the engine, so track markers sit on the craft
    /// rather than on its analytic orbit position.
    /// </summary>
    public static bool TryVehicleEgo(Vehicle v, out double3 posEgo)
    {
        posEgo = Vec.Zero;
        if (!IsAlive(v)) return false;

        // Ask the engine, for the same reason BeginDraw does: this is where the vehicle is
        // actually being drawn. Deriving it from GetPositionEcl gives the analytic on-rails
        // position instead, and lines drawn to it visibly miss the craft.
        try
        {
            Camera camera = Program.GetRenderCamera() ?? Program.GetMainCamera();
            if (camera is not null)
            {
                posEgo = camera.GetPositionEgo(v);
                if (Vec.IsFinite(posEgo)) return true;
            }
        }
        catch
        {
            // Fall back to the anchored conversion.
        }

        return TryEclToEgo(PositionEcl(v), out posEgo);
    }

    /// <summary>
    /// Where the middle of a craft's box is drawn, in Ego -- what a sight brackets and a director
    /// holds. A craft's position is its centre of mass, which on a vehicle with a heavy chassis is
    /// near the bottom: at x16 the head then sat on the wheels.
    /// </summary>
    public static bool TryVehicleCentreEgo(Vehicle v, out double3 posEgo)
    {
        if (!TryVehicleEgo(v, out posEgo)) return false;

        try
        {
            double3 centre = posEgo + (v.Asmb2Ego * v.MassToGeometryAsmb);
            if (Vec.IsFinite(centre)) posEgo = centre;
        }
        catch { /* the centre of mass, which is where it always looked */ }

        return true;
    }

    /// <summary>
    /// The middle of a craft's box where it is drawn, in Ecl -- what a camera has to point at. The
    /// simulated position is metres off it on a craft moving over the ground, by a different amount
    /// each frame, which at a 1° field is a picture jumping around its target.
    /// </summary>
    public static bool TryDrawnMiddleEcl(Vehicle v, out double3 ecl, int viewIndex = -1)
    {
        ecl = default;

        try
        {
            if (CameraFor(viewIndex) is not { } camera || !IsAlive(v)) return false;

            double3 middleAsmb = v.CenterOfMassAsmb + v.MassToGeometryAsmb;
            ecl = camera.EgoToEcl(middleAsmb.Transform(v.GetMatrixAsmb2Ego(camera)));
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    // The main camera, or one viewport's.
    private static Camera? CameraFor(int viewIndex)
        => viewIndex >= 0 && viewIndex != MainViewportIndex
            ? (TryViewport(viewIndex, out IGameViewport viewport) ? viewport.GetCamera() : null)
            : Program.GetMainCamera();

    /// <summary>The middle of a craft's box, in Ecl, against its simulated position.</summary>
    public static double3 CentreEcl(Vehicle v)
    {
        double3 at = PositionEcl(v);
        try
        {
            double3 centre = at + (v.Asmb2Ego * v.MassToGeometryAsmb);
            return Vec.IsFinite(centre) ? centre : at;
        }
        catch
        {
            return at;
        }
    }

    public static void DrawSphereEgo(double3 positionEgo, float radiusMetres, float4 colour)
    {
        Program.GizmosRenderer?.DrawSphere(positionEgo, radiusMetres, colour);
    }

    public static void DrawLineEgo(double3 startEgo, double3 endEgo, float4 colour)
    {
        Program.GizmosRenderer?.DrawLine(startEgo, endEgo, colour);
    }

    public static void DrawSphereEcl(double3 positionEcl, float radiusMetres, float4 colour)
    {
        if (Program.GizmosRenderer is null) return;
        if (!TryEclToEgo(positionEcl, out double3 ego)) return;
        Program.GizmosRenderer.DrawSphere(ego, radiusMetres, colour);
    }

    /// <summary>
    /// A torus: a ring of solid spheres, draped onto the terrain.
    ///
    /// <para>Spheres because they are the only solid thing the gizmo renderer draws — <c>Render</c>
    /// is <c>RenderSpheres</c> then <c>RenderLines</c>, with no filled polygon anywhere. Enough of
    /// them around the ring, each wider than the gap to the next, and the result reads as a tube
    /// rather than as beads.</para>
    /// </summary>
    /// <param name="tubeRadius">Thickness of the ring itself.</param>
    public static void DrawTorusEcl(double3 centreEcl, double3 normalEcl, double ringRadius,
                                    double tubeRadius, float4 colour, bool drape = true)
    {
        if (Program.GizmosRenderer is null) return;
        if (!Vec.IsFinite(centreEcl) || !(ringRadius > 0.0) || !(tubeRadius > 0.0)) return;

        double3 up = Vec.Unit(normalEcl);
        if (Vec.Len2(up) < 0.5) return;

        double3 seed = Math.Abs(up.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);
        double3 a = Vec.Unit(Vec.Cross(up, seed));
        double3 b = Vec.Unit(Vec.Cross(up, a));

        // Spaced closer together than they are wide, or it beads. Bounded so a large ring cannot
        // ask for thousands of spheres.
        int steps = (int)Math.Clamp(Math.Ceiling(Math.Tau * ringRadius / tubeRadius), 16, 160);
        Celestial? body = drape ? NearestCelestial(centreEcl) : null;

        for (int i = 0; i < steps; i++)
        {
            double angle = Math.Tau * i / steps;
            double3 at = centreEcl + ((a * Math.Cos(angle)) + (b * Math.Sin(angle))) * ringRadius;

            // Each bead sits on the ground under it, so the ring follows a slope instead of
            // burying one side and floating the other.
            if (body is not null && TrySnapToGround(body, at, out double3 ground, out _)) at = ground;

            if (TryEclToEgo(at, out double3 ego))
            {
                Program.GizmosRenderer.DrawSphere(ego, (float)tubeRadius, colour);
            }
        }
    }

    public static void DrawLineEcl(double3 startEcl, double3 endEcl, float4 colour)
    {
        if (Program.GizmosRenderer is null) return;
        if (!TryEclToEgo(startEcl, out double3 a)) return;
        if (!TryEclToEgo(endEcl, out double3 b)) return;
        Program.GizmosRenderer.DrawLine(a, b, colour);
    }
}
