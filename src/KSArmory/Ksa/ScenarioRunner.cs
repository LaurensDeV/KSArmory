using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Flies a scripted engagement with nobody watching, and says in the log what happened.
///
/// <para>KSA cannot run headless, but it can run unattended. Appearance still needs eyes: the
/// harness screenshots at each <c>CAPTURE</c> marker.</para>
///
/// <para>This file owns what every scenario shares: the request, the save, the clocks and the
/// verdict. An engagement runs inline; <see cref="BallisticScenario"/>, <see cref="DropScenario"/>
/// and <see cref="GunneryScenario"/> are the others.</para>
/// </summary>
internal sealed class ScenarioRunner
{
    // Every line this writes starts with this, so a harness can grep for one thing.
    private const string Tag = "SCENARIO";

    private enum Phase
    {
        Idle,
        LoadingSave,
        WaitingForWorld,
        Arming,
        Engaging,
        Flying,
        Settling,
        Dropping,
        Done,
    }

    private readonly Config _config;
    private Phase _phase = Phase.Idle;
    private Config? _before;
    private Log.Level _thresholdBefore;

    private string _name = string.Empty;
    private TestTarget.Profile _profile;
    private BallisticScenario? _ballistic;
    private DropScenario? _drop;
    private GunneryScenario? _gunnery;

    // Held, because the flights are crewed once the world has loaded.
    private ShotRequest _shot;

    // Which variant each rocket flies, when a batch is comparing two inside one world. Null is the
    // ordinary case: every rocket flies whatever was built.
    private ShotArms? _arms;
    private bool _holdKeys;
    private string _armSpec = string.Empty;
    private bool _keepStages;
    private bool _traceWarhead;

    // Select a launcher other than the bus on each rocket before it flies, as an operator would
    // from the panel. The computer must still release through the bus.
    private bool _selectOther;
    private readonly List<WeaponSystems.Entry> _selectScratch = [];
    private int _armPhase;

    private bool _isBallistic;
    private double _elapsed;
    private double _simElapsed;
    private double _budget = EngagementBudgetSeconds;
    private double _sinceSpawn;
    private bool _spawned;

    // "blackout=<kt>": a burst of that yield on the ground halfway to the target the moment the set
    // first holds it, damaging nothing, and what the radar holds reported every half second after.
    // "noblackout" is the control: the same burst with the effect switched off.
    private double _blackoutKt;
    private double _blackoutReported;
    private double _blackoutAt = -1.0;
    private Vehicle? _blackoutTarget;
    private const double BlackoutReportSeconds = 0.5;
    private const double BlackoutWatchSeconds = 20.0;
    private bool _capturedLaunch;
    private double _lastComplaint;
    private string _save = string.Empty;

    // A chase ridden through a list of world speeds, one stretch each, for a camera fault that shows
    // at some speeds and not others.
    private bool _chase;
    private bool _showClouds;
    private bool _twoClouds;
    private double _cloudWarp = 1.0;
    private double _stillAt = -1.0;
    private double _secondYield = 1.0;
    private double[] _speeds = [];

    // Where a run sets its craft down first, for a body no save is on: the gunnery mount, and the
    // drop, which goes there to burst on ground with no air over it.
    private (string Body, double LatitudeDeg, double LongitudeDeg)? _site;

    // A site is usually another body: the full system loads slower, and the craft settles where it
    // lands. Both the runs that can be given one make the same allowance.
    private const double SiteBudgetSeconds = 60.0;

    private int _speedIndex = -1;
    private double _speedHeldFor;

    // Wall clock, because the faults this is for are per frame. The first stretch waits out the
    // chase's own transition at one times speed, so no speed's numbers carry the ease.
    private const double ChaseSettleSeconds = 4.0;
    private const double SpeedHoldSeconds = 15.0;

    // Simulated seconds between world dumps while chasing: at 0.05x the default three is a minute of
    // wall clock, which a speed held for fifteen seconds never reaches.
    private const float ChaseDumpSeconds = 0.25f;

    // Longest an engagement may take before it is called a failure: 20 km at 300 m/s is over a minute.
    private const double EngagementBudgetSeconds = 90.0;

    // The same for a ballistic shot: wide enough for the whole shot at one times speed, since the
    // warp it asks for can be refused.
    private const double BallisticBudgetSeconds = 2400.0;

    // And its budget in simulated seconds, which catches a flight that is stuck rather than slow.
    // Sized for several rockets, whose releases are sequenced: eight measured 7 of 8 down at an hour.
    private const double BallisticSimBudgetSeconds = 5400.0;

    // The world needs a few seconds after load before a craft is flyable and a system is crewed.
    private const double SettleSeconds = 4.0;

    public ScenarioRunner(Config config, WarpPolicy warp, Func<WeaponSystem, BombSightOverlay> sightFor)
    {
        _config = config;
        _warp = warp;
        _sightFor = sightFor;
    }

    // A drop is judged against the sight the player sees, not a second one kept here.
    private readonly Func<WeaponSystem, BombSightOverlay> _sightFor;

    // A load, a climb and a fall, with the game's own start-up inside it.
    private const double DropBudgetSeconds = 300.0;

    // The policy has to be told when the harness moves the world, or it stands down as overridden.
    private readonly WarpPolicy _warp;

    // One flight per rocket, each with its own magazine, group and verdict, and one shared list of
    // which craft are ours so none of them aims at another.
    private readonly List<BallisticScenario> _flights = [];
    private readonly List<Vehicle> _shooters = [];

    // One view, claimed by whichever flight gets its salvo away first.
    private readonly bool[] _viewTaken = new bool[1];
    private readonly List<string?> _outcomes = [];

    // Which arm each flight drew, in crewing order, for the run's summary.
    private readonly List<string> _armFlown = [];

    // Crewed once, from whatever the roster first holds: a rocket appearing later is not picked up.
    private bool _crewed;

    private void SelectOther(WeaponSystems roster, Vehicle craft)
    {
        roster.AllOn(craft, _selectScratch);
        foreach (WeaponSystems.Entry entry in _selectScratch)
        {
            if (Catalogue.ProvidesGuidance(entry.Weapon.Profile.PartId)) continue;

            roster.Select(craft, entry.Ordinal);
            Report($"{_name}: selected {entry.DisplayName} on {KsaWorld.DisplayName(craft)}, not the bus");
            return;
        }

        Report($"{_name}: nothing but the bus on {KsaWorld.DisplayName(craft)} to select");
    }

    private void CrewTheFlights(WeaponSystems roster, IcbmComputers? icbms)
    {
        if (_crewed || icbms is null) return;

        foreach (IcbmComputer computer in icbms.All)
        {
            if (!KsaWorld.IsAlive(computer.Craft)) continue;

            // Crewed is not able to fly this shot: a computer goes wherever a part provides guidance,
            // and counting one that cannot reach leaves the real rocket aiming at bare ground.
            if (!BallisticScenario.CouldReachTheAim(computer, roster.GuidedFrom(computer.Craft)?.Weapon,
                                                    _shot))
            {
                continue;
            }

            // Drawn here and applied when the flight arms, after the harness forces its own settings.
            ShotArms.Arm? arm = _arms?.For(_shooters.Count, _armPhase);

            if (arm is { } drawn)
            {
                // Per craft: the only record of which rocket flew which variant.
                Report($"{_name}: {KsaWorld.DisplayName(computer.Craft)} flies arm {drawn.Describe()}");
                _armFlown.Add(drawn.Name);
            }

            if (_selectOther) SelectOther(roster, computer.Craft);

            _shooters.Add(computer.Craft);
            _flights.Add(new BallisticScenario(
                _shot, line => Report($"{_name}: {line}"), computer, _shooters, _viewTaken, arm));
            _outcomes.Add(null);
        }

        if (_flights.Count == 0) return;

        _crewed = true;
        _ballistic = _flights[0];

        // The count a batch checks against what it asked for: a run that left rockets on the pad
        // otherwise looks like one that flew them all.
        Report($"{_name}: crewed {_flights.Count} flight(s): "
               + string.Join(", ", _shooters.ConvertAll(KsaWorld.DisplayName)));

        // The split as flown, which an odd or unreachable rocket can make lopsided.
        if (_arms is not null)
        {
            Report($"{_name}: arms " + string.Join(", ",
                _armFlown.GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}")));
        }
    }

    private void FlyThem(WeaponSystems roster, IcbmComputers? icbms, double dt, double playerStep)
    {
        if (_flights.Count == 0) return;

        bool allDone = true;

        for (int i = 0; i < _flights.Count; i++)
        {
            if (_outcomes[i] is not null) continue;

            _outcomes[i] = _flights[i].Update(roster, icbms, dt, playerStep);

            if (_outcomes[i] is null) allDone = false;
        }

        if (!allDone) return;

        // Not FinishAll yet: WarheadTrace reports on the frame after the round stops flying, so the
        // last impact and END can land together.
        _phase = Phase.Settling;
        _settleFrom = _elapsed;
    }

    // Hold the run open until every trace has reported, rather than for a guessed duration.
    private const double SettleBudgetSeconds = 30.0;

    private double _settleFrom;

    private void Settle(IcbmComputers? icbms, double playerStep)
    {
        _ = playerStep;

        bool outstanding = false;

        if (icbms is not null)
        {
            foreach (IcbmComputer computer in icbms.All)
            {
                if (computer.TraceOutstanding) { outstanding = true; break; }
            }
        }

        if (!outstanding) { FinishAll(); return; }

        // A trace that never reports must not hang the run: bounded, and logged.
        if (_elapsed - _settleFrom > SettleBudgetSeconds)
        {
            Report($"{_name}: settle gave up after {SettleBudgetSeconds:F0} s"
                   + " -- a warhead trace never reported");
            FinishAll();
        }
    }

    // Every flight's verdict on its own line, because a batch scores them one by one. The run's own
    // outcome is the worst of them: a night that quietly lost a rocket must not read as a pass.
    private void FinishAll()
    {
        int flew = 0;

        for (int i = 0; i < _flights.Count; i++)
        {
            if (_flights[i].Committed) flew++;

            Report($"{_name}: FLIGHT {KsaWorld.DisplayName(_shooters[i])} :: {_outcomes[i]}");
        }

        string worst = _outcomes.Exists(o => o is not null && o.StartsWith("FAIL"))
                           ? "FAIL"
                           : "PASS";

        Finish($"{worst} {flew} of {_flights.Count} flight(s) flew");
    }

    // One world, one clock: every flight's request is collected and the slowest wins (Sim/WorldSpeed.cs).
    private readonly List<double> _wantedSpeeds = [];

    private void ApplyWorldSpeed()
    {
        _wantedSpeeds.Clear();
        for (int i = 0; i < _flights.Count; i++) _wantedSpeeds.Add(_flights[i].WantedSpeed);

        double speed = WorldSpeed.Slowest(_wantedSpeeds);

        if (!double.IsNaN(speed) && !speed.Equals(_speedAsked))
        {
            if (KsaWorld.SetSimulationSpeed(speed))
            {
                _speedAsked = speed;
                _warp.NoteOurOwnRequest(speed);
            }
            else
            {
                _speedAsked = double.NaN;
            }
        }
    }

    private double _speedAsked = double.NaN;

    /// <summary>
    /// The scenario the harness asked for, or null. A short file beside the log, consumed as it is
    /// read so a second launch does not silently re-run the last request.
    ///
    /// <para>Line one is the request. Line two, if there is one, is the arm spec, on its own line
    /// because <see cref="ShotArms"/> uses the same <c>|</c> as the request. Line three is the phase.</para>
    /// </summary>
    public static string? Requested()
    {
        try
        {
            string path = Path.Combine(Log.Folder, "scenario.txt");
            if (!File.Exists(path)) return null;

            string text = File.ReadAllText(path).TrimEnd();
            File.Delete(path);

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True once a scenario has been asked for, whatever it has done since.</summary>
    public bool Active => _phase != Phase.Idle;

    /// <summary>
    /// Reads the scenario file, if the harness left one: a file rather than an environment variable,
    /// which does not survive a Windows process launched from WSL.
    /// </summary>
    public void Begin(string? request)
    {
        if (_phase != Phase.Idle || string.IsNullOrWhiteSpace(request)) return;

        // Everything below overrides the player's own settings for the flight.
        _before = _config.Snapshot();
        _thresholdBefore = Log.Threshold;

        // Trimmed per line: a CRLF from WSL would put a carriage return into the save name.
        string[] lines = request.Split('\n');
        request = lines[0].Trim();
        _armSpec = lines.Length > 1 ? lines[1].Trim() : string.Empty;
        _armPhase = lines.Length > 2 && int.TryParse(lines[2].Trim(), out int phase) ? phase : 0;

        // Line four is options, space separated.
        string[] options = lines.Length > 3
            ? lines[3].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        _keepStages = Array.IndexOf(options, "keepstages") >= 0;
        _traceWarhead = Array.IndexOf(options, "trace") >= 0;
        _selectOther = Array.IndexOf(options, "selectother") >= 0;
        _holdKeys = Array.IndexOf(options, "holdkeys") >= 0;

        // Nobody can tick Verbose log in a scripted run, and the developer detail -- the per-part
        // blast sweep among it -- is only ever wanted from one.
        if (Array.IndexOf(options, "verbose") >= 0) Log.Threshold = Log.Level.Debug;

        _chase = Array.IndexOf(options, "chase") >= 0;

        // "clouds": this run is being photographed, so the decoration below stays on and the chase
        // stays off.
        _showClouds = Array.IndexOf(options, "clouds") >= 0;

        // The cloud follows the cloud switch rather than having one of its own. At zero the pass still
        // runs and paints the rings, holes and tracers; only a burst's own visuals are off.
        // Timing runs either way, so the cloud-off run is the baseline. "noshader" keeps the cloud
        // and its pinned camera and turns only the pass off: same scene, same view, one variable.
        bool noShader = Array.IndexOf(options, "noshader") >= 0;

        // "twoclouds" or "twoclouds=<multiple>": a second burst a few kilometres from the first, so the
        // pass runs with two clouds standing; the multiple is on the second burst's yield.
        // "cloudwarp=<n>": run the linger at that speed, since the drop's own warp is handed back the
        // instant the store lands. 0 pauses instead.
        _cloudWarp = 1.0;
        _stillAt = -1.0;
        _blackoutKt = 0.0;
        _blackoutReported = 0.0;
        _blackoutAt = -1.0;
        _blackoutTarget = null;
        _config.NuclearBlackout = Array.IndexOf(options, "noblackout") < 0;

        foreach (string option in options)
        {
            if (!option.StartsWith("blackout=", StringComparison.Ordinal)) continue;

            if (double.TryParse(option["blackout=".Length..], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double kt)
                && kt > 0.0)
            {
                _blackoutKt = kt;
            }
            else
            {
                Log.Warn($"scenario: ignored '{option}' -- a blackout is blackout=<kilotonnes>");
            }
        }

        // "stillat=<age>": freeze the world at that burst age and photograph it three times, so
        // what changes between them is the renderer's own noise and nothing in the world.
        foreach (string option in options)
        {
            if (!option.StartsWith("stillat=", StringComparison.Ordinal)) continue;

            if (double.TryParse(option["stillat=".Length..], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double age)
                && age >= 0.0)
            {
                _stillAt = age;
            }
            else
            {
                Log.Warn($"scenario: ignored '{option}' -- a still is stillat=<seconds>");
            }
        }

        _twoClouds = false;
        _secondYield = 1.0;

        // "fuse=<m>" and "chute=<m/s>": the store bursts that far over the ground, under a chute that
        // brings it down that fast -- an air burst dropped for real rather than set off by the bridge.
        _fuse = OptionNumber(options, "fuse=");
        _chute = OptionNumber(options, "chute=");

        foreach (string option in options)
        {
            if (!option.StartsWith("cloudwarp=", StringComparison.Ordinal)) continue;

            if (double.TryParse(option["cloudwarp=".Length..], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double speed)
                && speed >= 0.0)
            {
                _cloudWarp = speed;
            }
            else
            {
                Log.Warn($"scenario: ignored '{option}' -- a linger speed is cloudwarp=<number>");
            }
        }

        foreach (string option in options)
        {
            if (!option.StartsWith("twoclouds", StringComparison.Ordinal)) continue;

            _twoClouds = true;

            int split = option.IndexOf('=');
            if (split < 0) continue;

            if (double.TryParse(option[(split + 1)..], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double times)
                && times > 0.0)
            {
                _secondYield = times;
            }
            else
            {
                Log.Warn($"scenario: ignored '{option}' -- a yield multiple is twoclouds=<number>");
            }
        }

        _config.ShaderPass = _showClouds && !noShader ? 1f : 0f;

        CloudPassCost.Begin();
        CloudWatch.Reset();

        // "watchelev=<deg>": stand the cloud watch that far above the horizontal instead, low enough
        // to be under the weather deck.
        foreach (string option in options)
        {
            if (!option.StartsWith("watchelev=", StringComparison.Ordinal)) continue;

            if (double.TryParse(option["watchelev=".Length..], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double deg)
                && deg > 0.0 && deg < 89.0)
            {
                CloudWatch.ElevationDeg = deg;
            }
            else
            {
                Log.Warn($"scenario: ignored '{option}' -- a watch elevation is watchelev=<degrees>");
            }
        }

        Report($"{_name}: GPU timing on, cloud {(_showClouds ? "ON" : "off -- this run is the baseline")}");

        // "speeds=0.05,0.1,1": held a stretch each once the first round is up. One that does not read
        // as a positive speed is dropped and said, rather than failing a run that can still fly the rest.
        foreach (string option in options)
        {
            if (!option.StartsWith("speeds=", StringComparison.Ordinal)) continue;

            List<double> speeds = [];
            foreach (string field in option["speeds=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (double.TryParse(field, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double speed) && speed > 0.0)
                {
                    speeds.Add(speed);
                }
                else
                {
                    Log.Warn($"scenario: ignored world speed '{field}'");
                }
            }

            _speeds = [.. speeds];
        }

        // "site=Mars,15,-160": set the craft down there first, for a body no save is on.
        _site = null;
        foreach (string option in options)
        {
            if (!option.StartsWith("site=", StringComparison.Ordinal)) continue;

            string[] fields = option["site=".Length..].Split(',');
            if (fields.Length == 3 && fields[0].Length > 0
                && double.TryParse(fields[1], System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out double latitude)
                && double.TryParse(fields[2], System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out double longitude))
            {
                _site = (fields[0], latitude, longitude);
            }
            else
            {
                Log.Warn($"scenario: ignored '{option}' -- a site is site=<body>,<latitude>,<longitude>");
            }
        }

        // "name" or "name|save". settings.toml's startVehicle is only read by the configuration
        // dialog, so loading a save (GameSaves.LoadSaveGame) is the only way in without a click.
        string[] parts = request.Split('|', 2);
        _save = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        // "name" or "name:arguments", in the name rather than a second file that could go stale.
        string[] named = parts[0].Trim().Split(':', 2);
        _name = named[0].Trim();

        if (_name == "mirv")
        {
            BeginBallistic(named.Length > 1 ? named[1].Trim() : string.Empty);
            return;
        }

        if (_name == "drop")
        {
            BeginDrop(named.Length > 1 ? named[1].Trim() : string.Empty);
            return;
        }

        if (_name == "gunnery")
        {
            BeginGunnery(named.Length > 1 ? named[1].Trim() : string.Empty);
            return;
        }

        _profile = _name switch
        {
            "overhead" => TestTarget.Profile.Overhead,
            "passing" => TestTarget.Profile.PassingBy,
            _ => TestTarget.Profile.HeadOn,
        };

        _budget = EngagementBudgetSeconds;
        _phase = Phase.LoadingSave;
        Report($"{_name}: START profile={_profile} save='{_save}'");
    }

    private double _fuse;
    private double _chute;

    // A non-negative number after a prefix in the option list, or zero where there is none.
    private static double OptionNumber(IEnumerable<string> options, string prefix)
    {
        foreach (string option in options)
        {
            if (!option.StartsWith(prefix, StringComparison.Ordinal)) continue;

            if (double.TryParse(option[prefix.Length..], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double value)
                && value >= 0.0)
            {
                return value;
            }

            Log.Warn($"scenario: ignored '{option}' -- it is {prefix}<number>");
        }

        return 0.0;
    }

    private void BeginDrop(string arguments)
    {
        if (!DropScenario.Request.TryParse(arguments, out DropScenario.Request drop, out string trouble))
        {
            Finish($"FAIL the request could not be read -- {trouble}");
            return;
        }

        _drop = new DropScenario(drop, line => Report($"{_name}: {line}"), _sightFor, _showClouds && !_chase)
        {
            ChaseTheCloud = _showClouds && _chase,
            Site = _site,
            LingerSpeed = _cloudWarp,
            StillAt = _stillAt,
            SecondBurst = _twoClouds,
            SecondBurstYield = _secondYield,
            FuseMetres = _fuse,
            ChuteSink = _chute,
        };

        // Same allowance the gunnery run makes: a site is usually another body, the full system
        // loads slower, and the craft settles where it lands.
        _budget = DropBudgetSeconds + (_site is null ? 0.0 : SiteBudgetSeconds);
        _phase = Phase.LoadingSave;
        Report($"{_name}: START {drop.Describe()}"
               + (_site is { } site ? $", from {site.Body} at {site.LatitudeDeg:F2}, {site.LongitudeDeg:F2}" : string.Empty)
               + $" save='{_save}'");
    }

    private void BeginGunnery(string arguments)
    {
        if (!GunneryScenario.Request.TryParse(arguments, out GunneryScenario.Request gunnery, out string trouble))
        {
            Finish($"FAIL the request could not be read -- {trouble}");
            return;
        }

        _gunnery = new GunneryScenario(gunnery, line => Report($"{_name}: {line}")) { Site = _site };

        // Nobody is watching, and whether a miss was the barrel still laying is only in the debug log.
        Log.Threshold = Log.Level.Debug;

        _budget = gunnery.BudgetSeconds + (_site is null ? 0.0 : SiteBudgetSeconds);
        _phase = Phase.LoadingSave;
        Report($"{_name}: START {gunnery.Describe()}"
               + (_site is { } site ? $", from {site.Body} at {site.LatitudeDeg:F2}, {site.LongitudeDeg:F2}" : string.Empty)
               + $" save='{_save}'");
    }

    private void BeginBallistic(string arguments)
    {
        if (!ShotRequest.TryParse(arguments, out ShotRequest shot, out string trouble))
        {
            Finish($"FAIL the request could not be read -- {trouble}");
            return;
        }

        // Refused here rather than at crewing: a mistyped spec that flies the baseline twice reports
        // a dead heat.
        if (_armSpec.Length > 0)
        {
            if (!ShotArms.TryParse(_armSpec, out ShotArms arms, out string bad))
            {
                Finish($"FAIL the arms could not be read -- {bad}");
                return;
            }

            _arms = arms;
        }

        // Crewed later, once the save has loaded and the roster holds something: with several
        // rockets there is one flight each, and none of them exists yet.
        _shot = shot;
        _isBallistic = true;

        // Nobody is watching and there is no second chance to ask for the numbers. Off everywhere else.
        _config.TraceWarhead = true;

        // A standing cloud costs 6-8 ms a frame (2026-09-09-walk2: 18-24 ms against 23-30 ms), and
        // frame time buys simulation rate. It changes nothing a burst does, and is set here where no
        // arm can reach it. Kept when the run is being photographed.
        _config.NuclearClouds = _showClouds;

        // A spent stage arcing back down is pure frame time, which buys simulation rate. Set here, so
        // it cannot differ between arms.
        // Off only when a run is deliberately stressing the bubble. A spent stack separates at about
        // a metre a second, so keeping it holds every rocket inside the 4.194 km at which
        // SplitBubbles would release it -- which makes the shared bubble certain instead of a
        // one-in-four wait. docs/ACCURACY-PLAN.md 3ax.
        _config.DisposeSpentStages = !_keepStages;

        // Off unless asked for: it re-flies a prediction of one warhead every frame, which
        // is measurement rather than anything the shot needs.
        if (_traceWarhead) _config.TraceWarhead = true;

        if (_keepStages)
        {
            Log.Info("SCENARIO keeping every spent stage, so each rocket stays inside the 4.194 km "
                     + "at which the engine would split it out of its physics bubble");
        }

        _budget = BallisticBudgetSeconds;
        _phase = Phase.LoadingSave;
        Report($"{_name}: START {shot.Describe()} save='{_save}'");
    }

    /// <summary>
    /// One frame of the scenario. Does nothing unless one was asked for.
    ///
    /// <para>The world runs on <paramref name="simStep"/>; the budgets are wall clock on purpose, so a
    /// paused game cannot hang an unattended run.</para>
    /// </summary>
    public void Update(WeaponSystems roster, IcbmComputers? icbms, double simStep, double playerStep)
    {
        if (_phase is Phase.Idle or Phase.Done) return;
        if (!double.IsFinite(playerStep) || playerStep <= 0.0) return;

        // The UI holding the keyboard is what an open popup does to the rocket being flown, without the popup.
        if (_holdKeys) KsaWorld.HoldTheKeyboard();

        // Nobody is there to click a popup away, and an open one holds the controlled rocket's throttle.
        IReadOnlyList<string> closed = KsaWorld.CloseEnginePopups();
        if (closed.Count > 0)
        {
            Report($"{_name}: closed KSA's {string.Join(", ", closed)}, which holds the throttle of the craft "
                   + "being flown while it is open");
        }

        double dt = double.IsFinite(simStep) && simStep > 0.0 ? simStep : 0.0;

        _elapsed += playerStep;
        // A hold for a burn window or a coast to the release is not a stuck flight, and either can run for hours.
        if (!_flights.Exists(f => f.IsWaiting)) _simElapsed += dt;

        // Not while settling: every flight has been judged, and the settle carries its own bound.
        if (_phase != Phase.Settling)
        {
            if (_elapsed > _budget)
            {
                Finish($"TIMEOUT after {_elapsed:F0} s of wall clock -- {Stuck()}");
                return;
            }

            if (_isBallistic && _simElapsed > BallisticSimBudgetSeconds)
            {
                Finish($"TIMEOUT after {_simElapsed / 60.0:F0} minutes of world time -- {Stuck()}");
                return;
            }
        }

        WeaponSystems.Entry? entry = null;
        foreach (WeaponSystems.Entry e in roster.All)
        {
            if (e.Weapon.Platform is not null && e.Weapon.Launcher is not null) { entry = e; break; }
        }

        switch (_phase)
        {
            case Phase.LoadingSave:
                // A beat after load: asking the game to swap scenes while it is still building
                // the first one is not a case anything here can recover from.
                if (_elapsed < 3.0) return;

                if (_save.Length > 0)
                {
                    _drop?.NoteTheWorldBeforeTheLoad();

                    try
                    {
                        GameSaves.LoadSaveGame(_save);
                        Report($"{_name}: asked for save '{_save}'");
                    }
                    catch (Exception e)
                    {
                        Finish($"FAIL could not load '{_save}': {e.Message}");
                        return;
                    }
                }

                _phase = _isBallistic ? Phase.Flying
                         : _drop is not null ? Phase.Dropping
                         : Phase.WaitingForWorld;
                return;

            case Phase.Dropping:
                if (_drop!.Update(roster, dt, playerStep) is { } outcome) Finish(outcome);
                return;

            case Phase.Flying:
                CrewTheFlights(roster, icbms);
                FlyThem(roster, icbms, dt, playerStep);
                ApplyWorldSpeed();
                return;

            case Phase.Settling:
                Settle(icbms, playerStep);
                return;

            case Phase.WaitingForWorld:
                if (!KsaWorld.InFlight || entry is null)
                {
                    // Every few seconds, saying which of the two things it is still missing.
                    if (_elapsed - _lastComplaint > 10.0)
                    {
                        _lastComplaint = _elapsed;
                        Report($"{_name}: waiting -- "
                               + (KsaWorld.InFlight ? "in flight" : "NO CRAFT IN FLIGHT")
                               + ", " + (entry is null ? "NO SYSTEM CREWED" : "system crewed"));
                    }
                    return;
                }

                if (_elapsed < SettleSeconds) return;

                Report($"{_name}: crewed {KsaWorld.DisplayName(entry.Weapon.Platform!)} "
                       + $"with {entry.Weapon.Profile.DisplayName}");
                _phase = Phase.Arming;
                return;

            case Phase.Arming:
                if (entry is null) return;

                entry.Policy.AutoEngage = true;
                entry.Policy.MissilesEnabled = true;
                entry.Policy.GunsEnabled = true;
                entry.Policy.BeamEnabled = true;
                _config.DrawOverlays = true;

                Report($"{_name}: auto-engage on, {entry.Weapon.Ammo} rounds");

                if (_chase) RideTheChase(entry);

                _phase = Phase.Engaging;
                return;

            case Phase.Engaging:
                if (entry is null) return;

                if (_speeds.Length > 0) StepWorldSpeeds(entry.Weapon, playerStep);

                if (_gunnery is not null)
                {
                    if (_gunnery.Update(entry, dt) is { } scored) Finish(scored);
                    return;
                }

                Engage(entry, dt);
                return;
        }
    }

    // The chase only takes a view already on the firing craft, and only rides the craft the panel is
    // focused on, which follows control -- so both are put there rather than left to the save.
    private void RideTheChase(WeaponSystems.Entry entry)
    {
        entry.Policy.ChaseRounds = true;
        _config.DiagnosticDump = true;
        _config.DiagnosticIntervalSeconds = ChaseDumpSeconds;
        _budget += ChaseSettleSeconds + (SpeedHoldSeconds * _speeds.Length);

        bool onIt = KsaWorld.GoTo(entry.Weapon.Platform);
        string speeds = _speeds.Length > 0
            ? $", then {string.Join(", ", Array.ConvertAll(_speeds, s => $"{s:0.###}x"))} for {SpeedHoldSeconds:F0} s each"
            : string.Empty;

        Report($"{_name}: chase on{(onIt ? string.Empty : " -- but the view could not be put on the craft")}{speeds}");
    }

    // One times speed until the chase has settled onto the first round, then each speed in turn. The
    // policy is told, or it reads the harness's own request as somebody overriding it.
    private void StepWorldSpeeds(WeaponSystem gun, double playerStep)
    {
        if (_speedIndex >= _speeds.Length) return;
        if (_speedIndex < 0 && gun.Rounds.Count == 0) return;

        _speedHeldFor += playerStep;
        if (_speedHeldFor < (_speedIndex < 0 ? ChaseSettleSeconds : SpeedHoldSeconds)) return;

        _speedHeldFor = 0.0;
        if (++_speedIndex >= _speeds.Length) return;

        double speed = _speeds[_speedIndex];
        if (KsaWorld.SetSimulationSpeed(speed))
        {
            _warp.NoteOurOwnRequest(speed);
            Report($"{_name}: world at {speed:0.###}x");
        }
        else
        {
            Report($"{_name}: world speed {speed:0.###}x refused");
        }
    }

    private void Engage(WeaponSystems.Entry entry, double dt)
    {
        WeaponSystem system = entry.Weapon;

        if (!_spawned)
        {
            // The same numbers the panel's buttons use.
            if (TestTarget.Spawn(system.Platform!, _profile, 30.0, 300.0, 1500.0, "Gemini7") is not { } target)
            {
                Finish("FAIL could not spawn a target");
                return;
            }

            _spawned = true;
            Report($"{_name}: target away, {_profile}");

            _blackoutTarget = target;
            return;
        }

        _sinceSpawn += dt;

        if (_blackoutKt > 0.0 && _blackoutAt < 0.0 && system.Radar.Tracks.Count > 0
            && _blackoutTarget is { } held)
        {
            _blackoutAt = _sinceSpawn;
            BurstBetween(system.Platform!, held);
        }

        if (_blackoutAt >= 0.0 && _sinceSpawn - _blackoutAt <= BlackoutWatchSeconds
            && _sinceSpawn - _blackoutReported >= BlackoutReportSeconds)
        {
            _blackoutReported = _sinceSpawn;
            Report($"{_name}: blackout +{_sinceSpawn - _blackoutAt:F1} s -- "
                   + $"{system.Radar.Tracks.Count} track(s), "
                   + $"{system.Radar.MaskedByBurst} behind the fireball, "
                   + $"locked {(system.Radar.Locked is null ? "nothing" : "the target")}");
        }

        // The first round leaving is the moment worth a picture.
        if (!_capturedLaunch && system.Rounds.Count > 0)
        {
            _capturedLaunch = true;
            Report($"{_name}: CAPTURE launch");
        }

        foreach (IProjectile round in system.Rounds)
        {
            if (round.State == RoundState.Detonated)
            {
                Finish($"PASS detonated {round.MissDistance:F1} m from the target "
                       + $"after {round.Age:F1} s, {system.Ammo} rounds left");
                return;
            }
        }

        // Rounds are reaped, so a detonation can be missed between frames. The system's own
        // count falling with nothing in the air is the same news arriving late.
        if (_sinceSpawn > 15.0 && system.Rounds.Count == 0 && system.Ammo < system.Profile.TubeCount)
        {
            Finish($"PASS engagement over, {system.Ammo} rounds left "
                   + "(outcome from the system, not a round -- see the lines above)");
        }
    }

    // On the ground under the midpoint, as the burst tool would: the cloud and the ionised air, with
    // no blast, so the radar's picture is the only thing that changes.
    private void BurstBetween(Vehicle platform, Vehicle target)
    {
        double3 middle = (KsaWorld.PositionEcl(platform) + KsaWorld.PositionEcl(target)) * 0.5;

        if (!KsaWorld.TrySnapToGround(middle, out double3 ground))
        {
            Report($"{_name}: blackout -- the ground under the midpoint could not be found");
            return;
        }

        double charge = _blackoutKt * 1.0e6;
        NuclearClouds.Begin(ground, platform, charge);

        Report($"{_name}: blackout -- {(_config.NuclearBlackout ? string.Empty : "CONTROL, effect off, ")}"
               + $"{_blackoutKt:F1} kt on the ground "
               + $"{Vec.Len(ground - KsaWorld.PositionEcl(platform)) / 1000.0:F1} km out, ionised to "
               + $"{FireballBlackout.Radius(charge, 0.0):F0} m for {FireballBlackout.Seconds(charge):F1} s");
    }

    // What a timeout was waiting for, with the group of any warheads already away. Of the flight
    // actually holding the run up, which on a multi-rocket save is rarely the first.
    private string Stuck()
    {
        if (_drop is not null) return $"{_phase}, {_drop.Where}";
        if (_ballistic is null || _phase != Phase.Flying) return _phase.ToString();

        for (int i = 0; i < _flights.Count; i++)
        {
            if (_outcomes[i] is not null) continue;

            return $"{KsaWorld.DisplayName(_shooters[i])}: {_flights[i].Where}; "
                   + $"{_flights[i].Judge().Said}";
        }

        return $"{_ballistic.Where}; {_ballistic.Judge().Said}";
    }

    private void Finish(string outcome)
    {
        _phase = Phase.Done;
        for (int i = 0; i < _flights.Count; i++) _flights[i].Release();
        _drop?.Release();
        _gunnery?.Release();

        if (_before is not null) _config.Restore(_before);
        Log.Threshold = _thresholdBefore;
        Report($"{_name}: {outcome}");
        Report($"{_name}: END");
    }

    private static void Report(string line) => Log.Info($"{Tag} {line}");
}
