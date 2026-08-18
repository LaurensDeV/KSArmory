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
    private double3 _rollReference;
    private bool _warpIsOurs;
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
        _rollReference = Vec.Zero;
        PredictedImpact = null;
        PredictedMissMetres = double.NaN;
        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} designated {site.Describe()}");
    }

    /// <summary>Forget the target and the flight, and hand the vehicle back.</summary>
    public void Abort(string why)
    {
        // A warp asked for on this shot's behalf outlives the shot otherwise, and the player is
        // left fast-forwarding towards a burn that is no longer going to happen.
        if (_warpIsOurs)
        {
            KsaWorld.StopAutoWarp();
            _warpIsOurs = false;
        }

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

        // Attitude is driven for every phase that is doing something, not only while an engine is
        // lit. A hold can be an hour long and the vehicle is pointed at the burn for all of it; and
        // after cutoff the bus has to keep the line it was cut off on for the warheads to leave
        // along. Both were left free before, which is a vehicle drifting when it should be settled.
        if (Command.Phase is not (IcbmPhase.Idle or IcbmPhase.NoSolution))
        {
            _rollReference = AimFrame.Advance(_rollReference, Command.ThrustDirectionCci,
                                              -Vec.Unit(state.PositionCci), RollFallback(state));

            if (VehicleCommand.TryAim(Craft, Parent!, state.PositionCci, Command.ThrustDirectionCci,
                                      _rollReference))
            {
                _driving = true;
            }
        }

        if (Command.EngineOn)
        {
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
        }

        if (Config.AutoRelease && Command.ReadyToDeploy) Release(release);

        EndOurWarp();
    }

    /// <summary>
    /// Hand the wait to KSA's own warp-to-a-time. Pressed, never automatic.
    ///
    /// <para>Warping is an action rather than a setting, and taking the player's time control
    /// because a target happened to be designated is not a thing a weapon gets to do. They may have
    /// set a tenth speed to watch something.</para>
    /// </summary>
    public bool TryWarpToWindow()
    {
        if (Program.Phase != IcbmPhase.Holding) return false;
        if (!ReferenceEquals(Craft, KsaWorld.ControlledVehicle)) return false;
        if (KsaWorld.IsAutoWarpActive) return false;

        double wait = Command.SecondsToBurn;
        if (!double.IsFinite(wait) || wait <= IcbmProgram.WarpHoldLeadSeconds * 2.0) return false;

        if (!KsaWorld.TryAutoWarpTo(wait, IcbmProgram.WarpHoldLeadSeconds)) return false;

        _warpIsOurs = true;
        Log.Info($"warping {IcbmProgram.Clock(wait)} to the burn window on {KsaWorld.DisplayName(Craft)}");
        return true;
    }

    /// <summary>Whether the window is far enough away for warping to it to be worth offering.</summary>
    public bool CanWarpToWindow
        => Program.Phase == IcbmPhase.Holding
        && !KsaWorld.IsAutoWarpActive
        && ReferenceEquals(Craft, KsaWorld.ControlledVehicle)
        && double.IsFinite(Command.SecondsToBurn)
        && Command.SecondsToBurn > IcbmProgram.WarpHoldLeadSeconds * 2.0;

    // Ends a warp this computer started, and only one it started. KSA's warp is still travelling
    // when it reaches its target, so handing over at speed leaves the hold trying to brake the
    // world from a thousand times in one frame - which computes a speed of nearly zero and pauses
    // the game. Stopping it resets the speed. A warp the *player* started is theirs.
    private void EndOurWarp()
    {
        if (!_warpIsOurs) return;

        if (!KsaWorld.IsAutoWarpActive) { _warpIsOurs = false; return; }

        double wait = Command.SecondsToBurn;
        if (Program.Phase == IcbmPhase.Holding && double.IsFinite(wait)
            && wait > IcbmProgram.WarpHoldLeadSeconds)
        {
            return;
        }

        Log.Info($"stopping the warp on {KsaWorld.DisplayName(Craft)}, "
                 + $"{IcbmProgram.Clock(Math.Max(wait, 0.0))} to the burn");
        KsaWorld.StopAutoWarp();
        _warpIsOurs = false;
    }

    // Hand the wait to KSA's own warp-to-a-time. Only while holding, only for the craft being
    // flown, and only out to a margin short of the burn - the last minute belongs to WarpPolicy,
    // which cannot slow the world down at all while an auto-warp is running.

    /// <summary>Let one warhead go at the aim point, if there is one to let go and it is ready.</summary>
    public bool Release(IManualFire? weapon)
    {
        if (weapon is null || !weapon.ReadyToFire) return false;
        if (TargetEcl() is not { } targetEcl) return false;

        return weapon.FireAt(targetEcl);
    }

    /// <summary>The trajectory, in the ecliptic, for drawing. Empty until a prediction has run.</summary>
    // Something square to the vertical for the roll to clock to when the planet cannot supply one,
    // which is the whole of a vertical rise. Downrange is horizontal by construction; before there
    // is one, the way the vehicle is already moving will do.
    private double3 RollFallback(in IcbmState state)
        => Program.DownrangeCci.Equals(Vec.Zero) ? state.VelocityCci : Program.DownrangeCci;

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
