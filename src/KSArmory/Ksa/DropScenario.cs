using System.Globalization;
using System.Linq;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Flies a craft carrying a released store off the ground, lets the store go, and says where it
/// landed against where the sight said it would.
///
/// <para>A miss has three sources and one flight separates them. At the release the sight is flown
/// twice: once as the ring is, off the rack, and once off the state the round actually left with.
/// Those two differ by what the sight assumes about the release; the second against the landing is
/// the flight itself. A guided store is designated onto the ring as it goes, which is what a player
/// does, so its miss from the ring is the tail kit's.</para>
/// </summary>
internal sealed class DropScenario
{
    /// <summary>
    /// <c>[metres above ground][,degrees off vertical][,guided|dumb][,seconds until the craft is
    /// destroyed][,warp]</c>, every field optional.
    ///
    /// <para><c>warp</c> is a factor to set once the store is away, or <c>auto</c> for KSA's own
    /// warp-to-a-time. They are not the same test: the engine <em>refuses</em> a speed change while
    /// an auto-warp runs, and <see cref="WarpPolicy"/> has to stand down for that refusal rather than
    /// abandon the store over it.</para>
    ///
    /// <para><c>again</c> is <c>&lt;seconds&gt;@&lt;metres&gt;</c> — send the store somewhere else
    /// that long after the release, that far north of the ring — or <c>&lt;seconds&gt;@clear</c> to
    /// drop the designation instead. A store sent somewhere is scored against the new place; one
    /// whose designation is cleared is still scored against the old, because clearing is not how a
    /// store is recalled.</para>
    /// </summary>
    public readonly record struct Request(double ReleaseAglMetres, double PitchDeg, bool Guided,
                                          double KillAfterSeconds, double WarpFactor, bool AutoWarp,
                                          double AgainAfterSeconds, double AgainOffsetMetres)
    {
        public static bool TryParse(string text, out Request request, out string trouble)
        {
            request = new Request(1000.0, 0.0, Guided: true, double.NaN, double.NaN, AutoWarp: false,
                                  double.NaN, double.NaN);
            trouble = string.Empty;

            string[] fields = text.Split(',', StringSplitOptions.TrimEntries);
            double agl = request.ReleaseAglMetres;
            double pitch = request.PitchDeg;
            double kill = request.KillAfterSeconds;
            double warp = request.WarpFactor;
            bool auto = false;
            bool guided = request.Guided;
            double againAt = double.NaN;
            double againBy = double.NaN;

            if (Given(0) && !TryNumber(fields[0], out agl)) trouble = $"'{fields[0]}' is not a height";
            else if (Given(1) && !TryNumber(fields[1], out pitch)) trouble = $"'{fields[1]}' is not an angle";
            else if (Given(2) && fields[2] is not ("guided" or "dumb")) trouble = $"'{fields[2]}' is neither guided nor dumb";
            else if (Given(3) && !TryNumber(fields[3], out kill)) trouble = $"'{fields[3]}' is not a time";
            else if (Given(4) && fields[4] != "auto" && !TryNumber(fields[4], out warp)) trouble = $"'{fields[4]}' is neither a warp factor nor 'auto'";
            else if (agl <= 0.0) trouble = "the release height has to be above the ground";
            else if (pitch is < 0.0 or >= 90.0) trouble = "the pitch is degrees off vertical, 0 to 90";
            else if (Given(4) && fields[4] != "auto" && warp < 1.0) trouble = "the warp factor has to be at least 1";
            else if (Given(5) && !TryAgain(fields[5], out againAt, out againBy)) trouble = $"'{fields[5]}' is not <seconds>@<metres> or <seconds>@clear";

            if (trouble.Length > 0) return false;

            if (Given(2)) guided = fields[2] == "guided";
            if (Given(4) && fields[4] == "auto") auto = true;

            request = new Request(agl, pitch, guided, kill, warp, auto, againAt, againBy);
            return true;

            bool Given(int i) => fields.Length > i && fields[i].Length > 0;

            // NaN metres means clear rather than re-send, which is a different question: whether a
            // store already steering keeps its aim when the installation stops pointing at anything.
            static bool TryAgain(string text, out double at, out double by)
            {
                at = double.NaN;
                by = double.NaN;

                string[] halves = text.Split('@');
                if (halves.Length != 2 || !TryNumber(halves[0], out at) || at < 0.0) return false;
                if (halves[1] == "clear") return true;

                return TryNumber(halves[1], out by);
            }
        }

        public string Describe()
            => $"release at {ReleaseAglMetres:F0} m, {PitchDeg:F0} deg off vertical, "
               + (Guided ? "guided onto the ring" : "unguided")
               + (double.IsFinite(KillAfterSeconds)
                      ? $", craft destroyed {KillAfterSeconds:F1} s after the release"
                      : "")
               + (AutoWarp ? ", then KSA's own warp-to-a-time"
                           : double.IsFinite(WarpFactor) ? $", then {WarpFactor:F0}x timewarp" : "")
               + (double.IsFinite(AgainAfterSeconds)
                      ? double.IsFinite(AgainOffsetMetres)
                            ? $", sent {AgainOffsetMetres:F0} m north {AgainAfterSeconds:F0} s after the release"
                            : $", designation cleared {AgainAfterSeconds:F0} s after the release"
                      : "");

        private static bool TryNumber(string text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value);
    }

    private enum Phase
    {
        WaitingForWorld,
        Climbing,
        Falling,
        Lingering,
    }

    // Simulated seconds a store has to have been on a craft before it is flown: a craft is crewed a
    // few frames after a load, and its parts settle onto the ground after that.
    private const double SettleSeconds = 3.0;

    private const double PitchOverAglMetres = 150.0;
    private const double PitchRateDegPerSecond = 6.0;

    private const double LiftOffSeconds = 20.0;
    private const double ClimbBudgetSeconds = 150.0;
    private const double FallBudgetSeconds = 180.0;

    // Wall clock after the burst, sized off the hold so the chase's hand-back reaches the log before
    // the harness closes the game: a nuclear burst is watched for as long as something moves.
    private double LingerSeconds => CaptureAges() is { Length: > 0 } ages ? ages[^1] + 4.0
                                                                        : WatchSeconds + 4.0;

    // ...and it ends when the last capture is in, plus a moment for the screenshot to be written.
    // The wall-clock bound is the backstop for a drop with no captures, and for a paused run.
    private bool LingerIsDone
        => (double.IsFinite(_allCapturedAt) && _lingered >= _allCapturedAt + 4.0)
           || _lingered >= LingerSeconds + _stillHeld;

    private double _allCapturedAt = double.NaN;

    // Whether this burst has a fireball at all. Nothing to do with air: with none it still flashes,
    // briefly, as the device's own vapour.
    private bool BurstIsNuclear
        => (_watchTheCloud || ChaseTheCloud) && _round is { } r && r.Munition.ChargeKg >= MushroomCloud.ThresholdKg;

    // Whether a column is standing over it, as against thrown ground. Only one of them has a life
    // past its own rise to photograph.
    private bool CloudStands
        => BurstIsNuclear && _body is { } b && KsaWorld.HasAtmosphere(b);

    // How long the burst leaves something moving: a cloud's rise where there is air, the ejecta's
    // flight where there is not, so the capture fractions land on either kind of body. Latched at
    // the first ask: re-read off a landed round's frozen ecliptic point, the gravity there creeps.
    private double _watch = double.NaN;

    private double WatchSeconds
    {
        get
        {
            if (double.IsFinite(_watch)) return _watch;

            _watch = _round is { } round
                         ? BurstEjecta.LingerSeconds(round.PositionEcl, null, round.Munition.ChargeKg)
                         : ChaseView.MinLingerSeconds;

            return _watch;
        }
    }

    private const double BarMetres = 30.0;
    private const double ProgressEverySeconds = 2.0;

    private readonly Request _request;
    private readonly Action<string> _report;
    private readonly Func<WeaponSystem, BombSightOverlay> _sightFor;

    // A load takes frames, and the scene it replaces can carry a store of its own. Flying that one
    // is a run about the wrong craft, which reads exactly like a run about the right one.
    private readonly HashSet<Vehicle> _fromBeforeTheLoad = [];
    private readonly List<Vehicle> _census = [];

    private Phase _phase = Phase.WaitingForWorld;
    private double _sim;
    private double _foundAt = double.NaN;
    private double _stagedAt;
    private double _saidAt = double.NegativeInfinity;
    private double _lingered;

    // When a craft set down at a site stopped being found, and how long that may last.
    private double _lostSince = double.NaN;
    private const double LostAfterSiteSeconds = 30.0;

    // How old the burst is on the world's own clock, which is not _lingered: the cloud is advanced on
    // the simulated step, so the two agree only at 1x.
    private double _burstAge;

    private WeaponSystem? _system;
    private Vehicle? _craft;
    private string _craftName = string.Empty;
    private double _pitchDeg;

    private IProjectile? _round;
    private Celestial? _body;
    private double _releasedAt;
    private double3 _ringAnchor;
    private double3 _flownAnchor;
    private bool _haveRing;
    private bool _haveFlown;
    private bool _killed;
    private bool _againDone;
    private object? _ringBody;
    private double3 _againAnchor;
    private bool _haveAgain;

    // What the ring claimed at the moment of the send, and where the store would have come down
    // untouched. A send the kit cannot reach is scored against these rather than against the aim:
    // see WalkedFarEnough.
    private bool _sentWasInReach;
    private double _sentReachMetres;
    private double3 _sentImpactAnchor;
    private bool _haveSentImpact;
    private bool _warpSet;
    private double _warpObserved;
    private string _verdict = string.Empty;

    public DropScenario(Request request, Action<string> report,
                        Func<WeaponSystem, BombSightOverlay> sightFor, bool watchTheCloud = false)
    {
        _request = request;
        _report = report;
        _sightFor = sightFor;
        _watchTheCloud = watchTheCloud;
    }

    // Whether this run exists to be looked at rather than scored. The chase stays on: it is the only
    // thing holding the view on the burst, and without it the view follows the climbing craft.
    private readonly bool _watchTheCloud;

    /// <summary>
    /// Where to set the craft down before anything is dropped; null drops from wherever the save left
    /// it. For a burst on a body with no air, which no save is on.
    /// </summary>
    public (string Body, double LatitudeDeg, double LongitudeDeg)? Site { get; init; }

    private const double PlaceSettleSeconds = 8.0;

    private bool _siteRequested;
    private bool _siteSettled;
    private double _sinceSite;

    // The craft set down somewhere no save puts it, and left to settle as a placed craft is. True
    // once it has, when the body it ended up on is said once.
    private bool PlaceCraft(Vehicle craft, double dt, out string? failed)
    {
        failed = null;
        if (Site is not { } site || _siteSettled) return true;

        if (!_siteRequested)
        {
            if (!KsaWorld.TryPlaceOnSurface(craft, site.Body, site.LatitudeDeg, site.LongitudeDeg))
            {
                failed = $"FAIL could not set the craft down on {site.Body} at "
                         + $"{site.LatitudeDeg:F2}, {site.LongitudeDeg:F2}";
                return false;
            }

            _siteRequested = true;
            _report($"set {_craftName} down on {site.Body} at "
                    + $"{site.LatitudeDeg:F2}, {site.LongitudeDeg:F2}");
            return false;
        }

        _sinceSite += dt;
        if (_sinceSite < PlaceSettleSeconds) return false;

        _siteSettled = true;

        if (KsaWorld.ParentBody(craft) is not { } body || body.Id != site.Body)
        {
            failed = $"FAIL the craft is not on {site.Body} after being set down there";
            return false;
        }

        // The medium beside the air: the store obeys what it stands in, and below the mean sphere a
        // body with no air can still read as ocean.
        double3 at = KsaWorld.PositionEcl(craft);
        _report($"on {body.Id}: "
                + (KsaWorld.HasAtmosphere(body)
                       ? "it has air, so a burst grows a column"
                       : "no air, so a burst throws ground instead of growing a column")
                + $"; standing {KsaWorld.HeightAboveTerrain(body, at):F0} m over the ground and "
                + $"{Vec.Len(at - body.GetPositionEcl()) - body.MeanRadius:F0} m over the mean sphere, "
                + $"in {KsaWorld.MediumDensityRatioAt(craft, at):G3} of the reference "
                + $"({KsaWorld.MediumDiagnosis(body, at)})");
        return true;
    }

    /// <summary>
    /// What the world runs at while the burst is watched, from the landing on; the fall's warp is
    /// handed back the instant the store lands.
    /// </summary>
    public double LingerSpeed { get; init; } = 1.0;

    /// <summary>
    /// A burst age to freeze the world at and photograph three times, or negative for none. With
    /// nothing moving, whatever differs between the three is the renderer's own frame-to-frame
    /// noise: the weather's jittered sampling, or grain in the march.
    /// </summary>
    public double StillAt { get; init; } = -1.0;

    private const int StillShots = 3;
    private const double StillSpacingSeconds = 0.5;

    private int _stillTaken = -1;
    private double _stillClock;
    private double _stillHeld;

    /// <summary>
    /// Whether to set off a second burst <see cref="SecondBurstMetres"/> from the first once the store
    /// lands, so <c>CloudPass</c> is seen drawing every standing cloud rather than the newest.
    /// </summary>
    public bool SecondBurst { get; init; }

    /// <summary>
    /// The scoring drop, chased, with its cloud drawn and photographed: what a player sees of a
    /// burst, where the pinned watch is what the pass is measured from.
    /// </summary>
    public bool ChaseTheCloud { get; init; }

    /// <summary>What to multiply that second burst's yield by, so one run carries two sizes.</summary>
    public double SecondBurstYield { get; init; } = 1.0;

    // Past the merge, which reaches only the combined fireball (55 m at the B61's yield), and inside
    // the pinned watch's frame, about 2.2 km of ground.
    private const double SecondBurstMetres = 1000.0;

    private bool _secondBurstDone;

    // Fired once the store is down, so the two clouds are a few frames apart in age rather than
    // one being born into a world the other has not reached yet.
    private void FireSecondBurst()
    {
        if (!SecondBurst || _secondBurstDone) return;
        if (_round is not { } round || _body is not { } body || _craft is not { } craft) return;
        if (!TryLocalFrame(craft, body, out _, out double3 east, out _, out _)) return;

        // Off the craft's live position: a dead round's PositionEcl is a frozen ecliptic point the
        // planet leaves behind at 29.8 km/s, about 477 m a frame at 1x.
        _secondBurstDone = true;

        double charge = round.Munition.ChargeKg * Math.Max(SecondBurstYield, 0.0);

        double3 at = KsaWorld.PositionEcl(craft) + (east * SecondBurstMetres);
        NuclearClouds.Begin(at, craft, charge);

        _report($"second burst {SecondBurstMetres / 1000.0:F1} km east of the first at "
                + $"{MushroomCloud.KilotonsFor(charge):F2} kt, so the pass has two clouds to draw; "
                + $"{NuclearClouds.Count} standing");
    }

    /// <summary>Which phase it is in, for a timeout to name.</summary>
    public string Where => _phase.ToString();

    /// <summary>Remembers every craft in the world, so none of them is flown once the save replaces it.</summary>
    public void NoteTheWorldBeforeTheLoad()
    {
        KsaWorld.CollectVehicles(_census);
        foreach (Vehicle v in _census) _fromBeforeTheLoad.Add(v);
    }

    /// <summary>Gives the craft back, however the run ends.</summary>
    public void Release()
    {
        if (_craft is { } craft && KsaWorld.IsAlive(craft)) AttitudeHook.Release(craft);
    }

    /// <summary>One frame. Null while the drop is still going, and the outcome once it is not.</summary>
    public string? Update(WeaponSystems roster, double dt, double playerStep)
    {
        _sim += dt;

        switch (_phase)
        {
            case Phase.WaitingForWorld:
                return Wait(roster, dt);

            case Phase.Climbing:
                return Climb(dt);

            case Phase.Falling:
                return Fall(dt);

            case Phase.Lingering:
                _lingered += playerStep;
                _burstAge += dt;

                // Pinned every frame while the cloud stands: the pose is the same in every run, so
                // what CloudPassCost reports is a number about the pass rather than about where
                // somebody left the camera.
                FireSecondBurst();

                if (_watchTheCloud && _craft is { } watched) CloudWatch.Update(watched);

                CaptureBurst();
                HoldStill(playerStep);
                TraceLinger();
                if (!LingerIsDone) return null;

                // Handed back here rather than left where it was: this one is the mod's own and a
                // scenario that walks off leaving the world at 20x has changed the session for
                // whatever runs next.
                if (LingerSpeed <= 0.0) KsaWorld.SetPaused(false);
                else if (LingerSpeed != 1.0) KsaWorld.SetSimulationSpeed(1.0);

                return _verdict;
        }

        return null;
    }

    // Fractions of the burst's watch: on a body with air, the rows of docs/NUCLEAR-EFFECT.md's
    // tracking table. 0.95, not 1.00, because the chase hands the view back at exactly the watch.
    private static readonly double[] CaptureFractions = [0.10, 0.30, 0.60, 0.95];

    // Those as ages, plus the flash and pulse before them and, for a column, the dissolve and the
    // ground after it, which are absolute ages: the fade starts well after the rise ends.
    private double[] CaptureAges()
    {
        double watch = WatchSeconds;
        if (!(watch > 0.0)) return [];

        double[] rise = [.. CaptureFractions.Select(f => f * watch)];
        if (!BurstIsNuclear || _round is not { } round) return rise;

        // The ball burns for under two seconds at the B61's yield, before the first rise fraction.
        double flash = MushroomCloud.FlashSeconds(MushroomCloud.KilotonsFor(round.Munition.ChargeKg));

        // Two in the luminous phase, since the whiteout peaks a tenth of the way in, and three across
        // the thermal pulse, whose signature is the minimum between its two maxima.
        double kt = MushroomCloud.KilotonsFor(round.Munition.ChargeKg);
        double pulseTrough = MushroomCloud.PulseTroughSeconds(round.Munition.ChargeKg);
        double pulsePeak = MushroomCloud.PulsePeakSeconds(kt);

        double[] withFlash =
        [
            MushroomCloud.PulseMinimumSeconds(kt) * 0.35,
            pulseTrough,
            pulsePeak,
            flash * 0.15,
            flash * 0.70,
            .. rise,
        ];

        // A chased run is photographed only while the chase holds the view, which is the rise.
        if (ChaseTheCloud) return withFlash;

        // The dissolve is the column's alone. Thrown ground has no life past its own arc.
        double[] withDissolve =
            CloudStands
                ? [.. withFlash, MushroomCloud.RiseSeconds + (MushroomCloud.StandSeconds * 0.85)]
                : withFlash;

        // And one past the cloud's whole life: the only frame that shows the ground still burned.
        return [.. withDissolve, MushroomCloud.LifeSeconds + 6.0];
    }

    private int _captured;
    private double _lingerSpeedSeen;
    private double _traced;

    // How often a run that is watching at something other than 1x says where the burst has got to.
    private const double TraceEverySeconds = 2.0;

    // The burst's age against the wall clock, only for a run not at 1x: paused, nothing else prints
    // between captures to tell a cloud that stopped ageing from a pass that died.
    private void TraceLinger()
    {
        if (LingerSpeed == 1.0) return;
        if (_lingered - _traced < TraceEverySeconds) return;

        _traced = _lingered;

        _report($"linger: burst {_burstAge:F1} s old after {_lingered:F1} s of wall clock, "
                + $"world {KsaWorld.SimulationSpeed:F2}x, {NuclearClouds.Count} cloud(s), "
                + $"{NuclearClouds.ScorchCount} mark(s)");
    }

    // Cued off the burst's simulated age, so a run at any speed photographs the same cloud; the
    // linger still ends on wall clock, since holding a camera is a viewing duration.
    private void CaptureBurst()
    {
        if (_round is not { } round) return;
        if (round.Munition.ChargeKg < MushroomCloud.ThresholdKg) return;

        double watch = WatchSeconds;
        if (!(watch > 0.0)) return;

        double[] ages = CaptureAges();

        while (_captured < ages.Length)
        {
            double age = ages[_captured];
            if (_burstAge < age) return;

            _captured++;
            if (_captured >= ages.Length) _allCapturedAt = _lingered;

            // The game's own framebuffer: tools/screenshot.sh needs the window in front.
            bool shot = KsaWorld.TryRequestScreenshot();

            // Tells a run that held its speed from one the engine refused.
            _lingerSpeedSeen = Math.Max(_lingerSpeedSeen, KsaWorld.SimulationSpeed);

            if (CloudPassCost.Report() is { Length: > 0 } cost) _report(cost);

            // Off the watch record, so the size is that of whatever stands there, column or dome.
            string drawn = NuclearClouds.TryWatch(out _, out _, out double radius, out double top, out _)
                               ? $"{radius / 1000.0:F2} km across, top {top / 1000.0:F2} km"
                               : "nothing standing";

            // Below the horizon a cloud is meant to be dark, which a picture alone cannot tell from a
            // lighting fault.
            string sun = "sun unknown";
            if (_body is { } lit && _round is { } r)
            {
                double deg = KsaWorld.SunElevationDeg(lit, r.PositionEcl);
                if (double.IsFinite(deg))
                {
                    sun = deg >= 0.0 ? $"sun {deg:F1} deg up" : $"sun {-deg:F1} deg BELOW the horizon";
                }
            }

            // The ball's glow at this age: close in the whiteout saturates, so the pulse captures are
            // readable only beside it.
            string burning = _round is { } burst
                                 ? $", glow {MushroomCloud.FlashAt(burst.Munition.ChargeKg, age).Glow:F0}"
                                 : string.Empty;

            _report($"{(shot ? "SHOT" : "CAPTURE")} burst at {age:F1} s "
                    + $"({age / watch:F2} of a {watch:F1} s watch), {drawn}, {sun}, "
                    + $"{NuclearClouds.ScorchCount} mark(s), "
                    + $"{KsaWorld.SimulationSpeed:F2}x after {_lingered:F1} s of wall clock"
                    + burning);
        }
    }

    private void HoldStill(double playerStep)
    {
        if (StillAt < 0.0 || _stillTaken >= StillShots || _burstAge < StillAt) return;

        if (_stillTaken < 0)
        {
            KsaWorld.SetPaused(true);
            _stillTaken = 0;
            _stillClock = 0.0;
            return;
        }

        _stillClock += playerStep;
        _stillHeld += playerStep;
        if (_stillClock < StillSpacingSeconds) return;

        _stillClock = 0.0;
        bool shot = KsaWorld.TryRequestScreenshot();
        _stillTaken++;

        _report($"{(shot ? "SHOT" : "CAPTURE")} still {_stillTaken} of {StillShots} at "
                + $"{_burstAge:F1} s, world paused");

        if (_stillTaken >= StillShots) KsaWorld.SetPaused(false);
    }

    /// <summary>A height fuse for the store, in metres over the ground: zero leaves it to burst on contact.</summary>
    public double FuseMetres { get; init; }

    /// <summary>A parachute for the store, as the rate it sinks at: zero leaves it without one.</summary>
    public double ChuteSink { get; init; }

    private bool _storeFitted;

    // Fits what was asked of the store to the profile it flies, once, before it is released.
    private void FitStore(MunitionProfile munition)
    {
        if (_storeFitted) return;
        _storeFitted = true;
        if (!(FuseMetres > 0.0) && !(ChuteSink > 0.0)) return;

        munition.BurstHeightMetres = (float)FuseMetres;
        munition.ChuteSinkMetresPerSecond = (float)ChuteSink;
        _report($"store fitted with a {(FuseMetres > 0.0 ? $"{FuseMetres:F0} m height fuse" : "contact fuse")}"
                + (ChuteSink > 0.0 ? $" and a {ChuteSink:F0} m/s chute" : string.Empty));
    }

    private string? Wait(WeaponSystems roster, double dt)
    {
        WeaponSystems.Entry? found = null;

        if (KsaWorld.InFlight)
        {
            foreach (WeaponSystems.Entry e in roster.All)
            {
                if (e.Weapon.Platform is not { } craft || e.Weapon.Launcher is null) continue;
                if (_fromBeforeTheLoad.Contains(craft)) continue;
                if (e.Weapon.Munition.Powered || !e.Weapon.Munition.HitsTerrain) continue;
                if (e.Weapon.Ammo <= 0) continue;

                found = e;
                FitStore(e.Weapon.Munition);

                // The chase rides whatever the panel is focused on, which is the controlled craft.
                if (ReferenceEquals(craft, KsaWorld.ControlledVehicle)) break;
            }
        }

        if (found is null)
        {
            _foundAt = double.NaN;

            // A craft set down at a site that then disappears is not coming back (a rocket on uneven
            // ground topples), so this wait has a budget like everything else here.
            if (_siteRequested)
            {
                if (double.IsNaN(_lostSince)) _lostSince = _sim;

                if (_sim - _lostSince > LostAfterSiteSeconds)
                {
                    return $"FAIL nothing to drop from, {LostAfterSiteSeconds:F0} s after the craft was set "
                           + "down at the site -- "
                           + (KsaWorld.InFlight
                                  ? "no store aboard any craft"
                                  : "the craft is no longer in flight, which is what one toppled on uneven "
                                    + "ground looks like");
                }
            }

            if (_sim - _saidAt >= 10.0)
            {
                _saidAt = _sim;
                _report("waiting -- " + (KsaWorld.InFlight ? "no store on any craft yet" : "no craft in flight"));
            }

            return null;
        }

        _lostSince = double.NaN;

        if (double.IsNaN(_foundAt)) _foundAt = _sim;
        if (_sim - _foundAt < SettleSeconds) return null;

        _system = found.Weapon;
        _craft = found.Weapon.Platform!;
        _craftName = KsaWorld.DisplayName(_craft);

        if (_request.Guided && !_system.Munition.Steers)
        {
            return $"FAIL a guided drop was asked for, and the {_system.Munition.DisplayName} does not steer";
        }

        if (!PlaceCraft(_craft, dt, out string? placing)) return placing;

        // Watching the cloud, the craft stays on the pad: with the chase off the camera is the
        // craft's, and a craft that climbs away takes the view with it.
        found.Policy.ChaseRounds = !_watchTheCloud;
        found.Policy.DrawBombSight = !_watchTheCloud;

        if (_watchTheCloud)
        {
            _report($"{_craftName} stays on the pad: {_system.Ammo} x "
                    + $"{_system.Munition.DisplayName}, chase off, watching from where it stands");

            _stagedAt = _sim;

            return Drop(_system, _craft, KsaWorld.ParentBody(_craft)!, 0.0,
                        KsaWorld.LocalUp(_craft), double3.Zero, KsaWorld.LocalUp(_craft));
        }

        _report($"flying {_craftName}"
                + (ReferenceEquals(_craft, KsaWorld.ControlledVehicle)
                       ? ""
                       : " -- not the controlled craft, so the chase will not ride its store")
                + $": {_system.Ammo} x {_system.Munition.DisplayName}, chase on");

        AttitudeHook.Stage(_craft);
        _stagedAt = _sim;
        _phase = Phase.Climbing;
        return null;
    }

    private string? Climb(double dt)
    {
        WeaponSystem system = _system!;
        Vehicle craft = _craft!;

        if (!KsaWorld.IsAlive(craft)) return "FAIL the craft was lost before the release";
        if (KsaWorld.ParentBody(craft) is not { } body) return "FAIL the craft has no body under it";

        if (!Steer(craft, body, dt, out double agl, out double3 up, out double3 wanted)) return null;

        double3 overGround = KsaWorld.VelocityEcl(craft)
                             - KsaWorld.GroundVelocityAt(craft, KsaWorld.PositionEcl(craft));
        double flying = _sim - _stagedAt;

        if (flying > LiftOffSeconds && agl < 20.0)
        {
            return $"FAIL the craft never left the ground: {agl:F0} m after {flying:F0} s";
        }

        if (flying > ClimbBudgetSeconds)
        {
            return $"FAIL the craft reached {agl:F0} m of the {_request.ReleaseAglMetres:F0} asked for "
                   + $"in {flying:F0} s";
        }

        if (_sim - _saidAt >= ProgressEverySeconds)
        {
            _saidAt = _sim;
            _report($"climbing: {agl:F0} m, {Vec.Dot(overGround, up):F0} m/s up, "
                    + $"{Vec.Len(overGround):F0} m/s over the ground, pitch {_pitchDeg:F0} deg, "
                    + $"nose {NoseOffDeg(craft, body, wanted):F0} deg off it");
        }

        if (agl < _request.ReleaseAglMetres || _pitchDeg < _request.PitchDeg) return null;

        return Drop(system, craft, body, agl, up, overGround, wanted);
    }

    private string? Drop(WeaponSystem system, Vehicle craft, Celestial body, double agl, double3 up,
                         double3 overGround, double3 wanted)
    {
        BombSightOverlay sight = _sightFor(system);

        _haveRing = sight.TryPredictNow(system, out double3 ringEcl)
                    && KsaWorld.TryAnchorToGround(ringEcl, out _ringBody, out _ringAnchor);

        if (_request.Guided)
        {
            if (!_haveRing) return "FAIL the sight had no solution to designate";

            if (!KsaWorld.TryAnchorToGround(ringEcl, out object? handle, out double3 anchor)
                || handle is null
                || !KsaWorld.TryGroundAnchorEcl(handle, anchor, out double3 aimEcl, out double3 aimVelocity))
            {
                return "FAIL the sight's impact could not be put on the ground";
            }

            system.Designate(Aimpoint.OnGround(handle, anchor, aimEcl, aimVelocity), "the sight's impact");
        }

        int before = system.Rounds.Count;
        if (!system.Release() || system.Rounds.Count <= before) return "FAIL the store would not release";

        IProjectile round = system.Rounds[^1];
        _round = round;
        _body = body;
        _releasedAt = _sim;

        double3 groundVelocity = KsaWorld.GroundVelocityAt(craft, system.PlatformEcl);
        _haveFlown = sight.TryPredictFrom(system, round.PositionEcl, round.VelocityEcl - groundVelocity,
                                          out double3 flownEcl)
                     && KsaWorld.TryAnchorToGround(flownEcl, out _, out _flownAnchor);

        double3 spin = round is Slug slug ? slug.SpinVelocityEcl : Vec.Zero;
        double3 ejected = round.VelocityEcl - KsaWorld.VelocityEcl(craft) - spin;
        double rackDeg = system.Launcher is { } launcher
                         && LauncherPart.TryGetTubeAxisEcl(craft, launcher, system.PodsPart,
                                                           system.Profile, 0, out double3 axis)
                             ? double.RadiansToDegrees(Vec.AngleBetween(ejected, axis))
                             : double.NaN;

        double speed = Vec.Len(overGround);
        double pathDeg = speed > 0.1
                             ? double.RadiansToDegrees(Math.Asin(Math.Clamp(Vec.Dot(overGround, up) / speed, -1.0, 1.0)))
                             : 90.0;

        _report("CAPTURE release");
        _report($"released at {agl:F0} m, {speed:F0} m/s over the ground at {pathDeg:F0} deg above the "
                + $"horizon, nose {NoseOffDeg(craft, body, wanted):F0} deg off the command; ejected "
                + $"{Vec.Len(ejected):F1} m/s at {rackDeg:F0} deg to the rack, spin {Vec.Len(spin):F2} m/s");

        _report(_haveRing && _haveFlown
                    ? $"the ring is {Offset(_flownAnchor, _ringAnchor)} from a flight off the release state"
                    : $"no comparison: the ring {(_haveRing ? "solved" : "did not solve")}, the flight off "
                      + $"the release state {(_haveFlown ? "solved" : "did not solve")}");

        _phase = Phase.Falling;
        return null;
    }

    private string? Fall(double dt)
    {
        IProjectile round = _round!;
        Vehicle craft = _craft!;
        double since = _sim - _releasedAt;

        if (KsaWorld.IsAlive(craft) && _body is { } body)
        {
            // Still flown, so a craft left to tumble does not fall back through the store's path.
            Steer(craft, body, dt, out _, out _, out _);

            if (double.IsFinite(_request.KillAfterSeconds) && !_killed && since >= _request.KillAfterSeconds)
            {
                _killed = true;
                AttitudeHook.Release(craft);
                KsaWorld.WaitForVehicleSolvers();
                KsaWorld.Destroy(craft, blastSeverity: 50f);
                _report($"destroyed {_craftName} {since:F1} s after the release, with the store still falling");
            }
        }

        // A second after the release, so the store is clear of the rack and the sight has settled.
        if (!_warpSet && since >= 1.0 && (_request.AutoWarp || double.IsFinite(_request.WarpFactor)))
        {
            _warpSet = true;
            ApplyWarp();
        }

        if (!_againDone && double.IsFinite(_request.AgainAfterSeconds)
            && since >= _request.AgainAfterSeconds)
        {
            _againDone = true;
            SendItSomewhereElse(since);
        }

        if (round.State == RoundState.Flying)
        {
            // Taken out of the world rather than failing to arrive: AbandonFlight drops it from the
            // roster without writing its State, so only the roster can say.
            if (_system is { } owner && !Holds(owner, round))
            {
                return $"FAIL the store was taken out of the world {since:F1} s after the release, "
                       + $"at {KsaWorld.SimulationSpeed:F0}x -- it was still flying";
            }

            if (since > FallBudgetSeconds) return $"FAIL the store was still falling {since:F0} s after the release";

            if (_sim - _saidAt >= ProgressEverySeconds && _body is { } under)
            {
                _saidAt = _sim;
                double3 overGround = round.VelocityEcl - KsaWorld.GroundVelocityAt(under, round.PositionEcl);
                _warpObserved = Math.Max(_warpObserved, KsaWorld.SimulationSpeed);
                _report($"falling: {since:F0} s, {Vec.Len(overGround):F0} m/s over the ground"
                        + (_system!.Platform is null ? ", loose" : "")
                        + $", {KsaWorld.SimulationSpeed:F0}x"
                        + (KsaWorld.IsAutoWarpActive ? " (auto)" : ""));
            }

            return null;
        }

        if (round.State != RoundState.Detonated) return $"FAIL the store ended {round.State} rather than landing";

        return Landed(round, since);
    }

    // Whether the roster still has this round. A landed one leaves on the frame it detonates, so
    // this is only meaningful while it is flying.
    private static bool Holds(WeaponSystem system, IProjectile round)
    {
        foreach (IProjectile held in system.Rounds)
        {
            if (ReferenceEquals(held, round)) return true;
        }

        return false;
    }

    // The half of post-release aiming a suite cannot reach: a designation arriving while the store
    // is already falling, and the region it is judged against.
    private void SendItSomewhereElse(double since)
    {
        if (_system is not { } system) return;

        if (!double.IsFinite(_request.AgainOffsetMetres))
        {
            system.ClearDesignation();
            _report($"CAPTURE again -- designation cleared {since:F1} s after the release; "
                    + "the store should keep the aim it already has");
            return;
        }

        if (_body is not { } body || !_haveRing
            || !KsaWorld.TryGroundAnchorEcl(_ringBody, _ringAnchor, out double3 ringEcl, out _))
        {
            _report("again: the ring could not be put back on the ground");
            return;
        }

        // North of the ring, in the local frame there. Any fixed direction would do; north is the
        // one the ascent already uses, so a run reads the same way throughout.
        double3 up = Vec.Unit(ringEcl - body.GetPositionEcl());
        double3 axis = Vec.Unit(body.GetBodyFixed2Ecl() * new double3(0, 0, 1));
        double3 north = Vec.Cross(up, Vec.Unit(Vec.Cross(axis, up)));

        double3 wantedEcl = ringEcl + (north * _request.AgainOffsetMetres);

        if (!KsaWorld.TryAnchorToGround(wantedEcl, out object? handle, out double3 anchor)
            || handle is null
            || !KsaWorld.TryGroundAnchorEcl(handle, anchor, out double3 aimEcl, out double3 aimVel))
        {
            _report("again: the new place could not be put on the ground");
            return;
        }

        _againAnchor = anchor;
        _haveAgain = true;

        _sentWasInReach = true;
        _haveSentImpact = false;

        if (StoreReach.FallingStore(system) is { } measured)
        {
            TailKitReach was = StoreReach.SolveNow(system, measured);
            _sentWasInReach = !was.Known || was.Covers(aimEcl);
            _sentReachMetres = was.RadiusMetres;
            _haveSentImpact = was.Known
                              && KsaWorld.TryAnchorToGround(was.ImpactEcl, out _, out _sentImpactAnchor);
        }

        string reach = StoreReach.FallingStore(system) is { } speaking
                           ? StoreReach.SolveNow(system, speaking).Describe(aimEcl)
                           : "no store in the air";

        // Past both rules a player's designation obeys: this store already has a target, and the
        // place may be beyond its reach. A store sent further than the ring says is the only
        // in-game evidence that the ring is a floor.
        system.Designate(Aimpoint.OnGround(handle, anchor, aimEcl, aimVel), "somewhere else",
                          asInstrument: true);
        _report($"CAPTURE again -- sent {_request.AgainOffsetMetres:F0} m north {since:F1} s after "
                + $"the release: {reach}");
    }

    private void ApplyWarp()
    {
        if (_request.AutoWarp)
        {
            // Half the remaining budget, which is long enough that the warp is still running while
            // the store falls -- the state the engine refuses a speed change in, and the one this
            // mode exists to sit in. The margin is KSA's own stopping distance.
            bool started = KsaWorld.TryAutoWarpTo(FallBudgetSeconds * 0.5, marginSeconds: 10.0);
            _report(started
                        ? "CAPTURE warp -- started KSA's own warp-to-a-time"
                        : "warp: KSA refused to start a warp-to-a-time");
            return;
        }

        bool set = KsaWorld.SetSimulationSpeed(_request.WarpFactor);
        _report(set
                    ? $"CAPTURE warp -- asked for {_request.WarpFactor:F0}x, world reads {KsaWorld.SimulationSpeed:F0}x"
                    : $"warp: {_request.WarpFactor:F0}x was refused");
    }

    private string? Landed(IProjectile round, double since)
    {
        // Back to real time before the linger, or the hand-back is watched at warp.
        if (_warpSet)
        {
            KsaWorld.StopAutoWarp();
            KsaWorld.SetSimulationSpeed(1.0);
            _report($"warp: peaked at {_warpObserved:F0}x while the store fell");
        }

        // ...and then straight back up again if this run is watching the cloud at speed. Ordered
        // after the hand-back rather than instead of it, because the two are different windows and
        // the engine refuses a change while its own warp-to-a-time is still stopping.
        if (LingerSpeed != 1.0)
        {
            // Zero is a pause and not a speed, and goes through the call that says so.
            // SetSimulationSpeed refuses it outright, so asking for it as a speed leaves the world
            // at 1x while the run reports it asked for a pause.
            bool held = LingerSpeed <= 0.0
                            ? KsaWorld.SetPaused(true)
                            : KsaWorld.SetSimulationSpeed(LingerSpeed);
            _report(held
                        ? $"CAPTURE linger at {LingerSpeed:F2}x -- world reads "
                          + $"{KsaWorld.SimulationSpeed:F2}x"
                        : $"linger: {LingerSpeed:F2}x was refused, watching at "
                          + $"{KsaWorld.SimulationSpeed:F2}x");
        }

        if (_body is not { } body) return "FAIL the store landed with no body recorded";

        // The burst is placed at an instant inside the frame and the body sample is at its end, so
        // the ground's own travel across that gap comes off before anchoring. At ~30 km/s it is
        // hundreds of metres.
        double3 burst = round.PositionEcl;

        // An air burst is scored from the ground under it, which is where the ring is drawn.
        double3 scored = BlastSweep.GroundAtSample(burst, KsaWorld.GroundVelocityAt(body, burst),
                                                   round.DetonationElapsedInFrame);
        if (round is Slug { BurstAtHeight: true })
        {
            if (!KsaWorld.TrySnapToGround(scored, out double3 under)) return "FAIL the air burst had no ground under it";
            _report($"burst {Vec.Len(scored - under):F0} m over the ground");
            scored = under;
        }

        if (!KsaWorld.TryAnchorToGround(scored, out _, out double3 landed))
        {
            return "FAIL the burst could not be put on the ground";
        }

        bool craftLived = _craft is { } craft && KsaWorld.IsAlive(craft);

        _report($"landed {since:F1} s after the release{(craftLived ? "" : ", its craft already gone")}: "
                + (_haveRing ? Offset(_ringAnchor, landed) : "unknown")
                + $" from the ring{(_request.Guided ? " it was designated onto" : "")}, "
                + (_haveFlown ? Offset(_flownAnchor, landed) : "unknown")
                + " from the flight off the release state"
                + (_haveAgain ? $", {Offset(_againAnchor, landed)} from where it was sent" : ""));

        // A send the kit could never reach is judged on the walk, not the arrival, which it would
        // always fail.
        if (_haveAgain && !_sentWasInReach)
        {
            _verdict = WalkedFarEnough(landed);
            _phase = Phase.Lingering;
            return null;
        }

        // A store sent somewhere else is judged against there. A store whose designation was merely
        // cleared is still judged against the ring: clearing is not a recall, and the point of that
        // run is that the aim it already had survives.
        bool sent = _haveAgain;
        double miss = sent ? Vec.Len(landed - _againAnchor)
                           : _haveRing ? Vec.Len(landed - _ringAnchor) : double.PositiveInfinity;

        string what = sent ? "where it was sent" : "the ring";

        _verdict = miss <= BarMetres
                       ? $"PASS {miss:F0} m from {what}, inside {BarMetres:F0} m"
                       : $"FAIL {(double.IsFinite(miss) ? $"{miss:F0} m" : "no aim")} from {what}, "
                         + $"past {BarMetres:F0} m";

        _phase = Phase.Lingering;
        return null;
    }

    // Whether a store sent out of reach still walked as far as the ring promised: the in-game evidence
    // that TailKitReach.SettlingMargin, measured headlessly, stays a floor. Over-delivering passes.
    private string WalkedFarEnough(double3 landed)
    {
        if (!_haveSentImpact) return "FAIL the unsteered landing at the send was not recorded";

        double walked = Vec.Len(landed - _sentImpactAnchor);
        double claimed = _sentReachMetres;

        if (!(claimed > 0.0)) return "FAIL the ring claimed no reach to be judged against";

        string how = $"walked {walked:F0} m of the {claimed:F0} m the ring claimed "
                     + $"({walked / claimed:F2}x), {Vec.Len(landed - _againAnchor):F0} m short of "
                     + "where it was sent";

        return walked >= claimed
                   ? $"PASS the reach held as a floor: {how}"
                   : $"FAIL the ring over-promised: {how}";
    }

    // Engine lit, full throttle, and the nose held on the pitch programme. Every frame, because an
    // attitude hold is dropped the frame it is not restated.
    private bool Steer(Vehicle craft, Celestial body, double dt, out double agl, out double3 up,
                       out double3 wanted)
    {
        VehicleCommand.SetEngine(craft, running: true);
        VehicleCommand.DriveThrottle(craft, 1.0);

        wanted = Vec.Zero;
        if (!TryLocalFrame(craft, body, out up, out double3 east, out double3 north, out agl)) return false;

        if (agl > PitchOverAglMetres)
        {
            _pitchDeg = Math.Min(_request.PitchDeg, _pitchDeg + (PitchRateDegPerSecond * dt));
        }

        double pitch = double.DegreesToRadians(_pitchDeg);
        wanted = (up * Math.Cos(pitch)) + (east * Math.Sin(pitch));

        // North is square to every direction the programme asks for, so the roll never has to
        // be re-derived through the vertical.
        doubleQuat toCci = body.GetCce2Cci();
        AttitudeHook.Hold(craft, wanted.Transform(toCci), north.Transform(toCci));
        return true;
    }

    internal static bool TryLocalFrame(Vehicle craft, Celestial body, out double3 up, out double3 east,
                                      out double3 north, out double agl)
    {
        double3 here = KsaWorld.PositionEcl(craft);
        up = Vec.Unit(here - body.GetPositionEcl());

        double3 axis = Vec.Unit(body.GetBodyFixed2Ecl() * new double3(0, 0, 1));
        east = Vec.Unit(Vec.Cross(axis, up));
        north = Vec.Cross(up, east);

        agl = KsaWorld.TrySnapToGround(here, out double3 ground, out double3 centre)
                  ? Vec.Len(here - centre) - Vec.Len(ground - centre)
                  : double.NaN;

        return Vec.Len2(east) > 0.5 && double.IsFinite(agl);
    }

    private static double NoseOffDeg(Vehicle craft, Celestial body, double3 wantedEcl)
        => KsaWorld.TryControlFrameCci(craft, body, out double3 nose, out _, out _)
               ? double.RadiansToDegrees(Vec.AngleBetween(nose, wantedEcl.Transform(body.GetCce2Cci())))
               : double.NaN;

    // Metres between two ground anchors on one body, with the east and north of it: a distance says
    // a drop missed, and only the direction says whether the release or the fall moved it.
    private static string Offset(double3 from, double3 to)
    {
        double3 d = to - from;
        double3 up = Vec.Unit(from);
        double3 east = Vec.Unit(Vec.Cross(new double3(0, 0, 1), up));
        double3 north = Vec.Cross(up, east);

        return $"{Vec.Len(d):F0} m (E {Math.Round(Vec.Dot(d, east)):+0;-0;0}, "
               + $"N {Math.Round(Vec.Dot(d, north)):+0;-0;0})";
    }
}
