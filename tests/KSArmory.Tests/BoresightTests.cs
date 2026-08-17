using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A launcher has to be able to see what it can shoot at.
///
/// That sounds too obvious to test, and it is exactly the invariant nothing else here covers.
/// <see cref="BoresightMode.PartForward"/> resolving to the part's +X — its mounting face's
/// normal — while every tube is declared along +Y leaves an air-launched seeker searching a
/// volume square to the rail carrying it: perpendicular at every attitude, so flying straight at
/// a target pins it near 90 degrees off axis and it is never detected.
///
/// The failure is silent in every direction: the profile loads, the launcher resolves, the
/// designation is accepted and then discarded a frame later, and the only symptom is a weapon
/// that will not fire.
/// </summary>
public class BoresightTests
{
    private static double AngleToTubeDeg(double3 boresight, double3 tube)
        => double.RadiansToDegrees(Vec.AngleBetween(boresight, tube));

    [Fact]
    public void PartForwardIsTheAxisATubeIsDeclaredAlong()
    {
        Assert.True(TubeGeometry.TryBoresightPartFrame(
            Arsenal.SidewinderRail, BoresightMode.PartForward, 0.0, 0.0, out double3 forward));

        Assert.Equal(TubeGeometry.ForwardAxis.X, forward.X, 9);
        Assert.Equal(TubeGeometry.ForwardAxis.Y, forward.Y, 9);
        Assert.Equal(TubeGeometry.ForwardAxis.Z, forward.Z, 9);
    }

    /// <summary>
    /// The two modes are the two perpendicular axes, and confusing them is the whole defect. If
    /// this ever passes trivially — both resolving to one axis — the distinction has collapsed.
    /// </summary>
    [Fact]
    public void MountNormalIsSquareToPartForward()
    {
        Assert.True(TubeGeometry.TryBoresightPartFrame(
            Arsenal.SidewinderRail, BoresightMode.PartForward, 0.0, 0.0, out double3 forward));
        Assert.True(TubeGeometry.TryBoresightPartFrame(
            Arsenal.SidewinderRail, BoresightMode.MountNormal, 0.0, 0.0, out double3 normal));

        Assert.Equal(0.0, Vec.Dot(forward, normal), 9);
    }

    /// <summary>
    /// The same mistake as <see cref="EveryLauncherThatFiresCanSeeDownItsOwnTubes"/>, made about a
    /// sight instead of a launcher: a head must be able to <em>see</em> everywhere it can
    /// <em>point</em>.
    ///
    /// <para>Detection cone and gimbal travel answer different questions — how sensitive the set
    /// is, and what the mount can reach — and bounding the second by the first leaves a band the
    /// head physically covers and is forbidden to look at. On the EO director that band was
    /// everything within 15 degrees of the horizon while the mount reached 20 degrees below it,
    /// so an incoming round sat 35 degrees inside the travel and was never detected.</para>
    ///
    /// <para>Az-el heads only. A roll-nod head's real aperture is bounded by its shell rather than
    /// by its travel, and is measured off the mesh by <c>tools/model/import-litening.py</c>, so
    /// its cone is answerable to that instead.</para>
    /// </summary>
    [Fact]
    public void EveryMastDirectorCanSeeEverywhereItCanPoint()
    {
        foreach (OpticProfile optic in Arsenal.Optics)
        {
            if (optic.Gimbal != GimbalKind.Mast) continue;

            SensorProfile sensor = Arsenal.SensorNamed(optic.Sensor);
            if (sensor.BoresightSource != BoresightMode.MountNormal) continue;

            // The boresight is the mounting face's normal, so it stands at 90 degrees elevation
            // and the furthest the head can be pointed from it is the bottom of its travel.
            double widest = 90.0 - optic.MinElevationDeg;

            Assert.True(sensor.ConeDeg >= widest,
                $"{optic.DisplayName}: its {sensor.DisplayName} searches {sensor.ConeDeg:F0} deg "
                + $"but the mount reaches {widest:F0} deg off boresight, so there is a "
                + $"{widest - sensor.ConeDeg:F0} deg band it can point into and never see.");
        }
    }

    /// <summary>
    /// The general form, and the one that catches the next weapon rather than this one: whatever
    /// a launcher's sensor boresights on, its own tubes must fall inside the cone it searches.
    ///
    /// <para>Two exemptions, both of which are the rule rather than holes in it.
    /// <see cref="BoresightMode.LocalUp"/> is not a part-frame direction at all — it depends on
    /// where the parent body is and the caller resolves it. And a launcher that <em>releases</em>
    /// rather than fires is square to its own rack on purpose: the store leaves along the tube and
    /// is immediately taken by gravity, so its sight looks where the bomb lands. Anything that
    /// flies out under its own power has to be looked for where it is sent.</para>
    /// </summary>
    [Fact]
    public void EveryLauncherThatFiresCanSeeDownItsOwnTubes()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            if (launcher.Tubes.Length == 0) continue;
            if (Arsenal.MunitionNamed(launcher.Munition).Guidance == GuidanceMode.None) continue;

            SensorProfile sensor = Arsenal.SensorNamed(launcher.Sensor);
            if (sensor.BoresightSource == BoresightMode.LocalUp) continue;

            Assert.True(TubeGeometry.TryBoresightPartFrame(
                launcher, sensor.BoresightSource, 0.0, 0.0, out double3 boresight));

            double half = sensor.ConeDeg;

            foreach (Tube tube in launcher.Tubes)
            {
                double offAxis = AngleToTubeDeg(boresight, tube.Direction);

                Assert.True(offAxis <= half,
                    $"{launcher.DisplayName}: its {sensor.DisplayName} boresights "
                    + $"{offAxis:F0} deg off its own tube, outside a {half:F0} deg cone. "
                    + "It cannot detect anything it is pointed at.");
            }
        }
    }
}
