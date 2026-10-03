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
    private static double3 South(double metres) => At(PadLatitudeDeg - double.RadiansToDegrees(metres / R));

    private static double DensityAt(double3 pointCci) => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    private static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    /// <summary>
    /// <c>GeoSat FAT</c>, the rocket in every <c>SOLVER SCALE</c> save, read off a SCALE 1 shot flown at
    /// 1.00x (<c>~/shots/2026-09-07-1751/shots/001-base.log</c>, the throttle line's thrust and mass).
    /// </summary>
    // Three SRBs lit alone at ignition: 249.8 t burnt in 100.2 s, flow 2.11 t/s growing to ~2.9,
    // exhaust 2,170 m/s at the pad and ~2,700 high up (impulse-weighted 2,385); 24.3 t of casings.
    // The core lights as they drop: 25.55 MN at 4,056 m/s, 88.9 t burnt to depletion, 9.6 t dropped.
    // The upper is 955 kN at 4,278 m/s on 31.8 t; its split is not in the log -- 15.2 t was left at a
    // 12,900 km cutoff without running dry -- and no shot here reaches it.
    private static IcbmFlightRig GameStack(bool reportsStackDeltaV)
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
                new() { DryMassKg = 12_000, PropellantKg = 19_800, ThrustNewtons = 955_000, ExhaustVelocity = 4_278 },
            ],
        };
    }

    // What the scenario forces before it arms (Ksa/BallisticScenario.cs Commit).
    private static IcbmConfig ScenarioConfig(double arrivalPreference)
        => new() { Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = arrivalPreference };

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
    {
        Out.WriteLine($"stack delta-v {(stackDeltaV ? "reported" : "absent")}, arrival preference {arrivalPreference}");
        Out.WriteLine("THE ASCENT'S FLOOR, NOT THE SHOT: no shove, trim, release, aim correction or bus drag.");
        Out.WriteLine($"{"range",7} {"reason",9} {"hdovr",5} {"latch",5} {"stage",5} {"t",5} {"alt",5} {"speed",6} "
                      + $"{"q Pa",6} {"climb",5} {"apogee",6} {"SRB on",11} {"least to gain pre-loop",22} "
                      + $"{"lands",6} {"miss",7}");

        MunitionProfile warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);

        foreach (double km in new[] { 100.0, 200.0, 300.0, 418.0, 500.0, 700.0, 1_000.0, 1_200.0, 2_000.0 })
        {
            IcbmFlightRig rig = GameStack(stackDeltaV);
            Watch watch = new(rig);
            rig.AimLoop = watch;

            IcbmProgram program = new(ScenarioConfig(arrivalPreference));
            double3 aim = South(km * 1000.0);
            IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 4_000.0);

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
                          + $"{lands} {miss}");
        }
    }
}
