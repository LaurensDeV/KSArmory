using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary><see cref="IcbmConfig.ShortShotCutsOffOnTheMiss"/>: <c>docs/SHORT-RANGE.md</c>.</summary>
public class MissCutoffTests
{
    private static (double Miss, double Residual) Fly25Km(double dragAreaM2, bool cutsOnTheMiss)
    {
        IcbmFlightRig rig = ShortShotHoverTests.RealLiquid2(dragAreaM2);
        rig.AttitudeHasInertia = true;
        rig.UsesKsaAttitudeLaw = true;
        IcbmConfig config = new()
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, FlyAnyRange = true,
            ShortShotCutsOffOnTheMiss = cutsOnTheMiss,
        };
        IcbmProgram program = new(config);
        double3 aim = ShortShotHoverTests.At(ShortShotHoverTests.PadLatitudeDeg - double.RadiansToDegrees(25_000.0 / FloorHoldTests.R));
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1_200.0);
        Assert.True(flight.Reached, "never cut off");
        Assert.True(ImpactPredictor.TryPredict(FloorHoldTests.Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, 1.0,
                                               ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(q => rig.DensityRatioAt(FloorHoldTests.Earth.AltitudeOf(q)), rig.Warhead!)));
        return (FloorHoldTests.R * Vec.AngleBetween(hit.GroundFixedPointCci, FloorHoldTests.Earth.CarryCci(aim, flight.CutoffSeconds)),
                program.ResidualAtCutoff);
    }

    // A pinned fall of a minute moves its landing when the burn ends anywhere but the solved cutoff: projected, these
    // landed 6.4-43 m out against 1.2-7.8, and flown at 25 km, 97 and 121 m.
    [Theory]
    [InlineData(12.0)]
    [InlineData(30.0)]
    [InlineData(45.0)]
    public void AShortFallIsCutOffAsWithoutTheSwitch(double dragAreaM2)
    {
        (double missOff, double residualOff) = Fly25Km(dragAreaM2, false);
        (double missOn, double residualOn) = Fly25Km(dragAreaM2, true);
        Assert.Equal(residualOff, residualOn, 9);
        Assert.Equal(missOff, missOn, 6);
    }
}
