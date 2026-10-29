using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Paintball's marks on the ground: where a round went off on or over it, a splat of the shooter's paint,
/// hundreds of metres across for a nuclear charge. Kept between frames, anchored to the body as a ring is,
/// and handed to <see cref="GroundRings"/> every UI pass for the ring pass to paint.
/// </summary>
internal static class GroundSplats
{
    private readonly record struct Splat(object Body, double3 Anchor, double Radius, float3 Colour, float Seed, float Hue);

    /// <summary>The most kept; the oldest go first.</summary>
    public const int MostKept = 64;

    private static readonly List<Splat> _splats = [];
    private static int _made;

    public static int Count => _splats.Count;

    /// <summary>A splat round where a round of hue <paramref name="hue"/> went off, its rays reaching <paramref name="radius"/>.</summary>
    public static void Add(double3 groundEcl, double radius, float hue)
    {
        if (!(radius > 0.0) || !KsaWorld.TryAnchorToGround(groundEcl, out object? body, out double3 anchor)
            || body is null)
        {
            return;
        }

        (float r, float g, float b) = Paintball.Colour(hue);
        float3 colour = new(r, g, b);

        // Paint on paint grows one splat, as a hull's coat does; kept apart, shells walking across the
        // ground fill the store and the oldest is dropped.
        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            Splat old = _splats[i];
            if (!ReferenceEquals(old.Body, body) || Math.Abs(old.Hue - hue) > 1e-4f
                || Vec.Len(old.Anchor - anchor) > old.Radius + radius)
            {
                continue;
            }

            double a = old.Radius * old.Radius, c = radius * radius;
            _splats.RemoveAt(i);
            _splats.Add(old with
            {
                Anchor = ((old.Anchor * a) + (anchor * c)) / (a + c),
                Radius = Math.Max(old.Radius, Math.Min(Math.Sqrt(a + c), Math.Max(old.Radius, radius) * GrowthLimit)),
            });
            return;
        }

        if (_splats.Count >= MostKept) _splats.RemoveAt(0);
        _splats.Add(new Splat(body, anchor, radius, colour, (_made++ % 97) * 3.17f, hue));
    }

    // A splat grows by area to at most this many times the largest that went into it.
    private const double GrowthLimit = 6.0;

    public static void Paint()
    {
        foreach (Splat splat in _splats) GroundRings.AddSplat(splat.Body, splat.Anchor, splat.Radius, splat.Colour, splat.Seed);
    }

    public static void Clear() => _splats.Clear();
}
