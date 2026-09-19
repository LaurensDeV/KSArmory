namespace KSArmory;

/// <summary>
/// What the stack can still do, as the four numbers guidance needs from it.
///
/// <para>Deliberately not a description of the rocket. Nothing here knows about stages, engines or
/// tanks — a stack that has just staged is simply a lighter one with a different thrust, and the
/// guidance loop reads these again every cycle rather than being told an event happened. That is
/// what lets it fly a vehicle it has never seen: whatever the player bolted together, the answer to
/// "how hard can you push and for how long" is the same four numbers.</para>
/// </summary>
internal readonly record struct BoosterPerformance(
    double ThrustNewtons,
    double MassFlowKgPerSec,
    double TotalMassKg,
    double PropellantMassKg)
{
    /// <summary>Effective exhaust velocity, which is what turns a velocity still to gain into a time.</summary>
    public double ExhaustVelocity => MassFlowKgPerSec > 0.0 ? ThrustNewtons / MassFlowKgPerSec : 0.0;

    public double AccelerationNow => TotalMassKg > 0.0 ? ThrustNewtons / TotalMassKg : 0.0;

    /// <summary>
    /// The mass-flow time constant: how long this stack would burn if it could consume its whole
    /// self. Not a burn time — it is the denominator every closed-form burn integral is written
    /// over, and it exceeds the real burn time by exactly the dry mass.
    /// </summary>
    public double Tau => MassFlowKgPerSec > 0.0 ? TotalMassKg / MassFlowKgPerSec : double.PositiveInfinity;

    /// <summary>How long the engines can actually keep running.</summary>
    public double BurnSecondsRemaining
        => MassFlowKgPerSec > 0.0 ? PropellantMassKg / MassFlowKgPerSec : 0.0;

    /// <summary>Tsiolkovsky over what is left in the tanks. The number that decides a shot is on.</summary>
    public double DeltaVRemaining
    {
        get
        {
            double dry = TotalMassKg - PropellantMassKg;
            if (!(dry > 0.0) || !(TotalMassKg > dry) || !(ExhaustVelocity > 0.0)) return 0.0;
            return ExhaustVelocity * Math.Log(TotalMassKg / dry);
        }
    }

    public bool CanThrust => ThrustNewtons > 0.0 && TotalMassKg > 0.0;

    /// <summary>
    /// How long thrusting at full throttle takes to gain a stated velocity, allowing for the stack
    /// getting lighter as it does.
    ///
    /// <para>Treating the acceleration as constant is what makes a cutoff late: an ICBM's final
    /// stage roughly triples its acceleration over the burn, so a linear estimate of the last
    /// second is out by most of it — and the last second is the one that decides where the warheads
    /// land.</para>
    /// </summary>
    public double SecondsToGain(double deltaV)
    {
        if (!(deltaV > 0.0)) return 0.0;
        if (!CanThrust) return double.PositiveInfinity;

        double ve = ExhaustVelocity;
        if (!(ve > 0.0)) return deltaV / AccelerationNow;

        // Inverting dv = -ve*ln(1 - t/tau).
        double t = Tau * (1.0 - Math.Exp(-deltaV / ve));
        return double.IsFinite(t) ? t : double.PositiveInfinity;
    }

    /// <summary>
    /// How far the stack travels along the thrust line while gaining that velocity, over and above
    /// what it would have coasted.
    ///
    /// <para>The guidance loop needs this to know where it will <em>be</em> at cutoff, which is
    /// where the trajectory has to be solved from. Solving from where it is now instead aims the
    /// arc from a point several hundred kilometres short of the real burnout.</para>
    /// </summary>
    public double ThrustDisplacement(double seconds)
    {
        if (!(seconds > 0.0) || !CanThrust) return 0.0;

        double ve = ExhaustVelocity;
        double tau = Tau;
        if (!(ve > 0.0) || !double.IsFinite(tau) || seconds >= tau)
        {
            return 0.5 * AccelerationNow * seconds * seconds;
        }

        double gained = -ve * Math.Log(1.0 - seconds / tau);
        double displacement = gained * seconds - (gained * tau - ve * seconds);
        return double.IsFinite(displacement) ? displacement : 0.5 * AccelerationNow * seconds * seconds;
    }
}
