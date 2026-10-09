using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    // Two things bound what may be commanded, and both exist to stop guidance flying the stack
    // into something. The airflow limit protects the vehicle; the horizon floor protects against a
    // handover on an airless body, where there is no dynamic pressure to wait for and the closed
    // loop would otherwise take over at treetop height already pointing downhill.
    private double3 Limit(double3 wanted, in IcbmState state)
    {
        double3 held = AscentProfile.HoldIntoTheAirflow(wanted, state.AirflowCci, state.DynamicPressurePa,
                                                        AllowedAngleOfAttackDeg(state));

        // A short shot braking at the top of a low arc has to point down; the floor is for a handover
        // on an airless body, which would otherwise go downhill at treetop height.
        if (state.Altitude >= Config.TurnEndMetres || _shortShot) return held;

        double3 up = state.UpCci;
        if (Vec.Dot(held, up) >= 0.0) return held;

        double3 horizontal = Vec.Unit(Vec.RejectFrom(held, up));
        return horizontal.Equals(Vec.Zero) ? up : horizontal;
    }

    /// <summary>
    /// The acceleration the stack is flown at, in standard gravities, or zero for no limit at all.
    ///
    /// <para>The tighter of two numbers that say different things. The operator's is about this
    /// shot — a stack they know is fragile, or one they want flown gently. The airframe's is what
    /// the engine will actually destroy it at, and it applies whether or not anybody typed
    /// anything: a computer that flies a rocket it was told nothing about cannot ask its operator
    /// for a limit only the engine knows.</para>
    /// </summary>
    public double AccelerationCapGee(in IcbmState state)
    {
        double airframe = state.StructuralLimitGee > 0.0
                              ? state.StructuralLimitGee * StructuralMarginFraction
                              : 0.0;

        double asked = Config.MaxAccelerationGee;

        if (asked <= 0.0) return airframe;
        if (airframe <= 0.0) return asked;

        return Math.Min(asked, airframe);
    }

    // The throttle held down to whatever keeps the stack inside that limit. A light upper stage on
    // a full-sized motor is the case: eighteen times its own weight in thrust is nothing unusual
    // once the boosters are gone, and flying it wide open tears the vehicle apart. Reads the
    // engine's own reported acceleration, so a stack that cannot throttle gets the number it would
    // have had.
    private double ThrottleUnderAccelerationCap(double wanted, in IcbmState state)
    {
        double capGee = AccelerationCapGee(state);
        if (capGee <= 0.0) return wanted;

        double full = state.Booster.AccelerationNow;
        if (full <= 0.0 || !double.IsFinite(full)) return wanted;

        // Against standard gravity rather than the local field: it is a structural limit on the
        // vehicle, and the number written on an airframe is in standard gravities.
        double cap = capGee * 9.80665;
        if (full <= cap) return wanted;

        return Math.Clamp(Math.Min(wanted, cap / full), 0.0, 1.0);
    }

    // Holds the remaining burn at the reserve rather than letting it run to nothing: the velocity
    // still to gain then decays rather than crossing zero, and the closed loop inherits something to
    // steer instead of an excess it cannot brake in the air.
    private double HoldBackTheAscent(double wanted, in IcbmState state)
    {
        if (!ReserveBinds(wanted, state)) return wanted;

        double keep = ReserveSeconds * state.Booster.AccelerationNow * wanted;
        return Math.Clamp(wanted * _toGain / keep, MinCommandedThrottle, wanted);
    }

    private double ReserveSeconds => Config.AscentReserveOverrideSeconds > 0.0
                                         ? Config.AscentReserveOverrideSeconds
                                         : Config.FlyAnyRange ? AscentReserveSeconds : 0.0;

    private bool ReserveBinds(double throttle, in IcbmState state)
    {
        double reserve = ReserveSeconds;
        if (!(reserve > 0.0) || Arc is null) return false;

        double accel = state.Booster.AccelerationNow * throttle;
        return accel > 0.0 && double.IsFinite(accel) && _toGain < reserve * accel;
    }

    private bool DropsSolidsUnderWeight(in IcbmState state)
        => Config.DropSolidsUnderWeight && Config.FlyAnyRange && state.OnlySolidsRunning
           && (_absorbing || StaysUnderTheReleaseAltitude(state));

    // A solid tails off for seconds before it is spent, and under the stack's weight it only sags the
    // path: flown at 150 km, 0.86 to 0.59 g for 6 s took what was left from 560 to 3,100 m/s and the stack
    // into the separation turning at 19 deg/s. Only with something after it to light.
    private bool SolidsUnderWeight(in IcbmState state)
        => DropsSolidsUnderWeight(state)
           && state.Booster.CanThrust
           && state.Booster.AccelerationNow < Vec.Len(state.Body.GravityCci(state.PositionCci))
           && double.IsFinite(state.StackDeltaV) && double.IsFinite(state.RunningStageDeltaV)
           && state.StackDeltaV > state.RunningStageDeltaV + MinNextStageMetresPerSecond;

    /// <summary>What the stack must have beyond its running solids for them to be dropped early.</summary>
    public const double MinNextStageMetresPerSecond = 100.0;
}
