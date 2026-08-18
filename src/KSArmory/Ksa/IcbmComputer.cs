using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One craft's ICBM computer: it reads the world, runs <see cref="IcbmProgram"/> and flies the
/// rocket to a place on a map.
///
/// <para>Everything that decides anything is in <c>Sim/</c> and tested there. What is here is the
/// two conversions that cannot be: the world into a <see cref="IcbmState"/>, and the program's
/// answer into writes on somebody else's vehicle.</para>
///
/// <para><b>Both conversions are into the parent body's inertial frame</b>, not the ecliptic. A
/// half-hour ballistic flight carries 54 million kilometres of the planet's own travel through
/// every ecliptic term, and a solve differencing two of them across even a fraction of a step leaks
/// a piece of it. Working in Cci removes the carrier exactly rather than approximately, because it
/// is the frame the engine's own orbital mechanics are written in.
/// See <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
/// </summary>
internal sealed class IcbmComputer
{
    // KSA's own value, so an orbit solved here and one drawn by the game agree.
    private const double GravitationalConstant = 6.6743e-11;

    // How often the impact prediction is re-flown. It is a readout, not a control loop.
    private const double PredictIntervalSeconds = 0.5;

    // Coarse enough to be cheap over half an hour, fine enough to land in the right place.
    private const double PredictStepSeconds = 2.0;

    private readonly List<double3> _path = [];
    private double _sincePredict = double.PositiveInfinity;
    private bool _driving;
    private double _throttleAchieved = 1.0;

    public Vehicle Craft { get; }

    public IcbmConfig Config { get; }

    public IcbmProgram Program { get; }

    /// <summary>Where it has been told to put the warheads. Nothing happens until this is set.</summary>
    public AimSite Target { get; private set; } = AimSite.None;

    /// <summary>The last command issued, which is what every readout on the panel is describing.</summary>
    public IcbmCommand Command { get; private set; }

    /// <summary>Where the vehicle would land if everything stopped now. Null when it would not.</summary>
    public ImpactPredictor.Impact? PredictedImpact { get; private set; }

    /// <summary>How far the predicted impact is from the aim point, along the ground.</summary>
    public double PredictedMissMetres { get; private set; } = double.NaN;

    /// <summary>The body the flight is around, as the guidance sees it.</summary>
    public BallisticBody Body { get; private set; }

    /// <summary>Height over the mean sphere, for readouts that mean nothing on the ground.</summary>
    public double AltitudeMetres { get; private set; }

    /// <summary>
    /// Seconds until the warheads arrive, from now.
    ///
    /// <para>Taken from the flown prediction wherever there is one, and from the plan only before
    /// there is. The two disagree while the burn is still running — the plan assumes it finishes,
    /// the prediction assumes it stops now — and the plan is the honest answer to "when will this
    /// arrive" right up until the engines quit.</para>
    /// </summary>
    public double SecondsToArrival
        => Program.IsBurning || Program.Phase == IcbmPhase.Holding
               ? Program.SecondsToArrival
               : PredictedImpact?.Seconds ?? double.NaN;

    public Celestial? Parent { get; private set; }

    public IcbmComputer(Vehicle craft, IcbmConfig config)
    {
        Craft = craft;
        Config = config;
        Program = new IcbmProgram(config);
    }

    public void Designate(AimSite site)
    {
        Target = site;
        Program.Reset();
        PredictedImpact = null;
        PredictedMissMetres = double.NaN;
        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} designated {site.Describe()}");
    }

    /// <summary>Forget the target and the flight, and hand the vehicle back.</summary>
    public void Abort(string why)
    {
        if (_driving)
        {
            VehicleCommand.SetEngine(Craft, running: false);
            VehicleCommand.ReleaseAttitude(Craft);
            _driving = false;
        }

        Program.Reset();
        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} stood down: {why}");
    }

    /// <param name="release">
    /// The weapon aboard, as the one thing this needs of it: something that can be told to shoot at
    /// a place. Null for a vehicle carrying nothing that lets go, which flies the arc regardless.
    /// </param>
    public void Update(double simStep, IManualFire? release)
    {
        if (!KsaWorld.IsAlive(Craft)) return;

        if (!Config.Armed)
        {
            // Standing down has to be an edge rather than a state. Writing "manual" every frame
            // would take the vehicle away from a player who is flying it by hand, on a computer
            // that is switched off.
            if (_driving) Abort("disarmed");
            Command = Program.Update(simStep, Sample(simStep, out _));
            return;
        }

        IcbmState state = Sample(simStep, out bool usable);
        if (!usable)
        {
            Command = Program.Update(simStep, state);
            return;
        }

        Command = Program.Update(simStep, state);

        Predict(simStep, state);

        if (Command.EngineOn)
        {
            if (VehicleCommand.TryAim(Craft, Parent!, state.PositionCci, Command.ThrustDirectionCci))
            {
                _driving = true;
            }

            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, Command.Throttle);
            VehicleCommand.SetEngine(Craft, running: true);

            if (Command.RequestStage)
            {
                Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} staging: {Command.Hold}");
                VehicleCommand.Stage(Craft);
            }
        }
        else if (_driving)
        {
            VehicleCommand.SetEngine(Craft, running: false);
            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, 1.0);

            // Attitude is kept after cutoff rather than released. The bus has to hold the line it
            // was cut off on for the warheads to leave along it, and handing the vehicle back the
            // instant the engines stop lets it tumble at exactly the wrong moment.
            VehicleCommand.TryAim(Craft, Parent!, state.PositionCci, Command.ThrustDirectionCci);
        }

        if (Config.AutoRelease && Command.ReadyToDeploy) Release(release);
    }

    /// <summary>Let one warhead go at the aim point, if there is one to let go and it is ready.</summary>
    public bool Release(IManualFire? weapon)
    {
        if (weapon is null || !weapon.ReadyToFire) return false;
        if (TargetEcl() is not { } targetEcl) return false;

        return weapon.FireAt(targetEcl);
    }

    /// <summary>The trajectory, in the ecliptic, for drawing. Empty until a prediction has run.</summary>
    public void PathEcl(List<double3> into)
    {
        into.Clear();
        if (Parent is null) return;

        doubleQuat cci2Cce = Parent.GetCci2Cce();
        double3 centre = Parent.GetPositionEcl();

        for (int i = 0; i < _path.Count; i++) into.Add(_path[i].Transform(cci2Cce) + centre);
    }

    /// <summary>Where the aim point is right now, in the ecliptic. Null when nothing is designated.</summary>
    public double3? TargetEcl()
    {
        if (Parent is null || !Target.IsSet) return null;
        return SurfacePointEcl(Parent, Target.LatitudeDeg, Target.LongitudeDeg);
    }

    private IcbmState Sample(double simStep, out bool usable)
    {
        usable = false;

        Parent = KsaWorld.ParentBody(Craft);
        if (Parent is null) return default;

        double mu = Parent.Mass * GravitationalConstant;

        // The spin axis is exactly +Z in a body's own Cci: KSA builds Ccf from Cci by rotating
        // about UnitZ and nothing else, so there is no obliquity term to carry here. It is the
        // *ecliptic* that sees the tilt.
        Body = new BallisticBody(mu, Parent.MeanRadius, new double3(0, 0, 1), Parent.GetAngularVelocity());

        doubleQuat cce2Cci = Parent.GetCce2Cci();
        double3 positionCci = (KsaWorld.PositionEcl(Craft) - Parent.GetPositionEcl()).Transform(cce2Cci);
        double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - Parent.GetVelocityEcl()).Transform(cce2Cci);

        double3 aimCci = default;
        bool hasAim = false;

        if (Target.IsSet && Target.BodyName == Parent.Id)
        {
            aimCci = (SurfacePointEcl(Parent, Target.LatitudeDeg, Target.LongitudeDeg)
                      - Parent.GetPositionEcl()).Transform(cce2Cci);
            hasAim = true;
        }

        FlightComputer computer = Craft.FlightComputer;
        ActiveEnginePerformance engines = computer.ActiveEnginePerformanceMax;

        BoosterPerformance booster = new(engines.Thrust, engines.MassFlowRate,
                                         Craft.TotalMass, Craft.PropellantMass);

        double density = KsaWorld.MediumDensityRatioAt(Parent, KsaWorld.PositionEcl(Craft));

        usable = Body.IsUsable;
        AltitudeMetres = Body.AltitudeOf(positionCci);

        return new IcbmState(Body, positionCci, velocityCci, aimCci, hasAim, booster, density,
                             Craft.IsAnyEnginePropellantAvailable(), _throttleAchieved);
    }

    // The aim point sits on the real ground rather than on the mean sphere, and that is not a
    // refinement. The whole solve is a transfer between two *points*, so a target standing five
    // kilometres up is hit by aiming at where it stands - no terrain model anywhere else in the
    // guidance, and no correction to apply afterwards.
    private static double3 SurfacePointEcl(Celestial body, double latitudeDeg, double longitudeDeg)
    {
        double3 dirCcf = body.GetDirCcfFromLatLon(latitudeDeg, longitudeDeg);
        double height = body.GetTerrainHeightFromDirCcf(dirCcf, accurate: true);
        return dirCcf.Transform(body.GetCcf2Cce()) * (body.MeanRadius + height) + body.GetPositionEcl();
    }

    private void Predict(double simStep, in IcbmState state)
    {
        _sincePredict += simStep;
        if (_sincePredict < PredictIntervalSeconds) return;
        _sincePredict = 0.0;

        if (ImpactPredictor.TryPredict(Body, state.PositionCci, state.VelocityCci, PredictStepSeconds,
                                       ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                       pathCci: _path))
        {
            PredictedImpact = hit;
            PredictedMissMetres = state.HasAim
                ? Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, state.AimNowCci)
                : double.NaN;
        }
        else
        {
            PredictedImpact = null;
            PredictedMissMetres = double.NaN;
        }
    }
}
