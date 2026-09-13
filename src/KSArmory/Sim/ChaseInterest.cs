using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Whether a chased round is still going anywhere, and so whether it is still worth watching.
///
/// <para>A round with nothing to arrive at can only run out of flight. A missile whose target a
/// sibling has just killed flies on until <c>MaxFlightSeconds</c> reaps it, and a camera riding it
/// all that way is watching a despawn. So the chase gives up on a round once it has been going
/// nowhere for <see cref="GraceSeconds"/> — long enough to keep the moment it lost the target, which
/// is usually a burst ahead of it in frame.</para>
/// </summary>
internal sealed class ChaseInterest
{
    /// <summary>
    /// How long a round may go nowhere before the chase lets go of it. Simulated seconds, because
    /// the camera is still riding the round: a pause holds it and slow motion stretches it, where
    /// wall clock would cut away from a frozen world.
    /// </summary>
    public const double GraceSeconds = 2.0;

    // Closing slower than this is not arriving anywhere a camera could wait for.
    private const double MinClosingSpeed = 1.0;

    private double _goingNowhere;

    /// <summary>
    /// Seconds until the round reaches what it is flying at, along the line of sight — or NaN when
    /// it is opening, or closing too slowly to count down to.
    /// </summary>
    public static double TimeToGo(double3 roundEcl, double3 roundVelocityEcl,
                                  double3 targetEcl, double3 targetVelocityEcl)
    {
        double3 toTarget = targetEcl - roundEcl;
        double range = Vec.Len(toTarget);

        if (!double.IsFinite(range)) return double.NaN;
        if (range < 1e-6) return 0.0;

        // Differenced here rather than handed in as a relative velocity: both carry the ecliptic's
        // ~29.8 km/s, and for a place on the ground the planet's spin, and those cancel only in the
        // subtraction.
        double closing = Vec.Dot(roundVelocityEcl - targetVelocityEcl, toTarget / range);

        return closing > MinClosingSpeed ? range / closing : double.NaN;
    }

    /// <summary>
    /// Seconds until a falling store comes down to the height of what it is aimed at, from how far
    /// above it the store is and how fast it is descending — or NaN with no gravity to fall in.
    ///
    /// <para>A store's countdown rather than <see cref="TimeToGo"/>, which is badly posed for a fall.
    /// Released with its carrier's motion a store is barely closing on a point below it, which the
    /// line of sight reads as minutes; passing beside that point it stops closing again, which
    /// reads as the fall getting longer. Height does neither: it runs down on every frame.</para>
    /// </summary>
    public static double TimeToFall(double3 roundEcl, double3 roundVelocityEcl,
                                    double3 targetEcl, double3 targetVelocityEcl, double3 gravity)
    {
        double g = Vec.Len(gravity);
        if (!(g > 1e-9) || !double.IsFinite(g)) return double.NaN;

        double3 up = -gravity / g;

        // Differenced here, for the same reason as TimeToGo: both carry the ecliptic's motion.
        double height = Vec.Dot(roundEcl - targetEcl, up);
        double descending = -Vec.Dot(roundVelocityEcl - targetVelocityEcl, up);

        if (!double.IsFinite(height) || !double.IsFinite(descending)) return double.NaN;
        if (height <= 0.0) return 0.0;

        // Drag-free, so it reaches zero a little before the ground does. The camera only needs it
        // to run down.
        return (-descending + Math.Sqrt((descending * descending) + (2.0 * g * height))) / g;
    }

    /// <summary>
    /// Whether the round will end by arriving: closing on something, or a store the ground stops.
    /// A bomb dropped on nothing is the second, and its arrival is what the chase is for.
    /// </summary>
    public static bool IsGoingSomewhere(bool stoppedByGround, double timeToGo)
        => stoppedByGround || double.IsFinite(timeToGo);

    /// <summary>
    /// Advances by one step, and says whether the round has now been going nowhere for
    /// <see cref="GraceSeconds"/>. Closing again starts the count over.
    /// </summary>
    public bool Lost(bool stoppedByGround, double timeToGo, double stepSeconds)
    {
        if (IsGoingSomewhere(stoppedByGround, timeToGo))
        {
            _goingNowhere = 0.0;
            return false;
        }

        if (double.IsFinite(stepSeconds) && stepSeconds > 0.0) _goingNowhere += stepSeconds;

        return _goingNowhere >= GraceSeconds;
    }

    /// <summary>Forgets the round, for the next chase.</summary>
    public void Reset() => _goingNowhere = 0.0;
}
