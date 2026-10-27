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
    /// <para>Not <see cref="TryAnchoredEgo"/>, whose anchor exists only on a frame some overlay draws.</para>
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
    /// <para>For handing the particle emitters, which take Ecl, a point known by where it is drawn.</para>
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

    private static DrawAnchor _anchor;

    private static bool _anchored;

    /// <summary>
    /// Establishes the frame for this draw pass. Call once before any Draw*Ecl call.
    ///
    /// <para><see cref="Vehicle.GetPositionEcl"/> is the analytic on-rails position, metres off where
    /// a landed craft is drawn. KSA draws through <c>camera.GetPositionEgo(vehicle)</c>, the physics
    /// position; anchoring there and adding Ecl offsets puts the overlay on the craft.</para>
    /// </summary>
    /// <param name="anchorEcl">
    /// Captured at the same instant as the geometry drawn, not re-read here: a frame later is about
    /// 500 m at 29.8 km/s and 60 fps.
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

            // Two instants on purpose: see DrawAnchor. GetPositionEgo, not EclToEgo, which agrees with
            // the render only while the followed craft's analytic and physics positions coincide.
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

    // False on any frame no overlay has called BeginDraw: the anchor is cleared by BeginFrame.
    public static bool TryAnchoredEgo(double3 posEcl, out double3 posEgo)
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

        // From the engine, as BeginDraw: GetPositionEcl is the analytic on-rails position.
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

        return TryAnchoredEgo(PositionEcl(v), out posEgo);
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
        if (!TryAnchoredEgo(positionEcl, out double3 ego)) return;
        Program.GizmosRenderer.DrawSphere(ego, radiusMetres, colour);
    }

    /// <summary>
    /// A torus: a ring of solid spheres, draped onto the terrain.
    ///
    /// <para>Spheres, the only solid the gizmo renderer draws.</para>
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

            if (TryAnchoredEgo(at, out double3 ego))
            {
                Program.GizmosRenderer.DrawSphere(ego, (float)tubeRadius, colour);
            }
        }
    }

    public static void DrawLineEcl(double3 startEcl, double3 endEcl, float4 colour)
    {
        if (Program.GizmosRenderer is null) return;
        if (!TryAnchoredEgo(startEcl, out double3 a)) return;
        if (!TryAnchoredEgo(endEcl, out double3 b)) return;
        Program.GizmosRenderer.DrawLine(a, b, colour);
    }
}
