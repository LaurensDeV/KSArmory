using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The rings to paint on the ground this frame, for <see cref="CloudPass"/> to draw as
/// <c>Shaders/KSArmoryRing.comp</c>.
///
/// <para>Held anchored to the body they lie on and put back into the ecliptic only when the pass
/// records, as the scorch marks are: the pass runs inside KSA's render against the camera of that
/// instant, and a point stored in the ecliptic would be left behind by the planet's motion.</para>
///
/// <para>Cleared at the start of each frame's draw, so a ring is painted only while the code that
/// asked for it is still asking. KSA skips the whole UI pass while the UI is hidden, so rings not
/// renewed for <see cref="StaleSeconds"/> are not painted: long enough to stand through the few
/// frames a screenshot hides the UI for, short enough that F2 takes them away.</para>
/// </summary>
internal static class GroundRings
{
    // An ellipse carries its long axis's tip as a second ground anchor, so the axis turns with the
    // planet as the centre does; Minor is its short radius, and zero for a circle.
    private readonly record struct Ring(object Body, double3 Anchor, double Radius, double Inner, float4 Colour,
                                        int Dashes, double3 TipAnchor = default, double Minor = 0.0);

    /// <summary>How many dashes a dashed ring is cut into, whatever its size.</summary>
    public const int Dashed = 48;

    private static readonly List<Ring> _rings = [];

    public static int Count => _rings.Count;

    private const double StaleSeconds = 0.5;
    private static readonly System.Diagnostics.Stopwatch _sinceRenewed = new();

    /// <summary>Whether the rings were handed over recently enough to still be what is wanted.</summary>
    public static bool Fresh => _sinceRenewed.IsRunning && _sinceRenewed.Elapsed.TotalSeconds < StaleSeconds;

    public static void BeginFrame()
    {
        _rings.Clear();
        _sinceRenewed.Restart();
    }

    /// <summary>
    /// A ring of <paramref name="radius"/> round a point on the ground, with a second one at
    /// <paramref name="inner"/> metres, or none at zero. The colour's alpha is the brightness it
    /// keeps over dark ground.
    /// </summary>
    public static bool Add(double3 centreEcl, double radius, double inner, float4 colour, int dashes = 0)
    {
        if (!(radius > 0.0) || !KsaWorld.TryAnchorToGround(centreEcl, out object? body, out double3 anchor)
            || body is null)
        {
            return false;
        }

        _rings.Add(new Ring(body, anchor, radius, inner, colour, dashes));
        return true;
    }

    /// <summary>
    /// An ellipse lying on the ground: <paramref name="majorEcl"/> is the long semi-axis as a vector,
    /// <paramref name="minor"/> the short one's length.
    /// </summary>
    public static bool AddEllipse(double3 centreEcl, double3 majorEcl, double minor, float4 colour)
    {
        double major = Vec.Len(majorEcl);
        if (!(major > 0.0) || !(minor > 0.0)) return false;
        if (!KsaWorld.TryAnchorToGround(centreEcl, out object? body, out double3 anchor) || body is null) return false;
        if (!KsaWorld.TryAnchorToGround(centreEcl + majorEcl, out object? tipBody, out double3 tip)
            || !ReferenceEquals(tipBody, body))
        {
            return false;
        }

        _rings.Add(new Ring(body, anchor, major, 0.0, colour, 0, tip, minor));
        return true;
    }

    public static bool TryAt(int index, out double3 centreEcl, out double3 up, out double radius,
                             out double inner, out float4 colour, out int dashes)
        => TryAt(index, out centreEcl, out up, out radius, out inner, out colour, out dashes, out _, out _);

    /// <param name="major">An ellipse's long axis as a unit vector in the ground plane, zero for a circle.</param>
    /// <param name="minor">An ellipse's short radius, zero for a circle.</param>
    public static bool TryAt(int index, out double3 centreEcl, out double3 up, out double radius,
                             out double inner, out float4 colour, out int dashes, out double3 major,
                             out double minor)
    {
        major = default;
        minor = 0.0;
        centreEcl = up = default;
        radius = inner = 0.0;
        colour = default;
        dashes = 0;

        if (index < 0 || index >= _rings.Count) return false;

        Ring ring = _rings[index];
        if (!KsaWorld.TryGroundAnchorEcl(ring.Body, ring.Anchor, out centreEcl, out _)) return false;
        if (ring.Body is not Celestial body) return false;

        try
        {
            up = Vec.Unit(centreEcl - body.GetPositionEcl());
        }
        catch
        {
            return false;
        }

        radius = ring.Radius;
        inner = ring.Inner;
        colour = ring.Colour;
        dashes = ring.Dashes;

        if (ring.Minor > 0.0)
        {
            if (!KsaWorld.TryGroundAnchorEcl(ring.Body, ring.TipAnchor, out double3 tipEcl, out _)) return false;

            double3 axis = tipEcl - centreEcl;
            major = Vec.Unit(axis - (up * Vec.Dot(axis, up)));
            minor = ring.Minor;
            if (Vec.Len2(major) < 0.5) return false;
        }

        return Vec.Len2(up) > 0.5;
    }
}
