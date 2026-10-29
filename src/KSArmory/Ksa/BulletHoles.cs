using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The holes shells have left in hulls, painted by <see cref="CloudPass"/> with <c>Shaders/KSArmoryHole.comp</c>.
/// <para>Painted along the shell's path, not round the point: the burst dents the skin, 0.57 m for a
/// 5"/54, and a mark round the undented point never reaches it.</para>
/// <para>Held in the struck sub-part's frame and carried to the camera through the matrix the engine
/// draws that sub-part with, so a hole has no epoch of its own and goes with the part.</para>
/// </summary>
internal static class BulletHoles
{
    /// <summary>
    /// One mark: the sub-part struck, the point in its frame, where the cast came from, its radii, for a
    /// paintball hit the paint's hue from 0 to 1 -- negative for a real hole, which tanks leak through --
    /// and the skin's normal there in the sub-part's frame, zero where the cast read none.
    /// </summary>
    internal readonly record struct Hole(Part SubPart, double3 Local, double3 CameFrom, float Core, float Scorch, float Seed,
                                         float Paint = -1f, double3 Normal = default)
    {
        public bool IsPaint => Paint >= 0f;
    }

    /// <summary>The most holes kept in the world; the oldest go first.</summary>
    public const int MaxHoles = 400;

    // How far back along its path the shell is cast from, so a hit on its first sub-step still
    // starts outside the hull.
    private const double CastFromMetres = 2.0;

    private static readonly List<Hole> _holes = [];

    // Each hole's own soot pattern, fixed when it is made so it does not change as older ones go.
    private static int _made;

    public static bool Enabled { get; set; } = true;

    public static int Count => _holes.Count;

    /// <summary>Every hole kept, oldest first.</summary>
    public static IReadOnlyList<Hole> All => _holes;

    public static void Clear() => _holes.Clear();

    /// <summary>
    /// Marks where <paramref name="round"/> struck <paramref name="craft"/>, cast along its path
    /// relative to the craft at the instant it struck. Nothing for a craft with no mesh to cast
    /// against.
    /// </summary>
    public static void Strike(Vehicle craft, IProjectile round, float paint = -1f)
    {
        if (!Enabled || !KsaWorld.IsAlive(craft)) return;

        double intoFrame = round.DetonationElapsedInFrame;
        double3 craftAt = KsaWorld.PositionEcl(craft) + (KsaWorld.VelocityEcl(craft) * intoFrame);
        double3 separation = craftAt - round.PositionEcl;
        double3 travel = round.VelocityEcl - KsaWorld.VelocityEcl(craft);

        if (!KsaWorld.TryHullHit(craft, separation, travel, CastFromMetres, out Part? subPart, out double3 local,
                                   out double3 cameFrom, out double3 normal)
            || subPart is null)
        {
            Log.Debug(() => $"no hole: {KsaWorld.DisplayName(craft)}'s mesh was not met along the shell's path");
            return;
        }

        (double core, double scorch) = HoleLook.RadiiFor(round.Munition.CalibreMm, round.Munition.ChargeKg);
        if (paint >= 0f)
        {
            AddPaint(new Hole(subPart, local, cameFrom, 0f,
                              (float)Paintball.SplatRadius(round.Munition.ChargeKg, round.Munition.CalibreMm),
                              _made++ % 97, paint, normal));
            return;
        }

        if (_holes.Count >= MaxHoles) _holes.RemoveAt(0);
        _holes.Add(new Hole(subPart, local, cameFrom, (float)core, (float)scorch, _made++ % 97, paint, normal));
        Log.Debug(() => $"hole in {subPart.Id} on {KsaWorld.DisplayName(craft)}, {core:F2}/{scorch:F2} m; {_holes.Count} held");
    }

    /// <summary>
    /// A paint splat where a burst's spray first meets <paramref name="craft"/>, on the line from the burst
    /// to its centre, both at the burst's instant. Nothing for a craft with no mesh on that line.
    /// </summary>
    public static void Splat(Vehicle craft, double3 burstEcl, double intoFrame, double radiusMetres, float paint)
    {
        if (!Enabled || !KsaWorld.IsAlive(craft)) return;

        double3 craftAt = KsaWorld.PositionEcl(craft) + (KsaWorld.VelocityEcl(craft) * intoFrame);
        double3 separation = craftAt - burstEcl;

        if (!KsaWorld.TryHullHit(craft, separation, separation, 0.0, out Part? subPart, out double3 local,
                                 out double3 cameFrom, out double3 normal)
            || subPart is null)
        {
            return;
        }

        AddPaint(new Hole(subPart, local, cameFrom, 0f, (float)radiusMetres, _made++ % 97, paint, normal));
    }

    // How many of the newest marks a new splat is checked against for one to grow instead.
    private const int PaintLookBack = 96;

    // A coat grows by area to at most this many times the largest splat that went into it, or this many metres.
    private const float CoatGrowth = 6f;
    private const float WidestCoatMetres = 4f;

    // Paint on paint thickens one coat: a splat overlapping one of its own colour on the same part grows
    // it by its area, at their area-weighted middle, and the coat counts as the newest mark. Kept as
    // separate splats, a gun's stream fills the store in seconds and the oldest paint is dropped.
    private static void AddPaint(Hole splat)
    {
        int oldest = Math.Max(0, _holes.Count - PaintLookBack);
        for (int i = _holes.Count - 1; i >= oldest; i--)
        {
            Hole coat = _holes[i];
            if (!coat.IsPaint || !ReferenceEquals(coat.SubPart, splat.SubPart)
                || Math.Abs(coat.Paint - splat.Paint) > 1e-4f
                || Vec.Len(coat.Local - splat.Local) > coat.Scorch + splat.Scorch)
            {
                continue;
            }

            double a = coat.Scorch * coat.Scorch, b = splat.Scorch * splat.Scorch;
            double3 middle = ((coat.Local * a) + (splat.Local * b)) / (a + b);
            float limit = Math.Min(Math.Max(coat.Core, splat.Scorch) * CoatGrowth, WidestCoatMetres);
            float grown = (float)Math.Min(Math.Sqrt(a + b), Math.Max(limit, coat.Scorch));

            _holes.RemoveAt(i);
            _holes.Add(coat with
            {
                Local = middle,
                CameFrom = coat.CameFrom + (middle - coat.Local),
                Scorch = grown,
                Core = Math.Max(coat.Core, splat.Scorch),
            });
            return;
        }

        if (_holes.Count >= MaxHoles) _holes.RemoveAt(0);
        _holes.Add(splat with { Core = splat.Scorch });
    }

    /// <summary>
    /// A hole relative to one view's camera. <see cref="Side"/> is fixed to the part, so the petals turn
    /// with the hull rather than with the camera.
    /// </summary>
    public readonly record struct Placed(double Range, double3 Centre, double3 Inward, double3 Side,
                                         float Core, float Scorch, float Seed, float Paint);

    /// <summary>
    /// Asked once per craft carrying holes: whether any hole on it could reach a pixel of this view,
    /// from where the craft is, how far its parts reach from that, and the widest soot on it.
    /// </summary>
    public delegate bool CraftInView(double3 centreEgo, double radiusMetres, double widestScorchMetres);

    // What one view has resolved this frame: each craft's matrix, or null for one not worth painting,
    // and each struck sub-part's matrix off it. Holes crowd onto a few parts, so a part's chain up the
    // tree is walked once rather than three times a hole.
    private static readonly Dictionary<Vehicle, double4x4?> _craftMatrices = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Part, double4x4?> _partMatrices = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Vehicle, double> _widestScorch = new(ReferenceEqualityComparer.Instance);

    /// <summary>Every hole <paramref name="camera"/> could draw, placed for it, into <paramref name="into"/>.</summary>
    public static void Place(Camera camera, CraftInView inView, List<Placed> into)
    {
        into.Clear();
        _craftMatrices.Clear();
        _partMatrices.Clear();
        _widestScorch.Clear();

        foreach (Hole hole in _holes)
        {
            if (!KsaWorld.TryCraftOf(hole.SubPart, out Vehicle? craft)) continue;
            _widestScorch[craft] = Math.Max(_widestScorch.GetValueOrDefault(craft), hole.Scorch);
        }

        foreach (Hole hole in _holes)
        {
            if (!KsaWorld.TryCraftOf(hole.SubPart, out Vehicle? craft)) continue;

            if (!_craftMatrices.TryGetValue(craft, out double4x4? vehicle))
            {
                vehicle = KsaWorld.TryVehicleMatrixEgo(craft, camera, out double4x4 m, out double3 centre, out double radius)
                          && inView(centre, radius, _widestScorch[craft])
                              ? m
                              : null;
                _craftMatrices[craft] = vehicle;
            }

            if (vehicle is not { } asmb2Ego) continue;

            if (!_partMatrices.TryGetValue(hole.SubPart, out double4x4? part))
            {
                part = KsaWorld.TryPartMatrixEgo(hole.SubPart, in asmb2Ego, out double4x4 m) ? m : null;
                _partMatrices[hole.SubPart] = part;
            }

            if (part is not { } toEgo) continue;

            double3 centreEgo = hole.Local.Transform(toEgo);
            double3 inward = Vec.Unit(centreEgo - hole.CameFrom.Transform(toEgo));
            if (!Vec.IsFinite(centreEgo) || !Vec.IsFinite(inward) || Vec.Len2(inward) < 0.5) continue;

            // The part's own x, or its y where x lies along the path.
            double3 path = hole.Local - hole.CameFrom;
            double3 reference = Math.Abs(Vec.Unit(path).X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);
            double3 offset = (hole.Local + reference).Transform(toEgo) - centreEgo;
            double3 side = Vec.Unit(offset - (inward * Vec.Dot(offset, inward)));
            if (!Vec.IsFinite(side) || Vec.Len2(side) < 0.5) continue;

            into.Add(new Placed(Vec.Len(centreEgo), centreEgo, inward, side, hole.Core, hole.Scorch, hole.Seed, hole.Paint));
        }
    }

    // Pruned about once a second rather than every frame: a hole whose part has gone is skipped by
    // Place anyway, so this only bounds how long a dead one takes up a slot.
    private const int PruneEveryFrames = 60;
    private static int _sincePruned;

    public static void Prune()
    {
        if (++_sincePruned < PruneEveryFrames) return;

        _sincePruned = 0;
        _holes.RemoveAll(h => !KsaWorld.IsOnLiveCraft(h.SubPart));
    }
}
