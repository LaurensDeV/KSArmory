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
    // Distance in burst radii -- the half-width of whatever was drawn. The whole thing fills a
    // frame at about that, so this is it with a little room around: 2.4 km on a 1.31 km cloud.
    //
    // It is the measuring distance as well as the watching one, and deliberately so. What the
    // shader pass costs is set by how much of the screen it marches, so a number is only about the
    // pass if it is read from the view it is quoted for -- and the view worth quoting is the one
    // somebody would actually watch from.
    private const double Heights = 1.85;

    // Above the horizontal. Low, because the silhouette is the point: from overhead a mushroom is a
    // blob, which is what the chase rig sees and why it was never the shot for this.
    private const double DefaultElevationDeg = 14.0;

    /// <summary>The pose's elevation, which a scenario may lower to stand under the weather.</summary>
    public static double ElevationDeg { get; set; } = DefaultElevationDeg;

    // Where to look up the thing, as a fraction of its top. Below the middle: the cap is the wide
    // part and wants the room above it. Measured against the TOP rather than the radius, which is
    // the same number for a column and a quarter of it for a dust dome -- aiming a dome's own width
    // up the sky points the camera over it entirely.
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

            // ACROSS THE WIND, which is where a photographer would stand and is not what an
            // arbitrary perpendicular gives. Everything about a burst that is not symmetric about
            // its own axis lies on the downwind line -- the lean, the veer, the drift after the
            // rise, and the fallout on the ground -- so a camera at an arbitrary bearing to it
            // sees some fraction of all four and, on the bearing it happens to pick, none of them.
            // It is still off the burst rather than off the clock, so two runs are one picture.
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
