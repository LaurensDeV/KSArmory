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
internal sealed class Bridge
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
         "KSArmoryFireLightCompute"];

    public Bridge(Config config, Func<Vehicle?, WeaponSystem?> systemFor)
    {
        _config = config;
        _systemFor = systemFor;
    }

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
            ["thin"] = Enumerable.Range(0, NuclearClouds.Count).Count(NuclearClouds.IsThin),
            ["sky_wanted"] = CloudPass.SkyWanted,
            ["sky_drawn"] = CloudPass.SkyDrawn,
            ["camera_held"] = _pose is not null,
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
        if (KsaWorld.ControlledVehicle is not { } craft) return Failed("no craft is being flown");
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
