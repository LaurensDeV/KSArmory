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
    /// Whether a celestial body sits between two points — the planet in the way.
    ///
    /// <para>Every body in the system, not just the one being orbited: a marker hidden behind a
    /// moon is as unusable as one hidden behind the world under it.</para>
    /// </summary>
    /// <param name="blockedBy">The first body found in the way, or empty.</param>
    public static bool IsOccluded(double3 eyeEcl, double3 targetEcl, out string blockedBy)
        => IsOccluded(eyeEcl, targetEcl, 0.0, out blockedBy);

    /// <inheritdoc cref="IsOccluded(double3, double3, out string)"/>
    /// <param name="terrainMargin">
    /// Metres to inflate every body by, so a contact skimming the limb counts as hidden.
    /// </param>
    public static bool IsOccluded(double3 eyeEcl, double3 targetEcl, double terrainMargin,
                                  out string blockedBy)
    {
        blockedBy = string.Empty;
        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;
                if (!LineOfSight.BlockedByTerrain(eyeEcl, targetEcl, body.GetPositionEcl(),
                                                  body.MeanRadius, terrainMargin))
                {
                    continue;
                }

                blockedBy = body.Id ?? string.Empty;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // Each modifier contributes its amplitude times a weight, a lookup and a noise value all in
    // [0, 1], so the sum of the declared amplitudes is a supremum rather than an estimate. Earth's
    // total 7,525 m.
    private const double TerrainModifierHeadroomMetres = 8_000.0;

    // A bound the cheap reject may stand in front of the exact test with. Not MaxTerrainHeightApprox,
    // computed before the modifiers exist: ~5,692 m where the base field reaches 8,011
    // (docs/KSA-TERRAIN.md). MaxTerrainRadius is exact for the base field.
    private static double MaxTerrainHeightMetres(Celestial body)
    {
        try
        {
            double exact = body.MaxTerrainRadius - body.MeanRadius;

            return double.IsFinite(exact) && exact > 0.0
                       ? exact + TerrainModifierHeadroomMetres
                       : body.MaxTerrainHeightApprox;
        }
        catch
        {
            return body.MaxTerrainHeightApprox;
        }
    }

    /// <summary>
    /// Whether the skyline of the body under the eye hides the target, against the real height
    /// field. Asked only after <see cref="IsOccluded"/> and the cheap rejects have passed.
    /// </summary>
    /// <param name="samples">Height lookups this look may cost. Zero asks none.</param>
    public static bool IsHiddenByTerrain(double3 eyeEcl, double3 targetEcl, int samples,
                                         double clearance, out string blockedBy)
    {
        blockedBy = string.Empty;
        if (samples <= 0) return false;

        try
        {
            if (NearestBody(eyeEcl) is not { } body) return false;

            double3 centre = body.GetPositionEcl();

            if (!TerrainMask.Blocked(eyeEcl, targetEcl, centre, body.MeanRadius,
                                     MaxTerrainHeightMetres(body), samples, clearance,
                                     new TerrainHeights(body)))
            {
                return false;
            }

            blockedBy = body.Id ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The mean sphere of the body under a point: its centre and its radius.
    ///
    /// <para>A radius of zero means no body: "no ground here", not ground at the origin.</para>
    /// </summary>
    public static void MeanSphereUnder(double3 pointEcl, out double3 centreEcl, out double radius)
    {
        centreEcl = Vec.Zero;
        radius = 0.0;

        try
        {
            if (NearestBody(pointEcl) is not { } body) return;

            centreEcl = body.GetPositionEcl();
            radius = body.MeanRadius;
        }
        catch
        {
            radius = 0.0;
        }
    }

    // The body whose surface the point is nearest, by depth below the mean sphere rather than by
    // distance: a sensor low over a moon is far closer to the ground it is looking across than to
    // the planet that moon orbits.
    private static Celestial? NearestBody(double3 pointEcl)
    {
        if (Universe.CurrentSystem is not { } system) return null;

        Celestial? nearest = null;
        double nearestDepth = double.MaxValue;

        for (int i = 0; i < system.Count; i++)
        {
            if (system.GetIndex(i) is not Celestial body) continue;

            double depth = Vec.Len(pointEcl - body.GetPositionEcl()) - body.MeanRadius;
            if (depth >= nearestDepth) continue;

            nearest = body;
            nearestDepth = depth;
        }

        return nearest;
    }

    /// <summary>
    /// Anchors an ecliptic position to the body under it, as an offset in that body's own frame.
    ///
    /// <para>An ecliptic coordinate is left behind at ~29.8 km/s the instant it is written down.</para>
    /// </summary>
    public static bool TryAnchorToGround(double3 pointEcl, out object? body, out double3 anchor)
    {
        body = null;
        anchor = Vec.Zero;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            Celestial? nearest = null;
            double nearestRange = double.MaxValue;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial candidate) continue;

                double range = Vec.Len(pointEcl - candidate.GetPositionEcl()) - candidate.MeanRadius;
                if (range >= nearestRange) continue;

                nearest = candidate;
                nearestRange = range;
            }

            if (nearest is null) return false;

            body = nearest;
            anchor = doubleQuat.Conjugate(nearest.GetBodyFixed2Ecl()) * (pointEcl - nearest.GetPositionEcl());
            return Vec.IsFinite(anchor);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Turns a ground anchor back into a live position and the velocity of the ground there.
    ///
    /// <para>Orbital motion and spin, 465 m/s at Earth's equator. Spin is about body-fixed <c>+Z</c>:
    /// <c>GetAngularVelocityCci</c> returns <c>(0, 0, GetAngularVelocity())</c>.</para>
    /// </summary>
    public static bool TryGroundAnchorEcl(object? body, double3 anchor,
                                          out double3 positionEcl, out double3 velocityEcl)
    {
        positionEcl = Vec.Zero;
        velocityEcl = Vec.Zero;

        if (body is not Celestial celestial) return false;

        try
        {
            doubleQuat spin = celestial.GetBodyFixed2Ecl();
            double3 offset = spin * anchor;

            positionEcl = celestial.GetPositionEcl() + offset;

            double3 axis = Vec.Unit(spin * new double3(0, 0, 1));
            velocityEcl = celestial.GetVelocityEcl()
                          + Vec.Cross(axis * celestial.GetAngularVelocity(), offset);

            return Vec.IsFinite(positionEcl) && Vec.IsFinite(velocityEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// How far along a ray the ground of the nearest body is first met, within
    /// <paramref name="maxRange"/>. Walked out from <paramref name="eye"/> by <see cref="TerrainRay"/>.
    /// </summary>
    public static bool TryRayGround(double3 eye, double3 direction, double maxRange,
                                    out Celestial? nearest, out double nearestRange)
    {
        nearest = null;
        nearestRange = double.MaxValue;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;

                double3 centre = body.GetPositionEcl();
                double top = MaxTerrainHeightMetres(body);

                // Far enough to cross the whole body. Only the part below its highest ground is
                // walked, and the walk stops at the first place it is under it.
                double reach = Math.Min(maxRange, 2.0 * (Vec.Len(eye - centre) + body.MeanRadius + top));

                // Terrain only: a pad's 8 m pedestal added here is an 8 m thicker planet, moving the
                // point 2.8 km at 5 km (docs/BLOCKED-ON-KSA.md).
                if (!TerrainRay.TryFirstHit(eye, direction, reach, centre, body.MeanRadius, top,
                                            new TerrainHeights(body, accurate: true), out double range)
                    || range >= nearestRange)
                {
                    continue;
                }

                nearest = body;
                nearestRange = range;
            }

            return nearest is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where the cursor's ray first meets a celestial's ground, as a place a craft can be put or a
    /// round can be sent.
    ///
    /// <para>The nearest body hit, walked out by <see cref="TerrainRay"/>: the mean sphere's hit
    /// refined by height lands behind a hill seen side-on.</para>
    /// </summary>
    public static bool TryCursorGroundPoint(out double3 groundEcl,
                                            out double latitudeDeg, out double longitudeDeg,
                                            out string bodyName)
    {
        groundEcl = default;
        latitudeDeg = 0.0;
        longitudeDeg = 0.0;
        bodyName = string.Empty;

        try
        {
            if (!TryCursorRayEcl(out double3 eye, out double3 direction)) return false;
            if (!TryRayGround(eye, direction, double.MaxValue, out Celestial? nearest, out double nearestRange))
            {
                return false;
            }

            if (nearest is null) return false;

            groundEcl = eye + (Vec.Unit(direction) * nearestRange);

            double3 cce = groundEcl - nearest.GetPositionEcl();
            latitudeDeg = nearest.GetLatitudeFromCce(cce);
            longitudeDeg = nearest.GetLongitudeFromCce(cce);
            bodyName = nearest.Id ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where on the body nearest it a point is, as the latitude and longitude a craft can be set down
    /// at with <see cref="TryPlaceOnSurface"/>.
    /// </summary>
    public static bool TryLatitudeLongitude(double3 pointEcl, out string bodyName,
                                            out double latitudeDeg, out double longitudeDeg)
    {
        bodyName = string.Empty;
        latitudeDeg = double.NaN;
        longitudeDeg = double.NaN;

        if (!TryAnchorToGround(pointEcl, out object? found, out _) || found is not Celestial body) return false;

        try
        {
            double3 cce = pointEcl - body.GetPositionEcl();
            latitudeDeg = body.GetLatitudeFromCce(cce);
            longitudeDeg = body.GetLongitudeFromCce(cce);
            bodyName = body.Id ?? string.Empty;

            return double.IsFinite(latitudeDeg) && double.IsFinite(longitudeDeg);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a craft down at a latitude and longitude on the body it is nearest.
    ///
    /// <para>Through <c>Vehicle.TeleportToLocation</c>, which builds the resting state from the
    /// craft's box so the hull, not the origin, ends on the ground.</para>
    /// </summary>
    public static bool TryPlaceOnSurface(Vehicle craft, string bodyName,
                                         double latitudeDeg, double longitudeDeg)
    {
        if (!IsAlive(craft)) return false;
        if (!double.IsFinite(latitudeDeg) || !double.IsFinite(longitudeDeg)) return false;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;
                if (body.Id != bodyName) continue;

                craft.TeleportToLocation(body, latitudeDeg, longitudeDeg);
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            Log.Warn($"could not place {DisplayName(craft)}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// How far a point is above the ground under it, rather than above the mean sphere.
    ///
    /// <para>On the Moon the two differ by kilometres. Accurate, once per burst; an unanswering
    /// height field falls back to the mean sphere.</para>
    /// </summary>
    public static double HeightAboveTerrain(Celestial body, double3 positionEcl)
    {
        try
        {
            double3 cce = positionEcl - body.GetPositionEcl();
            double radius = Vec.Len(cce);
            if (!(radius > 0.0)) return 0.0;

            return new TerrainHeights(body, accurate: true).TryHeight(cce / radius, out double height)
                       ? radius - (body.MeanRadius + height)
                       : radius - body.MeanRadius;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// Where a craft stands: the surface point under it, and its latitude and longitude.
    ///
    /// <para>From the craft's position: a cursor ray through it meets the ground behind it.</para>
    /// </summary>
    public static bool TryCraftSurfacePoint(Vehicle craft, out double3 groundEcl,
                                            out double latitudeDeg, out double longitudeDeg,
                                            out string bodyName)
    {
        groundEcl = default;
        latitudeDeg = 0.0;
        longitudeDeg = 0.0;
        bodyName = string.Empty;

        if (!IsAlive(craft)) return false;

        try
        {
            if (craft.Parent is not Celestial body) return false;

            double3 cce = PositionEcl(craft) - body.GetPositionEcl();
            if (!Vec.IsFinite(cce) || Vec.Len(cce) < 1.0) return false;

            groundEcl = body.GetSurfacePositionEclFromCce(cce);
            latitudeDeg = body.GetLatitudeFromCce(cce);
            longitudeDeg = body.GetLongitudeFromCce(cce);
            bodyName = body.Id ?? string.Empty;
            return Vec.IsFinite(groundEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Puts a point on the ground beneath it: same direction from the body's centre, radius taken
    /// from the terrain there.
    /// </summary>
    public static bool TrySnapToGround(double3 nearEcl, out double3 onGroundEcl) =>
        TrySnapToGround(nearEcl, out onGroundEcl, out _);

    /// <summary>As above, and hands back the centre of the body it draped onto.</summary>
    public static bool TrySnapToGround(double3 nearEcl, out double3 onGroundEcl, out double3 centreEcl)
    {
        centreEcl = Vec.Zero;
        onGroundEcl = nearEcl;

        return NearestCelestial(nearEcl) is { } nearest
               && TrySnapToGround(nearest, nearEcl, out onGroundEcl, out centreEcl);
    }

    // Found once for a whole draped ring rather than once a point: every point of a ring is over the
    // same body. NearestBody's rule, so a ring and a sensor agree about which body a point is over.
    private static Celestial? NearestCelestial(double3 nearEcl)
    {
        try
        {
            return NearestBody(nearEcl);
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySnapToGround(Celestial nearest, double3 nearEcl, out double3 onGroundEcl,
                                        out double3 centreEcl)
    {
        centreEcl = Vec.Zero;
        onGroundEcl = nearEcl;

        try
        {
            double3 centre = nearest.GetPositionEcl();
            centreEcl = centre;
            double3 dirCce = Vec.Unit(nearEcl - centre);
            if (Vec.Len2(dirCce) < 0.5) return false;

            double height = nearest.GetTerrainHeightFromDirCce(dirCce, accurate: true);
            if (!double.IsFinite(height)) return false;

            onGroundEcl = centre + dirCce * (nearest.MeanRadius + height);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
