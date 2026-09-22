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
/// <para><b>Answered at the frame's end, in both of the body's motions.</b> The centre differenced
/// out below is the celestial's end-of-frame sample, and <c>GetTerrainHeightFromDirCce</c> resolves
/// the direction through <c>GetCcf2Cce()</c> — the end-of-frame <em>rotation</em>. A sub-step
/// part-way through the frame therefore has to hand in a point walked forward by the ground's full
/// velocity, spin included, which is <see cref="Slug.GroundQueryAtOwnEpoch"/>. There is no time
/// argument here to do it with, and there deliberately is not: the sight and the camera ask this
/// from places with no frame phase at all.</para>
///
/// <para>Terrain only, deliberately. A launch pad is 8 m of pedestal 40 m across and adding it here
/// models it as an 8 m thicker planet everywhere; where a structure's surface is has no answer in
/// this engine, and <c>docs/BLOCKED-ON-KSA.md</c> records why. A bomb dropped on a pad therefore
/// bursts at ground level beside it rather than on top of it.</para>
/// </summary>
internal sealed class GroundTest : IGroundTest
{
    /// <summary>One for every round in the air. What it carries is a cache of which body is
    /// underfoot, which is shared deliberately: rounds in one world are nearly always over the same
    /// one.</summary>
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
            // Kept honest by two tests, below. It re-scans by itself when they stop holding, which
            // is what a round arriving somewhere else does -- and the cache is shared by every
            // round, so "somewhere else" includes another round entirely, over another body.
            if (_lastBody is { } cached)
            {
                double depth = Vec.Len(positionEcl - cached.GetPositionEcl()) - cached.MeanRadius;

                // Plainly over that body, as well as ahead of the runner-up. The runner-up alone
                // is not enough, and fails for the one case it most needs to catch: it was
                // measured from somewhere else. With Earth cached and the round on the Moon, the
                // depth to Earth (3.78e8 m) and the Moon's depth from Earth (3.82e8 m) are the
                // same number to within the two radii -- so the guard passed, every round on the
                // Moon was tested against Earth's surface, and a bomb released 7 m over lunar
                // ground fell through it and kept going.
                //
                // Being within a mean radius of the surface cannot be true of two bodies at once
                // at any separation this system has. A round further out than that rescans every
                // step, which is what the cache was avoiding -- and is the right price, because a
                // round out there is between bodies rather than about to arrive on one.
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
                    // Said once per change, never per step. Which body a round is being tested
                    // against is invisible from outside and is the whole answer when one falls
                    // through the ground.
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
