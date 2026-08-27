using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

/// <summary>
/// Where the ground is under a round, from the engine's own height field.
///
/// <para><c>Celestial.GetTerrainHeightFromDirCce</c> is the same query the cursor's ground point is
/// refined with, so a bomb arrives on the surface the player is looking at rather than on the mean
/// sphere — which over a pad or a hillside are hundreds of metres apart.</para>
///
/// <para>Terrain only, deliberately. A launch pad is 8 m of pedestal 40 m across and adding it here
/// models it as an 8 m thicker planet everywhere; where a structure's surface is has no answer in
/// this engine, and <c>docs/BLOCKED-ON-KSA.md</c> records why. A bomb dropped on a pad therefore
/// bursts at ground level beside it rather than on top of it.</para>
/// </summary>
internal sealed class GroundTest : IGroundTest
{
    /// <summary>Stateless, so every round in the air shares one.</summary>
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

            // The body last chosen, checked first. A full scan is one position read and a length
            // per celestial, and this is asked once per round per step -- so on a world carrying
            // several rockets it is the system walked thousands of times a frame to be told the
            // same answer. Anything over Earth is over Earth for the whole flight.
            //
            // Kept honest by the runner-up: the scan records how far behind the second-best body
            // was, and the cache is trusted only while the chosen body is still ahead of that. The
            // margin between a planet underfoot and its moon is hundreds of thousands of
            // kilometres, so it holds for a whole flight and re-scans by itself when it stops
            // holding -- which is what a round arriving somewhere else does.
            if (_lastBody is { } cached)
            {
                double depth = Vec.Len(positionEcl - cached.GetPositionEcl()) - cached.MeanRadius;

                if (depth < _lastRunnerUpDepth)
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
