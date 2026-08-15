using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A launcher has to be able to see what it can shoot at.
///
/// That sounds too obvious to test, and it is exactly the invariant that broke: every
/// air-launched seeker searched a volume square to the rail carrying it, because
/// <see cref="BoresightMode.PartForward"/> resolved to the part's +X — its mounting face's
/// normal — while every tube is declared along +Y. Perpendicular at every attitude, so flying
/// straight at a target left it pinned near 90 degrees off axis and never detected.
///
/// Nothing in the suite looked for it. The failure is silent in every direction: the profile
/// loads, the launcher resolves, the designation is accepted and then discarded a frame later,
/// and the only symptom is a weapon that will not fire.
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
