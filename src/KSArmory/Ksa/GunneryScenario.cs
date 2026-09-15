using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A gun shooting at drones one after another, with every shell it fires scored where it burst.
///
/// <para>The engagement scenarios end on the first detonation, which says a gun can hit and nothing
/// about how well — and a flak gun's first shell is rarely its best. This keeps shooting, crosses each
/// drone past the mount rather than diving it onto it, and reports each burst's distance from the
/// target, its range and its fuse time, then the spread of them against the shell's lethal radius.</para>
///
/// <para>Or at the ground: <c>ground</c> designates a place that far out and scores where each shell
/// comes down against it, short or long — the check on a gun laid to land there rather than along the
/// line of sight to it. Or at a craft: <c>craft</c> designates the nearest one and scores whether each
/// shell struck it, which is the check that nothing near the mount can be flown through.</para>
///
/// <para>A drone is judged with its pieces. A burst that breaks parts off one leaves several craft
/// named after it, all still flying and all still engaged, so a drone is over when none of them is
/// left or its pass is — and whatever is left then is taken out of the world before the next one
/// comes, or its shells would be scored against the wrong drone.</para>
/// </summary>
internal sealed class GunneryScenario
{
    // How long after its closest approach a drone is given before it is taken out of the world: long
    // enough for the shells fired at it on the way past to arrive.
    private const double PassSeconds = 15.0;

    // Between one drone going and the next arriving, so no shell fired at the last is scored against
    // the next.
    private const double GapSeconds = 3.0;

    // Longest a shell fired at the ground may be in the air: the 5"/54's whole life, and some.
    private const double ShellFlightSeconds = 125.0;

    /// <summary>
    /// What to fly: <c>drones,profile,seconds,speed,miss,spin,burn</c>, every field optional. The
    /// <c>ground</c> profile fires <c>drones</c> shells at the ground <c>miss</c> metres out instead, and
    /// <c>craft</c> at the nearest other craft.
    /// </summary>
    /// <param name="SpinDegPerSecond">How fast each drone leaves tumbling, so its drag area and thrust turn under the lead.</param>
    /// <param name="Burn">Whether each drone flies with its engine lit, so it is not slowing while its drag is not zero.</param>
    /// <param name="Ground">Shoot at a place on the ground rather than at drones.</param>
    /// <param name="AtCraft">Shoot at the nearest other craft rather than at drones.</param>
    /// <param name="PlaceCraftMetres">
    /// With <c>craft</c>, set the nearest craft down this far out along the drones' bearing before shooting
    /// at it. Zero shoots at it where it stands.
    /// </param>
    public readonly record struct Request(int Drones, TestTarget.Profile Profile, double Seconds,
                                          double Speed, double MissMetres, double SpinDegPerSecond, bool Burn,
                                          bool Ground = false, bool AtCraft = false, double PlaceCraftMetres = 0.0)
    {
        public static Request Default => new(4, TestTarget.Profile.PassingBy, 30.0, 250.0, 3000.0, 0.0, false);

        /// <summary>Wall clock the whole run may take, the game's own start included.</summary>
        public double BudgetSeconds => 90.0 + (PlaceCraftMetres > 0.0 ? PlaceSettleSeconds + 10.0 : 0.0)
                                       + (Drones * (Ground || AtCraft ? GapSeconds + ShellFlightSeconds
                                                                      : Seconds + PassSeconds + GapSeconds + 20.0));

        public static bool TryParse(string text, out Request request, out string trouble)
        {
            request = Default;
            trouble = string.Empty;

            string[] f = text.Split(',', StringSplitOptions.TrimEntries);
            string At(int i) => i < f.Length ? f[i] : string.Empty;

            int drones = request.Drones;
            if (At(0).Length > 0 && (!int.TryParse(At(0), out drones) || drones < 1))
            {
                trouble = $"'{At(0)}' is not a number of drones";
                return false;
            }

            TestTarget.Profile profile = request.Profile;
            bool ground = false;
            bool craft = false;
            if (At(1).Length > 0)
            {
                switch (At(1))
                {
                    case "passing": profile = TestTarget.Profile.PassingBy; break;
                    case "overhead": profile = TestTarget.Profile.Overhead; break;
                    case "head-on": profile = TestTarget.Profile.HeadOn; break;
                    case "ground": ground = true; break;
                    case "craft": craft = true; break;
                    default:
                        trouble = $"'{At(1)}' is not passing, overhead, head-on, ground or craft";
                        return false;
                }
            }

            if (!Positive(At(2), request.Seconds, out double seconds, ref trouble)
                || !Positive(At(3), request.Speed, out double speed, ref trouble)
                || !Positive(At(4), request.MissMetres, out double miss, ref trouble))
            {
                return false;
            }

            double spin = request.SpinDegPerSecond;
            if (At(5).Length > 0
                && (!double.TryParse(At(5), System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out spin) || !(spin >= 0.0)))
            {
                trouble = $"'{At(5)}' is not a spin in degrees a second";
                return false;
            }

            bool burn = request.Burn;
            if (At(6).Length > 0)
            {
                switch (At(6))
                {
                    case "burn": burn = true; break;
                    case "coast": burn = false; break;
                    default:
                        trouble = $"'{At(6)}' is not burn or coast";
                        return false;
                }
            }

            // At a craft the distance is where to set it down, and only when one was asked for.
            double place = craft && At(4).Length > 0 ? miss : 0.0;

            request = new Request(drones, profile, seconds, speed, miss, spin, burn, ground, craft, place);
            return true;
        }

        private static bool Positive(string text, double fallback, out double value, ref string trouble)
        {
            value = fallback;
            if (text.Length == 0) return true;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0.0)
            {
                return true;
            }

            trouble = $"'{text}' is not a positive number";
            return false;
        }

        public string Describe()
            => AtCraft
                ? $"{Drones} shell(s) at the nearest craft"
                  + (PlaceCraftMetres > 0.0 ? $", set down {PlaceCraftMetres / 1000.0:F1} km out" : string.Empty)
                : Ground
                ? $"{Drones} shell(s) at the ground {MissMetres / 1000.0:F1} km out"
                : $"{Drones} drone(s) {Profile},{Speed:F0} m/s, {Seconds:F0} s out ({Speed * Seconds / 1000.0:F1} km), "
               + $"passing {MissMetres:F0} m off"
               + (SpinDegPerSecond > 0.0 ? $", tumbling at {SpinDegPerSecond:F0} deg/s" : string.Empty)
               + (Burn ? ", engine lit" : string.Empty);
    }

    // Off every body axis, so the tumble turns the airflow across all three faces rather than about one.
    private static readonly double3 SpinAxis = Vec.Unit(new double3(0.3, 1.0, 0.5));

    private readonly Request _request;
    private readonly Action<string> _report;
    private readonly Action<IProjectile> _onRoundEnded;

    private WeaponSystem? _wired;
    private double _lethal = double.NaN;

    private Vehicle? _drone;
    private string _droneName = string.Empty;
    private bool _seenPieces;
    private double _nearest = double.PositiveInfinity;
    private readonly List<Vehicle> _pieces = [];

    private int _spawned;
    private int _hit;
    private double _sinceSpawn;
    private double _gap;

    private readonly List<(double RangeKm, double Miss)> _bursts = [];
    private int _onProximity;
    private int _expired;
    private int _unscored;

    public GunneryScenario(Request request, Action<string> report)
    {
        _request = request;
        _report = report;
        _onRoundEnded = OnRoundEnded;
    }

    /// <summary>One frame. Null while the run goes on, the verdict once it is over.</summary>
    public string? Update(WeaponSystems.Entry entry, double dt)
    {
        WeaponSystem gun = entry.Battery;

        if (gun.Profile.GunMunition is not { } munition) return $"FAIL {gun.Profile.DisplayName} carries no gun";

        if (!ReferenceEquals(_wired, gun))
        {
            Release();
            gun.RoundEnded = _onRoundEnded;
            _wired = gun;
            _lethal = Warhead.LethalRadius(Catalogue.MunitionNamed(munition).ChargeKg);
        }

        if (_request.Ground || _request.AtCraft) return UpdateGround(gun, dt);

        if (_drone is not null)
        {
            _sinceSpawn += dt;

            // Held down every frame: the throttle is a key the mod holds, not a setting it makes.
            if (_request.Burn) VehicleCommand.DriveThrottle(_drone, 1.0);

            SampleDrone(gun.Platform!, dt);
            CollectPieces();
            if (_pieces.Count > 0) _seenPieces = true;

            // A drone that has not reached the world yet has no pieces either, so none only counts
            // once there were some.
            bool gone = _seenPieces && _pieces.Count == 0;
            if (!gone && _sinceSpawn < _request.Seconds + PassSeconds) return null;

            if (_nearest <= _lethal) _hit++;

            _report($"drone {_spawned}: nearest burst {(double.IsFinite(_nearest) ? $"{_nearest:F1} m" : "none")}, "
                    + (gone ? "nothing of it left flying"
                            : $"{_pieces.Count} piece(s) still flying, taken out of the world"));

            foreach (Vehicle piece in _pieces) KsaWorld.Remove(piece);

            _drone = null;
            _gap = 0.0;
        }

        // Shells still in the air were fired at the drone just gone, and are scored as its.
        if (gun.Rounds.Count > 0) return null;

        _gap += dt;
        if (_gap < GapSeconds) return null;

        if (_spawned >= _request.Drones) return Verdict(gun);

        _drone = TestTarget.Spawn(gun.Platform!, _request.Profile, _request.Seconds, _request.Speed,
                                  _request.MissMetres, "Gemini7",
                                  SpinAxis * double.DegreesToRadians(_request.SpinDegPerSecond));
        if (_drone is null) return "FAIL could not spawn a target";
        if (_request.Burn) VehicleCommand.SetEngine(_drone, true);

        _spawned++;
        _droneName = KsaWorld.DisplayName(_drone);
        _seenPieces = false;
        _haveVelocity = false;
        _lastShape = null;
        _nearest = double.PositiveInfinity;
        _sinceSpawn = 0.0;
        _report($"drone {_spawned} of {_request.Drones} away as '{_droneName}', {gun.GunShotsFired} shell(s) fired so far");
        return null;
    }

    public void Release()
    {
        if (_wired is not null && _wired.RoundEnded == _onRoundEnded) _wired.RoundEnded = null;
        _wired = null;
    }

    private const double SampleEverySeconds = 0.5;

    private double3 _lastVelocity;
    private double _sampleSeconds;
    private bool _haveVelocity;

    // What the drone is doing to its velocity, twice a second, as the lead models it: slowing along
    // the airflow read as a drag coefficient, and whatever pushes it across that. The lead holds the
    // coefficient and the push as they are at the shot, so a miss that is neither the gun nor the
    // shell is one of those two changing. Measured off the accelerometer the lead reads and off the
    // velocity itself, because the two can disagree.
    private void SampleDrone(Vehicle platform, double dt)
    {
        if (_drone is null || !KsaWorld.IsAlive(_drone)) return;

        double3 velocity = KsaWorld.VelocityEcl(_drone);
        if (!_haveVelocity)
        {
            _lastVelocity = velocity;
            _sampleSeconds = 0.0;
            _haveVelocity = true;
            return;
        }

        _sampleSeconds += dt;
        if (_sampleSeconds < SampleEverySeconds) return;

        double3 position = KsaWorld.PositionEcl(_drone);
        double3 pull = KsaWorld.GravityAt(platform, position);
        double3 air = velocity - KsaWorld.GroundVelocityAt(platform, position);
        double density = KsaWorld.MediumDensityRatioAt(platform, position);

        string accelerometer = Split(KsaWorld.AccelerationEcl(_drone) - pull, air, pull, density);
        string flown = Split(((velocity - _lastVelocity) * (1.0 / _sampleSeconds)) - pull, air, pull, density);

        // The engine's drag area is a function of where the air meets the body, not of airspeed, so the
        // airflow in the drone's own frame is what a coefficient that drifts has to be read against.
        double3 flowBody = Vec.Unit(air).Transform(doubleQuat.Conjugate(_drone.Body2Cce));

        // The drag area the lead flies, for that airflow: if drag is only this, the measured slowing over
        // v² ρ, times the mass, over the area stays constant through the pass. And the area the shape read
        // at the last sample said it would have now, carried through the turn: the check on the tumble.
        DragShape? shape = KsaWorld.DragShapeOf(_drone);
        double area = shape?.AreaFacing(air) ?? double.NaN;
        double predicted = _lastShape?.AreaFacing(air, _sampleSeconds) ?? double.NaN;
        double airspeed = Vec.Len(air);
        double slowing = -Vec.Dot(KsaWorld.AccelerationEcl(_drone) - pull, Vec.Unit(air));
        double perArea = shape is { } s && area > 0.0 && density > 1e-6
            ? slowing * s.Mass / (airspeed * airspeed * density * area)
            : double.NaN;
        double turning = shape is { } t ? double.RadiansToDegrees(Vec.Len(t.BodyRates)) : double.NaN;

        Log.Debug(() => $"  drone {_spawned} at {_sinceSpawn:F1} s: {airspeed:F0} m/s, density {density:F3}; "
                        + $"accelerometer {accelerometer}; velocity {flown}; "
                        + $"airflow in body {flowBody.X:F3},{flowBody.Y:F3},{flowBody.Z:F3}; turning {turning:F1} deg/s; "
                        + $"drag area {area:F3} m2 against {predicted:F3} predicted, slowing per area {perArea:F4}");

        _lastVelocity = velocity;
        _lastShape = shape;
        _sampleSeconds = 0.0;
    }

    private DragShape? _lastShape;

    // A felt acceleration as the lead splits it: along the airflow, as a drag coefficient where it is
    // slowing, and across it, up and sideways.
    private static string Split(double3 felt, double3 air, double3 pull, double density)
    {
        double speed = Vec.Len(air);
        if (!(speed > 1.0) || !Vec.IsFinite(felt)) return "n/a";

        double3 along = Vec.Unit(air);
        double3 up = Vec.Unit(Vec.RejectFrom(-pull, along));

        double alongAccel = Vec.Dot(felt, along);
        double3 across = felt - (along * alongAccel);
        double acrossUp = Vec.Dot(across, up);
        double acrossSide = Vec.Len(across - (up * acrossUp));
        double k = density > 1e-6 ? -alongAccel / (speed * speed * density) : double.NaN;

        return $"along {alongAccel:F2} (k {k:E2}), up {acrossUp:F2}, side {acrossSide:F2}";
    }

    // The drone and everything a burst broke off it: a piece is named after what it came off.
    private void CollectPieces()
    {
        _pieces.Clear();

        foreach (Vehicle vehicle in KsaWorld.Vehicles)
        {
            if (!KsaWorld.IsAlive(vehicle)) continue;

            string name = KsaWorld.DisplayName(vehicle);
            if (name == _droneName || name.StartsWith(_droneName + "_", StringComparison.Ordinal)) _pieces.Add(vehicle);
        }
    }

    private void OnRoundEnded(IProjectile round)
    {
        if (round is not Slug shell) return;

        // What a chase riding the shell shows as it lands, which no line in the log can say.
        if (_request.Ground || _request.AtCraft) _report("CAPTURE landing");

        if (_request.AtCraft)
        {
            ScoreStrike(shell);
            return;
        }

        if (_request.Ground)
        {
            ScoreLanding(shell);
            return;
        }

        if (shell.State == RoundState.Expired)
        {
            _expired++;
            _report($"shell expired after {shell.Age:F1} s");
            return;
        }

        if (!double.IsFinite(shell.MissDistance))
        {
            _unscored++;
            _report($"shell burst after {shell.Age:F1} s; what it was aimed at broke up before it arrived");
            return;
        }

        double rangeKm = Vec.Len(shell.OffsetFromPlatform) / 1000.0;
        _bursts.Add((rangeKm, shell.MissDistance));
        _nearest = Math.Min(_nearest, shell.MissDistance);
        if (!shell.BurstOnTime) _onProximity++;

        _report($"burst {_bursts.Count}: drone {_spawned}, {rangeKm:F2} km out, "
                + (shell.BurstOnTime ? $"timed at {shell.FuseSeconds:F2} s" : $"on proximity after {shell.Age:F2} s")
                + $", {shell.MissDistance:F1} m from the target");
    }

    private bool _designated;
    private int _fired;
    private readonly List<(double Miss, double Along)> _landings = [];

    // A place on the ground that far out, and one shell at a time onto it, each off a lay that has
    // settled since the last came down.
    // Set down once, then left to settle: the engine builds the resting state over a few frames, and a
    // craft designated mid-teleport is not where it will be.
    private const double PlaceSettleSeconds = 8.0;
    private Vehicle? _placedCraft;
    private double _sincePlaced;

    // The nearest craft set down that far out along the drones' bearing, so a long shot at something on
    // the ground can be flown from a save that parks it beside the mount. True once it has settled.
    private bool PlaceCraft(Vehicle platform, double dt, out string? failed)
    {
        failed = null;

        if (_placedCraft is null)
        {
            if (NearestCraft(platform) is not { } craft)
            {
                failed = "FAIL found no other craft to set down";
                return false;
            }

            double3 guess = KsaWorld.PositionEcl(platform)
                            + (TestTarget.ApproachBearing(platform) * _request.PlaceCraftMetres);
            if (!KsaWorld.TryLatitudeLongitude(guess, out string body, out double lat, out double lon)
                || !KsaWorld.TryPlaceOnSurface(craft, body, lat, lon))
            {
                failed = $"FAIL could not set '{KsaWorld.DisplayName(craft)}' down "
                         + $"{_request.PlaceCraftMetres / 1000.0:F1} km out";
                return false;
            }

            _placedCraft = craft;
            _sincePlaced = 0.0;
            _report($"set '{KsaWorld.DisplayName(craft)}' down at {lat:F4}, {lon:F4}");
            return false;
        }

        _sincePlaced += dt;
        return _sincePlaced >= PlaceSettleSeconds;
    }

    private string? UpdateGround(WeaponSystem gun, double dt)
    {
        if (!_designated)
        {
            if (_request.PlaceCraftMetres > 0.0 && !PlaceCraft(gun.Platform!, dt, out string? failed)) return failed;

            if (!TryPlace(gun.Platform!, out Aimpoint place, out string what)) return $"FAIL found no {what}";

            gun.Designate(place, what);
            _designated = true;
            _report($"designated {what}");
            return null;
        }

        if (gun.Rounds.Count > 0)
        {
            _gap = 0.0;
            return null;
        }

        _gap += dt;
        if (_gap < GapSeconds) return null;

        // A craft that has been destroyed takes its designation with it, and leaves nothing to fire at.
        bool done = _fired >= _request.Drones || (_request.AtCraft && gun.Designation.Kind == AimpointKind.None);
        if (done) return _request.AtCraft ? CraftVerdict() : GroundVerdict();
        if (!gun.ReadyToFire) return null;

        if (gun.FireBurst())
        {
            _fired++;
            _gap = 0.0;
        }

        return null;
    }

    // Along the bearing the drones come in on, which the mount is known to traverse to, and dropped onto
    // the terrain there. Anchored to the body, so it keeps its place as the planet turns.
    private static bool TryGroundOut(Vehicle platform, double metres, out Aimpoint place)
    {
        place = Aimpoint.Nothing;

        double3 guess = KsaWorld.PositionEcl(platform) + (TestTarget.ApproachBearing(platform) * metres);
        if (!GroundTest.Shared.TryGround(guess, out double3 centre, out double radius)) return false;

        double3 ground = centre + (Vec.Unit(guess - centre) * radius);
        if (!KsaWorld.TryAnchorToGround(ground, out object? body, out double3 anchor)) return false;

        place = Aimpoint.OnGround(body!, anchor, ground, KsaWorld.GroundVelocityAt(platform, ground));
        return true;
    }

    // Where a shell came down against the designated place, re-read from the body at this instant and
    // carried back to the moment it struck, and how far of that is short or long.
    private void ScoreLanding(Slug shell)
    {
        if (shell.State == RoundState.Expired)
        {
            _expired++;
            _report($"shell expired after {shell.Age:F1} s without coming down");
            return;
        }

        Aimpoint place = _wired?.Designation ?? Aimpoint.Nothing;
        if (place.Kind != AimpointKind.Ground
            || !KsaWorld.TryGroundAnchorEcl(place.Handle, place.Anchor, out double3 at, out double3 velocity))
        {
            _unscored++;
            _report($"shell came down after {shell.Age:F1} s with no designated ground to score it against");
            return;
        }

        double3 aim = at + (velocity * shell.DetonationElapsedInFrame);
        double3 mount = shell.PositionEcl - shell.OffsetFromPlatform;
        double3 miss = shell.PositionEcl - aim;
        double along = Vec.Dot(miss, Vec.Unit(aim - mount));

        _landings.Add((Vec.Len(miss), along));
        _report($"shell {_landings.Count}: came down {Vec.Len(miss):F1} m from the point, "
                + $"{Math.Abs(along):F1} m {(along < 0.0 ? "short" : "long")}, after {shell.Age:F1} s"
                + (shell.HitGround ? string.Empty : ", in the air"));
    }

    private string _craftName = string.Empty;
    private int _struck;

    // What this run shoots at: the nearest other craft, or the ground that far out.
    private bool TryPlace(Vehicle platform, out Aimpoint place, out string what)
    {
        if (!_request.AtCraft)
        {
            what = $"the ground {_request.MissMetres / 1000.0:F1} km out";
            return TryGroundOut(platform, _request.MissMetres, out place);
        }

        place = Aimpoint.Nothing;
        what = "other craft";

        // The one set down, not whatever is nearest now: moving it out can leave another craft closer.
        if ((_placedCraft ?? NearestCraft(platform)) is not { } craft) return false;

        double3 at = KsaWorld.PositionEcl(craft);
        _craftName = KsaWorld.DisplayName(craft);
        what = $"'{_craftName}' {Vec.Len(at - KsaWorld.PositionEcl(platform)):F0} m away";
        place = Aimpoint.OnVehicle(craft, at, KsaWorld.VelocityEcl(craft), KsaWorld.MeanRadius(craft));
        return true;
    }

    private static Vehicle? NearestCraft(Vehicle platform)
    {
        double3 from = KsaWorld.PositionEcl(platform);
        Vehicle? nearest = null;
        double best = double.MaxValue;

        foreach (Vehicle vehicle in KsaWorld.Vehicles)
        {
            if (ReferenceEquals(vehicle, platform) || !KsaWorld.IsAlive(vehicle)) continue;

            double range = Vec.Len(KsaWorld.PositionEcl(vehicle) - from);
            if (range >= best) continue;

            best = range;
            nearest = vehicle;
        }

        return nearest;
    }

    // Whether a shell ended against the designated craft or a piece of it, or somewhere else.
    private void ScoreStrike(Slug shell)
    {
        string name = shell.StruckBody is Vehicle struck ? KsaWorld.DisplayName(struck) : string.Empty;
        bool onCraft = name.Length > 0
                       && (name == _craftName || name.StartsWith(_craftName + "_", StringComparison.Ordinal));
        double range = Vec.Len(shell.OffsetFromPlatform);

        if (onCraft) _struck++;

        _report($"shell {_fired}: "
                + (onCraft ? $"struck '{name}' {range:F0} m out after {shell.Age:F2} s"
                   : shell.State == RoundState.Expired ? $"expired after {shell.Age:F1} s"
                   : shell.HitGround ? $"came down on the ground {range:F0} m out"
                   : $"burst {range:F0} m out without touching the craft"));
    }

    private string CraftVerdict()
    {
        Release();

        return (_fired > 0 && _struck == _fired ? "PASS " : "FAIL ")
               + $"{_struck} of {_fired} shell(s) struck '{_craftName}'";
    }

    private string GroundVerdict()
    {
        Release();

        string tally = $"{_fired} shell(s) fired at the ground {_request.MissMetres / 1000.0:F1} km out, "
                       + $"{_expired} expired, {_unscored} unscored";
        if (_landings.Count == 0) return $"FAIL no shell came down to score; {tally}";

        List<double> misses = [.. _landings.Select(l => l.Miss).Order()];
        List<double> alongs = [.. _landings.Select(l => l.Along).Order()];
        double median = misses[misses.Count / 2];
        double along = alongs[alongs.Count / 2];

        return (median <= _lethal ? "PASS " : "FAIL ")
               + $"{misses.Count} came down: median {median:F1} m from the point, worst {misses[^1]:F1} m, "
               + $"median {Math.Abs(along):F1} m {(along < 0.0 ? "short" : "long")}; {tally}";
    }

    private string Verdict(WeaponSystem gun)
    {
        Release();

        string tally = $"{_hit} of {_spawned} drones had a burst inside the {_lethal:F1} m lethal radius, "
                       + $"{gun.GunShotsFired} shells fired, {_expired} expired, {_unscored} aimed at pieces gone before they arrived";

        if (_bursts.Count == 0) return $"FAIL no shell burst with a target to score against; {tally}";

        List<double> all = [.. _bursts.Select(b => b.Miss).Order()];
        double median = all[all.Count / 2];
        double ninety = all[Math.Min(all.Count - 1, (int)Math.Ceiling(0.9 * all.Count) - 1)];
        int inside = all.Count(m => m <= _lethal);

        return (median <= _lethal ? "PASS " : "FAIL ")
               + $"{all.Count} bursts ({_onProximity} on proximity): median {median:F1} m, 90% within {ninety:F1} m, "
               + $"worst {all[^1]:F1} m, {inside} inside the lethal radius; by range {Band(0.0, 4.0)}, "
               + $"{Band(4.0, 6.0)}, {Band(6.0, double.PositiveInfinity)}; {tally}";
    }

    // The misses in one band of range, because a gun's accuracy is a function of how far it is shooting
    // and a single median over a whole pass hides which end is wrong.
    private string Band(double fromKm, double toKm)
    {
        List<double> misses = [.. _bursts.Where(b => b.RangeKm >= fromKm && b.RangeKm < toKm).Select(b => b.Miss).Order()];
        string label = double.IsPositiveInfinity(toKm) ? $"beyond {fromKm:F0} km" : $"{fromKm:F0}-{toKm:F0} km";

        return misses.Count == 0 ? $"{label} none" : $"{label} {misses.Count} at median {misses[misses.Count / 2]:F1} m";
    }
}
