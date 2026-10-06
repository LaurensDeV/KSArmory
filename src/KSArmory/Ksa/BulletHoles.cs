using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The holes shells have left in hulls, for <see cref="CloudPass"/> to paint as
/// <c>Shaders/KSArmoryHole.comp</c>.
///
/// <para><b>Painted along the path the shell came in on, not round the point it met.</b> The same
/// burst dents the hull, measured at 0.57 m deep for a 5"/54, and a mark round the undented point
/// never reaches the skin as drawn; projected along the path it lands on whatever surface is there.</para>
///
/// <para><b>Held in the frame of the sub-part that was struck</b>, as the engine's own ray cast
/// answers it, and carried to the camera through <c>Part.MatrixAsmb2Ego</c> when the pass records —
/// the matrix the engine draws that sub-part with. So a hole stays on the mesh however the craft
/// flies, turns or articulates, with no epoch of its own to get wrong, and goes with the part if a
/// breakup takes it onto another craft.</para>
/// </summary>
internal static class BulletHoles
{
    /// <summary>One hole: the sub-part struck, the point in its frame, where the cast came from, and its radii.</summary>
    internal readonly record struct Hole(Part SubPart, double3 Local, double3 CameFrom, float Core, float Scorch, float Seed);

    /// <summary>The most holes kept in the world; the oldest go first.</summary>
    public const int MaxHoles = 400;

    // How far back along its path the shell is cast from, so a hit on its first sub-step still
    // starts outside the hull.
    private const double CastFromMetres = 2.0;

    private static readonly List<Hole> _holes = [];

    // Each hole's own soot pattern, fixed when it is made so it does not change as older ones go.
    private static int _made;

    /// <summary>Whether holes are made and painted, from the session's setting.</summary>
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
    public static void Strike(Vehicle craft, IProjectile round)
    {
        if (!Enabled || !KsaWorld.IsAlive(craft)) return;

        double intoFrame = round.DetonationElapsedInFrame;
        double3 craftAt = KsaWorld.PositionEcl(craft) + (KsaWorld.VelocityEcl(craft) * intoFrame);
        double3 separation = craftAt - round.PositionEcl;
        double3 travel = round.VelocityEcl - KsaWorld.VelocityEcl(craft);

        if (!KsaWorld.TryHullHit(craft, separation, travel, CastFromMetres, out Part? subPart, out double3 local,
                                   out double3 cameFrom)
            || subPart is null)
        {
            Log.Debug(() => $"no hole: {KsaWorld.DisplayName(craft)}'s mesh was not met along the shell's path");
            return;
        }

        (double core, double scorch) = HoleLook.RadiiFor(round.Munition.CalibreMm, round.Munition.ChargeKg);

        if (_holes.Count >= MaxHoles) _holes.RemoveAt(0);
        _holes.Add(new Hole(subPart, local, cameFrom, (float)core, (float)scorch, _made++ % 97));
        Log.Debug(() => $"hole in {subPart.Id} on {KsaWorld.DisplayName(craft)}, {core:F2}/{scorch:F2} m; {_holes.Count} held");
    }

    /// <summary>
    /// A hole as one view draws it: relative to that view's camera, the way the shell went in, a
    /// direction square to that fixed to the part — so the petals turn with the hull rather than with
    /// the camera — its two radii and the seed its pattern is drawn from.
    /// </summary>
    public readonly record struct Placed(double Range, double3 Centre, double3 Inward, double3 Side,
                                         float Core, float Scorch, float Seed);

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

            into.Add(new Placed(Vec.Len(centreEgo), centreEgo, inward, side, hole.Core, hole.Scorch, hole.Seed));
        }
    }

    // Pruned about once a second rather than every frame: a hole whose part has gone is skipped by
    // Place anyway, so this only bounds how long a dead one takes up a slot.
    private const int PruneEveryFrames = 60;
    private static int _sincePruned;

    /// <summary>Drops every hole whose part is no longer on a live craft.</summary>
    public static void Prune()
    {
        if (++_sincePruned < PruneEveryFrames) return;

        _sincePruned = 0;
        _holes.RemoveAll(h => !KsaWorld.IsOnLiveCraft(h.SubPart));
    }
}
