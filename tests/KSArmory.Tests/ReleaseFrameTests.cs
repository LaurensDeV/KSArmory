using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Which attitude a tube's turn is measured from, and which one it is applied to.
///
/// <para>The two are not the same. The tube axes and the line they average to are measured at the
/// launcher's <em>actual</em> attitude; the held command is the one it was <em>asked</em> for. A
/// turn built from the first and applied to the second leaves the difference in the answer — the
/// vehicle's own standing pointing error, plus everything its roll has done since, because KSA's
/// flight computer holds no roll angle at all. <c>docs/MIRV-NEXT.md</c> item 5 has the engine's
/// side.</para>
///
/// <para><b>The vehicle here obeys the engine's law, not the command.</b> KSA rebuilds the attitude
/// error as a pointing-only rotation about <c>cross(nose, commanded)</c> and leaves the roll where
/// it was, so a vehicle that has arrived has turned by exactly that much and no more. Every test
/// below asks where the tube ends up after that rotation, which is the only thing the release can
/// depend on.</para>
/// </summary>
public class ReleaseFrameTests(ITestOutputHelper Out)
{
    private static readonly double Ejection = Arsenal.ReentryVehicleMk21.LaunchSpeed;

    private const double Step = 1.0 / 60.0;

    private static double Degrees(double radians) => radians * 180.0 / Math.PI;

    // The bus's tubes in its own body frame.
    private static double3[] BodyTubes()
    {
        Tube[] tubes = Arsenal.MirvBus.Tubes;
        double3[] axes = new double3[tubes.Length];
        for (int i = 0; i < tubes.Length; i++) axes[i] = Vec.Unit(tubes[i].Direction);
        return axes;
    }

    // A launcher pointing somewhere, with a stated roll about its own axis. The pair is what the
    // engine leaves free and what a latched axis therefore goes stale against.
    private static double3[] Pose(double3[] body, double noseErrorDegrees, double rollRadians)
    {
        double3 nose = Vec.Unit(new double3(Math.Cos(noseErrorDegrees * Math.PI / 180.0),
                                            Math.Sin(noseErrorDegrees * Math.PI / 180.0), 0.0));

        doubleQuat point = Vec.RotationFromTo(new double3(1, 0, 0), nose);
        doubleQuat spin = doubleQuat.CreateFromAxisAngle(nose, rollRadians);

        double3[] axes = new double3[body.Length];
        for (int i = 0; i < body.Length; i++) axes[i] = Vec.Unit(spin * (point * body[i]));
        return axes;
    }

    // Where a tube ends up once the vehicle has done as it was told. The engine turns the nose onto
    // the commanded direction along the shortest arc and touches nothing else, so everything on the
    // launcher rides that one rotation.
    private static double3 Arrived(double3 noseNow, double3 commanded, double3 tubeNow)
        => Vec.Unit(Vec.RotationFromTo(noseNow, commanded) * tubeNow);

    private static ReleaseSituation At(int tube, double3 axisNow, double3 noseNow,
                                       double3 held, double3 heldRoll)
        => new(ReadyToDeploy: true, NextTube: tube, TubesLeft: 6 - tube, NextTubeAxisCci: axisNow,
               NoseAxisCci: noseNow, EjectionMetresPerSecond: Ejection,
               SweepMetresPerSecond: 0.0, SecondsLeftToDeploy: double.PositiveInfinity,
               HeldDirectionCci: held, HeldRollCci: heldRoll);

    /// <summary>
    /// The vehicle is not where it was told to be, and it never is: the flight computer holds an
    /// attitude to its own deadband and its roll to nothing. A turn applied to the command carries
    /// all of that into every tube.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(3.0, 0.0)]
    [InlineData(0.0, 2.4)]
    [InlineData(9.0, 0.7)]
    public void ATubeLandsOnTheLineWhateverTheVehicleIsActuallyDoing(double errorDegrees,
                                                                    double rollRadians)
    {
        double3 held = new(1, 0, 0);
        double3 heldRoll = new(0, 0, 1);

        double3[] body = BodyTubes();
        double3[] live = Pose(body, errorDegrees, rollRadians);
        double3 noseNow = ReleasePointing.ReferenceAxis(live);

        ReleaseSequence deploy = new();
        Assert.True(deploy.Begin(live));

        double3 reference = deploy.ReferenceCci;
        double worst = 0.0;

        for (int tube = 0; tube < body.Length; tube++)
        {
            ReleaseCommand r = deploy.Update(Step, At(tube, live[tube], noseNow, held, heldRoll));

            worst = Math.Max(worst, Degrees(Vec.AngleBetween(
                                        Arrived(noseNow, r.DirectionCci, live[tube]), reference)));
        }

        Out.WriteLine($"{errorDegrees:F0} deg off the held line, {Degrees(rollRadians):F0} deg of "
                      + $"roll -> worst tube {worst:F4} deg off the line");

        Assert.True(worst < 1e-6,
                    $"a tube ends up {worst:F2} deg off the line on a vehicle that did exactly as it "
                    + "was told; the turn is being applied to an attitude the axes were not measured "
                    + "at");
    }

    /// <summary>
    /// Roll is the axis KSA holds a rate on and no angle, so a canted tube walks round its cone
    /// while a latched command stands still. Re-solving the turn from the live axis is what tracks
    /// it, and it is why the axes cannot simply be latched once.
    /// </summary>
    [Fact]
    public void ATubeStaysOnTheLineWhileTheBusRollsUnderIt()
    {
        double3 held = new(1, 0, 0);
        double3 heldRoll = new(0, 0, 1);

        double3[] body = BodyTubes();

        ReleaseSequence deploy = new();
        Assert.True(deploy.Begin(Pose(body, 0.0, 0.0)));

        double3 reference = deploy.ReferenceCci;
        double worst = 0.0;

        // Two and a bit turns about the launcher's own axis, which is the case that looks like
        // nothing happening: every tube stays exactly one cant off the nose the whole way round and
        // the line does not move, so only where each tube is on its cone has changed.
        for (int step = 0; step <= 96; step++)
        {
            double3[] live = Pose(body, 0.0, step * 2.0 * Math.PI / 40.0);
            double3 noseNow = ReleasePointing.ReferenceAxis(live);

            ReleaseCommand r = deploy.Update(Step, At(0, live[0], noseNow, held, heldRoll));

            worst = Math.Max(worst, Degrees(Vec.AngleBetween(
                                        Arrived(noseNow, r.DirectionCci, live[0]), reference)));
        }

        Out.WriteLine($"worst {worst:F4} deg off the line through two full rolls");

        Assert.True(worst < 1e-6,
                    $"the tube reaches {worst:F2} deg off the line as the bus rolls under a command "
                    + "built from where it used to be");
    }

    /// <summary>
    /// A launcher with one tube has no cant to remove, and the sequence collapses to pointing that
    /// tube at the line — which is the line the aim correction was already told the round leaves on.
    /// It costs a launcher like that nothing, which is the property that lets the whole mechanism
    /// stay switched on for launchers it does not describe.
    /// </summary>
    [Fact]
    public void ALauncherWithOneTubeIsSimplyPointedAtTheLine()
    {
        double3 held = new(1, 0, 0);
        double3 heldRoll = new(0, 0, 1);
        double3 axis = Vec.Unit(new double3(1, 0, 0));

        ReleaseSequence deploy = new();
        Assert.True(deploy.Begin([axis]));

        // Drifted off the line since it was latched, which is what the engine's deadband guarantees.
        double3 drifted = Vec.Unit(new double3(Math.Cos(0.05), Math.Sin(0.05), 0.0));

        ReleaseCommand r = deploy.Update(Step, At(0, drifted, drifted, held, heldRoll));

        Assert.True(Degrees(Vec.AngleBetween(r.DirectionCci, deploy.ReferenceCci)) < 1e-9,
                    "a single tube is its own mean, so putting it on the line is the whole command");
        Out.WriteLine(r.Said);
    }
}
