using System.Reflection;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The short shot flown on the stack the game flies, rather than on <see cref="ShortRangeAscentTests"/>'
/// two throttleable liquid stages. <c>docs/SHORT-RANGE.md</c> Step 0.
///
/// <para><b>The table is the ascent's floor, not the shot.</b> The rig has no separation shove, no
/// trim, no release, no aim correction and no bus drag: the miss is the warhead flown from the
/// state the motors stopped in, with the drag the game gives it.</para>
/// </summary>
[Trait("kind", "study")]
public class GameStackShortRangeTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;
    private const double PadLatitudeDeg = 28.608;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 At(double latitudeDeg)
    {
        double lat = double.DegreesToRadians(latitudeDeg);
        return new double3(R * Math.Cos(lat), 0.0, R * Math.Sin(lat));
    }

    /// <summary>Due south of the pad, along the meridian.</summary>
    internal static double3 South(double metres) => At(PadLatitudeDeg - double.RadiansToDegrees(metres / R));

    private static double DensityAt(double3 pointCci) => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    private static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    /// <summary>
    /// <c>GeoSat FAT</c>, the rocket in every <c>SOLVER SCALE</c> save, read off a SCALE 1 shot flown at
    /// 1.00x (<c>~/shots/2026-09-07-1751/shots/001-base.log</c>, the throttle line's thrust and mass).
    /// </summary>
    // Three SRBs lit alone at ignition: 249.8 t burnt in 100.2 s, flow 2.11 t/s growing to ~2.9,
    // exhaust 2,170 m/s at the pad and ~2,700 high up (impulse-weighted 2,385); 24.3 t of casings.
    // The core lights as they drop: 25.55 MN at 4,056 m/s, 88.9 t burnt to depletion, 9.6 t dropped.
    // The upper is 955 kN at 4,278 m/s on 31.8 t, and burned down to 6.4 t still lit on a flown
    // 1,000 km shot (2026-10-03), so its dry mass is at most that.
    internal static IcbmFlightRig GameStack(bool reportsStackDeltaV)
    {
        double3 pad = At(PadLatitudeDeg);

        return new IcbmFlightRig
        {
            Body = Earth,
            PositionCci = pad,
            VelocityCci = Earth.GroundVelocityCci(pad),
            // The flown structural limit, 19.6 g, is max(5, 250 / radius).
            BoundingSphereRadiusMetres = 12.76,
            // Fitted to that shot: handover 162.5 s at 74.8 km with 2,551 m/s to gain against a flown
            // 166 s, 75 km and 2,524; cutoff 236.6 s at 201 km against ~235 s at 197. The rig's
            // default drag reached thin air 24 s early.
            DragAreaM2 = 60.0,
            StartsUnlit = true,
            ReportsStackDeltaV = reportsStackDeltaV,
            CommandLatencyFrames = 1,
            MinThrottle = 0.12,
            ThrottleRatePerSecond = 0.7,
            StepJitter = 0.5,
            Stages =
            [
                new()
                {
                    DryMassKg = 24_300, PropellantKg = 249_800,
                    ThrustNewtons = 2_110.0 * 2_170.0, ExhaustVelocity = 2_170,
                    VacuumExhaustVelocity = 2_700, BurnoutMassFlowRatio = 1.36, Solid = true,
                },
                new() { DryMassKg = 9_600, PropellantKg = 88_900, ThrustNewtons = 25_550_000, ExhaustVelocity = 4_056 },
                new() { DryMassKg = 6_000, PropellantKg = 25_800, ThrustNewtons = 955_000, ExhaustVelocity = 4_278 },
            ],
        };
    }

    // What the scenario forces before it arms (Ksa/BallisticScenario.cs Commit).
    private static IcbmConfig ScenarioConfig(double arrivalPreference, double ascentReserveSeconds = 0.0,
                                             bool flyAnyRange = false)
        => new()
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = arrivalPreference,
            AscentReserveSeconds = ascentReserveSeconds, FlyAnyRange = flyAnyRange,
        };

    /// <summary>Watches the program without moving the aim, and records why the burn ended.</summary>
    private sealed class Watch(IcbmFlightRig rig) : IcbmFlightRig.IAimLoop
    {
        private static readonly FieldInfo Countdown = Field("_countdown");
        private static readonly FieldInfo Lowest = Field("_lowestToGain");
        private static readonly FieldInfo Arrival = Field("_arrivalFromLaunch");

        private static FieldInfo Field(string name)
            => typeof(IcbmProgram).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
               ?? throw new InvalidOperationException($"IcbmProgram.{name} has moved; this study reads it");

        private double _t;
        private bool _sawClosedLoop;

        public double LeastToGainBeforeLoop = double.PositiveInfinity;
        public double LeastToGainAltitude = double.NaN;
        public double LeastToGainSeconds = double.NaN;
        public double HandoverAltitude = double.NaN;
        public double HandoverToGain = double.NaN;

        public bool Cut;
        public string Reason = "";
        public bool OnHandoverFrame;
        public bool Latched;
        public int StageAtCut;
        public double CutSeconds, CutAltitude, CutSpeed, CutQ, CutClimbDeg, Residual;

        public double3 Apply(double3 aimNowCci) => aimNowCci;
        public bool IsSteady => true;

        public void AfterUpdate(IcbmProgram p, in IcbmCommand command, double3 aimNowCci, double h)
        {
            _t += h;
            double alt = rig.Body.AltitudeOf(rig.PositionCci);

            if (p.Phase is IcbmPhase.Rising or IcbmPhase.PitchProgram && p.Arc is not null
                && p.VelocityToGain > 0.0 && p.VelocityToGain < LeastToGainBeforeLoop)
            {
                LeastToGainBeforeLoop = p.VelocityToGain;
                LeastToGainAltitude = alt;
                LeastToGainSeconds = _t;
            }

            if (p.Phase == IcbmPhase.ClosedLoop && !_sawClosedLoop)
            {
                _sawClosedLoop = true;
                HandoverAltitude = alt;
                HandoverToGain = p.VelocityToGain;
            }

            if (Cut || p.Phase != IcbmPhase.Coast) return;

            Cut = true;
            OnHandoverFrame = !_sawClosedLoop;
            if (OnHandoverFrame) HandoverAltitude = alt;
            Latched = double.IsFinite((double)Arrival.GetValue(p)!);
            StageAtCut = rig.StageIndex;
            CutSeconds = _t;
            CutAltitude = alt;
            double3 airflow = rig.VelocityCci - rig.Body.GroundVelocityCci(rig.PositionCci);
            CutSpeed = Vec.Len(rig.VelocityCci);
            CutQ = 0.5 * rig.DensityRatioAt(alt) * rig.SeaLevelDensity * Vec.Len2(airflow);
            CutClimbDeg = double.RadiansToDegrees(Math.Asin(Math.Clamp(
                Vec.Dot(rig.VelocityCci, Vec.Unit(rig.PositionCci)) / CutSpeed, -1.0, 1.0)));
            Residual = p.ResidualAtCutoff;

            double countdown = (double)Countdown.GetValue(p)!;
            double lowest = (double)Lowest.GetValue(p)!;
            double step = p.StepAtCutoff;

            // ShouldCutOff's three tests, in its order; the dry path is the fourth way out.
            Reason = command.Hold.StartsWith("burn ended") || command.Hold.StartsWith("the engines never lit") ? "burnout"
                   : Residual <= BurnoutGuidance.CutoffMetresPerSecond ? "threshold"
                   : countdown <= 0.5 * step * Math.Max(Math.Clamp(p.ThrottleAtCutoff, 0.0, 1.0), 1e-3) ? "countdown"
                   : lowest < IcbmProgram.BackstopBelow
                     && Residual > lowest + Math.Max(p.AccelerationAtCutoff * step, 1.0) ? "backstop"
                   : "?";
        }
    }

    private static double ApogeeAltitude(double3 r, double3 v)
    {
        double energy = 0.5 * Vec.Len2(v) - Mu / Vec.Len(r);
        double h = Vec.Len(Vec.Cross(r, v));
        double a = -Mu / (2.0 * energy);
        double e = Math.Sqrt(Math.Max(0.0, 1.0 + 2.0 * energy * h * h / (Mu * Mu)));
        return a * (1.0 + e) - R;
    }

    private static readonly string[] StageNames = ["SRB", "core", "upper", "none"];

    [Theory]
    [InlineData(false, 0.5)]
    [InlineData(true, 0.5)]
    [InlineData(false, 0.0)]
    [InlineData(true, 0.0)]
    public void TheShortShotOnTheGamesStack(bool stackDeltaV, double arrivalPreference)
        => Sweep(stackDeltaV, arrivalPreference, 0.0);

    /// <summary>The same sweep with <see cref="IcbmConfig.AscentReserveSeconds"/>, as the game flies it.</summary>
    [Theory]
    [InlineData(5.0)]
    [InlineData(10.0)]
    [InlineData(15.0)]
    public void TheShortShotHeldBackForTheClosedLoop(double reserveSeconds)
        => Sweep(true, 0.5, reserveSeconds);

    /// <summary>The same sweep with <see cref="IcbmConfig.FlyAnyRange"/>.</summary>
    [Fact]
    public void TheShortShotAtAnyRange() => Sweep(true, 0.5, 0.0, flyAnyRange: true);

    // Other kinds of stack from the same pad, none of them flown: an all-solid three-stage missile
    // (roughly Minuteman III), the SRBs with nothing above them, IcbmFlightTests' liquid pair, and a
    // big liquid core that cannot throttle below 40%.
    private static IcbmFlightRig FromThePad(List<IcbmFlightRig.Stage> stages, double dragAreaM2,
                                           double minThrottle, double radius)
    {
        double3 pad = At(PadLatitudeDeg);
        return new IcbmFlightRig
        {
            Body = Earth, PositionCci = pad, VelocityCci = Earth.GroundVelocityCci(pad), Stages = stages,
            BoundingSphereRadiusMetres = radius, DragAreaM2 = dragAreaM2, StartsUnlit = true,
            ReportsStackDeltaV = true, CommandLatencyFrames = 1, MinThrottle = minThrottle,
            ThrottleRatePerSecond = 0.7, StepJitter = 0.5,
        };
    }

    internal static IcbmFlightRig AllSolid() => FromThePad(
    [
        new() { DryMassKg = 2_300, PropellantKg = 20_800, ThrustNewtons = 900_000, ExhaustVelocity = 2_550, Solid = true },
        new() { DryMassKg = 800, PropellantKg = 6_200, ThrustNewtons = 270_000, ExhaustVelocity = 2_800, Solid = true },
        new() { DryMassKg = 1_500, PropellantKg = 3_300, ThrustNewtons = 150_000, ExhaustVelocity = 2_850, Solid = true },
    ], 2.5, 0.0, 9.0);

    internal static IcbmFlightRig SolidOnly() => FromThePad(
    [
        new()
        {
            DryMassKg = 27_300, PropellantKg = 249_800, ThrustNewtons = 2_110.0 * 2_170.0, ExhaustVelocity = 2_170,
            VacuumExhaustVelocity = 2_700, BurnoutMassFlowRatio = 1.36, Solid = true,
        },
    ], 50.0, 0.0, 12.76);

    internal static IcbmFlightRig Liquid() => FromThePad(
    [
        new() { DryMassKg = 4_000, PropellantKg = 46_000, ThrustNewtons = 1_400_000, ExhaustVelocity = 2_600 },
        new() { DryMassKg = 1_200, PropellantKg = 12_000, ThrustNewtons = 260_000, ExhaustVelocity = 3_000 },
    ], 4.0, 0.0, 0.0);

    internal static IcbmFlightRig HotLiquid() => FromThePad(
    [
        new() { DryMassKg = 9_600, PropellantKg = 88_900, ThrustNewtons = 25_550_000, ExhaustVelocity = 4_056 },
        new() { DryMassKg = 6_000, PropellantKg = 25_800, ThrustNewtons = 955_000, ExhaustVelocity = 4_278 },
    ], 25.0, 0.4, 12.76);

    [Theory]
    [InlineData("all-solid")]
    [InlineData("solid only")]
    [InlineData("liquid")]
    [InlineData("hot liquid")]
    public void AnyStackAtAnyRange(string stack)
        => Sweep(true, 0.5, 0.0, flyAnyRange: true, stack: stack,
                 ranges: [25.0, 50.0, 100.0, 200.0, 300.0, 500.0, 1_000.0, 2_000.0, 5_000.0]);

    /// <summary>
    /// The aim corrected against the warhead's own drag, as <c>IcbmComputer</c> corrects it — but
    /// looking from inside the air once the shot is short and its cutoff is near, where today
    /// <see cref="AimCorrection.DepartureIsWorthObserving"/> refuses. The refusal exists for a
    /// projected cutoff that is still the pad; twenty seconds out it is not.
    /// </summary>
    private sealed class AimingWatch(IcbmFlightRig rig, Watch watch, MunitionProfile warhead, bool inTheAir)
        : IcbmFlightRig.IAimLoop
    {
        private readonly AimCorrection _aim = new();
        private double _sincePredict = double.PositiveInfinity;

        public double3 Apply(double3 aimNowCci) => _aim.Apply(aimNowCci);
        public bool IsSteady => _aim.IsSteady;

        public void AfterUpdate(IcbmProgram p, in IcbmCommand command, double3 aimNowCci, double h)
        {
            watch.AfterUpdate(p, command, aimNowCci, h);

            if (double.IsFinite(p.CommittedArrivalFromNow)) _aim.Freeze();
            if (!p.IsBurning || p.Arc is not { } arc) return;

            _sincePredict += h;
            if (_sincePredict < 0.5) return;
            _sincePredict = 0.0;

            bool clear = AimCorrection.DepartureIsWorthObserving(DensityAt(p.CutoffPositionCci));
            bool near = inTheAir && p.IsShortShot && command.SecondsToCutoff < 20.0;
            if (!clear && !near) return;

            if (!ImpactPredictor.TryPredict(Earth, p.CutoffPositionCci, arc.RequiredVelocityCci, 1.0,
                                            ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                            null, null, new ImpactPredictor.Drag(DensityAt, warhead)))
            {
                return;
            }

            double3 target = rig.Body.CarryCci(aimNowCci, command.SecondsToCutoff);
            _aim.Observe(hit.GroundFixedPointCci, target);
        }
    }

    [Theory]
    [InlineData("game", false)]
    [InlineData("game", true)]
    [InlineData("all-solid", false)]
    [InlineData("all-solid", true)]
    [InlineData("solid only", false)]
    [InlineData("solid only", true)]
    public void TheShortShotWithItsAimCorrected(string stack, bool inTheAir)
        => Sweep(true, 0.5, 0.0, flyAnyRange: true, stack: stack, aimInTheAir: inTheAir,
                 ranges: [25.0, 50.0, 100.0, 200.0, 300.0, 1_000.0, 2_000.0]);

    private void Sweep(bool stackDeltaV, double arrivalPreference, double reserveSeconds, bool flyAnyRange = false,
                       string stack = "game", double[]? ranges = null, bool? aimInTheAir = null)
    {
        if (aimInTheAir is { } air) Out.WriteLine($"aim corrected, {(air ? "inside the air near cutoff" : "only above the air")}");
        Out.WriteLine($"stack: {stack}");
        Out.WriteLine($"stack delta-v {(stackDeltaV ? "reported" : "absent")}, arrival preference {arrivalPreference}, "
                      + $"ascent reserve {reserveSeconds} s{(flyAnyRange ? ", any range" : "")}");
        Out.WriteLine("THE ASCENT'S FLOOR, NOT THE SHOT: no shove, trim, release, aim correction or bus drag.");
        Out.WriteLine($"{"range",7} {"reason",9} {"hdovr",5} {"latch",5} {"stage",5} {"t",5} {"alt",5} {"speed",6} "
                      + $"{"q Pa",6} {"climb",5} {"apogee",6} {"SRB on",11} {"least to gain pre-loop",22} "
                      + $"{"lands",6} {"miss",7} {"hdovr gain",10} {"left t",6}");

        MunitionProfile warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);

        foreach (double km in ranges ?? [50.0, 100.0, 150.0, 200.0, 300.0, 418.0, 500.0, 700.0, 1_000.0, 1_200.0, 2_000.0])
        {
            IcbmFlightRig rig = stack switch
            {
                "all-solid" => AllSolid(),
                "solid only" => SolidOnly(),
                "liquid" => Liquid(),
                "hot liquid" => HotLiquid(),
                _ => GameStack(stackDeltaV),
            };
            Watch watch = new(rig);
            rig.AimLoop = aimInTheAir is { } inAir
                              ? new AimingWatch(rig, watch, Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21), inAir)
                              : watch;

            IcbmProgram program = new(ScenarioConfig(arrivalPreference, reserveSeconds, flyAnyRange));
            double3 aim = South(km * 1000.0);
            IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 6_000.0);

            string head = $"{km,6:F0}k";
            if (!flight.Reached || !watch.Cut)
            {
                Out.WriteLine($"{head} never cut off: {flight.FinalPhase} '{flight.Hold}'");
                continue;
            }

            string lands = "  --", miss = "    --";
            if (ImpactPredictor.TryPredict(Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, 1.0,
                                           ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                           null, null, new ImpactPredictor.Drag(DensityAt, warhead)))
            {
                lands = $"{GroundMetres(hit.GroundFixedPointCci, Earth.CarryCci(At(PadLatitudeDeg), flight.CutoffSeconds)) / 1000.0,6:F0}";
                miss = $"{GroundMetres(hit.GroundFixedPointCci, Earth.CarryCci(aim, flight.CutoffSeconds)) / 1000.0,7:F1}";
            }

            string srb = rig.SolidBurnedOnSeconds > 0.0
                             ? $"{rig.SolidBurnedOnSeconds,4:F0}s {rig.SolidAddedMetresPerSecond,4:F0}m/s"
                             : "         -";
            string least = double.IsFinite(watch.LeastToGainBeforeLoop)
                               ? $"{watch.LeastToGainBeforeLoop,6:F0} m/s @{watch.LeastToGainAltitude / 1000.0,5:F1} km {watch.LeastToGainSeconds,4:F0}s"
                               : "                    -";

            Out.WriteLine($"{head} {watch.Reason,9} {(watch.OnHandoverFrame ? "yes" : "no"),5} {(watch.Latched ? "yes" : "no"),5} "
                          + $"{StageNames[Math.Min(watch.StageAtCut, 3)],5} {watch.CutSeconds,5:F0} {watch.CutAltitude / 1000.0,5:F1} "
                          + $"{watch.CutSpeed,6:F0} {watch.CutQ,6:F0} {watch.CutClimbDeg,5:F1} "
                          + $"{ApogeeAltitude(flight.CutoffPositionCci, flight.CutoffVelocityCci) / 1000.0,6:F0} {srb,11} {least,22} "
                          + $"{lands} {miss} {watch.HandoverToGain,10:F0} {flight.PropellantLeftKg / 1000.0,6:F1}");
        }
    }
}
