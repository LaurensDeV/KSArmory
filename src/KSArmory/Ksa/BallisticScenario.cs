using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Flies one ballistic shot with nobody watching: designate, arm, stage, and report what the
/// warheads did.
///
/// <para>The engagement scenarios beside this one settle in a minute; a ballistic shot is seven
/// minutes of simulated flight with five events in it worth knowing about, spread across four of
/// them. So this reports as it goes rather than only at the end — <b>the SCENARIO lines are the
/// report</b>, and the verdict at the bottom is a summary of them, not the whole output.</para>
///
/// <para><b>It does not fight <see cref="WarpPolicy"/>.</b> One speed is asked for, once, at the
/// moment the shot is committed, and nothing here writes the speed again: the policy holds the
/// world down for the burn, the trim and the rounds in the air, and hands the rest back. Asking for
/// a speed the policy is holding is a loop neither side wins, which is why the number asked for
/// sits under what the policy would allow a burn to run at anyway.</para>
///
/// <para>What it cannot do is put a rocket on the pad — see <c>CLAUDE.md</c>, "A fully
/// self-contained scenario is not possible from a mod". It waits for a craft that already has one
/// aboard.</para>
/// </summary>
internal sealed class BallisticScenario
{
    /// <summary>
    /// How fast the world is asked to run, once, when the shot is committed.
    ///
    /// <para>Under the ceiling <see cref="WarpPolicy"/> allows a guided burn — one
    /// <see cref="IcbmProgram.MaxFaithfulStep"/> less its margin, about eleven times normal at
    /// sixty frames a second — so the ascent runs at the speed asked for and the policy never has
    /// to take it away. The descent is a different matter: a reentry vehicle in thick air needs a
    /// far shorter step than that, and the policy slows the world for it and gives this back
    /// afterwards.</para>
    /// </summary>
    public const double WarpFactor = 8.0;

    /// <summary>
    /// How long after the last release, in simulated seconds, before a salvo is called finished.
    ///
    /// <para>A bus that gives up with warheads still aboard is a real outcome rather than a hang,
    /// and waiting for a magazine that will never empty is how a harness turns one into the
    /// other.</para>
    /// </summary>
    public const double SalvoOverSeconds = 120.0;

    private readonly ShotRequest _shot;
    private readonly Action<string> _say;
    private readonly ShotGroup _group = new();

    // Buffered rather than reported where it happens: the hook fires inside the battery's round
    // loop, which is inside the engine's frame hook, and a scenario's output belongs in its own
    // pass where the ordering is the one the log shows.
    private readonly List<string> _landed = [];

    private readonly Action<IProjectile> _onRoundEnded;

    private IcbmComputer? _computer;
    private WeaponSystem? _wired;
    private Vehicle? _flownFrom;

    private bool _committed;
    private int _loaded;
    private int _ammoWas = -1;
    private int _ended;
    private double _sinceRelease;
    private double _sinceComplaint;

    private IcbmPhase _reported = IcbmPhase.Idle;
    private bool _saidCutoff;
    private bool _saidStaging;
    private string _saidTrim = "";
    private bool _capturedDeployment;
    private bool _capturedImpact;

    public BallisticScenario(ShotRequest shot, Action<string> say)
    {
        _shot = shot;
        _say = say;
        _onRoundEnded = OnRoundEnded;
    }

    /// <summary>What state a timeout would name, so a run that never got there says where it stuck.</summary>
    public string Where => _computer is null ? "waiting for a craft with a ballistic computer"
                         : !_committed ? "waiting for a launch solution"
                         : $"{_reported}, {_group.Released} released, {_group.Arrived} down";

    /// <summary>Whatever this shot has come to so far, which is what a timeout reports as well.</summary>
    public ShotVerdict Judge() => _group.Judge(_shot.BarMetres);

    /// <summary>Let go of the battery, so a finished scenario is not still being called back.</summary>
    public void Release()
    {
        if (_wired is not null) _wired.RoundEnded = null;
        _wired = null;
    }

    /// <summary>One frame of the shot. Returns the outcome once there is one, and null until then.</summary>
    public string? Update(WeaponSystems roster, IcbmComputers? icbms, double simStep, double playerStep)
    {
        _sinceComplaint += playerStep;

        if (icbms is null) return null;

        // Only while the craft is still being looked for. Past the commit the computer is the only
        // thing that knows where the warheads were sent, and it answers that from the body and the
        // designation rather than from the craft — so losing the launcher must not lose the score.
        if (!_committed && !KsaWorld.IsAlive(_computer?.Craft)) _computer = null;

        if (_computer is null)
        {
            Find(roster, icbms);
            return null;
        }

        WeaponSystem? battery = roster.For(_computer.Craft)?.Battery;
        Wire(battery);

        if (!_committed)
        {
            Commit();
            return null;
        }

        ReportPhase();
        ReportRefusedStaging(battery);
        ReportCutoff();
        ReportSeparation();
        ReportTrim();
        ReportReleases(battery, simStep);
        ReportImpacts();

        if (_group.Released == 0 || _ended < _group.Released) return null;
        if (_sinceRelease < SalvoOverSeconds) return null;

        ShotVerdict verdict = _group.Judge(_shot.BarMetres);
        string held = _loaded > _group.Released ? $", {_loaded - _group.Released} still aboard" : "";

        return $"{(verdict.Pass ? "PASS" : "FAIL")} {verdict.Said}{held}";
    }

    // The first craft that could fly this shot: a ballistic computer, a launcher with something in
    // it, and still on the ground. The last of those is asked of IcbmProgram rather than answered
    // here, because the phase machine picks a vehicle up by exactly that test and two of them would
    // drift apart silently.
    private void Find(WeaponSystems roster, IcbmComputers icbms)
    {
        // KSA loads a save paused, and everything below is read from a computer that only samples
        // the world inside the mod's clock gate — so on a paused world nothing is ever true, and a
        // scenario that only waits waits for ever. Unpausing is the scenario's business rather than
        // the computer's: a player who paused did so on purpose.
        if (KsaWorld.IsPaused && KsaWorld.SetSimulationSpeed(1.0))
        {
            _say("the world was paused; asked for 1x so the flight can start");
        }

        string why = "nothing in the scene carries a ballistic computer";

        foreach (IcbmComputer computer in icbms.All)
        {
            if (!KsaWorld.IsAlive(computer.Craft)) continue;

            string name = KsaWorld.DisplayName(computer.Craft);

            // Null until the computer has sampled the world once, which it cannot do while the
            // clock gate is shut. Named separately from the tests below because it means "not
            // looked yet" rather than "looked and the answer was no".
            if (computer.Parent is not { } parent)
            {
                why = $"{name} has a ballistic computer that has not sampled the world yet";
                continue;
            }

            WeaponSystem? battery = roster.For(computer.Craft)?.Battery;

            if (battery?.Launcher is null)
            {
                why = $"{name} has a ballistic computer but no launcher the mod recognises";
                continue;
            }

            if (battery.Ammo <= 0)
            {
                why = $"{name}'s launcher is empty";
                continue;
            }

            double airspeed = Vec.Len(KsaWorld.VelocityEcl(computer.Craft)
                                      - KsaWorld.GroundVelocityAt(parent, KsaWorld.PositionEcl(computer.Craft)));

            if (!IcbmProgram.IsOnTheGround(computer.AltitudeMetres, airspeed,
                                           computer.Config.TurnStartMetres))
            {
                why = $"{name} is at {computer.AltitudeMetres / 1000.0:F1} km doing {airspeed:F0} m/s, "
                      + "which is not on a pad";
                continue;
            }

            _computer = computer;
            _loaded = battery.Ammo;
            _ammoWas = battery.Ammo;
            _flownFrom = computer.Craft;

            _say($"{KsaWorld.DisplayName(computer.Craft)} on the ground at {WhereItStands(computer)}, "
                 + $"{battery.Ammo} x {battery.Munition.DisplayName} aboard");

            computer.Designate(new AimSite(parent.Id, _shot.LatitudeDeg, _shot.LongitudeDeg,
                                           "scenario aim point"));

            _say($"aimed at {_shot.Describe()}{Downrange(computer)}");
            return;
        }

        if (_sinceComplaint < 10.0) return;
        _sinceComplaint = 0.0;

        // Which test failed, not that one did. A scenario nobody is watching has to say what it is
        // waiting for or a mis-set save costs a whole timeout to learn nothing from.
        _say($"waiting -- {why}");
    }

    // Arm, and then light the first engine once the program has a trajectory to fly.
    //
    // Two frames, and that ordering is forced rather than tidy: the phase machine returns "not
    // armed" before running any of itself, so there is no launch solution to gate the staging on
    // until the master arm is set. Staging without one lights a rocket with nothing steering it.
    private void Commit()
    {
        if (_computer is not { } computer) return;

        if (!computer.Config.Armed)
        {
            computer.Config.Armed = true;
            _say("armed");
            return;
        }

        ReportPhase();

        if (computer.Program.Phase != IcbmPhase.Rising) return;

        VehicleCommand.Stage(computer.Craft);
        _committed = true;

        _say($"staged, reach {computer.Program.Reach}");

        if (KsaWorld.SetSimulationSpeed(WarpFactor))
        {
            _say($"asked the world for {WarpFactor:F0}x; the warp policy takes it back "
                 + "for the burn, the trim and the rounds in the air");
        }
    }

    // The computer never stages past a launcher that could come off, because the next sequence on
    // somebody's craft might be the joint holding the warheads. So a stack that needs a second
    // stage will not get one, and the shot falls short for a reason no phase line names.
    private void ReportRefusedStaging(WeaponSystem? battery)
    {
        if (_saidStaging || battery is null || _computer is not { } computer) return;
        if (!computer.Command.RequestStage || !battery.CanSeparate) return;

        _saidStaging = true;

        _say("the program wants a stage and will not take one past a launcher that can separate -- "
             + "a multi-stage stack has to be staged by hand from here");
    }

    // Assigned rather than added to, and re-assigned whenever the battery changes: a decoupler puts
    // the launcher on another craft mid-flight, and the rounds go with it.
    //
    // Nothing to wire is not a reason to let go. A system whose craft was destroyed goes on flying
    // what it had in the air off the roster's loose list — the same object, no longer answering to
    // For() — and those rounds are still the shot being scored.
    private void Wire(WeaponSystem? battery)
    {
        if (battery is null || ReferenceEquals(battery, _wired)) return;

        if (_wired is not null) _wired.RoundEnded = null;
        _wired = battery;
        battery.RoundEnded = _onRoundEnded;
    }

    private void ReportPhase()
    {
        if (_computer is not { } computer) return;
        if (computer.Program.Phase == _reported) return;

        _reported = computer.Program.Phase;

        _say($"{_reported} at {computer.AltitudeMetres / 1000.0:F0} km, "
             + $"{computer.Command.VelocityToGain:F0} m/s to gain, "
             + $"impact in {IcbmProgram.Clock(computer.SecondsToArrival)} :: {computer.Command.Hold}");
    }

    // The two numbers that separate a shot that was never aimed right from one that was aimed right
    // and flown badly: what the engines left ungained, and how far the mod's own prediction of the
    // arc it cut off on lands from the target.
    private void ReportCutoff()
    {
        if (_saidCutoff || _computer is not { } computer) return;
        if (!double.IsFinite(computer.Program.ResidualAtCutoff)) return;

        _saidCutoff = true;

        string predicted = double.IsFinite(computer.PredictedMissMetres)
            ? $"{computer.PredictedMissMetres / 1000.0:F2} km off"
            : "nothing predicted";

        _say($"CAPTURE cutoff: residual {computer.Program.ResidualAtCutoff:F2} m/s, "
             + $"own prediction {predicted}");
    }

    // The computer moves onto the craft the launcher is now riding, so the craft it is flying
    // changing is the split having landed. Nothing else can move it.
    private void ReportSeparation()
    {
        if (_computer is not { } computer) return;
        if (ReferenceEquals(computer.Craft, _flownFrom)) return;

        _flownFrom = computer.Craft;
        _say($"separated; the bus is {KsaWorld.DisplayName(computer.Craft)}");
    }

    private void ReportTrim()
    {
        if (_computer is not { } computer) return;
        if (computer.TrimSaid.Length == 0 || computer.TrimSaid == _saidTrim) return;

        _saidTrim = computer.TrimSaid;

        _say($"trim: {computer.TrimSaid} -- owed "
             + $"{Rate(computer.TrimOwedAtSplitMetresPerSecond)} at the split, "
             + $"{Rate(computer.TrimOwedOnReleaseMetresPerSecond)} on release");
    }

    // Most of the trim's life is spent before either number exists, and "NaN m/s" in a report reads
    // as a fault rather than as a measurement that has not been taken yet.
    private static string Rate(double metresPerSecond)
        => double.IsFinite(metresPerSecond) ? $"{metresPerSecond:F2} m/s" : "nothing yet";

    // A round leaving is the magazine going down by one. The angle beside it is read from the
    // command that let it go, which is this frame's and no other: by the next one the sequencer has
    // moved on to the next tube and is reporting how far off the line *that* one is.
    private void ReportReleases(WeaponSystem? battery, double simStep)
    {
        _sinceRelease += simStep;

        if (battery is null || _computer is not { } computer) return;

        int ammo = battery.Ammo;
        if (_ammoWas < 0) _ammoWas = ammo;

        for (int i = 0; i < _ammoWas - ammo; i++)
        {
            _group.Release();
            _sinceRelease = 0.0;

            ReleaseCommand deploy = computer.Deployment;
            string shot = $"warhead away from tube {deploy.Tube + 1}, "
                          + $"{deploy.OffLineDegrees:F2} deg off the salvo's line, "
                          + $"{ammo} left";

            _say(_capturedDeployment ? shot : $"CAPTURE deployment: {shot}");
            _capturedDeployment = true;
        }

        _ammoWas = ammo;
    }

    private void ReportImpacts()
    {
        for (int i = 0; i < _landed.Count; i++)
        {
            _say(_capturedImpact ? _landed[i] : $"CAPTURE impact: {_landed[i]}");
            _capturedImpact = true;
        }

        _landed.Clear();
    }

    // Where the round ended, against the place it was sent. Nothing here may throw: it runs inside
    // the battery's round loop, which is inside the engine's frame hook.
    private void OnRoundEnded(IProjectile round)
    {
        try
        {
            _ended++;

            string what = RoundLabel.For(round.Tube);

            if (round.State != RoundState.Detonated)
            {
                _landed.Add($"{what} {round.State} after {round.Age:F0} s without arriving");
                return;
            }

            double miss = MissFromAim(round);
            _group.Arrive(miss);

            _landed.Add(double.IsFinite(miss)
                ? $"{what} down {miss / 1000.0:F2} km from the aim point after {round.Age:F0} s"
                : $"{what} down after {round.Age:F0} s, and where could not be measured");
        }
        catch
        {
            // A harness that takes the game down is worse than one that loses a number.
        }
    }

    // The burst against the aim point, both at one instant. The round's position is back-dated into
    // the frame it burst in and the aim point is a place on a turning planet, so the aim has to be
    // carried to the same instant - up to half a kilometre of the frame's own motion otherwise, in
    // one direction, which reads as a common bias on every warhead of the group.
    private double MissFromAim(IProjectile round)
    {
        if (_computer is not { } computer || computer.Parent is not { } parent) return double.NaN;
        if (computer.TargetEcl() is not { } aimEcl) return double.NaN;

        double3 aimAtBurst = aimEcl + KsaWorld.GroundVelocityAt(parent, aimEcl)
                                      * round.DetonationElapsedInFrame;

        double miss = Vec.Len(round.PositionEcl - aimAtBurst);
        return double.IsFinite(miss) ? miss : double.NaN;
    }

    private static string WhereItStands(IcbmComputer computer)
    {
        if (computer.Parent is not { } parent) return "an unreadable place";

        try
        {
            double3 cce = KsaWorld.PositionEcl(computer.Craft) - parent.GetPositionEcl();
            return $"{parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3} "
                   + $"on {parent.Id}";
        }
        catch
        {
            return "an unreadable place";
        }
    }

    // How far the shot has to go, which is the one number that says whether the aim point makes
    // sense for the pad the craft happens to be standing on.
    private static string Downrange(IcbmComputer computer)
    {
        if (computer.Parent is not { } parent) return "";
        if (computer.TargetEcl() is not { } aimEcl) return "";

        try
        {
            double3 centre = parent.GetPositionEcl();
            double angle = Vec.AngleBetween(KsaWorld.PositionEcl(computer.Craft) - centre, aimEcl - centre);
            return $" ({parent.MeanRadius * angle / 1000.0:F0} km downrange)";
        }
        catch
        {
            return "";
        }
    }
}
