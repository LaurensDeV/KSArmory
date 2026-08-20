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

    /// <summary>
    /// The step used while the program says short ones are not needed — a coast, in other words.
    /// The game allows timewarp there for the same reason, and stepping it finely would spend
    /// minutes of test time integrating something nothing is steering.
    ///
    /// <para>Coarse on purpose. Guidance re-solves from wherever the vehicle actually is, so a
    /// rough coast costs nothing at cutoff — it just moves the vehicle somewhere slightly
    /// different, which the loop then flies from.</para>
    /// </summary>
    public double CoastStepSeconds = 2.0;

    /// <summary>
    /// Frames between a command being issued and the vehicle acting on it.
    ///
    /// <para>Zero is the rig's original behaviour and is a lie the real game does not tell: KSA
    /// copies control inputs into its worker in <c>PrepareWorker</c>, which runs before this mod's
    /// hook, so a write lands on the <em>next</em> frame. A cutoff therefore arrives late and the
    /// stack burns on past it. With this at zero the rig cannot see any error in the cutoff at all.
    /// </para>
    /// </summary>
    public int CommandLatencyFrames;

    /// <summary>
    /// How fast the throttle can actually move, in fraction per second.
    ///
    /// <para>Infinite is the rig's original behaviour — a servo that arrives instantly, which is
    /// what hid the cost of the throttle-down ramp. <b>Zero or less means the stack cannot throttle
    /// at all</b> and stays at full until the engine is shut off, which is the case the cutoff has
    /// to survive: it is what a solid motor does, and what any engine does if the mod's throttle
    /// write does not reach it.</para>
    /// </summary>
    public double ThrottleRatePerSecond = double.PositiveInfinity;

    /// <summary>
    /// The least throttle the engine will hold, whatever it is asked for.
    ///
    /// <para>Zero is the rig's original behaviour and is another thing the game does not do:
    /// KSA clamps a throttle command to the craft's own <c>GetMinThrottle</c>, so the ramp bottoms
    /// out an order of magnitude above <see cref="IcbmProgram.MinCommandedThrottle"/> and the burn
    /// creeps at that for the last couple of seconds.</para>
    /// </summary>
    public double MinThrottle;

    /// <summary>
    /// How unevenly the step arrives, as a fraction either side of the nominal one.
    ///
    /// <para>Zero is a metronome, which nothing outside a test is. KSA's step is
    /// <c>dtPlayer x achievedFraction x simSpeed</c> and <c>dtPlayer</c> carries the display's
    /// frame pacing — measured in flight alternating between 8.33 ms and 25.0 ms on a 120 Hz
    /// screen, which is 0.5 here. <b>A constant step cannot see the cutoff defect this exists to
    /// catch</b>, because what the frozen thrust line leaves behind is driven by the solve moving
    /// between frames.</para>
    /// </summary>
    public double StepJitter;

    /// <summary>What the throttle actually is, as opposed to what was asked for.</summary>
    public double ThrottleAchieved { get; private set; } = 1.0;

    /// <summary>
    /// Something riding the flight that moves the aim and watches what the shot does about it —
    /// <see cref="AimCorrection"/> wired the way <c>Ksa/IcbmComputer.cs</c> wires it.
    ///
    /// <para>Null is a vehicle aimed exactly where it was pointed, which is every other suite.</para>
    /// </summary>
    public IAimLoop? AimLoop;

    /// <inheritdoc cref="AimLoop"/>
    internal interface IAimLoop
    {
        /// <summary>Where to actually solve to, given where the shot is meant to land.</summary>
        double3 Apply(double3 aimNowCci);

        /// <summary>Whether the aim has stopped moving, which is what the arrival latch waits for.</summary>
        bool IsSteady { get; }

        /// <summary>One cycle, after the program has been stepped and before the vehicle acts.</summary>
        void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci, double stepSeconds);
    }

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
        double PeakAngleOfAttackDeg,
        double3 LastBurnDirectionCci,
        double3 CoastDirectionCci);

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
        double3 lastBurnDirection = Vec.Zero;
        Queue<IcbmCommand> inFlight = new();
        ThrottleAchieved = 1.0;
        int frame = 0;

        while (elapsed < maxSeconds)
        {
            double h = program.NeedsShortSteps ? step : Math.Max(step, CoastStepSeconds);
            if (program.NeedsShortSteps && StepJitter > 0.0)
            {
                h = step * (frame++ % 2 == 0 ? 1.0 + StepJitter : 1.0 - StepJitter);
            }

            double altitude = Body.AltitudeOf(PositionCci);
            double density = DensityRatioAt(altitude);
            double3 airflow = VelocityCci - Body.GroundVelocityCci(PositionCci);

            double3 aimNow = Body.CarryCci(aimAtEpoch, elapsed);

            {
                IcbmState state = new(Body, PositionCci, VelocityCci,
                                      AimLoop?.Apply(aimNow) ?? aimNow, HasAim: true,
                                      Performance(), density,
                                      PropellantAvailable: StageIndex < Stages.Count
                                                           && Stages[StageIndex].PropellantKg > 0.0,
                                      // What the stack has, never what was asked of it. A real one
                                      // ramps, and one with solid motors ignores the ask entirely.
                                      ThrottleAchieved: ThrottleAchieved,
                                      AimIsSteady: AimLoop?.IsSteady ?? true);

                command = program.Update(elapsed == 0.0 ? 0.0 : h, state);

                if (command.RequestStage && StageIndex < Stages.Count) StageIndex++;
            }

            // After the step and before the vehicle acts on it, which is where the computer's own
            // freeze and prediction sit. The cutoff frame is observed too: it is the last cycle the
            // correction would ever have seen, and leaving it out hides what the aim ended up worth.
            AimLoop?.AfterUpdate(program, command, aimNow, h);

            if (program.Phase == IcbmPhase.Coast)
            {
                return new Flight(true, PositionCci, VelocityCci, elapsed,
                                  StageIndex < Stages.Count ? Stages[StageIndex].PropellantKg : 0.0,
                                  program.Phase, command.Hold, peakQ, peakAoa,
                                  lastBurnDirection, command.ThrustDirectionCci);
            }

            if (program.Phase == IcbmPhase.NoSolution || program.Phase == IcbmPhase.Idle)
            {
                return new Flight(false, PositionCci, VelocityCci, elapsed, 0.0,
                                  program.Phase, command.Hold, peakQ, peakAoa,
                                  lastBurnDirection, command.ThrustDirectionCci);
            }

            // The last direction commanded while the burn still had real work left in it.
            if (program.VelocityToGain > IcbmProgram.HoldDirectionBelow)
            {
                lastBurnDirection = command.ThrustDirectionCci;
            }

            // What the vehicle is acting on this frame, which is not what was just decided.
            IcbmCommand applied = command;
            if (CommandLatencyFrames > 0)
            {
                inFlight.Enqueue(command);
                applied = inFlight.Count > CommandLatencyFrames ? inFlight.Dequeue() : default;
            }

            SlewThrottle(applied, h);

            Swing(applied.ThrustDirectionCci, h);

            double q = 0.5 * density * SeaLevelDensity * Vec.Len2(airflow);
            peakQ = Math.Max(peakQ, q);
            if (q > 200.0) peakAoa = Math.Max(peakAoa, Vec.AngleBetween(airflow, _pointing) * 180.0 / Math.PI);

            Integrate(applied, h, density, airflow);

            elapsed += h;
        }

        return new Flight(false, PositionCci, VelocityCci, elapsed, 0.0, program.Phase,
                          "ran out of time", peakQ, peakAoa, lastBurnDirection,
                          command.ThrustDirectionCci);
    }

    private void SlewThrottle(in IcbmCommand command, double step)
    {
        double wanted = command.EngineOn ? Math.Clamp(command.Throttle, MinThrottle, 1.0) : 1.0;

        if (ThrottleRatePerSecond <= 0.0)
        {
            ThrottleAchieved = 1.0;
            return;
        }

        if (double.IsInfinity(ThrottleRatePerSecond))
        {
            ThrottleAchieved = wanted;
            return;
        }

        double limit = ThrottleRatePerSecond * step;
        double error = wanted - ThrottleAchieved;
        ThrottleAchieved += Math.Clamp(error, -limit, limit);
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

        double throttle = Math.Clamp(ThrottleAchieved, 0.0, 1.0);

        bool burning = command.EngineOn && throttle > 0.0
                       && StageIndex < Stages.Count && Stages[StageIndex].PropellantKg > 0.0;

        if (burning && mass > 0.0)
        {
            Stage s = Stages[StageIndex];
            double burnt = Math.Min(s.PropellantKg, s.MassFlow * throttle * step);
            acceleration += _pointing * (s.ThrustNewtons * throttle / mass);
            s.PropellantKg -= burnt;
        }

        // Velocity first, then position on the new velocity: symplectic, and stable at a step this
        // coarse in a way that plain Euler is not.
        VelocityCci += acceleration * step;
        PositionCci += VelocityCci * step;
    }
}
