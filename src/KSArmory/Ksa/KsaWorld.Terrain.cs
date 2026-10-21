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

    /// <summary>
    /// Whether the skyline of the body under the eye hides the target, against the real height
    /// field.
    ///
    /// <para>Terrain only, and for one body. <see cref="IsOccluded"/> runs over every body in the
    /// system and stays in front of this: a ridge on another world only matters when that world is
    /// between the two points, which the sphere has already caught. So this is asked once the
    /// cheap rejects have all passed, and never for a contact they threw out.</para>
    /// </summary>
    /// <param name="samples">Height lookups this look may cost. Zero asks none.</param>
    // Each modifier contributes its amplitude times a weight, a lookup and a noise value all in
    // [0, 1], so the sum of the declared amplitudes is a supremum rather than an estimate. Earth's
    // total 7,525 m.
    private const double TerrainModifierHeadroomMetres = 8_000.0;

    // How high the terrain can possibly reach, as a bound the cheap reject may stand in front of
    // the exact test with.
    //
    // Celestial.MaxTerrainHeightApprox is NOT such a bound, and using it gives false negatives. It
    // is computed in the Celestial constructor, before Universe.SetupRenderData populates the
    // modifiers, so erosion, dunes and detail contribute nothing -- and it samples a 16,384-point
    // Fibonacci spiral, about 176 km apart on Earth. Measured against the shipped height texture it
    // returns ~5,692 m where the base field alone reaches 8,011. A sightline six kilometres over the
    // Himalayas is then declared unmasked without a single sample, which is the false negative
    // CLAUDE.md's "a sphere containing the terrain cannot produce a false negative" forbids.
    //
    // Astronomical.MaxTerrainRadius is exact for the base field, straight off the template. Over-
    // padding costs one thing: a contact high above the ground pays for samples it did not need.
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
    /// <para>A radius of zero means no body could be resolved, which every caller has to read as
    /// "no ground here" rather than as ground at the origin.</para>
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
    /// <para>The only description of a place that does not move. An ecliptic coordinate is left
    /// behind at the body's ~29.8 km/s the instant it is written down, and a round sent to one
    /// reads that whole frame velocity as closing speed.</para>
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
    /// <para>The velocity carries the body's orbital motion <em>and</em> its spin. The spin term
    /// is not decoration: 465 m/s at Earth's equator is 4.6 km of miss over a ten-second flight,
    /// which would read as the round simply being inaccurate.</para>
    ///
    /// <para>Spin is about body-fixed <c>+Z</c>. Read out of the engine, not assumed:
    /// <c>IParentBody.GetAngularVelocityCci</c> returns <c>(0, 0, GetAngularVelocity())</c> and
    /// <c>Celestial.GetCcf2Cci</c> turns about <c>double3.UnitZ</c>.</para>
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

            // Orbital motion, plus the surface sweeping under it. omega x r, with omega taken
            // about the spin axis the body's own frame defines.
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

                // Terrain only. A launch pad is 8 m of pedestal 40 m across, and adding it here
                // models it as an 8 m thicker planet: at 5 km the resolved point moves 2.8 km, and
                // sweeping the cursor over the pad edge swings the bearing from the mount through
                // 168 degrees between one pixel and the next. Where a structure's surface is has
                // no answer in this engine -- see docs/BLOCKED-ON-KSA.md.
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
    /// <para>Nearest body hit, not the one being orbited: pointing at a moon on the horizon should
    /// mean the moon. Walked out from the eye by <see cref="TerrainRay"/>, because the ray meets the
    /// ground where it first goes under it — the mean sphere's hit refined by the height under that
    /// answer lands behind a hill seen side-on, on the terrain beyond what the pointer is on.</para>
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
    /// <para><c>Vehicle.TeleportToLocation</c> does the work, and doing it this way rather than by
    /// writing a position is what makes the craft arrive upright and resting: it builds the
    /// kinematic state from the craft's own bounding box, so the hull ends up on the ground rather
    /// than the origin.</para>
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
    /// <para>The two differ by the terrain, which on the Moon is kilometres — so a burst sitting on
    /// lunar highland reads as a high airburst against the mean sphere and a surface burst against
    /// the ground it is actually touching. Anything <em>deciding</em> something from a height has to
    /// ask this; the mean sphere is only good enough for a line in a log.</para>
    ///
    /// <para>Accurate, because it is asked once per burst and wants the surface where it actually
    /// is. A height field that will not answer falls back to the mean sphere.</para>
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
    /// <para>Placing something at a craft has to use the craft's own position rather than the
    /// ground the cursor ray reaches. A ray through a vehicle's middle carries on and meets the
    /// ground <em>behind</em> it, so aiming at a craft and using the ray puts the answer a
    /// vehicle-height's worth of parallax past it.</para>
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
    ///
    /// </summary>
    public static bool TrySnapToGround(double3 nearEcl, out double3 onGroundEcl) =>
        TrySnapToGround(nearEcl, out onGroundEcl, out _);

    /// <summary>
    /// As above, and hands back the centre of the body it draped onto -- which it had to find
    /// anyway. A caller that needs the local up gets it for free instead of walking the system
    /// again for a body this has already identified.
    /// </summary>
    public static bool TrySnapToGround(double3 nearEcl, out double3 onGroundEcl, out double3 centreEcl)
    {
        centreEcl = Vec.Zero;
        onGroundEcl = nearEcl;

        return NearestCelestial(nearEcl) is { } nearest
               && TrySnapToGround(nearest, nearEcl, out onGroundEcl, out centreEcl);
    }

    // The body whose centre is nearest a point, by a walk of the whole system. Found once for a
    // whole draped ring rather than once a point: every point of a ring is over the same body.
    private static Celestial? NearestCelestial(double3 nearEcl)
    {
        try
        {
            if (Universe.CurrentSystem is not { } system) return null;

            Celestial? nearest = null;
            double best = double.MaxValue;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;

                double distance = Vec.Len(nearEcl - body.GetPositionEcl());
                if (distance >= best) continue;

                best = distance;
                nearest = body;
            }

            return nearest;
        }
        catch
        {
            return null;
        }
    }

    // The snap onto a body already found.
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
