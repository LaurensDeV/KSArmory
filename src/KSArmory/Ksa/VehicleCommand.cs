using BepuUtilities;
using Brutal.GlfwApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The only place this mod flies somebody else's rocket, and every write is one the game makes for
/// itself: the attitude through the flight computer's <c>Custom</c> track target, as
/// <c>PhysicsBubble</c> points a manoeuvring unit; ignition, throttle and thrusters through
/// <see cref="Vehicle.ProcessInput"/>, the keyboard's call; staging through
/// <c>ActivateNextSequence</c>. Public members only, so a KSA change is a build error.
///
/// <para><b>Commands take a frame to arrive.</b> KSA copies a vehicle's control inputs into its
/// worker state in <c>PrepareWorker</c>, which runs before this mod's hook — so a write made now
/// is acted on next frame. That is the same latency the player's own keypress has, and it is why
/// the guidance times its cutoff rather than waiting to observe one.</para>
/// </summary>
internal static class VehicleCommand
{
    // KSA's aiming takes a position, not a direction; this far out the two agree below any drive's hold.
    private const double AimPointDistance = 1e12;

    private const double ThrottleTolerance = 0.02;

    /// <summary>
    /// Points the nose along a direction in the parent body's inertial frame.
    /// <para>Laid out as <see cref="VehicleReferenceFrameEx.GetTgt2Cci"/> lays it out, so the nose is
    /// the engine's nose. The roll reference is <see cref="AimFrame"/>'s, not the engine's direction
    /// of the planet, which reverses through the vertical and spins a vertical rise.</para>
    /// <para>In <c>EclBody</c>, whose frame rates are zero: an inertial direction wants no feed-forward.</para>
    /// </summary>
    public static bool TryAim(Vehicle craft, double3 directionCci, double3 rollReferenceCci, bool loosely = false)
    {
        if (!KsaWorld.IsAlive(craft)) return false;
        if (KsaWorld.ParentBody(craft) is not { } parent) return false;

        // Resolved here rather than passed in, because this runs inside the engine's own frame pass
        // where the vehicle's state is fresh and the caller's is a frame old.
        doubleQuat cce2Cci = parent.GetCce2Cci();
        double3 positionCci = (craft.GetPositionEcl() - parent.GetPositionEcl()).Transform(cce2Cci);

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

        // KSA only ever raises the pointing deadband, so a staged stack keeps its booster's on the
        // bus: measured 11.4 degrees against a rate bit worth 0.07. Assigning the profile resets it,
        // and KSA re-raises it to the real floor on the same frame.
        computer.SetAttitudeProfile(loosely ? FlightComputerAttitudeProfile.Balanced : FlightComputerAttitudeProfile.Strict);

        // An automatic burn would fight this for the attitude and run its own throttle.
        computer.BurnMode = FlightComputerBurnMode.Manual;

        return true;
    }

    /// <summary>Holding an attitude of any kind, whoever set it.</summary>
    public static bool HoldsAttitude(Vehicle craft)
        => KsaWorld.IsAlive(craft) && craft.FlightComputer.AttitudeMode != FlightComputerAttitudeMode.Manual;

    public static void ReleaseAttitude(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        FlightComputer computer = craft.FlightComputer;
        computer.AttitudeMode = FlightComputerAttitudeMode.Manual;
        computer.AttitudeTrackTarget = FlightComputerAttitudeTrackTarget.None;
        computer.CustomAttitudeTarget = double3.Zero;
    }

    /// <summary>
    /// An impulse and an angular impulse about the centre of mass, in the craft's assembly frame,
    /// written as <c>Vehicle.Split</c> pushes two halves apart: into the physics state, off rails,
    /// orbit rebuilt. Neither the speed nor the rim's spin passes <paramref name="windSpeed"/>
    /// (<see cref="BlastShove.Saturate"/>). Only from <see cref="AttitudeHook"/>'s window.
    /// </summary>
    public static bool TryShove(Vehicle craft, double3 impulseAsmb, double3 angularImpulseAsmb, double windSpeed,
                                out double3 velocityChangeAsmb, out double3 spinChangeAsmb)
    {
        velocityChangeAsmb = default;
        spinChangeAsmb = default;

        if (!KsaWorld.IsAlive(craft) || !craft.HasPhysicsBubble) return false;

        double mass = craft.TotalMass;
        if (!(mass > 0.0)) return false;

        Symmetric3x3 inverse = Symmetric3x3.Invert(craft.TotalMassPropsBody.Inertia);
        double3 l = angularImpulseAsmb;
        double3 dw = new((inverse.XX * l.X) + (inverse.YX * l.Y) + (inverse.ZX * l.Z),
                         (inverse.YX * l.X) + (inverse.YY * l.Y) + (inverse.ZY * l.Z),
                         (inverse.ZX * l.X) + (inverse.ZY * l.Y) + (inverse.ZZ * l.Z));
        double3 dv = impulseAsmb / mass;
        if (!Vec.IsFinite(dv)) return false;
        if (!Vec.IsFinite(dw)) dw = Vec.Zero;

        // One share for both: the push and the turn are the same wind falling off as the craft is carried.
        double naive = Vec.Len(dv);
        double carried = BlastShove.Saturate(naive, windSpeed);
        double share = naive > 0.0 ? carried / naive : 1.0;
        dv *= share;
        dw *= share;

        double rim = KsaWorld.MeanRadius(craft);
        if (rim > 0.0) dw = Vec.Unit(dw) * BlastShove.Saturate(Vec.Len(dw), windSpeed / rim);
        if (!Vec.IsFinite(dv)) dv = Vec.Zero;
        if (!Vec.IsFinite(dw)) dw = Vec.Zero;

        PhysicsStates states = craft.GetPhysicsStatesMutable();
        if (craft.Situation.IsOnRails())
        {
            states.UpdateFromAnalytic(craft.Orbit, in craft.Orbit.StateVectors, craft.Body2Cce, craft.BodyRates,
                                      Situation.Maneuvering);
        }

        states.Kinematic.VelocityPhys += dv.Transform(states.Kinematic.Body2Phys);
        states.Kinematic.AngularVelocityPhys += dw.Transform(states.Kinematic.Body2Phys);
        states.Props.SetOnRails(isOnRails: false);
        craft.SetFlightPlan(new FlightPlan(states.ComputeOrbit(craft.OrbitColor), craft.Hash));

        velocityChangeAsmb = dv;
        spinChangeAsmb = dw;
        return true;
    }

    public static void SetEngine(Vehicle craft, bool running)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        craft.ProcessInput(running ? InputAction.MainEngineStartup : InputAction.MainEngineShutdown,
                           GlfwKeyAction.Press, default);
    }

    /// <summary>
    /// A servo, since KSA's throttle moves only at a fixed rate under its two held keys. Returns the
    /// throttle the vehicle has, which guidance times its cutoff on rather than the one asked for.
    /// </summary>
    public static double DriveThrottle(Vehicle craft, double wanted, bool throughTheClear = false)
    {
        if (!KsaWorld.IsAlive(craft)) return 1.0;

        double have = craft.GetManualThrottle();
        double want = Math.Clamp(wanted, craft.GetMinThrottle(), 1.0);

        bool up = have < want - ThrottleTolerance;
        bool down = have > want + ThrottleTolerance;
        AttitudeHook.ThrottleKey(craft, throughTheClear ? (up ? 1 : down ? -1 : 0) : 0);

        craft.ProcessInput(InputAction.MainEngineThrottleUp,
                           up ? GlfwKeyAction.Press : GlfwKeyAction.Release, default);
        craft.ProcessInput(InputAction.MainEngineThrottleDown,
                           down ? GlfwKeyAction.Press : GlfwKeyAction.Release, default);

        return have;
    }

    /// <summary>
    /// Holds the thrusters on in the named directions and off in the rest: <c>ThrusterCommandFlags</c>
    /// persist until released, so every direction is written every cycle. The attitude hold clears
    /// only the rotation flags. A direction no nozzle answers is ignored, so <see cref="BusTrim"/> measures.
    /// </summary>
    public static void DriveTranslation(Vehicle craft, TrimAxes fire)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        Hold(InputAction.TranslateForward, TrimAxes.Forward);
        Hold(InputAction.TranslateBackward, TrimAxes.Backward);
        Hold(InputAction.TranslateRight, TrimAxes.Right);
        Hold(InputAction.TranslateLeft, TrimAxes.Left);
        Hold(InputAction.TranslateDown, TrimAxes.Down);
        Hold(InputAction.TranslateUp, TrimAxes.Up);

        void Hold(InputAction action, TrimAxes direction)
            => craft.ProcessInput(action,
                                  (fire & direction) != TrimAxes.None
                                      ? GlfwKeyAction.Press
                                      : GlfwKeyAction.Release,
                                  default);
    }

    /// <summary>
    /// In <c>Pulse</c> a held translation key is one <c>MinimumPulseTime</c> pulse per thruster, at
    /// most every 0.15 s; assigned rather than toggled. The attitude hold survives it. Only from
    /// <see cref="AttitudeHook"/>'s window: a worker's results overwrite the flight computer.
    /// </summary>
    public static void SetPulseMode(Vehicle craft, bool pulsing)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        craft.FlightComputer.SetManualThrustMode(pulsing
                                                     ? FlightComputerManualThrustMode.Pulse
                                                     : FlightComputerManualThrustMode.Direct);
    }

    /// <summary>
    /// Fire the next stage, which is how an engine is lit as well as how one is dropped. Called from
    /// <see cref="AttitudeHook"/>'s window; anything else asks <see cref="AttitudeHook.Stage"/>.
    /// </summary>
    public static void Stage(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;
        craft.Parts.SequenceList.ActivateNextSequence(craft);
    }

    /// <summary>
    /// Switch every engine on the craft on or off as the part's Active box does, staged or not. For the bridge's
    /// measured burn, and a hop on a post-boost bus's own engine (<see cref="IcbmConfig.HopsBurnTheEngine"/>) once the
    /// bus is the craft's only engine; nothing else that flies a shot may light an engine out of sequence.
    /// </summary>
    public static void SetEnginesActive(Vehicle craft, bool active)
    {
        if (!KsaWorld.IsAlive(craft)) return;
        try
        {
            Span<EngineController> engines = craft.Parts.Modules.Get<EngineController>();
            for (int i = 0; i < engines.Length; i++) engines[i].SetIsActive(craft, active);
        }
        catch (Exception e)
        {
            Log.Warn($"engines: could not set active ({e.Message})");
        }
    }
}
