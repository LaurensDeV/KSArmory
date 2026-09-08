using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Whether a round the ground stops can still get to it.
///
/// <para><b>A store that falls has an ending of its own, so its age is the wrong thing to reap it
/// on.</b> A long fall is long, not stuck: a B61 released at 100 km takes 152 s to arrive and one
/// released at 250 km takes 246, both of which a two-minute self-destruct kills mid-flight. What
/// actually has to be caught is the round that will <em>never</em> arrive — released in orbit,
/// where it keeps the craft's orbital velocity and simply flies alongside it — because nothing else
/// will ever reap that one, and <see cref="WarpPolicy"/> holds timewarp down for as long as it is
/// in the air.</para>
///
/// <para>That is a question about the trajectory rather than about the clock, and for a coasting
/// store it is closed form: a conic whose lowest point is above the atmosphere never touches it.
/// Above the air there is no drag to change the answer, so this is exact rather than a heuristic —
/// and drag only ever lowers a periapsis, so an answer of "it arrives" cannot be undone later.</para>
/// </summary>
internal static class RoundReach
{
    /// <summary>
    /// True when the round may still reach <paramref name="ceilingRadius"/>, and <b>true whenever
    /// this cannot tell</b>: an unanswerable question leaves the age limit as the reaper.
    /// Destroying a round on a maybe is the one outcome with no way back.
    /// </summary>
    /// <param name="ceilingRadius">
    /// Where arriving begins: the top of the atmosphere on a body that has one, and the highest
    /// ground on a body that does not. Not the mean radius — a conic that clears the mean sphere
    /// can still meet a mountain.
    /// </param>
    public static bool CanReachGround(double mu, double3 positionCci, double3 velocityCci,
                                      double ceilingRadius)
    {
        if (!(ceilingRadius > 0.0) || !Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci))
        {
            return true;
        }

        // Already inside it, so the question is behind us: from here the round is arriving, and
        // the drag it is now in only shortens that.
        if (Vec.Len(positionCci) <= ceilingRadius) return true;

        double periapsis = Kepler.PeriapsisRadius(mu, positionCci, velocityCci);

        // NaN is "no answer", not "no arrival". It covers an open trajectory and a purely radial
        // one, and those want opposite verdicts - an escaping round never arrives, a round dropped
        // straight down always does - so neither is decided here.
        if (!double.IsFinite(periapsis)) return true;

        return periapsis <= ceilingRadius;
    }
}
