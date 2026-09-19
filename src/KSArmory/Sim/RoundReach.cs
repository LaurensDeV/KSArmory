using Brutal.Numerics;

namespace KSArmory;

/// <summary>Where a round the ground stops has got to, as the thing that decides what reaps it.</summary>
internal enum Approach
{
    /// <summary>
    /// Nothing could be read about it — no body, no ceiling, or a trajectory this does not answer.
    /// The age limit is the reaper, exactly as it is for a round nothing stops.
    /// </summary>
    Unknown,

    /// <summary>It can never reach the ground, and nothing else will ever reap it.</summary>
    Impossible,

    /// <summary>
    /// On its way but not yet where arriving happens. A coast is not the round being stuck, so the
    /// clock is held.
    /// </summary>
    Coasting,

    /// <summary>
    /// Where arriving happens, on a path this cannot vouch for ending on the ground, so the clock runs.
    /// </summary>
    Arriving,

    /// <summary>
    /// Where arriving happens, on a path that can only end on the ground. The ground is the reaper, so the
    /// clock is held.
    /// </summary>
    Landing,
}

/// <summary>
/// Whether a round the ground stops can still get to it, and whether it is getting there yet.
///
/// <para><b>A store that falls has an ending of its own, so its age is the wrong thing to reap it
/// on.</b> A long fall is long, not stuck: a B61 released at 100 km takes 152 s to arrive and one
/// released at 250 km takes 246, both of which a two-minute self-destruct kills mid-flight. What
/// has to be caught instead is the store that will <em>never</em> arrive — released in orbit, where
/// it keeps the craft's orbital velocity and simply flies alongside it — because nothing else will
/// ever reap that one, and <see cref="WarpPolicy"/> holds timewarp down while it is in the air.</para>
///
/// <para>That is a question about the trajectory rather than the clock, and for a coasting store it
/// is closed form: a conic whose lowest point is above where arriving begins never gets there. It
/// is exact out there because there is no drag to bend it, and it cannot be undone later because
/// drag only ever lowers a periapsis.</para>
///
/// <para><b>And a path that can only end on the ground needs no clock at all.</b> Inside the ceiling the
/// conic is no longer the whole flight, but it still bounds it: one whose lowest point is under the lowest
/// the ground can be ends on the ground, for the same reason. A shell lobbed under a weak pull stays up for
/// minutes — from Mars a 5"/54 at its longest reach flies past its two-minute clock — and reaping it on
/// the clock was what ended its reach there.</para>
///
/// <para><b>The clock still has to be able to run.</b> Holding it for everything that is not failing
/// leaves a round nothing reaps at all: a body with no atmosphere never starts an air clock, so a store
/// falling towards the Moon would have had only the ground to end it, and nothing whatsoever where the
/// ground could not be read. <see cref="Approach.Arriving"/> is therefore geometric rather than
/// atmospheric — below the ceiling, whatever the ceiling is made of — and it stays on the clock for what
/// the conic cannot vouch for: a path skimming above the lowest ground, and a round already under it.</para>
/// </summary>
internal static class RoundReach
{
    /// <param name="ceilingRadius">
    /// Where arriving begins: the top of the atmosphere on a body that has one, and the highest
    /// ground on a body that does not. Not the mean radius — a conic that clears the mean sphere
    /// can still meet a mountain, and this is asked in order to <em>destroy</em> a round.
    /// </param>
    /// <param name="floorRadius">
    /// A radius the ground is never below, or zero where that cannot be read, which never answers
    /// <see cref="Approach.Landing"/>.
    /// </param>
    public static Approach Classify(double mu, double3 positionCci, double3 velocityCci,
                                    double ceilingRadius, double floorRadius = 0.0)
    {
        if (!(ceilingRadius > 0.0) || !Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci))
        {
            return Approach.Unknown;
        }

        // Inside it the round is where arriving happens, and the clock is the right instrument again --
        // unless the path can only end on the ground, which nothing inside can undo.
        if (Vec.Len(positionCci) <= ceilingRadius)
        {
            return EndsOnTheGround(mu, positionCci, velocityCci, floorRadius) ? Approach.Landing : Approach.Arriving;
        }

        double periapsis = Kepler.PeriapsisRadius(mu, positionCci, velocityCci);

        // NaN is "no answer", not "no arrival". It covers an open trajectory and a purely radial
        // one, and those want opposite verdicts — an escaping round never arrives, a round dropped
        // straight down always does — so neither is decided here. Destroying a round on a maybe is
        // the one outcome with no way back.
        if (!double.IsFinite(periapsis)) return Approach.Unknown;

        return periapsis <= ceilingRadius ? Approach.Coasting : Approach.Impossible;
    }

    // A round already under the floor has gone through ground that could not be read, and only the clock
    // will end it.
    private static bool EndsOnTheGround(double mu, double3 positionCci, double3 velocityCci, double floorRadius)
    {
        if (!(floorRadius > 0.0) || Vec.Len(positionCci) <= floorRadius) return false;

        double periapsis = Kepler.PeriapsisRadius(mu, positionCci, velocityCci);
        return double.IsFinite(periapsis) && periapsis <= floorRadius;
    }
}
