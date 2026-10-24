using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

/// <summary>
/// Where the ground is under a round, from <c>Celestial.GetTerrainHeightFromDirCce</c>, the query the
/// cursor's ground point is refined with.
/// <para>Answered at the frame's end in both of the body's motions: the centre is the end-of-frame
/// sample and the direction resolves through the end-of-frame rotation, so a sub-step hands in a point
/// walked forward by the ground's full velocity (<see cref="Slug.GroundQueryAtOwnEpoch"/>).</para>
/// <para>Terrain only: a structure's surface has no answer in this engine (<c>docs/BLOCKED-ON-KSA.md</c>).</para>
/// </summary>
internal sealed class GroundTest : IGroundTest
{
    /// <summary>Shared by every round: it caches which body is underfoot, nearly always the same one.</summary>
    public static readonly GroundTest Shared = new();

    // Which body the last lookup landed on, and how far behind the second-best was. Not thread
    // safe and does not need to be: every caller is on the frame thread.
    private Celestial? _lastBody;
    private double _lastRunnerUpDepth = double.MaxValue;

    public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
    {
        centreEcl = default;
        surfaceRadius = 0.0;

        if (!Vec.IsFinite(positionEcl)) return false;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            Celestial? nearest = null;
            double nearestDepth = double.MaxValue;

            // The body last chosen first: asked once per round per step, a full scan walks the
            // system thousands of times a frame. The two tests below re-scan when they fail.
            if (_lastBody is { } cached)
            {
                double depth = Vec.Len(positionEcl - cached.GetPositionEcl()) - cached.MeanRadius;

                // Within a mean radius as well as ahead of the runner-up, which is measured from the
                // old position: with Earth cached and a round on the Moon, the depth to Earth
                // (3.78e8 m) and the Moon's own from Earth (3.82e8 m) pass that test alone.
                if (depth < _lastRunnerUpDepth && depth < cached.MeanRadius)
                {
                    nearest = cached;
                    nearestDepth = depth;
                }
            }

            if (nearest is null)
            {
                double runnerUp = double.MaxValue;

                // Nearest by depth below the mean sphere rather than by distance: a round low over
                // a moon is far closer to the ground it is about to meet than to the planet it
                // orbits.
                for (int i = 0; i < system.Count; i++)
                {
                    if (system.GetIndex(i) is not Celestial body) continue;

                    double depth = Vec.Len(positionEcl - body.GetPositionEcl()) - body.MeanRadius;
                    if (depth >= nearestDepth) { if (depth < runnerUp) runnerUp = depth; continue; }

                    runnerUp = nearestDepth;
                    nearest = body;
                    nearestDepth = depth;
                }

                if (!ReferenceEquals(_lastBody, nearest))
                {
                    // Once per change: the whole answer when a round falls through the ground.
                    Log.Debug($"ground test: now against {nearest?.Id ?? "nothing"}"
                              + $" (runner-up {runnerUp / 1000.0:F0} km deep)");
                }

                _lastBody = nearest;
                _lastRunnerUpDepth = runnerUp;
            }

            if (nearest is null) return false;

            centreEcl = nearest.GetPositionEcl();

            double3 dirCce = Vec.Unit(positionEcl - centreEcl);
            if (!Vec.IsFinite(dirCce) || Vec.Len(dirCce) < 0.5) return false;

            double height = nearest.GetTerrainHeightFromDirCce(dirCce, accurate: true);
            if (!double.IsFinite(height)) return false;

            // The height field answers with terrain, so under an ocean it reports the seabed. A
            // round would fall through the waterline and burst on the bottom, unseen. Same query
            // KsaWorld.MediumDensityRatioAt uses to know it is in water.
            double seaLevel = 0.0;
            bool hasSea = false;
            if (nearest.GetOceanReference() is { } sea && sea.Density > 0.0)
            {
                hasSea = true;
                seaLevel = sea.Level;
            }

            height = GroundSurface.Height(height, seaLevel, hasSea);

            surfaceRadius = nearest.MeanRadius + height;
            return surfaceRadius > 0.0;
        }
        catch
        {
            return false;
        }
    }
}
