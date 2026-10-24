using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which way a round in flight is pointing, carried from one drawn frame to the next.
///
/// <para>Onto the airflow, at a rate the airflow sets, so a released store leaves at its seated attitude
/// and noses over as its fins gain authority.</para>
///
/// <para><b>Carried, not derived</b>: a store thrown upwards comes down facing the other way, there is no
/// one turn between opposite directions, and a derived heading flips over the top of the climb.</para>
///
/// <para><b>Keyed on dynamic pressure, not speed</b>, so a store released in vacuum keeps its attitude
/// rather than snapping onto its 7 km/s orbital velocity.</para>
/// </summary>
internal static class BodyAttitude
{
    /// <summary>Below this, in m/s, the airflow says nothing and the attitude stands.</summary>
    public const double NoAuthoritySpeed = 2.0;

    /// <summary>Above this the airflow turns the body at the full rate.</summary>
    public const double FullAuthoritySpeed = 40.0;

    /// <summary>
    /// How fast a body the airflow has full authority over turns onto it. Far past anything a round
    /// flies — thirty g at 700 m/s is 24 deg/s — so nothing in real air visibly lags its flight;
    /// what it bounds is a turn with nothing in between, over the top of a vertical throw.
    /// </summary>
    public const double FullAuthorityTurnRateDegPerSecond = 720.0;

    // Density ratio times speed squared, so at sea level the two speeds above are the band edges.
    private const double NoAuthorityPressure = NoAuthoritySpeed * NoAuthoritySpeed;
    private const double FullAuthorityPressure = FullAuthoritySpeed * FullAuthoritySpeed;

    // Square to the mesh's nose, so a body tipping about it keeps its own axis still.
    private static readonly double3 MeshSide = new(0, 0, 1);

    /// <summary>How much say the airflow has over the body, from none to all of it.</summary>
    /// <param name="velocityLocal">Velocity relative to the air, which is the ground's frame.</param>
    /// <param name="mediumDensityRatio">Air density where the round is, against sea level. Zero in
    /// vacuum, where the airflow has no say however fast the round is going.</param>
    public static double Authority(double3 velocityLocal, double mediumDensityRatio = 1.0)
    {
        if (!Vec.IsFinite(velocityLocal)) return 0.0;
        if (!double.IsFinite(mediumDensityRatio) || mediumDensityRatio <= 0.0) return 0.0;

        double pressure = mediumDensityRatio * Vec.Len2(velocityLocal);
        return Math.Clamp((pressure - NoAuthorityPressure) / (FullAuthorityPressure - NoAuthorityPressure),
                          0.0, 1.0);
    }

    /// <summary>
    /// A body's attitude, body to ecliptic, turned toward the airflow for
    /// <paramref name="seconds"/>. The turn is about the axis square to the nose and the airflow,
    /// so it adds no roll about the nose.
    /// </summary>
    /// <param name="velocityLocal">Velocity relative to the air. <em>Local</em>: an ecliptic
    /// velocity carries ~29.8 km/s of orbital motion and would point every round the same way.</param>
    public static doubleQuat Turn(doubleQuat attitudeEcl, double3 velocityLocal,
                                  double mediumDensityRatio, double seconds)
    {
        double authority = Authority(velocityLocal, mediumDensityRatio);
        if (authority <= 0.0 || !double.IsFinite(seconds) || seconds <= 0.0) return attitudeEcl;

        double3 nose = attitudeEcl * FireGeometry.NoseAxis;
        double3 along = Vec.Unit(velocityLocal);

        double rate = double.DegreesToRadians(FullAuthorityTurnRateDegPerSecond) * authority;
        double turn = Math.Min(Vec.AngleBetween(nose, along), rate * seconds);
        if (!(turn > 0.0)) return attitudeEcl;

        // Dead astern there is no one turn onto the airflow, so the body tips over about an axis of
        // its own rather than one picked off the ecliptic.
        double3 axis = Vec.Cross(nose, along);
        if (Vec.Len2(axis) < 1e-12) axis = attitudeEcl * MeshSide;

        doubleQuat turned = doubleQuat.CreateFromAxisAngle(Vec.Unit(axis), turn) * attitudeEcl;
        return Vec.IsFinite(turned * FireGeometry.NoseAxis) ? turned : attitudeEcl;
    }

    /// <summary>
    /// Whether a round is still beside its launcher, so the airflow may not turn it yet: until it has
    /// travelled half its length from its seat. Slid off a rail that is about where its aft hanger leaves
    /// the front; thrown off an ejector, turned about its middle any sooner, its tail swings into the hull.
    /// </summary>
    public static bool StillBesideLauncher(double3 travelSinceLaunch, double bodyLength)
        => Vec.IsFinite(travelSinceLaunch) && bodyLength > 0.0
           && Vec.Len(travelSinceLaunch) < 0.5 * bodyLength;

    /// <summary>A body's attitude rolled about its own nose by <paramref name="rollRad"/>; the nose stays where it was.</summary>
    public static doubleQuat Roll(doubleQuat attitudeEcl, double rollRad)
    {
        if (!double.IsFinite(rollRad) || rollRad == 0.0) return attitudeEcl;

        doubleQuat rolled = attitudeEcl * doubleQuat.CreateFromAxisAngle(FireGeometry.NoseAxis, rollRad);
        return Vec.IsFinite(rolled * FireGeometry.NoseAxis) ? rolled : attitudeEcl;
    }
}
