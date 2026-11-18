using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The divert reach a bus will have, guessed before it has flown: the cheapest conic from where it stands to the
/// lead target, which is what the flight has to beat. <c>docs/WIDE-SPREAD.md</c> step 8 has the measurements.
/// </summary>
internal static class PadReach
{
    /// <summary>
    /// The coast after cutoff, as a share of that conic's flight, that every flight came in over: 0.58 at the least
    /// over six rigged stacks, 500-12,902 km and arrival preferences 0 to 1 (<c>PadCoastStudyTests</c>).
    /// </summary>
    public const double CoastShareOfTheConic = 0.55;

    /// <summary>The coast a walk may be planned on before launch; NaN where no conic reaches the aim.</summary>
    public static double CoastFloorSeconds(BallisticBody body, double3 positionCci, double3 velocityCci, double3 aimNowCci)
        => BallisticArc.TryCheapest(body, positionCci, velocityCci, aimNowCci, out BallisticArc.Solution conic)
               ? CoastShareOfTheConic * conic.FlightSeconds
               : double.NaN;

    /// <summary>
    /// The free reach on that conic at <paramref name="beforeArrivalSeconds"/>, carried back by the planet's turn so it
    /// reads as though that instant were now. Measured at the coast floor, where the coast measures its own.
    /// </summary>
    public static bool TryFree(BallisticBody body, double3 positionCci, double3 velocityCci, double3 aimNowCci,
                               double beforeArrivalSeconds, in ReleaseFocus.Air air, out DivertFootprint footprint)
    {
        footprint = default;

        if (!(beforeArrivalSeconds > 0.0)) return false;
        if (!BallisticArc.TryCheapest(body, positionCci, velocityCci, aimNowCci, out BallisticArc.Solution conic)) return false;

        double toRelease = conic.FlightSeconds - beforeArrivalSeconds;
        if (!(toRelease > 0.0)) return false;

        if (!Kepler.TryCoast(body.Mu, positionCci, conic.RequiredVelocityCci, toRelease, out double3 releaseCci,
                             out double3 releaseVelocityCci))
        {
            return false;
        }

        double3 nowCci = body.CarryCci(releaseCci, -toRelease);
        double3 nowVelocityCci = body.CarryCci(releaseVelocityCci, -toRelease);

        return ReleaseFocus.FlownSensitivity.TryFly(body, nowCci, nowVelocityCci, air) is { } flown
               && DivertFootprint.TryFrom(body, flown, DivertFootprint.ArrivalClock.Free, fromTheRealState: false,
                                          out footprint);
    }
}
