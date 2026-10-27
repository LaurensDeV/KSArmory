namespace KSArmory;

/// <summary>
/// What the stack can still do, as the four numbers guidance needs from it.
///
/// <para>Nothing here knows about stages, engines or tanks: a stack that has just staged is a lighter one
/// with a different thrust, read again every cycle, which is what lets guidance fly any vehicle.</para>
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
    /// The mass-flow time constant: how long the stack would burn if it could consume its whole self.
    /// Not a burn time; it exceeds the real one by the dry mass over the mass flow.
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
    /// How long full throttle takes to gain a stated velocity, allowing for the stack getting lighter:
    /// a final stage roughly triples its acceleration over the burn, so a constant one makes cutoff late.
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
    /// How far the stack travels along the thrust line while gaining that velocity, over what it would
    /// have coasted: where it will be at cutoff, which is where the arc is solved from.
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
