using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which way a round in flight is pointing.
///
/// <para>Along the airflow, once there is any — but a round released rather than fired has none
/// at the moment it lets go, and normalising a near-zero vector yields whatever direction the
/// residual happened to have. Every other round leaves its tube at between 25 and 1100 m/s and is
/// never near that; a bomb starts at nothing and builds up.</para>
///
/// <para>The band is not only a guard against noise. A real store leaves its rack pointing where
/// the rack pointed and noses over as its fins gain authority, which is a second or two rather
/// than an instant — so easing across the band is closer to the thing than snapping at a
/// threshold would be, and it costs no per-round state.</para>
///
/// <para><b>What turns a body is dynamic pressure, not speed.</b> Speed alone is the same number
/// in a hurricane and in orbit, and a body released in vacuum keeps the attitude it was let go
/// with — there is nothing to weathervane against. Keying on speed snaps a store onto its orbital
/// velocity the instant it separates, which is 7 km/s pointing across the launcher.</para>
/// </summary>
internal static class BodyAttitude
{
    /// <summary>Below this, in m/s, the airflow says nothing and the release attitude stands.</summary>
    public const double NoAuthoritySpeed = 2.0;

    /// <summary>Above this the airflow decides entirely.</summary>
    public const double FullAuthoritySpeed = 40.0;

    // The band as dynamic pressure — density ratio times speed squared. Calibrated so that at sea
    // level, where the ratio is 1, the two speeds above are exactly the band edges: every round
    // this mod fired before anything reached vacuum behaves identically.
    private const double NoAuthorityPressure = NoAuthoritySpeed * NoAuthoritySpeed;
    private const double FullAuthorityPressure = FullAuthoritySpeed * FullAuthoritySpeed;

    /// <param name="velocityLocal">Velocity relative to the air, which is the ground's frame.</param>
    /// <param name="releaseHeading">Where the launcher was pointing — what a store leaves along.</param>
    /// <param name="mediumDensityRatio">Air density where the round is, against sea level. Zero in
    /// vacuum, where the release attitude stands however fast the round is going.</param>
    public static double3 Heading(double3 velocityLocal, double3 releaseHeading,
                                  double mediumDensityRatio = 1.0)
    {
        double3 fallback = Vec.IsFinite(releaseHeading) && Vec.Len2(releaseHeading) > 1e-9
                               ? Vec.Unit(releaseHeading)
                               : new double3(0, 1, 0);

        if (!Vec.IsFinite(velocityLocal)) return fallback;
        if (!double.IsFinite(mediumDensityRatio) || mediumDensityRatio <= 0.0) return fallback;

        double speed = Vec.Len(velocityLocal);
        double pressure = mediumDensityRatio * speed * speed;
        if (pressure <= NoAuthorityPressure) return fallback;

        double3 along = Vec.Unit(velocityLocal);
        if (pressure >= FullAuthorityPressure) return along;

        double t = (pressure - NoAuthorityPressure) / (FullAuthorityPressure - NoAuthorityPressure);
        double3 eased = fallback + (along - fallback) * t;

        // Opposed directions cancel to nothing halfway across the band. Rare -- it needs a store
        // released backwards -- and the release attitude is the better answer when it happens.
        return Vec.Len2(eased) > 1e-9 ? Vec.Unit(eased) : fallback;
    }
}
