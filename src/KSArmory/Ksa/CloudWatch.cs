using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A camera pinned on a standing cloud, for a run that is being looked at or priced.
///
/// <para><b>It exists because the measurement needed it.</b> What the shader pass costs is set by
/// how much of the screen it marches, so a camera the operator is free to move makes every number
/// out of <see cref="CloudPassCost"/> a number about where somebody happened to be standing. Two
/// runs cannot be compared until the view is the same in both.</para>
///
/// <para>The pose is also the one worth watching from, which is not a coincidence: a mushroom cloud
/// is a side-on silhouette, and both the chase rig and a camera left on the pad look at it from
/// somewhere else. Far enough to see the whole column, low enough to see it against the sky, aimed
/// at the middle rather than the crater.</para>
/// </summary>
internal static class CloudWatch
{
    // Distance in cloud heights. The whole column fills a frame at about its own height, so this is
    // that with a little room around it: 2.4 km on a 1.31 km cloud.
    //
    // It is the measuring distance as well as the watching one, and deliberately so. What the
    // shader pass costs is set by how much of the screen it marches, so a number is only about the
    // pass if it is read from the view it is quoted for -- and the view worth quoting is the one
    // somebody would actually watch from.
    private const double Heights = 1.85;

    // Above the horizontal. Low, because the silhouette is the point: from overhead a mushroom is a
    // blob, which is what the chase rig sees and why it was never the shot for this.
    private const double ElevationDeg = 14.0;

    // Where to look up the column, as a fraction of its top. Below the middle: the cap is the wide
    // part and wants the room above it.
    private const double AimAt = 0.45;

    private const double FieldOfViewDeg = 50.0;

    private static bool _said;

    /// <summary>Forgets that it has spoken, so the next run says where it is watching from.</summary>
    public static void Reset() => _said = false;

    /// <summary>
    /// Points the main view at the newest cloud. Returns false when there is none, which leaves the
    /// camera exactly where it was rather than snapping it anywhere.
    /// </summary>
    public static bool Update(Vehicle followed)
    {
        try
        {
            if (!KsaWorld.IsAlive(followed)) return false;
            if (!NuclearClouds.TryNewest(out double3 burstEcl, out double3 up,
                                         out double top, out _, out _, out _)) return false;
            if (!(top > 0.0)) return false;

            double3 unitUp = Vec.Unit(up);

            // A bearing off the burst rather than off the camera or the clock, so the same burst is
            // always watched from the same side and two runs are the same picture.
            double3 east = Vec.Unit(Vec.AnyPerpendicular(unitUp));

            double distance = Heights * top;
            double elevation = ElevationDeg * Math.PI / 180.0;

            double3 eye = burstEcl
                          + (east * (distance * Math.Cos(elevation)))
                          + (unitUp * (distance * Math.Sin(elevation)));

            double3 at = burstEcl + (unitUp * (AimAt * top));
            double3 forward = at - eye;
            if (!Vec.IsFinite(forward) || Vec.Len2(forward) < 1.0) return false;

            // Against where the engine has the followed craft NOW, which is what the offset has to
            // be measured from: a separation taken from this mod's own sample of it carries a frame
            // of its motion. On a pad drop the craft is standing still and the two agree, which is
            // exactly why this is the run to pin a camera in.
            double3 offset = eye - KsaWorld.PositionEcl(followed);

            if (!KsaWorld.TryLookFromMainViewport(offset, Vec.Unit(forward), unitUp,
                                                  FieldOfViewDeg)) return false;

            if (!_said)
            {
                _said = true;
                Log.Info($"cloud watch: {distance / 1000.0:F2} km out at {ElevationDeg:F0} deg, "
                         + $"looking {AimAt:P0} up a {top / 1000.0:F2} km column");
            }

            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"cloud watch could not point the view: {e.Message}");
            return false;
        }
    }
}
