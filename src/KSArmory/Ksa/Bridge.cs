using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Commands from outside the game, read from a folder beside the log and answered in another:
/// what lets an agent in a terminal pause, move the camera, set off a burst, photograph it and
/// reload the shaders in a game that stays running. <c>tools/ksa-mcp/server.py</c> is the other
/// end, and <c>docs/VISUAL-TESTING.md</c> is why.
///
/// <para><b>Files, not a socket.</b> The mod reaches the network exactly once, when a player clicks
/// Send, and <c>tools/check-network.sh</c> holds it to that; a folder needs no port and works across
/// the WSL boundary as it is.</para>
///
/// <para>One command at a time, oldest first. A command that takes frames — a step, a capture —
/// holds the queue until it answers, so a sequence of them is a script that runs in order.</para>
/// </summary>
internal sealed class Bridge : IViewPose
{
    private readonly Config _config;

    // A few times a second is quick enough for a person and costs one directory listing.
    private const double PollSeconds = 0.1;
    private double _sincePoll;

    private Func<double, double, Reply?>? _running;
    private BridgeCommand? _current;

    // The pose the bridge holds the view in, and the view it took, to hand back.
    private CloudWatch.Pose? _pose;

    // Degrees a second the held camera circles the burst, on the player's clock so it turns with the
    // world paused: a camera moving between frames is what the cloud's accumulation has to survive.
    private double _orbitDegPerSecond;
    private KsaWorld.MainView _saved;
    private Vehicle? _posedFrom;

    private static readonly string[] ShaderIds =
        ["KSArmoryCloudCompute", "KSArmoryCloudResolveCompute", "KSArmoryShockCompute",
         "KSArmoryFireLightCompute", "KSArmoryRingCompute", "KSArmoryHoleCompute", "KSArmoryLeakCompute"];

    public Bridge(Config config, Func<Vehicle?, WeaponSystem?> systemFor,
                  Func<IEnumerable<WeaponSystems.Entry>> systems, Func<IReadOnlyList<WeaponSystem>> loose,
                  Countermeasures countermeasures, Action<Vehicle> focus,
                  Func<IEnumerable<OpticalHeads.Entry>> heads)
    {
        _heads = heads;
        _loose = loose;
        _focus = focus;
        _config = config;
        _systemFor = systemFor;
        _systems = systems;
        _countermeasures = countermeasures;
    }

    private readonly Func<IEnumerable<WeaponSystems.Entry>> _systems;
    private readonly Func<IEnumerable<OpticalHeads.Entry>> _heads;
    private readonly Countermeasures _countermeasures;
    private readonly Action<Vehicle> _focus;
    private readonly Func<IReadOnlyList<WeaponSystem>> _loose;

    // The weapons system a burst that damages is judged by: the flown craft's own, so the burst is
    // its warhead going off there, and the craft itself is its platform -- dented, never broken.
    private readonly Func<Vehicle?, WeaponSystem?> _systemFor;

    // The last burst that damaged, in the frame of the craft it was set off beside: a dent's push
    // is measured against it, and a bare ecliptic point is left behind by the planet.
    private (Vehicle Craft, double3 BurstAsmb)? _lastDamage;

    // Asked for from the panel, and taken up by the next Update: the panel draws in the UI pass and
    // the bridge runs in the frame hook, and a capture must not start inside the UI pass.
    private static bool _playerAsked;

    /// <summary>Asks for a capture of what the player is looking at, with a note of the state.</summary>
    public static void RequestPlayerCapture() => _playerAsked = true;

    /// <summary>The folder the last one went to, for the panel to say so.</summary>
    public static string? LastPlayerCapture { get; private set; }

    private static string Root => Path.Combine(Log.Folder, "bridge");
    private static string Inbox => Path.Combine(Root, "in");
    private static string Outbox => Path.Combine(Root, "out");

    private readonly record struct Reply(bool Ok, string Error, Dictionary<string, object?> Data);

    private static Reply Done(Dictionary<string, object?>? data = null) => new(true, string.Empty, data ?? []);
    private static Reply Failed(string why) => new(false, why, []);

    /// <summary>
    /// One frame: advances the command running, or takes the next. Called from the one hook KSA
    /// always calls, so it runs in the menu and with the UI hidden. Nothing here may throw.
    /// </summary>
    public void Update(double dtPlayer, double dtSim)
    {
        try
        {
            HoldFlight();

            if (_pose is { } held && _orbitDegPerSecond != 0.0 && double.IsFinite(dtPlayer))
            {
                _pose = held with { AzimuthDeg = held.AzimuthDeg + (_orbitDegPerSecond * dtPlayer) };
            }

            if (_running is not null && _current is not null)
            {
                if (_running(dtPlayer, dtSim) is { } reply) Answer(_current, reply);
                return;
            }

            if (_playerAsked)
            {
                _playerAsked = false;
                StartPlayerCapture();
                return;
            }

            _sincePoll += dtPlayer;
            if (_sincePoll < PollSeconds) return;
            _sincePoll = 0.0;

            if (!Directory.Exists(Inbox)) return;

            string? next = Directory.GetFiles(Inbox, "*.json").Order(StringComparer.Ordinal).FirstOrDefault();
            if (next is null) return;

            string text = File.ReadAllText(next);
            File.Delete(next);

            if (!BridgeCommand.TryParse(text, out BridgeCommand? command, out string trouble))
            {
                Log.Warn($"bridge: refused {Path.GetFileName(next)} -- {trouble}");
                return;
            }

            Start(command!);
        }
        catch (Exception e)
        {
            if (_current is { } command) Answer(command, Failed($"threw: {e.Message}"));
            else Log.Warn($"bridge: {e.Message}");
        }
    }

    // Eight frames as quickly as KSA will take them, and a note written first, so it is the state at
    // the moment the player pressed rather than after the frames.
    private void StartPlayerCapture()
    {
        string id = "player-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string folder = Path.Combine(Outbox, id);
        Directory.CreateDirectory(folder);

        Dictionary<string, object?> note = new()
        {
            ["status"] = Status().Data,
            ["tunables"] = ShaderTunables.All.ToDictionary(t => t.Name, t => (object?)ShaderTunables.Value(t)),
            ["config"] = typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Instance)
                                       .ToDictionary(f => f.Name, f => (object?)Convert.ToString(
                                                         f.GetValue(_config), CultureInfo.InvariantCulture)),
        };
        File.WriteAllText(Path.Combine(folder, "note.json"), JsonSerializer.Serialize(note, JsonOptions));

        try
        {
            // Shared, because the log is held open for writing the whole session.
            using FileStream stream = new(Path.Combine(Log.Folder, "KSArmory.log"), FileMode.Open,
                                          FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new(stream);
            string[] log = reader.ReadToEnd().Split('\n');
            File.WriteAllLines(Path.Combine(folder, "log-tail.txt"), log.Skip(Math.Max(0, log.Length - 300)));
        }
        catch (IOException)
        {
            // A note without the log's tail still says what the state was.
        }

        LastPlayerCapture = folder;
        Log.Info($"capture for Claude: {folder}");

        string request = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id,
            ["cmd"] = "capture",
            ["label"] = "player",
            ["frames"] = 8,
            ["every_frames"] = 1,
        });

        if (BridgeCommand.TryParse(request, out BridgeCommand? command, out _)) Start(command!);
    }

    /// <summary>Holds the view on the newest burst while a pose is set. From the camera pass.</summary>
    public void DriveCamera()
    {
        try
        {
            if (_watch is not null)
            {
                HoldOnRound();
                return;
            }

            if (_pose is not { } pose || _posedFrom is not { } craft || !KsaWorld.IsAlive(craft)) return;

            CloudWatch.Update(craft, pose);
        }
        catch
        {
            // The view is the player's to take back; a pose that cannot be held is simply not held.
        }
    }

    private void Start(BridgeCommand command)
    {
        _current = command;
        Log.Info($"bridge: {command.Name} ({command.Id})");

        Reply? now = command.Name switch
        {
            "status" => Status(),
            "pause" => KsaWorld.SetPaused(true) ? Done() : Failed("the world would not pause"),
            "resume" => KsaWorld.SetPaused(false) ? Done() : Failed("the world would not resume"),
            "speed" => KsaWorld.SetSimulationSpeed(command.Number("x", 1.0)) ? Done() : Failed("speed refused"),
            "set" => Set(command),
            "get" => Get(command),
            "clear" => Clear(),
            "burst" => Burst(command),
            "dents" => Dents(command),
            "camera" => Camera(command),
            "reload_shaders" => ReloadShaders(),
            "tune" => Tune(command),
            "cost" => Cost(command),
            "player_capture" => PressTheButton(),
            "site" => Site(command),
            "step" => BeginStep(command),
            "capture" => BeginCapture(command),
            "load" => BeginLoad(command),
            "system" => System(command),
            "optic" => Optic(command),
            "dispense" => Dispense(command),
            "fly" => Fly(command),
            "watch" => Watch(command),
            "fire" => Fire(command),
            "spawn" => Spawn(command),
            "ground" => Ground(command),
            "save" => SaveGame(command),
            "control" => Control(command),
            "remove" => RemoveCraft(command),
            "orbit" => PutInOrbit(command),
            _ => Failed($"no command '{command.Name}'"),
        };

        if (now is { } reply) Answer(command, reply);
    }

    private void Answer(BridgeCommand command, Reply reply)
    {
        _running = null;
        _current = null;

        Directory.CreateDirectory(Outbox);

        Dictionary<string, object?> body = new()
        {
            ["id"] = command.Id,
            ["cmd"] = command.Name,
            ["ok"] = reply.Ok,
            ["error"] = reply.Ok ? null : reply.Error,
            ["data"] = reply.Data,
        };

        // Written aside and moved, so the other end never reads half a reply.
        string final = Path.Combine(Outbox, command.Id + ".json");
        string partial = final + ".part";
        File.WriteAllText(partial, JsonSerializer.Serialize(body, JsonOptions));
        File.Move(partial, final, overwrite: true);

        if (!reply.Ok) Log.Warn($"bridge: {command.Name} ({command.Id}) failed -- {reply.Error}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ---- commands that answer at once -------------------------------------------------------

    private Reply Status()
    {
        Dictionary<string, object?> data = new()
        {
            ["build"] = Build.Version,
            ["in_flight"] = KsaWorld.InFlightScene,
            ["craft"] = KsaWorld.ControlledVehicle is { } craft ? KsaWorld.DisplayName(craft) : null,
            ["paused"] = KsaWorld.IsPaused,
            ["speed"] = KsaWorld.SimulationSpeed,
            ["clouds"] = NuclearClouds.Count,
            ["marks"] = NuclearClouds.ScorchCount,
            ["glows"] = NuclearClouds.GlowCount,
            ["curtains"] = NuclearClouds.AuroraCount,
            ["waves"] = NuclearClouds.WaveCount,
            ["thin"] = Enumerable.Range(0, NuclearClouds.Count).Count(NuclearClouds.IsThin),
            ["sky_wanted"] = CloudPass.SkyWanted,
            ["sky_drawn"] = CloudPass.SkyDrawn,
            ["camera_held"] = _pose is not null,
            ["flares_in_air"] = Countermeasures.Live.Count(d => d.Profile.Kind == DecoyKind.Flare),
            ["chaff_in_air"] = Countermeasures.Live.Count(d => d.Profile.Kind == DecoyKind.Chaff),
            ["others"] = OthersFromFlown(),
            ["rounds"] = RoundsInFlight(),
        };

        if (NuclearClouds.TryNewest(out double age, out double charge))
        {
            data["newest_burst"] = new Dictionary<string, object?>
            {
                ["age_s"] = Math.Round(age, 3),
                ["kt"] = MushroomCloud.KilotonsFor(charge),
            };
        }

        return Done(data);
    }

    private Reply Set(BridgeCommand command)
    {
        string field = command.String("name");
        if (!command.TryRaw("value", out JsonElement value)) return Failed("set needs a name and a value");

        if (!BridgeCommand.TrySetField(_config, field, value, out string trouble)) return Failed(trouble);

        // The panel's tick box does this beside the write; the field alone changes nothing.
        if (field == nameof(Config.VerboseLog)) Log.Threshold = _config.VerboseLog ? Log.Level.Debug : Log.Level.Info;

        return Done(new() { [field] = BridgeCommand.FieldText(_config, field) });
    }

    // One craft's selected weapon fired at a point east, north and up of the craft, through the same
    // FireAt the designation tool uses -- a bomb released at a place, a missile at a spot in the sky.
    // Player seconds a gun is given to lay onto a bridge point before the shot is given up.
    private const double LayBudgetSeconds = 30.0;

    // The selected weapon, or with station=N the craft's Nth launcher.
    private WeaponSystem? StationOn(Vehicle craft, BridgeCommand command)
    {
        if (!command.Has("station")) return _systemFor(craft);

        int ordinal = (int)command.Number("station", 1) - 1;
        foreach (WeaponSystems.Entry e in _systems())
        {
            if (ReferenceEquals(e.Craft, craft) && e.Ordinal == ordinal) return e.Weapon;
        }

        return null;
    }

    private Reply? Fire(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");
        if (StationOn(craft, command) is not { } weapon) return Failed("no weapons system on that craft");
        if (KsaWorld.ParentBody(craft) is not { } body
            || !DropScenario.TryLocalFrame(craft, body, out double3 up, out double3 east, out double3 north, out double agl))
        {
            return Failed("no local frame under that craft");
        }

        double3 PointEcl() => KsaWorld.PositionEcl(craft) + (east * command.Number("east_m", 0.0))
                              + (north * command.Number("north_m", 0.0)) + (up * command.Number("up_m", -agl));
        double3 at = PointEcl();
        int before = weapon.Rounds.Count;

        // In play the mouse points the launcher before a click fires it. Here nothing does, so the
        // point is designated as a shift-click would and the shot waits for the lay -- without it a
        // shell leaves along the barrel's rest line, and a missile along the pods' and misses by
        // kilometres, since a 57E6 at speed cannot turn onto a point beside or behind it.
        bool belt = weapon.TriggerArmament == ArmamentKind.Belt;
        if ((belt || weapon.Profile.Trains) && command.Flag("lay", true))
        {
            Aimpoint aim = KsaWorld.TryAnchorToGround(at, out object? anchorBody, out double3 anchor)
                               ? Aimpoint.OnGround(anchorBody!, anchor, at, Vec.Zero)
                               : Aimpoint.AtPoint(at);
            weapon.Designate(aim, "a bridge point");

            int frames = 0;
            double waited = 0.0;
            _running = (dtPlayer, _) =>
            {
                waited += dtPlayer;

                // Two frames, so the lay is read against the new order rather than the last one's.
                if (++frames < 2 || !(belt ? weapon.GunsAreLaid : weapon.IsLaid))
                {
                    return waited > LayBudgetSeconds ? Failed($"not laid after {LayBudgetSeconds:F0} s: {weapon.Hold}") : null;
                }

                if (belt)
                {
                    return weapon.FireBurst((int)command.Number("rounds", 0.0))
                               ? Done(new() { ["burst"] = true,
                                              ["weapon"] = weapon.Profile.DisplayName,
                                              ["laid_after_s"] = Math.Round(waited, 2),
                                              ["agl_m"] = Math.Round(agl) })
                               : Failed($"refused: {weapon.Hold}");
                }

                // Taken again now: an ecliptic point held across the lay is left seconds of ~30 km/s behind.
                int ahead = weapon.Rounds.Count;
                return weapon.FireAt(PointEcl())
                           ? Done(new() { ["fired"] = weapon.Rounds.Count - ahead,
                                          ["weapon"] = weapon.Profile.DisplayName,
                                          ["laid_after_s"] = Math.Round(waited, 2),
                                          ["agl_m"] = Math.Round(agl) })
                           : Failed($"refused: {weapon.Hold}");
            };

            return null;
        }

        bool fired = weapon.FireAt(at);

        return fired
                   ? Done(new() { ["fired"] = weapon.Rounds.Count - before, ["weapon"] = weapon.Profile.DisplayName,
                                  ["agl_m"] = Math.Round(agl) })
                   : Failed($"refused: {weapon.Hold}");
    }

    // Every system with rounds in the air, crewed or loose, and how many.
    private List<object?> RoundsInFlight()
    {
        List<object?> seen = [];
        foreach (WeaponSystem s in AllSystems())
        {
            int flying = s.Rounds.Count(r => r.State == RoundState.Flying);
            if (flying == 0) continue;

            seen.Add(new Dictionary<string, object?>
            {
                ["system"] = SystemName(s),
                ["loose"] = s.Platform is null,
                ["flying"] = flying,
            });
        }

        return seen;
    }

    private IEnumerable<WeaponSystem> AllSystems()
    {
        foreach (WeaponSystems.Entry e in _systems()) yield return e.Weapon;
        foreach (WeaponSystem s in _loose()) yield return s;
    }

    private static string SystemName(WeaponSystem s)
        => s.Platform is { } craft ? KsaWorld.DisplayName(craft) : s.LooseName;

    // One round held in the main view, crewed or loose, from a distance and a direction relative to its
    // flight: azimuth 0 and elevation 0 is straight behind it, 180 straight ahead of it. Held until the
    // round ends or release=true, so a paused capture can look at what a round is drawn as.
    private (WeaponSystem System, IProjectile Round, double Distance, double AzimuthDeg, double ElevationDeg,
             double FovDeg)? _watch;

    private Reply Watch(BridgeCommand command)
    {
        if (command.Flag("release", false))
        {
            ReleaseWatch();
            return Done();
        }

        string craft = command.String("craft");
        bool looseOnly = command.Flag("loose", false);
        int tube = (int)command.Number("tube", 0.0);

        foreach (WeaponSystem s in AllSystems())
        {
            if (looseOnly && s.Platform is not null) continue;
            if (craft.Length > 0 && SystemName(s) != craft) continue;

            // No tube asked for is any round, a gun's shells among them: those carry a negative tube.
            IProjectile? round = s.Rounds.FirstOrDefault(r => r.State == RoundState.Flying
                                                              && (tube == 0 || r.Tube == tube));
            if (round is null) continue;

            if (_watch is null && _pose is null)
            {
                _saved = KsaWorld.RememberMainView();
                _posedFrom = KsaWorld.ControlledVehicle;
            }

            _pose = null;
            _watch = (s, round, command.Number("distance_m", 25.0), command.Number("azimuth_deg", 20.0),
                      command.Number("elevation_deg", 10.0), command.Number("fov_deg", 30.0));

            // Following the round itself, as the chase does: an offset from any other craft is applied
            // a frame later against where that craft is then, and the two drift by their relative motion.
            _watchFollow.Track(round, s);
            if (!KsaWorld.TryFollowOnMainViewport(_watchFollow))
            {
                _watch = null;
                return Failed("the view refused to follow the round");
            }

            return Done(new()
            {
                ["system"] = SystemName(s),
                ["round"] = RoundLabel.For(round.Tube),
                ["munition"] = round.Munition.DisplayName,
                ["loose"] = s.Platform is null,
                ["age_s"] = Math.Round(round.Age, 2),
            });
        }

        return Failed(looseOnly ? "no loose round in the air" : "no round in the air");
    }

    private readonly RoundFollowable _watchFollow = new();

    ChaseOrbit? IViewPose.Orbit => null;

    private void HoldOnRound()
    {
        if (_watch is not { } w) return;

        if (w.Round.State != RoundState.Flying)
        {
            ReleaseWatch();
            return;
        }

        if (TryWatchPose(out double3 fromRound, out double3 up))
        {
            KsaWorld.TryLookFromMainViewport(fromRound, -fromRound, up, w.FovDeg, this);
        }
    }

    // Asked again inside the engine's viewport pass. The view follows the round, so the offset is the
    // separation from it and nothing else, and needs nothing the engine has since moved.
    bool IViewPose.TryPose(double3 followedEcl, out double3 offsetFromFollowed, out double3 forwardEcl,
                           out double3 upEcl, out double fovDeg)
    {
        fovDeg = _watch?.FovDeg ?? 30.0;
        forwardEcl = upEcl = offsetFromFollowed = Vec.Zero;
        if (!TryWatchPose(out offsetFromFollowed, out upEcl)) return false;

        forwardEcl = -offsetFromFollowed;
        return true;
    }

    // Where the eye sits from the round, in the round's own flight frame: behind it along its velocity,
    // turned by the azimuth about local up and raised by the elevation.
    private bool TryWatchPose(out double3 fromRound, out double3 up)
    {
        fromRound = up = Vec.Zero;
        if (_watch is not { } w || w.Round.State != RoundState.Flying) return false;
        if (!w.System.TryRoundEffectEcl(w.Round, out double3 roundEcl)) return false;

        double3 along = Vec.Unit(w.Round.VelocityLocal);
        up = w.System.EffectBody is { } body ? Vec.Unit(roundEcl - KsaWorld.PositionEcl(body)) : Vec.Zero;
        if (Vec.Len2(along) < 0.5 || Vec.Len2(up) < 0.5) return false;

        double3 side = Vec.Unit(Vec.Cross(along, up));
        if (Vec.Len2(side) < 0.5) side = Vec.AnyPerpendicular(along);
        double az = double.DegreesToRadians(w.AzimuthDeg);
        double el = double.DegreesToRadians(w.ElevationDeg);
        double3 flat = (-along * Math.Cos(az)) + (side * Math.Sin(az));
        fromRound = ((flat * Math.Cos(el)) + (up * Math.Sin(el))) * w.Distance;
        return Vec.IsFinite(fromRound);
    }

    private void ReleaseWatch()
    {
        if (_watch is null) return;

        _watch = null;
        KsaWorld.StopDrivingMainView();
        KsaWorld.TryHandBackMainView(_saved, _watchFollow, out _, out _);
        _watchFollow.Track(null, null);
        _posedFrom = null;
    }

    // Every other craft as seen from the one being flown: range, compass bearing, elevation and speed.
    private static List<object?> OthersFromFlown()
    {
        List<object?> seen = [];
        if (KsaWorld.ControlledVehicle is not { } flown || KsaWorld.ParentBody(flown) is not { } body) return seen;
        if (!DropScenario.TryLocalFrame(flown, body, out double3 up, out double3 east, out double3 north, out double agl)) return seen;

        double3 here = KsaWorld.PositionEcl(flown);
        foreach (Vehicle v in KsaWorld.Vehicles)
        {
            if (ReferenceEquals(v, flown) || !KsaWorld.IsAlive(v)) continue;

            double3 to = KsaWorld.PositionEcl(v) - here;
            double range = Vec.Len(to);
            if (range > 200_000.0) continue;

            double bearing = double.RadiansToDegrees(Math.Atan2(Vec.Dot(to, east), Vec.Dot(to, north)));
            seen.Add(new Dictionary<string, object?>
            {
                ["name"] = KsaWorld.DisplayName(v),
                ["range_m"] = Math.Round(range),
                ["bearing_deg"] = Math.Round((bearing + 360.0) % 360.0, 1),
                ["elevation_deg"] = Math.Round(double.RadiansToDegrees(Math.Asin(Math.Clamp(Vec.Dot(to, up) / range, -1.0, 1.0))), 1),
                ["radar_m2"] = Math.Round(RadarSignature.CrossSectionFor(KsaWorld.MeanRadius(v))),
                ["mass_kg"] = Math.Round(KsaWorld.MassKg(v), 2),
                ["situation"] = v.Situation.ToString(),
            });
        }

        seen.Add(new Dictionary<string, object?>
        {
            ["name"] = "(flown) " + KsaWorld.DisplayName(flown),
            ["agl_m"] = Math.Round(agl),
            ["speed_ms"] = Math.Round(Vec.Len(KsaWorld.VelocityEcl(flown) - KsaWorld.GroundVelocityAt(flown, here))),
            ["heat_kw_sr"] = Math.Round(KsaWorld.HeatOf(flown), 1),
            ["radar_m2"] = Math.Round(RadarSignature.CrossSectionFor(KsaWorld.MeanRadius(flown))),
            ["mass_kg"] = Math.Round(KsaWorld.MassKg(flown), 2),
        });

        return seen;
    }

    // A stock craft parked on the ground at lat/lon, as a target: craft is the stock save's name.
    private static Reply Spawn(BridgeCommand command)
    {
        if (KsaWorld.ControlledVehicle is not { } flown) return Failed("no craft is being flown");
        if (Detonation.BodyFor(flown) is not { } body) return Failed("no body under the craft");

        string stock = command.String("craft");
        if (stock.Length == 0) stock = "Rocket";
        string name = command.String("name");
        if (name.Length == 0) name = $"Target {stock}";

        return TestTarget.SpawnParked(flown, stock, name, body, command.Number("lat", 0.0), command.Number("lon", 0.0)) is { }
            ? Done(new() { ["name"] = name })
            : Failed($"could not park a {stock}");
    }

    // The ground's height against sea level at lat/lon, negative where it is seabed -- or along a line
    // to to_lat/to_lon in steps -- so a place can be surveyed without setting a craft down on it.
    private static Reply Ground(BridgeCommand command)
    {
        if (KsaWorld.ControlledVehicle is not { } flown || Detonation.BodyFor(flown) is not { } body)
        {
            return Failed("no body under the craft");
        }
        KsaWorld.TrySeaLevel(body, out double sea);

        double lat = command.Number("lat", 0.0), lon = command.Number("lon", 0.0);
        double toLat = command.Number("to_lat", lat), toLon = command.Number("to_lon", lon);
        int steps = Math.Clamp((int)command.Number("steps", 0.0), 0, 200);

        List<object?> line = [];
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0.0 : (double)i / steps;
            double la = lat + ((toLat - lat) * t), lo = lon + ((toLon - lon) * t);
            double3 dir = body.GetDirCcfFromLatLon(la, lo);
            double height = body.GetTerrainHeightFromDirCcf(dir, accurate: true);
            line.Add(new Dictionary<string, object?>
            {
                ["lat"] = Math.Round(la, 5), ["lon"] = Math.Round(lo, 5), ["ground_m"] = Math.Round(height - sea, 1),
            });
        }

        return Done(new() { ["sea_level_m"] = Math.Round(sea, 1), ["points"] = line });
    }

    // Fly the named craft: the camera follows it and the controls drive it.
    private static Reply Control(BridgeCommand command)
        => CraftNamed(command.String("craft")) is { } craft && KsaWorld.GoTo(craft)
               ? Done(new() { ["craft"] = KsaWorld.DisplayName(craft) })
               : Failed("no such craft, or the engine would not hand it over");

    // A named craft taken out of the world, leaving nothing behind -- never the one being flown, which
    // the engine would take the scene with.
    private static Reply RemoveCraft(BridgeCommand command)
    {
        string name = command.String("craft");
        if (name.Length == 0 || CraftNamed(name) is not { } craft) return Failed("no such craft");
        if (ReferenceEquals(craft, KsaWorld.ControlledVehicle)) return Failed("that craft is being flown; control another first");
        KsaWorld.Remove(craft);
        return Done(new() { ["removed"] = name });
    }

    // A named craft put on a circular orbit alt_km up, passing over lat/lon on heading_deg, as KSA's own
    // Set Orbit window queues it -- so a scenario save can start a bus in orbit rather than on a pad.
    private static Reply PutInOrbit(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");
        if (Detonation.BodyFor(craft) is not Celestial body) return Failed("no body under the craft");

        double altitude = command.Number("alt_km", 300.0) * 1000.0;
        double heading = double.DegreesToRadians(command.Number("heading_deg", 180.0));
        UniverseTime at = Universe.GetNextSimStep().NextTime;

        double3 up = Vec.Unit(body.GetCcf2Cci(at) * body.GetDirCcfFromLatLon(command.Number("lat", 0.0), command.Number("lon", 0.0)));
        double3 east = Vec.Unit(Vec.Cross(new double3(0, 0, 1), up));
        double3 north = Vec.Cross(up, east);
        if (!Vec.IsFinite(east)) return Failed("no heading exists over a pole");

        double radius = body.MeanRadius + altitude;
        double3 positionCci = up * radius;
        double3 velocityCci = ((north * Math.Cos(heading)) + (east * Math.Sin(heading))) * Math.Sqrt(((IParentBody)body).Mu / radius);

        Orbit orbit = Orbit.CreateFromStateCci(body, at, positionCci, velocityCci, craft.Orbit.OrbitLineColor);
        InputEvents.TeleportInputBuffer.Add(new InputEvents.TeleportInputData
        {
            Vehicle = craft, Orbit = orbit, Body2Cce = null, BodyRates = double3.Zero,
        });
        return Done(new() { ["craft"] = KsaWorld.DisplayName(craft), ["periapsis_km"] = Math.Round((orbit.Periapsis - body.MeanRadius) / 1000.0, 1) });
    }

    // The game written to a save of this name, as KSA's own save console command writes it.
    private static Reply SaveGame(BridgeCommand command)
    {
        string name = command.String("name");
        if (name.Length == 0) return Failed("a save needs a name");
        GameSaves.MakeUncompressedSave(name);
        return Done(new() { ["name"] = name });
    }

    // A craft by the name it shows, falling back to the one being flown.
    private static Vehicle? CraftNamed(string name)
    {
        if (string.IsNullOrEmpty(name)) return KsaWorld.ControlledVehicle;

        foreach (Vehicle v in KsaWorld.Vehicles)
        {
            if (KsaWorld.IsAlive(v) && KsaWorld.DisplayName(v) == name) return v;
        }

        return null;
    }

    // One craft's weapons settings, as the panel would set them, and optionally its missiles flown on a
    // seeker of the named band for testing a decoy against it: "infrared", "radar", "radar-gated", "stock".
    private Reply System(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");

        if (command.Flag("focus", false)) _focus(craft);

        List<object?> changed = [];
        foreach (WeaponSystems.Entry e in _systems())
        {
            if (!ReferenceEquals(e.Craft, craft)) continue;

            SystemConfig p = e.Policy;
            if (command.Has("auto_engage")) p.AutoEngage = command.Flag("auto_engage", p.AutoEngage);
            if (command.Has("protect")) p.ProtectControlledVehicle = command.Flag("protect", p.ProtectControlledVehicle);
            if (command.Has("silent")) p.RadarSilent = command.Flag("silent", p.RadarSilent);
            if (command.Has("guns")) p.GunsEnabled = command.Flag("guns", p.GunsEnabled);
            if (command.Has("chase")) p.ChaseRounds = command.Flag("chase", p.ChaseRounds);

            string trigger = command.String("trigger");
            if (trigger == "cannon") e.Weapon.TriggerArmament = ArmamentKind.Belt;
            else if (trigger == "tubes") e.Weapon.TriggerArmament = ArmamentKind.Tubes;

            string seeker = command.String("seeker");
            if (seeker.Length > 0)
            {
                MunitionProfile stock = Catalogue.MunitionNamed(e.Weapon.Munition.Name);
                MunitionProfile flown = stock.Copy();
                if (seeker != "stock")
                {
                    flown.Guidance = GuidanceMode.Seeker;
                    flown.Band = seeker == "infrared" ? SeekerBand.Infrared : SeekerBand.Radar;
                    flown.DopplerGateMps = seeker == "radar-gated" ? 40f : 0f;
                    flown.CountermeasureResistance = (float)command.Number("resistance", 0.0);
                    flown.SeekerFovDeg = Math.Max(flown.SeekerFovDeg, 40f);
                }

                e.Weapon.FlyRoundsAs(flown);
            }

            changed.Add(new Dictionary<string, object?>
            {
                ["launcher"] = e.Weapon.Profile.DisplayName,
                ["auto_engage"] = p.AutoEngage,
                ["trigger"] = e.Weapon.TriggerArmament == ArmamentKind.Belt ? "cannon" : "tubes",
                ["protect"] = p.ProtectControlledVehicle,
                ["guidance"] = e.Weapon.Munition.Guidance.ToString(),
                ["band"] = e.Weapon.Munition.Band.ToString(),
            });
        }

        return changed.Count > 0 ? Done(new() { ["systems"] = changed }) : Failed("no weapons system on that craft");
    }

    // A craft's directors, as their rows would set them. view is "new" for a spare camera window,
    // "main", "off" or a window's index; the reply reads each window's camera back.
    private Reply Optic(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");

        List<object?> heads = [];
        foreach (OpticalHeads.Entry e in _heads())
        {
            if (!ReferenceEquals(e.Head.Platform, craft)) continue;

            OpticConfig p = e.Policy;
            string view = command.String("view");
            if (view == "off") p.Viewport = -1;
            else if (view == "main") p.Viewport = KsaWorld.MainViewportIndex;
            else if (view == "new")
            {
                if (!KsaWorld.TryOpenCameraWindow(out int opened)) return Failed("no camera window spare");
                p.Viewport = opened;
            }
            else if (int.TryParse(view, out int index)) p.Viewport = index;

            if (command.Has("magnification")) p.Magnification = SightZoom.Clamp(command.Number("magnification", 1.0));
            if (command.Has("tracking")) p.Tracking = command.Flag("tracking", p.Tracking);
            if (command.Has("bearing_deg") || command.Has("elevation_deg"))
            {
                p.Manual = true;
                p.ManualBearingDeg = (float)command.Number("bearing_deg", p.ManualBearingDeg);
                p.ManualElevationDeg = (float)command.Number("elevation_deg", p.ManualElevationDeg);
            }
            if (command.Has("manual")) p.Manual = command.Flag("manual", p.Manual);
            if (Enum.TryParse(command.String("sensor"), ignoreCase: true, out SensorMode sensor)) p.Sensor = sensor;
            if (command.Has("lase")) p.Lasing = command.Flag("lase", p.Lasing);
            if (command.Has("code") && LaserCode.IsValid((int)command.Number("code", 0))) p.LaserCode = (int)command.Number("code", 0);

            Dictionary<string, object?> row = new()
            {
                ["head"] = e.Head.Profile.DisplayName,
                ["viewport"] = p.Viewport,
                ["magnification"] = p.Magnification,
                ["tracking"] = p.Tracking,
                ["sensor"] = p.Sensor.ToString(),
            };

            if (p.Viewport >= 0)
            {
                row["fov_deg"] = double.RadiansToDegrees(KsaWorld.ViewportFovRad(p.Viewport));
                if (KsaWorld.TryViewportPicture(p.Viewport, out float2 pos, out float2 size, out _))
                {
                    row["picture"] = new[] { pos.X, pos.Y, size.X, size.Y };
                }
                if (KsaWorld.TryReadViewportPose(p.Viewport, out double3 eye, out _, out _)
                    && e.Head.Platform is { } platform)
                {
                    row["eye_from_craft_m"] = Vec.Len(eye - KsaWorld.PositionEcl(platform));
                }
                row["nearby"] = KsaWorld.DescribeViewportContext(p.Viewport);
                row["aim"] = e.Head.DescribeAim(p.Viewport);
            }

            heads.Add(row);
        }

        return heads.Count > 0 ? Done(new() { ["heads"] = heads }) : Failed("no director on that craft");
    }

    // A craft flown by numbers: engine lit, full throttle, nose held at a pitch from the vertical on a
    // compass heading, restated every frame because an attitude hold is dropped the frame it is not.
    private (Vehicle Craft, double PitchDeg, double HeadingDeg, bool Burning)? _flight;

    private Reply Fly(BridgeCommand command)
    {
        if (command.Flag("stop", false))
        {
            if (_flight is { } was)
            {
                VehicleCommand.DriveThrottle(was.Craft, 0.0);
                VehicleCommand.ReleaseAttitude(was.Craft);
            }

            _flight = null;
            return Done();
        }

        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");

        _flight = (craft, command.Number("pitch_deg", 0.0), command.Number("heading_deg", 90.0),
                   command.Flag("engine", true));
        if (command.Flag("stage", false)) AttitudeHook.Stage(craft);

        return Done(new() { ["flying"] = KsaWorld.DisplayName(craft) });
    }

    private void HoldFlight()
    {
        if (_flight is not { } f) return;
        if (!KsaWorld.IsAlive(f.Craft) || KsaWorld.ParentBody(f.Craft) is not { } body)
        {
            _flight = null;
            return;
        }

        VehicleCommand.SetEngine(f.Craft, running: f.Burning);
        VehicleCommand.DriveThrottle(f.Craft, f.Burning ? 1.0 : 0.0);

        if (!DropScenario.TryLocalFrame(f.Craft, body, out double3 up, out double3 east, out double3 north, out _)) return;

        double pitch = double.DegreesToRadians(f.PitchDeg);
        double heading = double.DegreesToRadians(f.HeadingDeg);
        double3 level = (north * Math.Cos(heading)) + (east * Math.Sin(heading));
        double3 wanted = (up * Math.Cos(pitch)) + (level * Math.Sin(pitch));
        double3 side = Vec.Unit(Vec.Cross(wanted, up));
        if (Vec.Len2(side) < 0.5) side = north;

        doubleQuat toCci = body.GetCce2Cci();
        AttitudeHook.Hold(f.Craft, wanted.Transform(toCci), side.Transform(toCci));
    }

    // Presses a craft's countermeasure controls: kind is flare, chaff or both; auto sets auto-dispense.
    private Reply Dispense(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");

        if (command.Has("auto")) _countermeasures.SetAuto(craft, command.Flag("auto", false));

        string kind = command.String("kind");
        if (kind is "flare" or "both") _countermeasures.Dispense(craft, DecoyKind.Flare);
        if (kind is "chaff" or "both") _countermeasures.Dispense(craft, DecoyKind.Chaff);

        (int flares, int flareCap) = _countermeasures.Load(craft, DecoyKind.Flare);
        (int chaff, int chaffCap) = _countermeasures.Load(craft, DecoyKind.Chaff);

        return Done(new()
        {
            ["flares"] = $"{flares}/{flareCap}",
            ["chaff"] = $"{chaff}/{chaffCap}",
            ["auto"] = _countermeasures.AutoOn(craft),
        });
    }

    private Reply Get(BridgeCommand command)
    {
        string field = command.String("name");
        return BridgeCommand.FieldText(_config, field) is { } text
                   ? Done(new() { [field] = text })
                   : Failed($"no field '{field}' on Config");
    }

    private static Reply Clear()
    {
        NuclearClouds.Clear();
        return Done();
    }

    // A burst where the panel's burst tool would put one, but placed by numbers: metres east and
    // north of the craft being flown, on the ground there. The explosion and the cloud, and no
    // damage -- that is fire control's, and a test shot should not break the scene it is testing.
    private Reply Burst(BridgeCommand command)
    {
        if (KsaWorld.ControlledVehicle is not { } craft) return Failed("no craft is being flown");
        if (Detonation.BodyFor(craft) is not { } body) return Failed("no body under the craft");

        double kt = command.Number("kt", 0.3);
        if (!(kt > 0.0)) return Failed("kt must be positive");

        double3 craftEcl = KsaWorld.PositionEcl(craft);
        if (MapFrame.TryAt(body.GetPositionEcl(), craftEcl, body.GetRotationAxisCce()) is not { } frame)
        {
            return Failed("no local frame at the craft");
        }

        double3 above = craftEcl + (frame.East * command.Number("east_m", 2000.0))
                        + (frame.North * command.Number("north_m", 0.0));
        if (!KsaWorld.TrySnapToGround(above, out double3 ground)) return Failed("no ground under that point");

        // Over the sea the surface is the water, not the seabed under it: up_m is measured from there.
        if (KsaWorld.TrySeaLevel(body, out double sea))
        {
            double3 fromCentre = ground - body.GetPositionEcl();
            double seaRadius = body.MeanRadius + sea;
            if (Vec.Len(fromCentre) < seaRadius) ground = body.GetPositionEcl() + (Vec.Unit(fromCentre) * seaRadius);
        }

        double charge = kt * 1.0e6;

        // up_m sets it off that far above the ground: an air burst, and the one way to put a burst
        // over a craft that is sitting on it. Below zero it is under the ground, which is no burst
        // anybody sets off and the only way to put one under a craft resting on it.
        double up = command.Number("up_m", 0.0);
        ground += frame.Up * up;

        // Lifted by the fireball, as the burst tool lifts it, so the ball is not drawn half-buried.
        double3 lifted = ground + (frame.Up * (up != 0.0 ? 0.0 : Math.Max(MushroomCloud.PeakFireballRadius(kt), 2.0)));
        // KSA's own explosion as well unless told not to, which is how a warhead goes off; without it
        // the burst is this mod's drawing alone, which is what separates the two when one misdraws.
        // count sets off that many in the same frame, spacing_m apart eastward: a bus's salvo, which a
        // burst a call can never be, since calls are further apart than one burst stays one event.
        int count = Math.Clamp((int)command.Number("count", 1.0), 1, 12);
        double spacing = command.Number("spacing_m", 0.0);
        for (int i = 0; i < count; i++)
        {
            double3 offset = frame.East * (spacing * i);
            if (command.Flag("explode", true)) Detonation.Explode(lifted + offset, charge, craft);
            NuclearClouds.Begin(ground + offset, craft, charge);
        }

        // And, asked for, what a warhead of that yield does to every craft it reaches.
        if (command.Flag("damage", false))
        {
            if (_systemFor(craft) is not { } system) return Failed("the craft carries no weapons system to judge the burst by");

            MunitionProfile warhead = Arsenal.NukeB61.Copy();
            warhead.ChargeKg = (float)charge;
            // in_frame_s sets it off that long before the end of the step, from where the ground was
            // then, as a round that detonates part-way through a step reports it: the case a burst on
            // the step boundary never exercises.
            double inFrame = Math.Min(command.Number("in_frame_s", 0.0), 0.0);
            double3 atBurst = ground + (KsaWorld.GroundVelocityAt(craft, ground) * inFrame);
            system.SplashAt(atBurst, warhead, command.Flag("spare_own", true), inFrame);
            _lastDamage = (craft, KsaWorld.EclToVehicleAsmb(craft, ground));
        }

        return Done(new()
        {
            ["kt"] = kt,
            ["range_m"] = Math.Round(Vec.Len(ground - craftEcl)),
        });
    }

    // The dents the engine holds on the flown craft, each with the angle between its push and the
    // line from the last damaging burst to it: zero is pushed straight along the blast. clear takes
    // them all off, so a test starts from none.
    private Reply Dents(BridgeCommand command)
    {
        if (KsaWorld.ControlledVehicle is not { } craft) return Failed("no craft is being flown");

        if (command.Flag("clear", false)) return Done(new() { ["cleared_parts"] = KsaWorld.ClearDents(craft) });

        List<Dictionary<string, object?>> listed = [];
        foreach ((double3 centre, double3 push, double radius, double depth) in KsaWorld.DentsOn(craft))
        {
            double? offBlast = null;
            if (_lastDamage is { } last && ReferenceEquals(last.Craft, craft))
            {
                double3 along = Vec.Unit(centre - last.BurstAsmb);
                offBlast = Math.Round(double.RadiansToDegrees(Math.Acos(Math.Clamp(Vec.Dot(along, Vec.Unit(push)), -1.0, 1.0))), 1);
            }

            listed.Add(new()
            {
                ["depth_m"] = Math.Round(depth, 3),
                ["radius_m"] = Math.Round(radius, 2),
                ["deg_off_blast"] = offBlast,
            });
        }

        return Done(new() { ["count"] = listed.Count, ["dents"] = listed });
    }

    private Reply Camera(BridgeCommand command)
    {
        if (command.Flag("release", false))
        {
            if (_pose is null) return Done();

            _pose = null;
            KsaWorld.TryHandBackMainView(_saved, _posedFrom, out _, out _);
            _posedFrom = null;
            return Done();
        }

        if (KsaWorld.ControlledVehicle is not { } craft) return Failed("no craft is being flown");

        if (_pose is null)
        {
            _saved = KsaWorld.RememberMainView();
            _posedFrom = craft;
        }

        _pose = new CloudWatch.Pose(command.Number("azimuth_deg", 0.0), command.Number("elevation_deg", 14.0),
                                    command.Number("distance_m", 0.0), command.Number("aim", 0.45),
                                    command.Number("turn_deg", 0.0));
        _orbitDegPerSecond = command.Number("orbit_deg_s", 0.0);

        return Done(new() { ["held"] = true });
    }

    // The craft set down somewhere else on its body, or another -- which is also how a scene is had
    // at night, since the time of day is where the sun is from where the craft stands. Says the sun's
    // height there, so a caller looking for night can tell whether it got one.
    private Reply? Site(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");
        if (Detonation.BodyFor(craft) is not { } here) return Failed("no body under the craft");

        string body = command.String("body");
        if (body.Length == 0) body = here.Id;

        if (!KsaWorld.TryPlaceOnSurface(craft, body, command.Number("lat", 0.0), command.Number("lon", 0.0)))
        {
            return Failed($"could not place the craft on {body}");
        }

        NuclearClouds.Clear();

        // Answered a moment later: the move lands on a later frame, and the sun read on this one is
        // the sun where the craft was.
        double waited = 0.0;
        _running = (dtPlayer, _) =>
        {
            waited += dtPlayer;
            if (waited < 1.0) return null;

            return Detonation.BodyFor(craft) is { } now
                       ? Done(new() { ["body"] = now.Id,
                                      ["sun_elevation_deg"] = Math.Round(
                                          KsaWorld.SunElevationDeg(now, KsaWorld.PositionEcl(craft)), 2) })
                       : Done();
        };

        return null;
    }

    // What the panel's Capture for Claude button does, for a test that cannot click it.
    private static Reply PressTheButton()
    {
        RequestPlayerCapture();
        return Done();
    }

    // A shader constant set while the game runs: the pipelines rebuild with it on the next frame.
    // With no name, says what every one is; with reset, puts them all back.
    private static Reply Tune(BridgeCommand command)
    {
        if (command.Flag("reset", false)) ShaderTunables.Reset();

        string name = command.String("name");
        if (name.Length > 0
            && !ShaderTunables.TrySet(name, command.Number("value", double.NaN), out string trouble))
        {
            return Failed(trouble);
        }

        return Done(ShaderTunables.All.ToDictionary(t => t.Name, t => (object?)ShaderTunables.Value(t)));
    }

    // What the pass costs the GPU since the last reset, against the whole frame beside it. A reset
    // turns KSA's profiler on, which it is not by default; paused, the frames still render, so a
    // reset and a read a few seconds later measure one frozen instant.
    private static Reply Cost(BridgeCommand command)
    {
        if (command.Flag("reset", false))
        {
            CloudPassCost.Begin();
            return Done();
        }

        return CloudPassCost.HasSamples
                   ? Done(new Dictionary<string, object?> { ["cost"] = CloudPassCost.Report() })
                   : Failed("nothing measured -- send cost with reset first");
    }

    // KSA's own reloader cannot reach a mod's shader: it maps a path by finding "Content" in it.
    // What it does is ShaderReference.DoLoad, which is internal, and the pass's pipelines are then
    // dropped so they rebuild against the new module. A file that is missing makes DoLoad destroy
    // the old module and keep nothing, so that is checked first.
    private static Reply ReloadShaders()
    {
        MethodInfo? doLoad = typeof(FileReference).GetMethod("DoLoad", BindingFlags.NonPublic | BindingFlags.Instance);
        if (doLoad is null) return Failed("FileReference.DoLoad did not resolve");

        List<string> reloaded = [];
        List<string> seen = [];
        foreach (string id in ShaderIds)
        {
            if (!ModLibrary.TryGet<ShaderReference>(id, out ShaderReference? shader) || shader is null)
            {
                return Failed($"no shader '{id}'");
            }

            if (!File.Exists(shader.ModPath)) return Failed($"'{shader.ModPath}' is not there");

            // What was compiled and whether the module actually changed, because a reload that
            // answers "reloaded" and draws the old shader is otherwise indistinguishable from one
            // that worked.
            FileInfo file = new(shader.ModPath);
            nint before = shader.Shader?.VkHandle ?? 0;

            try
            {
                doLoad.Invoke(shader, null);
            }
            catch (TargetInvocationException e)
            {
                return Failed($"{id} did not compile: {e.InnerException?.Message ?? e.Message}");
            }

            nint after = shader.Shader?.VkHandle ?? 0;
            string sha1 = Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(shader.ModPath))).ToLowerInvariant();
            string line = $"{id}: {shader.ModPath}, {file.Length} bytes written {file.LastWriteTime:HH:mm:ss}, "
                          + $"sha1 {sha1[..12]}, module {before:X} -> {after:X}";
            seen.Add(line);
            Log.Info($"bridge: reloaded {line}");

            reloaded.Add(id);
        }

        CloudPass.Release();
        return Done(new() { ["reloaded"] = reloaded, ["modules"] = seen });
    }

    // ---- commands that take frames ----------------------------------------------------------

    // Runs the world for so many simulated seconds and stops it again, so what is photographed next
    // is at an age that was asked for rather than one that happened.
    private Reply? BeginStep(BridgeCommand command)
    {
        double seconds = command.Number("seconds", 1.0);
        if (!(seconds > 0.0)) return Failed("seconds must be positive");

        double advanced = 0.0;
        double waited = 0.0;
        KsaWorld.SetPaused(false);

        _running = (dtPlayer, dtSim) =>
        {
            waited += dtPlayer;
            advanced += Math.Max(dtSim, 0.0);

            if (advanced >= seconds)
            {
                KsaWorld.SetPaused(true);
                return Done(new() { ["advanced_s"] = Math.Round(advanced, 3) });
            }

            return waited > (seconds * 20.0) + 30.0 ? Failed($"only {advanced:F2} s passed") : null;
        };

        return null;
    }

    private Reply? BeginLoad(BridgeCommand command)
    {
        string save = command.String("save");
        if (save.Length == 0) return Failed("load needs a save");

        try
        {
            GameSaves.LoadSaveGame(save);
        }
        catch (Exception e)
        {
            return Failed($"could not load '{save}': {e.Message}");
        }

        _pose = null;
        double waited = 0.0;

        _running = (dtPlayer, _) =>
        {
            waited += dtPlayer;

            // A beat past the craft appearing, for the world to settle round it.
            if (waited > 3.0 && KsaWorld.InFlight)
            {
                return Done(new() { ["craft"] = KsaWorld.DisplayName(KsaWorld.ControlledVehicle!) });
            }

            return waited > 60.0 ? Failed("no craft after 60 s") : null;
        };

        return null;
    }

    // Photographs through KSA's own capture, which names files to the second: so each one is moved
    // aside and renamed before the next is asked for, and two can never be one file. Every picture
    // gets a manifest beside it -- the age, the camera and where the burst is on screen -- so nothing
    // has to be matched up by its time afterwards.
    private Reply? BeginCapture(BridgeCommand command)
    {
        string label = command.String("label");
        if (label.Length == 0) label = "shot";
        label = string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

        int frames = Math.Clamp((int)command.Number("frames", 1.0), 1, 120);
        double everySim = Math.Max(command.Number("every_s", 0.0), 0.0);
        int everyFrames = Math.Max((int)command.Number("every_frames", 1.0), 1);

        string folder = Path.Combine(Outbox, command.Id);
        Directory.CreateDirectory(folder);

        string shots = Path.Combine(KSA.Constants.DocumentsFolderPath, "exports", "screenshots");

        List<Dictionary<string, object?>> taken = [];
        int index = 0;
        bool waiting = false;
        DateTime askedAt = default;
        Dictionary<string, object?>? manifest = null;
        double sinceSim = 0.0;
        int sinceFrames = 0;
        double waited = 0.0;
        int warming = 0;

        // Paused with a spacing asked for, the world is run that far and stopped again before each
        // picture, so the ages are exactly the ones asked for however long a command takes to arrive.
        bool stepping = KsaWorld.IsPaused && everySim > 0.0;
        int settle = 0;

        _running = (dtPlayer, dtSim) =>
        {
            if (!waiting)
            {
                sinceSim += Math.Max(dtSim, 0.0);
                sinceFrames++;

                if (stepping && index > 0)
                {
                    if (sinceSim < everySim)
                    {
                        if (KsaWorld.IsPaused) KsaWorld.SetPaused(false);
                        return null;
                    }

                    if (!KsaWorld.IsPaused)
                    {
                        KsaWorld.SetPaused(true);
                        settle = 0;
                    }

                    // A frame or two for the paused world to be the one drawn.
                    if (++settle < 3) return null;
                }

                bool due = index == 0 || stepping
                           || (everySim > 0.0 ? sinceSim >= everySim : sinceFrames >= everyFrames);
                if (!due) return null;

                askedAt = DateTime.UtcNow.AddSeconds(-0.5);

                // WITH THE UI HIDDEN FOR A MOMENT FIRST. The cloud's history is blended over frames,
                // and under the game's windows it holds no cloud -- so a screenshot, which hides the
                // UI for the one frame it takes, showed each window as a dark grainy rectangle on the
                // cloud. KSA's own warm-up hides it for these frames first; the manifest is written on
                // the frame the picture is actually taken, so it still describes that instant.
                if (!KsaWorld.TryRequestScreenshot(flags: $"warm={WarmFrames}"))
                {
                    return Failed("KSA would not take a screenshot");
                }

                manifest = null;
                warming = WarmFrames;
                waiting = true;
                waited = 0.0;
                return null;
            }

            if (manifest is null && --warming <= 0) manifest = Manifest(label, index);

            waited += dtPlayer;
            if (waited > 10.0) return Failed($"screenshot {index} never arrived");

            string? file = Directory.Exists(shots)
                               ? new DirectoryInfo(shots).GetFiles("ksa_*.png")
                                                         .Where(f => f.LastWriteTimeUtc >= askedAt && f.Length > 0)
                                                         .OrderByDescending(f => f.LastWriteTimeUtc)
                                                         .FirstOrDefault()?.FullName
                               : null;
            if (file is null) return null;

            string name = $"{index:D2}-{label}";
            string target = Path.Combine(folder, name + ".png");

            try
            {
                File.Move(file, target, overwrite: true);
            }
            catch (IOException)
            {
                // Still being written; the next frame will find it finished.
                return null;
            }

            manifest ??= Manifest(label, index);
            manifest["file"] = target;
            File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(manifest, JsonOptions));
            taken.Add(manifest);

            index++;
            waiting = false;
            sinceSim = 0.0;
            sinceFrames = 0;

            return index >= frames ? Done(new() { ["folder"] = folder, ["frames"] = taken }) : null;
        };

        return null;
    }

    // How many frames the UI is hidden for before a capture: the history keeps about an eighth of
    // each frame, so after these under a twentieth of what the windows left is still in it.
    private const int WarmFrames = 24;

    // What a picture was of, recorded on the frame it was taken.
    private Dictionary<string, object?> Manifest(string label, int index)
    {
        Dictionary<string, object?> m = new()
        {
            ["label"] = label,
            ["index"] = index,
            ["wall"] = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["paused"] = KsaWorld.IsPaused,
            ["speed"] = KsaWorld.SimulationSpeed,
            ["whiteout"] = Math.Round(BurstFlash.Whiteout, 3),
            ["glare"] = Math.Round(BurstFlash.Glare, 3),
            ["fov_deg"] = Math.Round(KsaWorld.MainViewFovDeg(), 2),
            ["shader_pass"] = _config.ShaderPass,
            ["nuclear_blackout"] = _config.NuclearBlackout,
        };

        if (NuclearClouds.TryNewest(out double age, out double charge, out double height))
        {
            double kt = MushroomCloud.KilotonsFor(charge);
            NuclearClouds.TryNewestShape(out MushroomCloud.Shape shape);

            m["burst_age_s"] = Math.Round(age, 3);
            m["kt"] = kt;
            m["burst_height_m"] = Math.Round(height);
            m["cap_centre_m"] = Math.Round(shape.CapCentre);
            m["cap_radius_m"] = Math.Round(shape.CapRadius);
        }

        if (NuclearClouds.TryWatch(out double3 burstEcl, out double3 up, out _, out double top, out _))
        {
            m["burst_screen"] = Screen(burstEcl);
            m["cap_screen"] = NuclearClouds.TryNewestShape(out MushroomCloud.Shape capShape)
                                  ? Screen(burstEcl + (Vec.Unit(up) * capShape.CapCentre))
                                  : null;
            m["top_screen"] = Screen(burstEcl + (Vec.Unit(up) * top));

            if (NuclearClouds.TryNewestShape(out MushroomCloud.Shape boxShape))
            {
                m["cloud_box"] = CloudBox(burstEcl, Vec.Unit(up), boxShape);
            }

            if (KsaWorld.TryMainCameraPose(out double3 eye, out _))
            {
                double3 toEye = eye - burstEcl;
                double range = Vec.Len(toEye);
                m["camera_range_m"] = Math.Round(range);
                m["camera_elevation_deg"] = Math.Round(
                    90.0 - double.RadiansToDegrees(Vec.AngleBetween(toEye, up)), 2);
            }

            if (Detonation.BodyFor(KsaWorld.ControlledVehicle) is { } body)
            {
                m["sun_elevation_deg"] = Math.Round(KsaWorld.SunElevationDeg(body, burstEcl), 2);
            }
        }

        return m;
    }

    // The cloud as it stands now, as a box on screen: the stem's flared foot to the cap's crown, and
    // the cap's width either side. What a crop wants, where the final top is kilometres of sky above
    // a cloud still rising.
    private static double[]? CloudBox(double3 burstEcl, double3 up, MushroomCloud.Shape shape)
    {
        if (!KsaWorld.TryMainCameraPose(out _, out double3 forward)) return null;

        double3 right = Vec.Unit(Vec.Cross(forward, up));
        if (!Vec.IsFinite(right)) return null;

        double wide = Math.Max(shape.CapRadius + shape.CapTube, shape.StemRadius * 3.5);
        double crown = shape.CapCentre + (2.1 * shape.CapTube);

        double3[] corners =
        [
            burstEcl + (right * wide), burstEcl - (right * wide),
            burstEcl + (up * crown) + (right * wide), burstEcl + (up * crown) - (right * wide),
        ];

        double[]? box = null;
        foreach (double3 corner in corners)
        {
            if (Screen(corner) is not { } p) return null;

            box = box is null ? [p[0], p[1], p[0], p[1]]
                              : [Math.Min(box[0], p[0]), Math.Min(box[1], p[1]),
                                 Math.Max(box[2], p[0]), Math.Max(box[3], p[1])];
        }

        return box;
    }

    // Where a point falls in the picture, as fractions from the top left, or null when it is behind
    // the camera. The same matrix the frame was drawn with, so a crop taken off it is exact.
    private static double[]? Screen(double3 pointEcl)
    {
        if (Program.GetMainCamera() is not { } camera) return null;

        double4 clip = camera.EgoToClipDouble(pointEcl - camera.PositionEcl);
        if (!(clip.W > 1e-6)) return null;

        return [Math.Round((clip.X / clip.W * 0.5) + 0.5, 4), Math.Round((clip.Y / clip.W * 0.5) + 0.5, 4)];
    }
}
