using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A camera pinned on a standing cloud. What the shader pass costs is set by how much of the screen it
/// marches, so <see cref="CloudPassCost"/>'s numbers compare only from a fixed pose; this one is side-on,
/// the column whole and against the sky.
/// </summary>
internal static class CloudWatch
{
    // The chase's observer distance, so a cost is read from the view somebody would watch from.
    private const double Heights = ChaseView.ObserverRadii;

    private const double DefaultElevationDeg = ChaseView.ObserverElevationDeg;

    /// <summary>The pose's elevation, which a scenario may lower to stand under the weather.</summary>
    public static double ElevationDeg { get; set; } = DefaultElevationDeg;

    // A fraction of the top, not of the radius: the two agree for a column and a dome's width is a
    // quarter of its top, so aiming by width points the camera over it.
    private const double AimAt = 0.45;

    private const double FieldOfViewDeg = 50.0;

    private static bool _said;

    /// <summary>Forgets that it has spoken and where it stood, so the next run starts fresh.</summary>
    public static void Reset()
    {
        _said = false;
        ElevationDeg = DefaultElevationDeg;
    }

    /// <summary>
    /// Where to stand instead of the default: degrees round from across the wind, degrees up,
    /// metres out (zero for the default distance), and how far up the top to look -- and how far to
    /// turn the view from the burst about the vertical, which at 180 looks at the land the flash is
    /// lighting rather than at the flash.
    /// </summary>
    public readonly record struct Pose(double AzimuthDeg, double ElevationDeg, double DistanceMetres,
                                       double AimAt, double TurnDeg = 0.0);

    /// <summary>
    /// Points the main view at the newest burst. Returns false when there is none, which leaves the
    /// camera exactly where it was rather than snapping it anywhere.
    /// </summary>
    public static bool Update(Vehicle followed, Pose? pose = null)
    {
        try
        {
            if (!KsaWorld.IsAlive(followed)) return false;
            if (!NuclearClouds.TryWatch(out double3 burstEcl, out double3 up,
                                        out double radius, out double top,
                                        out double3 downwind)) return false;
            if (!(radius > 0.0)) return false;

            double3 unitUp = Vec.Unit(up);

            // Across the wind: the lean, the drift and the fallout all lie on the downwind line, and
            // the bearing is the burst's own, so two runs are one picture.
            double3 acrossWind = Vec.Unit(Vec.Cross(unitUp, downwind));
            double3 east = Vec.Len2(acrossWind) > 0.5
                               ? acrossWind
                               : Vec.Unit(Vec.AnyPerpendicular(unitUp));

            double distance = pose is { DistanceMetres: > 0.0 } p ? p.DistanceMetres : Heights * radius;
            double elevation = (pose?.ElevationDeg ?? ElevationDeg) * Math.PI / 180.0;

            // Turned round the vertical from across the wind, so a pose names the same side of any
            // burst.
            double azimuth = (pose?.AzimuthDeg ?? 0.0) * Math.PI / 180.0;
            double3 downwindFlat = Vec.Unit(Vec.Cross(east, unitUp));
            double3 facing = (east * Math.Cos(azimuth)) + (downwindFlat * Math.Sin(azimuth));

            double3 eye = burstEcl
                          + (facing * (distance * Math.Cos(elevation)))
                          + (unitUp * (distance * Math.Sin(elevation)));

            double3 at = burstEcl + (unitUp * ((pose?.AimAt ?? AimAt) * top));
            double3 forward = at - eye;
            if (!Vec.IsFinite(forward) || Vec.Len2(forward) < 1.0) return false;

            if (pose is { TurnDeg: not 0.0 } turned)
            {
                // Rodrigues about the vertical: the height of the gaze is kept, its bearing turned.
                double turn = turned.TurnDeg * Math.PI / 180.0;
                double3 k = unitUp;
                forward = (forward * Math.Cos(turn)) + (Vec.Cross(k, forward) * Math.Sin(turn))
                          + (k * (Vec.Dot(k, forward) * (1.0 - Math.Cos(turn))));
            }

            // Against where the engine has the followed craft now: a separation from this mod's own
            // sample of it carries a frame of its motion.
            double3 offset = eye - KsaWorld.PositionEcl(followed);

            if (!KsaWorld.TryLookFromMainViewport(offset, Vec.Unit(forward), unitUp,
                                                  FieldOfViewDeg)) return false;

            if (!_said)
            {
                _said = true;
                Log.Info($"cloud watch: {distance / 1000.0:F2} km out at {ElevationDeg:F0} deg, "
                         + $"looking {AimAt:P0} up a {top / 1000.0:F2} km top "
                         + $"on a {radius / 1000.0:F2} km burst");
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
