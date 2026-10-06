using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A tank's own shape in its own frame, x along its axis: a truncated cone from its base (−x) to its
/// top, with a semi-ellipsoid dome on each end. That is KSA's one family of tank — a cylinder is the
/// cone with equal radii, a sphere is two domes with nothing between — so one description, and one
/// test of what is inside it, covers every tank the game builds.
/// </summary>
/// <param name="Straight">The length of the cone between the domes.</param>
internal readonly record struct TankShape(double Straight, double RadiusBase, double RadiusTop, double DomeBase, double DomeTop)
{
    /// <summary>A sphere of that radius.</summary>
    public static TankShape Sphere(double radius) => new(0.0, radius, radius, radius, radius);

    /// <summary>
    /// A cone <paramref name="length"/> long overall, domes included, each dome as tall as its end's
    /// radius times <paramref name="domeFraction"/> — how KSA's own templates size them.
    /// </summary>
    public static TankShape Conical(double length, double radiusBase, double radiusTop, double domeFraction)
    {
        double domeBase = radiusBase * domeFraction, domeTop = radiusTop * domeFraction;
        return new TankShape(Math.Max(length - domeBase - domeTop, 0.0), radiusBase, radiusTop, domeBase, domeTop);
    }

    /// <summary>The space inside a wall that thick: what the liquid fills.</summary>
    public TankShape Inside(double wall)
    {
        double w = Math.Max(wall, 0.0);
        return new TankShape(Straight, Math.Max(RadiusBase - w, 0.0), Math.Max(RadiusTop - w, 0.0),
                             Math.Max(DomeBase - w, 0.0), Math.Max(DomeTop - w, 0.0));
    }

    /// <summary>Whether a point is inside, grown by <paramref name="share"/> of its size.</summary>
    public bool Contains(double3 p, double share = 0.0)
    {
        double grow = 1.0 + share;
        double across = Math.Sqrt((p.Y * p.Y) + (p.Z * p.Z));
        double half = Straight * 0.5;

        if (p.X >= -half && p.X <= half)
        {
            double t = Straight > 0.0 ? (p.X + half) / Straight : 0.5;
            return across <= (RadiusBase + ((RadiusTop - RadiusBase) * t)) * grow;
        }

        (double dome, double radius, double beyond) = p.X < -half
            ? (DomeBase, RadiusBase, -half - p.X)
            : (DomeTop, RadiusTop, p.X - half);
        if (!(dome > 0.0) || !(radius > 0.0)) return false;

        double a = beyond / (dome * grow), b = across / (radius * grow);
        return (a * a) + (b * b) <= 1.0;
    }

    /// <summary>
    /// The same shape stretched to <paramref name="length"/> overall and <paramref name="radius"/> at its
    /// widest: along its axis and across it separately, so a sphere fitted to a long box becomes the
    /// capsule the box holds, and a cone keeps its taper and its domes their share of its length.
    /// </summary>
    public TankShape FitTo(double length, double radius)
    {
        (double low, double high, double widest) = Extent;
        double along = high > low ? length / (high - low) : 1.0;
        double across = widest > 0.0 ? radius / widest : 1.0;
        return new TankShape(Straight * along, RadiusBase * across, RadiusTop * across, DomeBase * along, DomeTop * along);
    }

    /// <summary>The ends along x and the widest radius.</summary>
    public (double Low, double High, double Radius) Extent
        => (-(Straight * 0.5) - DomeBase, (Straight * 0.5) + DomeTop, Math.Max(RadiusBase, RadiusTop));
}

/// <summary>
/// Where the liquid in a holed tank stands, and how fast it runs out of a hole below it.
///
/// <para><b>The liquid settles against the acceleration the craft feels</b> — the ground's push on
/// the pad, the thrust in a burn — so its surface is square to that and a craft on its side drains from
/// its lower holes; a hole above the surface vents only the gas over it, which KSA does not count as
/// mass. <b>In a coast it floats</b>, wetting the walls, and a hole is under it about as often as the
/// tank is full.</para>
///
/// <para><b>Driven by the weight above the hole and by a low tank pressure</b>,
/// <see cref="UllagePascals"/>. KSA models no pressure, and without one a coasting craft could not leak
/// at all; a real flight tank's couple of bar would make where the hole is almost irrelevant, so this
/// is low enough that depth and pull still double or halve a leak. Held constant, as a regulated tank
/// holds it, rather than falling as the tank vents.</para>
///
/// <para><b>The surface is found by counting points, not by solving the shape.</b> A tank is taken as
/// the cylinder filling its box, and points spread evenly through that volume put the surface of any
/// fill at any tilt where that share of them lies below it — end caps and all, which no closed form
/// for a tilted cylinder gets right.</para>
/// </summary>
internal static class TankLeak
{
    /// <summary>A sharp-edged orifice's: the jet necks down to about three fifths of the hole.</summary>
    public const double DischargeCoefficient = 0.61;

    /// <summary>Felt acceleration under which the liquid is taken to float free rather than settle.</summary>
    public const double WeightlessBelow = 0.05;

    /// <summary>The tank's pressure over the outside, in pascals: 0.2 bar, where a flight tank runs 1.5–3.</summary>
    public const double UllagePascals = 20_000.0;

    /// <summary>Points a tank is sampled with: its surface to about half a percent of its volume.</summary>
    public const int Samples = 256;

    /// <summary>
    /// Points spread evenly through the cylinder filling a box — along its longest side, as wide as the
    /// narrower of the other two — from a Halton sequence, so the spread is the same every time.
    /// </summary>
    public static double3[] FillPoints(double3 boxMin, double3 boxMax, int count = Samples)
    {
        double3 centre = (boxMin + boxMax) * 0.5;
        double[] size = [boxMax.X - boxMin.X, boxMax.Y - boxMin.Y, boxMax.Z - boxMin.Z];

        int axis = size[0] >= size[1] && size[0] >= size[2] ? 0 : size[1] >= size[2] ? 1 : 2;
        int a = (axis + 1) % 3, b = (axis + 2) % 3;
        double radius = 0.5 * Math.Min(size[a], size[b]);
        double half = 0.5 * size[axis];

        double3[] points = new double3[Math.Max(count, 1)];
        for (int i = 0; i < points.Length; i++)
        {
            double r = radius * Math.Sqrt(Halton(i + 1, 2));
            double theta = 2.0 * Math.PI * Halton(i + 1, 3);
            double along = ((2.0 * Halton(i + 1, 5)) - 1.0) * half;

            double[] p = new double[3];
            p[axis] = along;
            p[a] = r * Math.Cos(theta);
            p[b] = r * Math.Sin(theta);
            points[i] = centre + new double3(p[0], p[1], p[2]);
        }

        return points;
    }

    /// <summary>
    /// Points spread evenly through a tank's own shape, in its frame: the same sequence as
    /// <see cref="FillPoints(double3, double3, int)"/>, over the cylinder round the shape, keeping only
    /// the points inside it.
    /// </summary>
    public static double3[] FillPoints(TankShape shape, int count = Samples)
    {
        (double low, double high, double radius) = shape.Extent;
        List<double3> points = new(count);

        for (int i = 1; points.Count < count && i <= count * 64; i++)
        {
            double r = radius * Math.Sqrt(Halton(i, 2));
            double theta = 2.0 * Math.PI * Halton(i, 3);
            double3 p = new(low + ((high - low) * Halton(i, 5)), r * Math.Cos(theta), r * Math.Sin(theta));
            if (shape.Contains(p)) points.Add(p);
        }

        return [.. points];
    }

    /// <summary>
    /// How far past the tank's own surface a hole still counts as in it, as a share of its size: the
    /// skin's thickness and the dent that holed it. Anything proud of that — a raceway, a stringer, a
    /// fairing on the same part — is not the tank, and a hole in it leaks nothing.
    /// </summary>
    public const double SkinShare = 0.035;

    /// <summary>Whether a point is in the cylinder filling a tank's box, give or take its skin.</summary>
    public static bool IsInTank(double3 boxMin, double3 boxMax, double3 point)
    {
        double3 centre = (boxMin + boxMax) * 0.5;
        double[] size = [boxMax.X - boxMin.X, boxMax.Y - boxMin.Y, boxMax.Z - boxMin.Z];
        double[] at = [point.X - centre.X, point.Y - centre.Y, point.Z - centre.Z];

        int axis = size[0] >= size[1] && size[0] >= size[2] ? 0 : size[1] >= size[2] ? 1 : 2;
        int a = (axis + 1) % 3, b = (axis + 2) % 3;
        double radius = 0.5 * Math.Min(size[a], size[b]);
        double half = 0.5 * size[axis];

        double across = Math.Sqrt((at[a] * at[a]) + (at[b] * at[b]));
        return across <= radius * (1.0 + SkinShare) && Math.Abs(at[axis]) <= half * (1.0 + SkinShare);
    }

    /// <summary>
    /// How high along <paramref name="up"/> the surface of a <paramref name="fill"/> share of the tank
    /// stands: negative infinity for an empty one, and the top of a full one.
    /// </summary>
    public static double SurfaceHeight(ReadOnlySpan<double3> points, double3 up, double fill, Span<double> scratch)
    {
        if (points.Length == 0 || !(fill > 0.0)) return double.NegativeInfinity;

        Span<double> heights = scratch[..points.Length];
        for (int i = 0; i < points.Length; i++) heights[i] = Vec.Dot(points[i], up);
        heights.Sort();

        if (fill >= 1.0) return heights[^1];

        double at = fill * points.Length;
        int below = (int)Math.Floor(at);
        if (below <= 0) return heights[0];

        return heights[below - 1] + ((heights[Math.Min(below, points.Length - 1)] - heights[below - 1]) * (at - below));
    }

    /// <summary>
    /// How long the liquid takes to follow a change in what the craft feels, in seconds. Liquid moving
    /// across a tank under a small push takes seconds to get there, so an attitude thruster's pulse in a
    /// coast barely stirs it, where read raw it settled and floated the liquid several times a second.
    /// </summary>
    public const double SettleSeconds = 1.5;

    /// <summary>
    /// The acceleration the liquid has settled to after <paramref name="dt"/> more of
    /// <paramref name="felt"/>, from <paramref name="settled"/>: following it with a lag of
    /// <see cref="SettleSeconds"/>, so a burn settles it within seconds and a pulse does not.
    /// </summary>
    public static double3 Settle(double3 settled, double3 felt, double dt)
    {
        if (!(dt > 0.0) || !Vec.IsFinite(felt)) return settled;

        return settled + ((felt - settled) * (1.0 - Math.Exp(-dt / SettleSeconds)));
    }

    /// <summary>Whether a craft feeling this much acceleration has its liquid floating free.</summary>
    public static bool Weightless(double feltAcceleration) => !(feltAcceleration >= WeightlessBelow);

    /// <summary>
    /// What runs out of a hole of <paramref name="holeRadius"/>, in kg/s, through the necked-down jet.
    /// Settled, from a hole <paramref name="depth"/> metres under the surface and nothing from one above
    /// it; floating, from any hole, as often as the liquid is over it, which is the tank's
    /// <paramref name="fill"/>.
    /// </summary>
    public static double FlowKgPerSecond(double depth, double feltAcceleration, double density, double holeRadius,
                                         double fill)
    {
        if (!(density > 0.0) || !(holeRadius > 0.0) || !(fill > 0.0)) return 0.0;

        double speed = JetSpeed(depth, feltAcceleration, density);
        double wetted = Weightless(feltAcceleration) ? Math.Min(fill, 1.0) : 1.0;
        return DischargeCoefficient * density * Math.PI * holeRadius * holeRadius * speed * wetted;
    }

    /// <summary>
    /// How fast the liquid leaves a hole, in m/s — Bernoulli, <c>√(2·P/ρ + 2·a·h)</c>: the tank's
    /// pressure, and the weight of the liquid over the hole while it is settled; nothing from a hole
    /// above a settled surface.
    /// </summary>
    public static double JetSpeed(double depth, double feltAcceleration, double density)
    {
        if (!(density > 0.0)) return 0.0;

        double pressure = 2.0 * UllagePascals / density;
        if (Weightless(feltAcceleration)) return Math.Sqrt(pressure);

        return depth > 0.0 ? Math.Sqrt(pressure + (2.0 * feltAcceleration * depth)) : 0.0;
    }

    private static double Halton(int index, int radix)
    {
        double result = 0.0, f = 1.0;
        for (int i = index; i > 0; i /= radix)
        {
            f /= radix;
            result += f * (i % radix);
        }

        return result;
    }
}
