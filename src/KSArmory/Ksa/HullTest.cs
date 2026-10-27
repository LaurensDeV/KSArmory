using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Whether a round's step meets a craft's actual geometry, per triangle, through
/// <c>Part.RayCastEgo</c> — the cast KSA picks parts with.
///
/// <para>No camera: <c>GetMatrixAsmb2Ego</c> takes the frame origin as an argument, so passing the
/// round-relative separation casts in a metres-scale frame centred on the round.</para>
/// </summary>
internal sealed class HullTest : IHullTest
{
    public static readonly HullTest Shared = new();

    // Covers Core's kitten, a 0.4 m capsule 0.59 m from its origin. Not KsaWorld.MeanRadius's 5 m
    // floor, which as a contact radius nothing could miss.
    private const double UnsizedBodyRadius = 1.0;

    public HullVerdict Judge(object? body, double3 separation, double3 travel, out double fraction)
    {
        fraction = 0.0;

        if (body is not Vehicle craft) return HullVerdict.Unknown;
        if (!Vec.IsFinite(separation) || !Vec.IsFinite(travel)) return HullVerdict.Unknown;

        double length = Vec.Len(travel);
        if (!(length > 0.0)) return HullVerdict.Unknown;

        try
        {
            if (craft.Parts is not { } tree) return HullVerdict.Unknown;

            ReadOnlySpan<Part> parts = tree.Parts;

            if (!KsaWorld.HasPickableMesh(parts))
            {
                return AgainstSphere(craft, separation, travel, out fraction);
            }

            double4x4 asmb2Round = craft.GetMatrixAsmb2Ego(separation);
            Ray ray = new() { Origin = default, Direction = travel / length };

            double nearest = double.MaxValue;

            for (int i = 0; i < parts.Length; i++)
            {
                if (!parts[i].RayCastEgo(in asmb2Round, ray, out double near, out double far,
                                         out _, out _, out _, out _, out _, out _))
                {
                    continue;
                }

                // The cast is an unbounded ray and reports a negative near hit for an origin
                // already inside the hull, so both ends of the segment have to be imposed here.
                double hit = near >= 0.0 ? near : far >= 0.0 ? 0.0 : double.MaxValue;

                if (hit < nearest) nearest = hit;
            }

            if (nearest > length) return HullVerdict.Missed;

            fraction = nearest / length;
            return HullVerdict.Struck;
        }
        catch
        {
            // Unknown keeps the sphere: Missed would make the craft silently bulletproof.
            return HullVerdict.Unknown;
        }
    }

    // A kitten has no mesh to cast against (the character renderer draws it), so its sphere is
    // the answer, as it is for the engine's picking.
    private static HullVerdict AgainstSphere(Vehicle craft, double3 separation, double3 travel,
                                             out double fraction)
    {
        double radius = craft.MeanRadius;
        if (!double.IsFinite(radius) || radius <= 0.0) radius = UnsizedBodyRadius;

        return ContactSweep.TryReachSphere(separation, travel, radius, out fraction)
                   ? HullVerdict.Struck
                   : HullVerdict.Missed;
    }
}
