using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Flies a scripted engagement with nobody watching, and says in the log what happened.
///
/// <para>This exists because the gap between "the suite passes" and "it works" is a person
/// clicking things, and that person is the bottleneck on every behaviour change this mod makes.
/// KSA cannot run headless — it ships Windows-only natives and threads its simulation through a
/// Vulkan renderer — but it does not need to. It needs to run <em>unattended</em>, which is a
/// scenario file, a state machine and a line of output.</para>
///
/// <para>What it cannot do is judge appearance. Where a plume sits, whether a sight reads, whether
/// an explosion sounds right: those still need eyes. The <c>CAPTURE</c> markers exist for that —
/// the harness screenshots when it sees one, so the pictures at least arrive without anyone
/// sitting through the flight.</para>
/// </summary>
internal sealed class ScenarioRunner
{
    // Every line this writes starts with this, so a harness can grep for one thing.
    private const string Tag = "SCENARIO";

    private enum Phase
    {
        Idle,
        WaitingForWorld,
        Arming,
        Engaging,
        Done,
    }

    private readonly Config _config;
    private Phase _phase = Phase.Idle;

    private string _name = string.Empty;
    private TestTarget.Profile _profile;
    private double _elapsed;
    private double _sinceSpawn;
    private bool _spawned;
    private bool _capturedLaunch;

    // Longest a scenario may take before it is called a failure. Generous: a 20 km engagement at
    // 300 m/s closing is over a minute of flight before anything is decided.
    private const double TimeoutSeconds = 90.0;

    // The world needs a few seconds after load before a craft is flyable and a battery is crewed.
    private const double SettleSeconds = 4.0;

    public ScenarioRunner(Config config) => _config = config;

    /// <summary>
    /// The scenario the harness asked for, or null. A one-line file beside the log, consumed as
    /// it is read so a second launch does not silently re-run the last request.
    /// </summary>
    public static string? Requested()
    {
        try
        {
            string path = Path.Combine(Log.Folder, "scenario.txt");
            if (!File.Exists(path)) return null;

            string name = File.ReadAllText(path).Trim();
            File.Delete(path);

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True once a scenario has been asked for, whatever it has done since.</summary>
    public bool Active => _phase != Phase.Idle;

    /// <summary>
    /// Reads the scenario file, if the harness left one. Deliberately a file rather than an
    /// environment variable: the game is a Windows process launched from WSL and the environment
    /// does not survive that reliably, while a path both sides agree on always does.
    /// </summary>
    public void Begin(string? request)
    {
        if (_phase != Phase.Idle || string.IsNullOrWhiteSpace(request)) return;

        _name = request.Trim();
        _profile = _name switch
        {
            "overhead" => TestTarget.Profile.Overhead,
            "passing" => TestTarget.Profile.PassingBy,
            _ => TestTarget.Profile.HeadOn,
        };

        _phase = Phase.WaitingForWorld;
        Report($"{_name}: START profile={_profile}");
    }

    /// <summary>One frame of the scenario. Does nothing unless one was asked for.</summary>
    public void Update(BatteryRoster roster, double dt)
    {
        if (_phase is Phase.Idle or Phase.Done) return;
        if (!double.IsFinite(dt) || dt <= 0.0) return;

        _elapsed += dt;

        if (_elapsed > TimeoutSeconds)
        {
            Finish($"TIMEOUT after {_elapsed:F0} s in {_phase}");
            return;
        }

        BatteryRoster.Entry? entry = null;
        foreach (BatteryRoster.Entry e in roster.All)
        {
            if (e.Battery.Platform is not null && e.Battery.Launcher is not null) { entry = e; break; }
        }

        switch (_phase)
        {
            case Phase.WaitingForWorld:
                if (!KsaWorld.InFlight || entry is null) return;
                if (_elapsed < SettleSeconds) return;

                Report($"{_name}: crewed {KsaWorld.DisplayName(entry.Battery.Platform!)} "
                       + $"with {entry.Battery.Profile.DisplayName}");
                _phase = Phase.Arming;
                return;

            case Phase.Arming:
                if (entry is null) return;

                entry.Policy.Armed = true;
                entry.Policy.AutoEngage = true;
                entry.Policy.MissilesEnabled = true;
                _config.DrawOverlays = true;

                Report($"{_name}: armed, {entry.Battery.Ammo} rounds");
                _phase = Phase.Engaging;
                return;

            case Phase.Engaging:
                if (entry is null) return;
                Engage(entry, dt);
                return;
        }
    }

    private void Engage(BatteryRoster.Entry entry, double dt)
    {
        DefenceBattery battery = entry.Battery;

        if (!_spawned)
        {
            // The same numbers the panel's buttons use, so a scenario reproduces what a person
            // would have clicked rather than a case only the harness can produce.
            if (TestTarget.Spawn(battery.Platform!, _profile, 30.0, 300.0, 1500.0, "Gemini7") is null)
            {
                Finish("FAIL could not spawn a target");
                return;
            }

            _spawned = true;
            Report($"{_name}: target away, {_profile}");
            return;
        }

        _sinceSpawn += dt;

        // The first round leaving is the moment worth a picture: it shows the launcher, the round
        // on its way and the plume, which is most of what a screenshot can settle.
        if (!_capturedLaunch && battery.Rounds.Count > 0)
        {
            _capturedLaunch = true;
            Report($"{_name}: CAPTURE launch");
        }

        foreach (IProjectile round in battery.Rounds)
        {
            if (round.State == RoundState.Detonated)
            {
                Finish($"PASS detonated {round.MissDistance:F1} m from the target "
                       + $"after {round.Age:F1} s, {battery.Ammo} rounds left");
                return;
            }
        }

        // Rounds are reaped, so a detonation can be missed between frames. The battery's own
        // count falling with nothing in the air is the same news arriving late.
        if (_sinceSpawn > 15.0 && battery.Rounds.Count == 0 && battery.Ammo < battery.Profile.TubeCount)
        {
            Finish($"PASS engagement over, {battery.Ammo} rounds left "
                   + "(outcome from the battery, not a round -- see the lines above)");
        }
    }

    private void Finish(string outcome)
    {
        _phase = Phase.Done;
        Report($"{_name}: {outcome}");
        Report($"{_name}: END");
    }

    private static void Report(string line) => Log.Info($"{Tag} {line}");
}
