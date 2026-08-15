using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A local horizontal frame on a body: where "here" is, and which way east and north point.
///
/// <para>Built from three things a caller already has — the body's centre, a point on or above it,
/// and the body's rotation axis — so nothing here needs to know what a <c>Celestial</c> is. North
/// is the axis rather than anything ecliptic: a map whose north is the ecliptic pole is wrong by
/// the body's obliquity everywhere, which on Earth is 23°.</para>
/// </summary>
public readonly record struct MapFrame(double3 Centre, double3 Up, double3 East, double3 North,
                                       double Radius)
{
    /// <summary>
    /// The frame at <paramref name="anchorEcl"/>, or null where it cannot be built — at the poles,
    /// where the rotation axis and local up are the same line and no bearing is defined.
    /// </summary>
    /// <remarks>
    /// East is <c>axis × up</c> and north is <c>up × east</c>, which makes the triad right-handed
    /// and puts north towards the axis. Both are only defined away from the poles, and a map that
    /// silently picked an arbitrary perpendicular there would be a compass rose pointing at
    /// nothing.
    /// </remarks>
    public static MapFrame? TryAt(double3 centreEcl, double3 anchorEcl, double3 axisEcl)
    {
        double3 radial = anchorEcl - centreEcl;
        double radius = Vec.Len(radial);
        if (!(radius > 1.0) || !Vec.IsFinite(radial)) return null;

        double3 up = radial / radius;

        double3 east = Vec.Cross(axisEcl, up);
        if (Vec.Len2(east) < 1e-12 || !Vec.IsFinite(east)) return null;

        east = Vec.Unit(east);

        return new MapFrame(centreEcl, up, east, Vec.Cross(up, east), radius);
    }

    /// <summary>Where a world point sits in this frame: metres east, north and up.</summary>
    /// <remarks>
    /// A difference against the anchor rather than a coordinate, which is what keeps it usable:
    /// both terms are sampled in the same frame this frame was built in, so the ecliptic motion
    /// they share subtracts out. See <c>docs/FRAMES-AND-EPOCHS.md</c>.
    /// </remarks>
    public double3 ToLocal(double3 positionEcl)
    {
        double3 from = positionEcl - (Centre + Up * Radius);

        return new double3(Vec.Dot(from, East), Vec.Dot(from, North), Vec.Dot(from, Up));
    }

    /// <summary>
    /// A <em>direction</em> in this frame: east, north and up components, with no anchor
    /// subtracted.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ToLocal"/> and not a convenience. That one differences against the
    /// anchor, which is right for a position and wrong for a velocity — a velocity displaced by
    /// the anchor is not a velocity, it is a point some 6371 km away.
    /// </remarks>
    public double3 ToLocalDirection(double3 directionEcl)
        => new(Vec.Dot(directionEcl, East), Vec.Dot(directionEcl, North), Vec.Dot(directionEcl, Up));

    /// <summary>
    /// The direction from the body's centre through a point this far east and north of the anchor
    /// — which is what a height field is asked along.
    /// </summary>
    public double3 DirectionAt(double east, double north)
        => Vec.Unit(Up * Radius + East * east + North * north);
}

/// <summary>
/// A square of terrain around a point, as heights and as something to draw.
///
/// <para>Geometry only: sampling costs a height-field lookup per cell and belongs to the caller,
/// which is also what decides how often to pay it. A 2 km square at 64 cells is 4096 lookups, and
/// <see cref="SensorProfile.TerrainSamples"/> defaults to zero precisely because that per-frame
/// cost has never been measured — so a map caches its grid and re-samples on movement rather than
/// every frame.</para>
/// </summary>
public static class TerrainMap
{
    /// <summary>
    /// The spans on offer (m across the square). Detents rather than a continuous zoom, for the
    /// gunner's sight's reason: a scale arrived at by dragging is one nobody can return to.
    /// </summary>
    public static ReadOnlySpan<float> Spans => [500f, 1000f, 2000f, 5000f, 10000f];

    /// <summary>How many cells across a sampled grid is. 64 is 4096 lookups per refresh.</summary>
    public const int Cells = 64;

    /// <summary>
    /// The span one detent further in or out, saturating at the ends. Positive
    /// <paramref name="by"/> zooms <b>in</b>.
    /// </summary>
    /// <remarks>
    /// Stated in zoom rather than in span because those run opposite ways — zooming in makes the
    /// square <em>smaller</em> — and a caller wiring a "+" button to a step of the span gets a
    /// control that works backwards. Putting the sign here keeps it in one tested place.
    /// </remarks>
    public static float Zoom(float span, int by)
    {
        ReadOnlySpan<float> spans = Spans;

        int at = 0;
        for (int i = 1; i < spans.Length; i++)
        {
            if (Math.Abs(spans[i] - span) < Math.Abs(spans[at] - span)) at = i;
        }

        return spans[Math.Clamp(at - by, 0, spans.Length - 1)];
    }

    /// <summary>Where the centre of cell <paramref name="i"/> sits, in metres from the anchor.</summary>
    public static double CellOffset(int i, int cells, double span)
        => cells < 2 ? 0.0 : (i + 0.5) / cells * span - span * 0.5;

    /// <summary>
    /// A world point as a fraction of the square, with (0,0) its top-left and (1,1) its
    /// bottom-right. North is up the screen, so the north axis is negated on the way in.
    /// </summary>
    public static float2 ToUnitSquare(double3 local, double span)
    {
        if (!(span > 0.0)) return new float2(0.5f, 0.5f);

        return new float2((float)(local.X / span + 0.5), (float)(0.5 - local.Y / span));
    }

    /// <summary>True when a point is inside the drawn square.</summary>
    public static bool OnMap(float2 unit)
        => unit.X >= 0f && unit.X <= 1f && unit.Y >= 0f && unit.Y <= 1f;

    /// <summary>
    /// Where a point off the map meets its edge, so an off-square contact can be shown against
    /// the rim it left rather than dropped. Null for a point already on the map.
    /// </summary>
    public static float2? EdgeToward(float2 unit)
    {
        if (OnMap(unit)) return null;

        float2 from = new(unit.X - 0.5f, unit.Y - 0.5f);
        float reach = Math.Max(Math.Abs(from.X), Math.Abs(from.Y));
        if (!(reach > 1e-6f)) return null;

        // Scaled onto the square's edge rather than a circle, so the mark sits on the border the
        // contact actually crossed.
        return new float2(0.5f + from.X * 0.5f / reach, 0.5f + from.Y * 0.5f / reach);
    }

    /// <summary>
    /// Relief shading for one cell, from its neighbours' heights: 0 is fully shadowed and 1 fully
    /// lit.
    ///
    /// <para>A grid of raw heights read as grey is nearly unreadable — a 40 m rise across two
    /// kilometres is a couple of shades. Shading the <em>gradient</em> is what makes a valley look
    /// like a valley, and it costs no extra samples because the neighbours are already there.</para>
    /// </summary>
    public static double Relief(double west, double east, double south, double north, double metresPerCell)
    {
        if (!(metresPerCell > 0.0)) return 0.5;

        // The surface normal in local terms, with the height differences over twice the spacing.
        double dzde = (east - west) / (2.0 * metresPerCell);
        double dzdn = (north - south) / (2.0 * metresPerCell);

        double3 up = Vec.Unit(new double3(-dzde, -dzdn, 1.0));
        if (Vec.Len2(up) < 0.5) return 0.5;

        // Light from the north-west and well up, which is the convention every relief map uses:
        // lit from below reads as terrain turned inside out.
        double3 light = Vec.Unit(new double3(-0.5, 0.5, 0.7));

        return Math.Clamp((Vec.Dot(up, light) + 0.15) / 1.15, 0.0, 1.0);
    }

    /// <summary>
    /// Which way something is travelling over the ground: degrees clockwise from north, in
    /// <c>[0, 360)</c>. Null below <paramref name="still"/> (m/s), where there is no heading to
    /// report and the arrow would spin on the spot.
    /// </summary>
    /// <remarks>
    /// Takes a velocity already in the map's frame — east, north, up — so what is fed in decides
    /// what comes out. A craft's <em>ecliptic</em> velocity is 29.8 km/s of the planet's own
    /// motion and points every craft in the system the same way; the useful one is its velocity
    /// relative to the ground, which is the same distinction a round's airspeed obeys.
    /// </remarks>
    public static double? HeadingDeg(double3 localVelocity, double still = 0.5)
    {
        double east = localVelocity.X;
        double north = localVelocity.Y;

        if (!double.IsFinite(east) || !double.IsFinite(north)) return null;
        if (Math.Sqrt(east * east + north * north) < still) return null;

        double degrees = Math.Atan2(east, north) * 180.0 / Math.PI;

        return degrees < 0.0 ? degrees + 360.0 : degrees;
    }

    /// <summary>Speed over the ground (m/s), which is the horizontal part and nothing else.</summary>
    public static double GroundSpeed(double3 localVelocity)
        => Math.Sqrt(localVelocity.X * localVelocity.X + localVelocity.Y * localVelocity.Y);

    /// <summary>
    /// How far the anchor may drift before a cached grid is stale (m). A tenth of the span: the
    /// map is a picture of the ground rather than of the aircraft, so it need not follow every
    /// metre, and re-sampling on every frame is the cost this exists to avoid.
    /// </summary>
    public static double RefreshDistance(double span) => Math.Max(10.0, span * 0.1);
}
