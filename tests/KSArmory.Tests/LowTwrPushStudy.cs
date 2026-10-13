using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

[Trait("kind", "study")]
public class LowTwrPushStudy(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;
    private static BallisticBody Earth => FloorHoldTests.Earth;

    // TEST Any LowTWR off its flown throttle lines (~/shots/2026-10-13-orbits/lowtwr-base-1): 172.2 t at the pad, the A2
    // burning 1.51 t/s for 6.43 MN in vacuum; the A3 upper as Real Liquid2's. Drag 47-53 m^2 read off load against q.
    internal static IcbmFlightRig LowTwr()
    {
        double3 pad = GameStackShortRangeTests.South(0.0);
        return new IcbmFlightRig
        {
            Body = Earth, PositionCci = pad, VelocityCci = Earth.GroundVelocityCci(pad), StartsUnlit = true,
            BoundingSphereRadiusMetres = 8.0, DragAreaM2 = 50.0, ReportsStackDeltaV = true, CommandLatencyFrames = 1,
            MinThrottle = 0.10, ThrottleRatePerSecond = 0.7, StepJitter = 0.5, UnpoweredAttitudeRateDegPerSec = 1.0,
            AttitudeHasInertia = true, UsesKsaAttitudeLaw = true,
            Stages =
            [
                new() { DryMassKg = 7_000, PropellantKg = 134_100, ThrustNewtons = 5_923_000, ExhaustVelocity = 3_912, VacuumExhaustVelocity = 4_247 },
                new() { DryMassKg = 4_600, PropellantKg = 26_500, ThrustNewtons = 956_000, ExhaustVelocity = 4_500 },
            ],
        };
    }

    private static IcbmFlightRig GameStack()
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
        rig.AttitudeHasInertia = true;
        rig.StepJitter = 0.5;
        return rig;
    }

    private sealed class Watch : IcbmFlightRig.IAimLoop
    {
        public double EngagedAtRamp = double.NaN, ToGainThen = double.NaN;
        private double _ramp;
        public double3 Apply(double3 aimNowCci) => aimNowCci;
        public bool IsSteady => true;
        public void AfterUpdate(IcbmProgram p, in IcbmCommand command, double3 aimNowCci, double h)
        {
            _ramp = p.Phase == IcbmPhase.ClosedLoop && p.Countdown < IcbmProgram.ThrottleDownSeconds ? _ramp + h : 0.0;
            if (double.IsNaN(EngagedAtRamp) && p.Phase == IcbmPhase.ClosedLoop
                && p.HoldDirectionBelowNow <= IcbmProgram.PushesThroughHoldBelow + 1e-9
                && p.VelocityToGain > 2.0 * IcbmProgram.PushesThroughHoldBelow)
            {
                EngagedAtRamp = _ramp;
                ToGainThen = p.VelocityToGain;
            }
        }
    }

    private readonly record struct Result(double Miss, double Residual, double Rate, double Engaged, double ToGain);

    private static Result Fly(IcbmFlightRig rig, double km, double step, Action<IcbmConfig> arm, bool pulses)
    {
        Watch watch = new();
        rig.AimLoop = watch;
        rig.Warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);
        rig.ReleaseLaunchSpeed = 0.5;
        IcbmConfig config = FloorHoldTests.Config(0.5, true, true);
        config.PulsesBelowTheFloor = pulses;
        arm(config);
        IcbmProgram program = new(config);
        double3 aim = GameStackShortRangeTests.South(km * 1000.0);
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, step, 6_000.0);
        double miss = double.NaN;
        double3 thrown = flight.CutoffVelocityCci + Vec.Unit(flight.CoastDirectionCci) * 0.5;
        if (flight.Reached && ImpactPredictor.TryPredict(Earth, flight.CutoffPositionCci, thrown, 1.0, ImpactPredictor.DefaultMaxSeconds,
                out ImpactPredictor.Impact hit, null, null, new ImpactPredictor.Drag(q => rig.DensityRatioAt(Earth.AltitudeOf(q)), rig.Warhead!)))
            miss = R * Vec.AngleBetween(hit.GroundFixedPointCci, Earth.CarryCci(aim, flight.CutoffSeconds));
        return new Result(miss, program.ResidualAtCutoff, rig.PeakFloorRateDegPerSec, watch.EngagedAtRamp, watch.ToGainThen);
    }

    private static readonly (string Name, Action<IcbmConfig> Arm)[] Arms =
    [
        ("10 s", _ => { }),
        ("4 s", c => c.ShortShotPushesThroughAfterSeconds = 4.0),
        ("share .6", c => c.ShortShotPushesThroughAtDragShare = 0.6),
        ("share .7", c => c.ShortShotPushesThroughAtDragShare = 0.7),
    ];

    private void Row(string what, Result r)
        => Out.WriteLine($"  {what,-26} miss {r.Miss,7:F1} m  residual {r.Residual,5:F2} m/s  floor {r.Rate,5:F1} deg/s  pushed "
                         + (double.IsNaN(r.Engaged) ? "never" : $"{r.Engaged,4:F1} s in, {r.ToGain,5:F1} m/s left"));

    [Fact]
    public void Study()
    {
        Out.WriteLine("LowTWR:");
        foreach (double km in new[] { 200.0, 700.0 })
        foreach (var (name, arm) in Arms)
            Row($"{km,4:F0} km {name}", Fly(LowTwr(), km, 0.02, arm, pulses: true));

        Out.WriteLine("The game's stack at its floor, FloorHoldTests' low arcs:");
        int[] spun = new int[Arms.Length];
        foreach (double step in new[] { 0.02, 0.025 })
        foreach (double km in FloorHoldTests.LowArcs)
            for (int i = 0; i < Arms.Length; i++)
            {
                Result r = Fly(GameStack(), km, step, Arms[i].Arm, pulses: false);
                if (r.Rate > 20.0) spun[i]++;
                Row($"{km,4:F0} km {step * 1000,2:F0} ms {Arms[i].Name}", r);
            }

        for (int i = 0; i < Arms.Length; i++) Out.WriteLine($"{Arms[i].Name}: spun on {spun[i]} of 8");
    }
}
