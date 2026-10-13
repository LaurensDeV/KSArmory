using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The 25 km hover in the rig: Real Liquid2 with the Mk 21 aboard, so the drag solve runs. At 30 and 45 m^2 it ends stood
/// still over the target with its arrival re-pinned every other solve, as flown, unless
/// <see cref="IcbmConfig.ShortShotPushesThroughAStall"/> pushes it through. <c>docs/SHORT-RANGE.md</c>.
/// </summary>
[Trait("kind", "study")]
public class ShortShotHoverTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    internal const double PadLatitudeDeg = 28.608;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    internal static double3 At(double latitudeDeg)
    {
        double lat = double.DegreesToRadians(latitudeDeg);
        return new double3(R * Math.Cos(lat), 0.0, R * Math.Sin(lat));
    }

    // TEST RealLiquid2 off its flown throttle lines (~/shots/2026-10-06-fourrockets): 125.4 t and 2.90 MN at
    // the pad with 5,361 m/s in the stage, the upper 736 kN on 31.1 t with 8,617 m/s; the engine held 0.112.
    internal static IcbmFlightRig RealLiquid2(double dragAreaM2)
    {
        double3 pad = At(PadLatitudeDeg);
        return new IcbmFlightRig
        {
            Body = Earth, PositionCci = pad, VelocityCci = Earth.GroundVelocityCci(pad), StartsUnlit = true,
            BoundingSphereRadiusMetres = 6.0, DragAreaM2 = dragAreaM2, ReportsStackDeltaV = true, CommandLatencyFrames = 1,
            MinThrottle = 0.112, ThrottleRatePerSecond = 0.7, StepJitter = 0.5, UnpoweredAttitudeRateDegPerSec = 1.0,
            Warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21),
            Stages =
            [
                new() { DryMassKg = 19_300, PropellantKg = 75_000, ThrustNewtons = 2_900_000, ExhaustVelocity = 5_880 },
                new() { DryMassKg = 4_600, PropellantKg = 26_500, ThrustNewtons = 736_000, ExhaustVelocity = 4_500 },
            ],
        };
    }

    private sealed class LoopClock(IcbmFlightRig rig, ITestOutputHelper output, bool trace) : IcbmFlightRig.IAimLoop
    {
        private double _t, _lastPrint = -10.0;
        public double ClosedLoopAt = double.NaN;
        public double CutAt = double.NaN;

        public double3 Apply(double3 aimNowCci) => aimNowCci;
        public bool IsSteady => true;

        public void AfterUpdate(IcbmProgram p, in IcbmCommand command, double3 aimNowCci, double h)
        {
            _t += h;
            if (p.Phase == IcbmPhase.ClosedLoop && double.IsNaN(ClosedLoopAt)) ClosedLoopAt = _t;
            if (p.Phase == IcbmPhase.Coast && double.IsNaN(CutAt)) CutAt = _t;
            if (trace && p.Phase == IcbmPhase.ClosedLoop && _t - _lastPrint >= 5.0)
            {
                _lastPrint = _t;
                double3 up = Vec.Unit(rig.PositionCci);
                output.WriteLine($"  t {_t,6:F1} alt {rig.Body.AltitudeOf(rig.PositionCci) / 1000.0,6:F2} km climb "
                                 + $"{Vec.Dot(rig.VelocityCci - rig.Body.GroundVelocityCci(rig.PositionCci), up),7:F1} togain "
                                 + $"{p.VelocityToGain,7:F2} thr {command.Throttle,5:F3} offset {p.DragOffsetMetres,6:F0} m "
                                 + $"arrival {p.CommittedArrivalFromNow,6:F1} s");
            }
        }
    }

    private (double loopSeconds, bool cut) Fly(double dragAreaM2, double km, bool trace, Action<IcbmConfig>? arm = null)
    {
        IcbmFlightRig rig = RealLiquid2(dragAreaM2);
        LoopClock clock = new(rig, Out, trace);
        rig.AimLoop = clock;
        IcbmConfig config = new() { Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, FlyAnyRange = true };
        arm?.Invoke(config);
        IcbmProgram program = new(config);
        double3 aim = At(PadLatitudeDeg - double.RadiansToDegrees(km * 1000.0 / R));
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1_200.0);
        double loop = clock.CutAt - clock.ClosedLoopAt;
        string miss = "--";
        if (flight.Reached && ImpactPredictor.TryPredict(Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, 1.0,
                                                         ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit, null, null,
                                                         new ImpactPredictor.Drag(q => rig.DensityRatioAt(Earth.AltitudeOf(q)), rig.Warhead!)))
        {
            miss = $"{R * Vec.AngleBetween(hit.GroundFixedPointCci, Earth.CarryCci(aim, flight.CutoffSeconds)):F1} m";
        }
        Out.WriteLine($"{km} km drag area {dragAreaM2}{(arm is null ? "" : " push off")}: closed loop {loop:F1} s, cut "
                      + $"{!double.IsNaN(clock.CutAt)}, residual {program.ResidualAtCutoff:F2} m/s at throttle "
                      + $"{program.ThrottleAtCutoff:F3}, lands {miss}");
        return (loop, !double.IsNaN(clock.CutAt));
    }

    [Fact]
    public void Study()
    {
        // Without the warhead the drag solve never runs and no area stalls; with it, 12 and 20 m^2 close and 30 and 45 hover.
        foreach (double area in new[] { 12.0, 20.0, 30.0, 45.0 })
        {
            Fly(area, 25.0, trace: area == 30.0, c => c.ShortShotPushesThroughAStall = false);
        }
        foreach (double area in new[] { 12.0, 20.0, 30.0, 45.0 }) Fly(area, 25.0, trace: false);
    }
}
