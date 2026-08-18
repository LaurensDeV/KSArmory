using Brutal.Numerics;

namespace KSArmory.Tests;

/// <summary>
/// A whole rocket, flown headlessly, so the guidance can be judged on where it puts warheads
/// rather than on whether its intermediate numbers look plausible.
///
/// <para>Deliberately not the flight model the guidance assumes. It has drag the solver knows
/// nothing about, an attitude that lags the command, staging that changes the vehicle underneath
/// the loop and a coarser integration than anything in <c>Sim/</c> — because the whole claim being
/// tested is that velocity-to-be-gained guidance arrives anyway. A rig that shared the guidance's
/// model could only prove the arithmetic is self-consistent.</para>
/// </summary>
internal sealed class IcbmFlightRig
{
    public required BallisticBody Body { get; init; }

    public double3 PositionCci;
    public double3 VelocityCci;

    /// <summary>Stages, heaviest first. Each is burnt to nothing before the next is reachable.</summary>
    public required List<Stage> Stages { get; init; }

    /// <summary>How fast the vehicle can swing its thrust line. Zero points it wherever it is told.</summary>
    public double AttitudeRateDegPerSec = 12.0;

    /// <summary>Drag area over mass. Zero for a rig with no air resistance at all.</summary>
    public double DragAreaOverMass = 4e-5;

    public double ScaleHeightMetres = 8000.0;

    public double SeaLevelDensity = 1.225;

    public int StageIndex;

    private double3 _pointing;

    internal sealed class Stage
    {
        public required double DryMassKg;
        public required double PropellantKg;
        public required double ThrustNewtons;
        public required double ExhaustVelocity;

        public double MassFlow => ThrustNewtons / ExhaustVelocity;
    }

    internal readonly record struct Flight(
        bool Reached,
        double3 CutoffPositionCci,
        double3 CutoffVelocityCci,
        double CutoffSeconds,
        double PropellantLeftKg,
        IcbmPhase FinalPhase,
        string Hold,
        double PeakDynamicPressure,
        double PeakAngleOfAttackDeg);

    public double MassAbove(int from)
    {
        double m = 0.0;
        for (int i = from; i < Stages.Count; i++) m += Stages[i].DryMassKg + Stages[i].PropellantKg;
        return m;
    }

    public double DensityRatioAt(double altitude)
    {
        if (ScaleHeightMetres <= 0.0 || altitude > 20.0 * ScaleHeightMetres) return 0.0;
        return Math.Exp(-Math.Max(altitude, 0.0) / ScaleHeightMetres);
    }

    public BoosterPerformance Performance()
    {
        if (StageIndex >= Stages.Count) return new BoosterPerformance(0, 0, MassAbove(StageIndex), 0);
        Stage s = Stages[StageIndex];
        return new BoosterPerformance(s.ThrustNewtons, s.MassFlow, MassAbove(StageIndex), s.PropellantKg);
    }

    /// <summary>
    /// Fly it. <paramref name="aimAtEpoch"/> is fixed to the ground and carried by the spin.
    ///
    /// <para>The program is stepped every frame, which is what it expects: it decides for itself
    /// how often to re-solve the trajectory, and the frame is what runs its cutoff countdown down.
    /// </para>
    /// </summary>
    public Flight Fly(IcbmProgram program, double3 aimAtEpoch, double step, double maxSeconds)
    {
        _pointing = Vec.Unit(PositionCci);
        double elapsed = 0.0;
        IcbmCommand command = default;
        double peakQ = 0.0;
        double peakAoa = 0.0;

        while (elapsed < maxSeconds)
        {
            double altitude = Body.AltitudeOf(PositionCci);
            double density = DensityRatioAt(altitude);
            double3 airflow = VelocityCci - Body.GroundVelocityCci(PositionCci);

            {
                IcbmState state = new(Body, PositionCci, VelocityCci,
                                      Body.CarryCci(aimAtEpoch, elapsed), HasAim: true,
                                      Performance(), density,
                                      PropellantAvailable: StageIndex < Stages.Count
                                                           && Stages[StageIndex].PropellantKg > 0.0,
                                      // This rig honours a throttle command exactly. A real stack
                                      // ramps, and one with solid motors ignores it entirely, which
                                      // is why the program is told what it got rather than assuming.
                                      ThrottleAchieved: command.EngineOn ? command.Throttle : 1.0);

                command = program.Update(elapsed == 0.0 ? 0.0 : step, state);

                if (command.RequestStage && StageIndex < Stages.Count) StageIndex++;
            }

            if (program.Phase == IcbmPhase.Coast)
            {
                return new Flight(true, PositionCci, VelocityCci, elapsed,
                                  StageIndex < Stages.Count ? Stages[StageIndex].PropellantKg : 0.0,
                                  program.Phase, command.Hold, peakQ, peakAoa);
            }

            if (program.Phase == IcbmPhase.NoSolution || program.Phase == IcbmPhase.Idle)
            {
                return new Flight(false, PositionCci, VelocityCci, elapsed, 0.0,
                                  program.Phase, command.Hold, peakQ, peakAoa);
            }

            Swing(command.ThrustDirectionCci, step);

            double q = 0.5 * density * SeaLevelDensity * Vec.Len2(airflow);
            peakQ = Math.Max(peakQ, q);
            if (q > 200.0) peakAoa = Math.Max(peakAoa, Vec.AngleBetween(airflow, _pointing) * 180.0 / Math.PI);

            Integrate(command, step, density, airflow);

            elapsed += step;
        }

        return new Flight(false, PositionCci, VelocityCci, elapsed, 0.0, program.Phase,
                          "ran out of time", peakQ, peakAoa);
    }

    private void Swing(double3 wanted, double step)
    {
        double3 want = Vec.Unit(wanted);
        if (want.Equals(Vec.Zero)) return;
        if (AttitudeRateDegPerSec <= 0.0) { _pointing = want; return; }

        double limit = AttitudeRateDegPerSec * Math.PI / 180.0 * step;
        double angle = Vec.AngleBetween(_pointing, want);
        if (angle <= limit) { _pointing = want; return; }

        double3 axis = Vec.Cross(_pointing, want);
        if (Vec.Len2(axis) < 1e-18) { _pointing = want; return; }

        _pointing = Vec.Unit(doubleQuat.CreateFromAxisAngle(Vec.Unit(axis), limit) * _pointing);
    }

    private void Integrate(in IcbmCommand command, double step, double density, double3 airflow)
    {
        double mass = MassAbove(StageIndex);
        double3 acceleration = Body.GravityCci(PositionCci);

        if (DragAreaOverMass > 0.0 && density > 0.0)
        {
            double speed = Vec.Len(airflow);
            acceleration -= airflow * (0.5 * density * SeaLevelDensity * speed * DragAreaOverMass);
        }

        bool burning = command.EngineOn && command.Throttle > 0.0
                       && StageIndex < Stages.Count && Stages[StageIndex].PropellantKg > 0.0;

        if (burning && mass > 0.0)
        {
            Stage s = Stages[StageIndex];
            double burnt = Math.Min(s.PropellantKg, s.MassFlow * command.Throttle * step);
            acceleration += _pointing * (s.ThrustNewtons * command.Throttle / mass);
            s.PropellantKg -= burnt;
        }

        // Velocity first, then position on the new velocity: symplectic, and stable at a step this
        // coarse in a way that plain Euler is not.
        VelocityCci += acceleration * step;
        PositionCci += VelocityCci * step;
    }
}
