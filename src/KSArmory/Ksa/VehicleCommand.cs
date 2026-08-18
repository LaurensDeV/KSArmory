using Brutal.GlfwApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The only place this mod flies somebody else's rocket. Attitude, throttle, ignition and staging,
/// through KSA's own public interfaces and nothing else.
///
/// <para>Every write here is one the game already makes for itself somewhere. The attitude goes
/// through the flight computer's <c>Custom</c> track target, which is what <c>PhysicsBubble</c>
/// uses to point a kitten's manoeuvring unit; ignition, throttle and staging go through
/// <see cref="Vehicle.ProcessInput"/>, which is the same call the keyboard makes. Nothing is
/// patched and nothing private is reached for, which is what stops a KSA update turning this into
/// a rocket that flies sideways rather than into a build error.</para>
///
/// <para><b>Commands take a frame to arrive.</b> KSA copies a vehicle's control inputs into its
/// worker state in <c>PrepareWorker</c>, which runs before this mod's hook — so a write made now
/// is acted on next frame. That is the same latency the player's own keypress has, and it is why
/// the guidance times its cutoff rather than waiting to observe one.</para>
/// </summary>
internal static class VehicleCommand
{
    // Far enough that pointing at a place on the line and pointing along the line are the same
    // thing. KSA's own aiming takes a target position rather than a direction, and at this range
    // the difference is below the angle any drive can hold.
    private const double AimPointDistance = 1e12;

    // How far off the wanted throttle is close enough to stop working the control.
    private const double ThrottleTolerance = 0.02;

    /// <summary>
    /// Point the vehicle's nose along a direction in the parent body's inertial frame.
    ///
    /// <para>The rotation is laid out exactly as <see cref="VehicleReferenceFrameEx.GetTgt2Cci"/>
    /// lays it out — the engine's own "aim at that" frame, which is what its <c>Toward</c> mode
    /// uses — because guessing which body axis is the nose gives a vehicle that holds a perfectly
    /// steady attitude ninety degrees from the one asked for.</para>
    ///
    /// <para><b>What it does not borrow is the roll reference.</b> The engine's is the direction of
    /// the planet, which has no answer when the nose points at it or away from it, and does not
    /// merely fail there — it <em>reverses</em>. A vertical rise points away from the planet for
    /// its whole duration, so the commanded roll swings through half a turn and the vehicle spins
    /// on its own axis. <see cref="AimFrame"/> supplies one that is carried forward instead.</para>
    ///
    /// <para>The frame is <c>EclBody</c> rather than the local horizon, because its frame rates are
    /// zero: a commanded inertial direction wants no feed-forward, and the horizon frame's rates
    /// would have the flight computer chasing a rotation nobody asked for.</para>
    /// </summary>
    public static bool TryAim(Vehicle craft, Celestial parent, double3 positionCci,
                              double3 directionCci, double3 rollReferenceCci)
    {
        if (!KsaWorld.IsAlive(craft)) return false;

        double3 forward = Vec.Unit(directionCci);
        if (forward.Equals(Vec.Zero) || !Vec.IsFinite(positionCci)) return false;

        double3 across = Vec.Unit(Vec.Cross(rollReferenceCci, forward));
        if (across.Equals(Vec.Zero)) return false;

        double3 third = Vec.Unit(Vec.Cross(forward, across));
        if (third.Equals(Vec.Zero)) return false;

        doubleQuat target2Cci = doubleQuat.CreateFromRotationMatrix(new double4x4(
            forward.X, forward.Y, forward.Z, 0.0,
            across.X, across.Y, across.Z, 0.0,
            third.X, third.Y, third.Z, 0.0,
            0.0, 0.0, 0.0, 1.0));

        doubleQuat frame2Cci = VehicleReferenceFrameEx.GetEclBody2Cci(parent.GetCce2Cci());
        doubleQuat target2Frame = doubleQuat.Concatenate(target2Cci, frame2Cci.Inverse());

        FlightComputer computer = craft.FlightComputer;
        computer.AttitudeFrame = VehicleReferenceFrame.EclBody;
        computer.AttitudeTrackTarget = FlightComputerAttitudeTrackTarget.Custom;
        computer.CustomAttitudeTarget = VehicleReferenceFrame.EclBody.QuaternionToEulerAngles(target2Frame);
        computer.AttitudeMode = FlightComputerAttitudeMode.Auto;

        // An automatic burn would fight this for the attitude and run its own throttle.
        computer.BurnMode = FlightComputerBurnMode.Manual;

        return true;
    }

    /// <summary>Give the vehicle back to whoever was flying it.</summary>
    public static void ReleaseAttitude(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        FlightComputer computer = craft.FlightComputer;
        computer.AttitudeMode = FlightComputerAttitudeMode.Manual;
        computer.AttitudeTrackTarget = FlightComputerAttitudeTrackTarget.None;
        computer.CustomAttitudeTarget = double3.Zero;
    }

    public static void SetEngine(Vehicle craft, bool running)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        craft.ProcessInput(running ? InputAction.MainEngineStartup : InputAction.MainEngineShutdown,
                           GlfwKeyAction.Press, default);
    }

    /// <summary>
    /// Work the throttle toward a wanted setting, and report what the vehicle actually has.
    ///
    /// <para>KSA exposes no way to set a throttle outright — only the two controls a player holds
    /// down, which move it at a fixed rate. So this is a servo rather than an assignment, and the
    /// number it returns is the real one. Guidance is told that number rather than the one it
    /// asked for, because a stack whose motors cannot be throttled at all would otherwise have its
    /// cutoff timed against a thrust it never came down to.</para>
    /// </summary>
    public static double DriveThrottle(Vehicle craft, double wanted)
    {
        if (!KsaWorld.IsAlive(craft)) return 1.0;

        double have = craft.GetManualThrottle();
        double want = Math.Clamp(wanted, craft.GetMinThrottle(), 1.0);

        bool up = have < want - ThrottleTolerance;
        bool down = have > want + ThrottleTolerance;

        craft.ProcessInput(InputAction.MainEngineThrottleUp,
                           up ? GlfwKeyAction.Press : GlfwKeyAction.Release, default);
        craft.ProcessInput(InputAction.MainEngineThrottleDown,
                           down ? GlfwKeyAction.Press : GlfwKeyAction.Release, default);

        return have;
    }

    /// <summary>Fire the next stage, which is how an engine is lit as well as how one is dropped.</summary>
    public static void Stage(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;
        craft.Parts.SequenceList.ActivateNextSequence(craft);
    }
}
