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
    private readonly AimCorrection _aim = new();
    private double3 _trueAimCci;
    private MunitionProfile? _warhead;
    private bool _warpIsOurs;
    private IcbmPhase _reported = IcbmPhase.Idle;
    private double _sinceProbe;
    private double3 _lastCommanded;

    // Often enough to see an oscillation, rare enough not to fill the log with a burn's worth.
    private const double ProbeIntervalSeconds = 0.5;

    // How much of the remaining wait each warp leaves for the next one, and the most it will leave.
    // A fraction rather than a constant because the span is what decides how fast KSA warps: a
    // fixed margin off a ninety-minute hold is still approached at thousands of times speed.
    private const double MarginFraction = 0.2;

    private const double MaxMarginSeconds = 900.0;
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

    /// <summary>How far off the plane the vehicle is flying in the target sits, in degrees.</summary>
    public double OffPlaneDegrees { get; private set; }

    /// <summary>Roughly what turning the orbit that far would cost on its own.</summary>
    public double PlaneChangeCost { get; private set; }

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
        _reported = IcbmPhase.Idle;
        _aim.Reset();
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

        AttitudeHook.Release(Craft);

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
    public void Update(double simStep, double playerStep, IManualFire? release)
    {
        if (!KsaWorld.IsAlive(Craft)) return;

        // What the prediction is of. The bus cuts off above the air; the warheads it drops fly all
        // the way down through it, and they are the things that have to arrive.
        _warhead = release?.Munition;

        if (!Config.Armed)
        {
            // Standing down has to be an edge rather than a state. Writing "manual" every frame
            // would take the vehicle away from a player who is flying it by hand, on a computer
            // that is switched off.
            if (_driving) Abort("disarmed");
            Command = Program.Update(simStep, Sample(playerStep, out _));
            return;
        }

        IcbmState state = Sample(playerStep, out bool usable);
        if (!usable)
        {
            Command = Program.Update(simStep, state);
            return;
        }

        Command = Program.Update(simStep, state);

        // One line per phase change. Every gate in the program returns quietly, so a flight that
        // goes wrong leaves nothing behind saying which of them it went wrong at - and the panel
        // only shows the state it is in now, not the order it got there.
        if (Command.Phase != _reported)
        {
            _reported = Command.Phase;
            Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: {Command.Phase} at "
                     + $"{AltitudeMetres / 1000.0:F0} km, {Command.VelocityToGain:F0} m/s to gain, "
                     + $"burn in {IcbmProgram.Clock(Command.SecondsToBurn)}, "
                     + $"impact in {IcbmProgram.Clock(SecondsToArrival)}, "
                     + $"target {OffPlaneDegrees:F1} deg off plane ({PlaneChangeCost:F0} m/s), "
                     + $"reach {Command.Reach}"
                     + (double.IsFinite(Program.ResidualAtCutoff)
                            ? $", cut off {Program.ResidualAtCutoff:F1} m/s short"
                            : "")
                     // The mod's own prediction against its own aim. Near zero means the solution
                     // is self-consistent and whatever missed happened to the round afterwards;
                     // large means the arc never pointed at the target and the burn flying it
                     // perfectly was never going to help.
                     + PredictedImpactSaid()
                     + $" :: {Command.Hold}");
        }

        Predict(simStep, state);

        // Read before anything is written this frame. KSA replaces the whole flight computer from
        // its worker every frame, so this is what survived of last frame's command — and comparing
        // it with what is read straight after writing is the only way to tell a write that never
        // lands from one the engine reverts.
        FlightComputerAttitudeMode wasMode = Craft.FlightComputer.AttitudeMode;
        FlightComputerAttitudeTrackTarget wasTrack = Craft.FlightComputer.AttitudeTrackTarget;

        // Attitude is driven for every phase that is doing something, not only while an engine is
        // lit. A hold can be an hour long and the vehicle is pointed at the burn for all of it; and
        // after cutoff the bus has to keep the line it was cut off on for the warheads to leave
        // along. Both were left free before, which is a vehicle drifting when it should be settled.
        bool aimed = false;

        if (Command.Phase is not (IcbmPhase.Idle or IcbmPhase.NoSolution))
        {
            _rollReference = AimFrame.Advance(_rollReference, Command.ThrustDirectionCci,
                                              -Vec.Unit(state.PositionCci), RollFallback(state));

            // Handed to the hook rather than written here. A write from this pass is discarded
            // before anything reads it - see AttitudeHook.
            AttitudeHook.Hold(Craft, Command.ThrustDirectionCci, _rollReference);
            aimed = AttitudeHook.Installed;
            if (aimed) _driving = true;
        }
        else
        {
            AttitudeHook.Release(Craft);
        }

        ProbeAttitude(playerStep, wasMode, wasTrack, aimed);

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

        CarryOurWarp();
    }

    /// <summary>
    /// Hand the wait to KSA's own warp-to-a-time. Pressed, never automatic.
    ///
    /// <para>Warping is an action rather than a setting, and taking the player's time control
    /// because a target happened to be designated is not a thing a weapon gets to do. They may have
    /// set a tenth speed to watch something.</para>
    ///
    /// <para>One press covers the whole wait, in hops. That is not tidiness — it is the only way
    /// the handover can work. KSA scales its warp rate to the <em>span</em> it is asked to cover, so
    /// a single jump to the end of a ninety-minute hold arrives doing thousands of times normal
    /// speed, where the last minute of it passes in under two frames and there is nowhere to hand
    /// over. Each hop leaves a margin, and the next one covers a shorter span and so runs gentler,
    /// until the approach is slow enough to be caught.</para>
    /// </summary>
    public bool TryWarpToWindow()
    {
        if (!CanWarpToWindow) return false;

        double wait = Command.SecondsToBurn;
        double margin = Math.Clamp(wait * MarginFraction, IcbmProgram.WarpHoldLeadSeconds, MaxMarginSeconds);

        if (!KsaWorld.TryAutoWarpTo(wait, margin)) return false;

        _warpIsOurs = true;
        Log.Info($"warping to within {IcbmProgram.Clock(margin)} of the burn window on "
                 + $"{KsaWorld.DisplayName(Craft)}, {IcbmProgram.Clock(wait)} to go");
        return true;
    }

    /// <summary>Whether the window is far enough away for warping to it to be worth offering.</summary>
    public bool CanWarpToWindow
        => Program.Phase == IcbmPhase.Holding
        && !KsaWorld.IsAutoWarpActive
        && ReferenceEquals(Craft, KsaWorld.ControlledVehicle)
        && double.IsFinite(Command.SecondsToBurn)
        && Command.SecondsToBurn > IcbmProgram.WarpHoldLeadSeconds * 2.0;

    // Carries a warp this computer started through to the window, and ends it if the shot stops
    // wanting one. Only ever a warp it started: one the player started is theirs.
    private void CarryOurWarp()
    {
        if (!_warpIsOurs) return;

        if (Program.Phase != IcbmPhase.Holding)
        {
            if (KsaWorld.IsAutoWarpActive)
            {
                Log.Info($"stopping the warp on {KsaWorld.DisplayName(Craft)}, the hold is over");
                KsaWorld.StopAutoWarp();
            }

            _warpIsOurs = false;
            return;
        }

        // Still running: leave it alone. It stops itself at the margin, which is the whole reason
        // for asking it to stop short rather than braking the world by hand.
        if (KsaWorld.IsAutoWarpActive) return;

        // A hop finished. Close the remaining gap with another, shorter and therefore slower one.
        if (CanWarpToWindow && TryWarpToWindow()) return;

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

        bool away = weapon.FireAt(targetEcl);
        if (away) ProbeRelease();
        return away;
    }

    // What the prediction says about the state the warhead is actually leaving on, beside where the
    // round then lands. The phase line cannot answer this: it is printed before the frame's Predict
    // and while the engines are still lit, so it carries a prediction of the *solved* cutoff arc.
    // Only these two numbers isolate what the prediction and the round still disagree about, which
    // is the difference every remaining metre of the miss lives in.
    //
    // At INFO, and it earns it: this fires once per warhead released rather than per frame, and a
    // diagnostic nobody has switched on is one that is never there in the salvo that needed it.
    private void ProbeRelease()
    {
        if (Parent is not { } parent) return;
        if (_warhead is not { } warhead) return;

        try
        {
            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 positionCci = (KsaWorld.PositionEcl(Craft) - parent.GetPositionEcl()).Transform(cce2Cci);
            double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - parent.GetVelocityEcl()).Transform(cce2Cci);

            if (!ImpactPredictor.TryPredict(Body, positionCci, velocityCci, PredictStepSeconds,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, TerrainRadiusAt, null,
                                            new ImpactPredictor.Drag(DensityRatioAt, warhead)))
            {
                Log.Info("release probe: no impact predicted from the release state");
                return;
            }

            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());
            double miss = Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci);

            Log.Info($"release probe: predicted from the release state -> "
                      + $"{parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3}, "
                      + $"{miss / 1000.0:F1} km from the target, {hit.Seconds:F0} s of flight");
        }
        catch
        {
            // A probe that throws inside the frame hook is worse than one that says nothing.
        }
    }

    /// <summary>The trajectory, in the ecliptic, for drawing. Empty until a prediction has run.</summary>
    // What the flight computer makes of the attitude it is being given, which is the only way to
    // tell a command that is swinging from a vehicle that cannot hold a steady one. Both look like
    // tumbling from outside, and they want opposite fixes.
    private void ProbeAttitude(double playerStep, FlightComputerAttitudeMode wasMode,
                               FlightComputerAttitudeTrackTarget wasTrack, bool aimed)
    {
        if (!Program.IsBurning || Log.Threshold > Log.Level.Debug) return;

        _sinceProbe += playerStep;
        if (_sinceProbe < ProbeIntervalSeconds) return;
        _sinceProbe = 0.0;

        double3 wanted = Vec.Unit(Command.ThrustDirectionCci);
        double slew = _lastCommanded.Equals(Vec.Zero)
                          ? 0.0
                          : Vec.AngleBetween(_lastCommanded, wanted) * 180.0 / Math.PI;
        _lastCommanded = wanted;

        FlightComputer computer = Craft.FlightComputer;

        Log.Debug($"{KsaWorld.DisplayName(Craft)} attitude: aimed={aimed} "
                  + $"dir={(Vec.Len(Command.ThrustDirectionCci) > 0.0 ? "set" : "ZERO")} "
                  + $"slew {slew:F1} deg | before {wasMode}/{wasTrack} "
                  + $"-> after {computer.AttitudeMode}/{computer.AttitudeTrackTarget} | "
                  + $"error {computer.ErrorAngles} rates {computer.ErrorRates}");
    }

    // Where the mod thinks the arc lands, as a place rather than a distance. A distance says the
    // solution is wrong; the place says which way, and short-versus-sideways are different faults.
    private string PredictedImpactSaid()
    {
        if (!double.IsFinite(PredictedMissMetres) || PredictedImpact is not { } hit) return "";
        if (Parent is not { } parent) return "";

        try
        {
            // The prediction is un-carried to its own epoch, so it is a place on the ground in the
            // same terms the aim point is - which is what makes the two comparable at all.
            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());

            return $", own prediction {PredictedMissMetres / 1000.0:F1} km off "
                   + $"(lands {parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3})";
        }
        catch
        {
            return $", own prediction {PredictedMissMetres / 1000.0:F1} km off";
        }
    }

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

    private IcbmState Sample(double playerStep, out bool usable)
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
            _trueAimCci = (SurfacePointEcl(Parent, Target.LatitudeDeg, Target.LongitudeDeg)
                           - Parent.GetPositionEcl()).Transform(cce2Cci);

            // Aimed at the target plus whatever the flown prediction says the arc is losing. The
            // solver is exact for a *point* in vacuum; the round stops where the ground actually
            // is, and on a shallow arrival over rising terrain that is tens of kilometres short of
            // a summit. Correcting the aim is the only thing that closes it, because there is
            // nothing wrong with the trajectory - it arrives exactly where it was asked to.
            aimCci = _aim.Apply(_trueAimCci);
            hasAim = true;
        }

        FlightComputer computer = Craft.FlightComputer;
        ActiveEnginePerformance engines = computer.ActiveEnginePerformanceMax;

        BoosterPerformance booster = new(engines.Thrust, engines.MassFlowRate,
                                         Craft.TotalMass, Craft.PropellantMass);

        double density = KsaWorld.MediumDensityRatioAt(Parent, KsaWorld.PositionEcl(Craft));

        usable = Body.IsUsable;
        AltitudeMetres = Body.AltitudeOf(positionCci);

        if (hasAim)
        {
            double off = OrbitPlane.OffPlaneRadians(positionCci, velocityCci, aimCci);
            OffPlaneDegrees = off * 180.0 / Math.PI;
            PlaneChangeCost = OrbitPlane.PlaneChangeCost(Vec.Len(velocityCci), off);
        }
        else
        {
            OffPlaneDegrees = 0.0;
            PlaneChangeCost = 0.0;
        }

        return new IcbmState(Body, positionCci, velocityCci, aimCci, hasAim, booster, density,
                             Craft.IsAnyEnginePropellantAvailable(), _throttleAchieved, playerStep);
    }

    // Where the ground actually is under a point on the arc. Without this the prediction flies
    // down to the mean sphere while the round it is predicting stops on terrain, and on a shallow
    // deorbit that gap is enormous: the arc covers about twelve kilometres of ground per kilometre
    // of height near the end, so a target four kilometres up - which is most of the Andes - puts
    // the prediction fifty kilometres past where anything actually lands.
    //
    // The point arrives un-carried to the prediction's own epoch, so the body-fixed frame to read
    // it in is the one at that epoch, which is the current one.
    private double TerrainRadiusAt(double3 pointCci)
    {
        if (Parent is not { } parent) return Body.SurfaceRadius;

        try
        {
            double3 dirCcf = Vec.Unit(pointCci).Transform(parent.GetCci2Ccf());
            if (!Vec.IsFinite(dirCcf) || dirCcf.Equals(Vec.Zero)) return Body.SurfaceRadius;

            // Accurate, because GroundTest is accurate and the round stops where *it* says. A
            // coarse sample is a different height field, and on a shallow arrival every metre of
            // disagreement is about eleven metres of ground. Affordable because ImpactPredictor
            // only asks near the surface.
            double height = parent.GetTerrainHeightFromDirCcf(dirCcf, accurate: true);
            return double.IsFinite(height) ? parent.MeanRadius + height : Body.SurfaceRadius;
        }
        catch
        {
            return Body.SurfaceRadius;
        }
    }

    // How thick the air is at a point on the arc. The same field the round's own drag is read from,
    // so the prediction and the round cannot disagree about the atmosphere they are flying through.
    private double DensityRatioAt(double3 pointCci)
    {
        if (Parent is not { } parent) return 0.0;

        try
        {
            double3 positionEcl = pointCci.Transform(parent.GetCci2Cce()) + parent.GetPositionEcl();
            double density = KsaWorld.MediumDensityRatioAt(parent, positionEcl);
            return double.IsFinite(density) && density > 0.0 ? density : 0.0;
        }
        catch
        {
            return 0.0;
        }
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

        // While the engines are running, predict from where the arc *departs* rather than from
        // where the vehicle is. The current state is mid-burn and describes a trajectory nobody
        // intends to fly, so a correction driven by it never sees the shot being aimed - which
        // leaves the aim uncorrected for the whole burn, and by the coast the arc is fixed and the
        // warheads are already going.
        bool fromCutoff = Program.IsBurning && Program.Arc is not null;

        double3 fromCci = fromCutoff ? Program.CutoffPositionCci : state.PositionCci;
        double3 alongCci = fromCutoff ? Program.Arc!.Value.RequiredVelocityCci : state.VelocityCci;

        // Predicted with the warhead's drag rather than in vacuum. On a shallow deorbit arrival a
        // vacuum arc lands tens of kilometres beyond anything that actually flies it, and the aim
        // correction reads its own drag-free prediction - so it converges, reports zero, and the
        // rounds go on falling short. Measured at 54.6 km.
        ImpactPredictor.Drag? air =
            _warhead is { } warhead ? new ImpactPredictor.Drag(DensityRatioAt, warhead) : null;

        if (ImpactPredictor.TryPredict(Body, fromCci, alongCci, PredictStepSeconds,
                                       ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                       TerrainRadiusAt, _path, air))
        {
            PredictedImpact = hit;

            // Measured against the *target*, not against the biased aim: the bias is the correction
            // being applied, so scoring it against itself would report a perfect shot however far
            // the rounds actually land from the place the player picked.
            PredictedMissMetres = state.HasAim
                ? Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci)
                : double.NaN;

            if (state.HasAim) _aim.Observe(hit.GroundFixedPointCci, _trueAimCci);
        }
        else
        {
            PredictedImpact = null;
            PredictedMissMetres = double.NaN;
        }
    }
}
