using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The ballistic arc, drawn in the world: where the vehicle is going and where it was told to go.
///
/// <para>Two marks rather than one, and the gap between them is the whole point. The line is the
/// trajectory <see cref="ImpactPredictor"/> flew from the vehicle's actual state; the ring is the
/// place it was aimed at. While they disagree the burn is not finished — which is a thing to look
/// at rather than a number to read, and the reason this is drawn at all.</para>
/// </summary>
internal static class IcbmOverlay
{
    private static readonly float4 Arc = new(0.55f, 0.8f, 1.0f, 0.9f);
    private static readonly float4 Aim = new(1.0f, 0.45f, 0.35f, 1.0f);

    // Coarse enough that a half-hour arc is a few dozen lines rather than a few thousand.
    private const int MaxSegments = 96;

    public static void Draw(IcbmComputers computers, List<double3> scratch)
    {
        foreach (IcbmComputer computer in computers.All)
        {
            if (!computer.Config.DrawTrajectory) continue;
            if (!KsaWorld.IsAlive(computer.Craft)) continue;

            if (computer.TargetEcl() is { } target)
            {
                KsaWorld.DrawSphereEcl(target, 2000f, Aim);
            }

            computer.PathEcl(scratch);
            if (scratch.Count < 2) continue;

            int stride = Math.Max(1, scratch.Count / MaxSegments);

            for (int i = stride; i < scratch.Count; i += stride)
            {
                KsaWorld.DrawLineEcl(scratch[i - stride], scratch[i], Arc);
            }

            KsaWorld.DrawLineEcl(scratch[^Math.Min(scratch.Count, stride + 1)], scratch[^1], Arc);
        }
    }
}
