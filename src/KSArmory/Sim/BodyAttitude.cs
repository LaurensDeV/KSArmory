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
/// </summary>
internal static class BodyAttitude
{
    /// <summary>Below this, in m/s, the airflow says nothing and the release attitude stands.</summary>
    public const double NoAuthoritySpeed = 2.0;

    /// <summary>Above this the airflow decides entirely.</summary>
    public const double FullAuthoritySpeed = 40.0;

    /// <param name="velocityLocal">Velocity relative to the air, which is the ground's frame.</param>
    /// <param name="releaseHeading">Where the launcher was pointing — what a store leaves along.</param>
    public static double3 Heading(double3 velocityLocal, double3 releaseHeading)
    {
        double3 fallback = Vec.IsFinite(releaseHeading) && Vec.Len2(releaseHeading) > 1e-9
                               ? Vec.Unit(releaseHeading)
                               : new double3(0, 1, 0);

        if (!Vec.IsFinite(velocityLocal)) return fallback;

        double speed = Vec.Len(velocityLocal);
        if (speed <= NoAuthoritySpeed) return fallback;

        double3 along = Vec.Unit(velocityLocal);
        if (speed >= FullAuthoritySpeed) return along;

        double t = (speed - NoAuthoritySpeed) / (FullAuthoritySpeed - NoAuthoritySpeed);
        double3 eased = fallback + (along - fallback) * t;

        // Opposed directions cancel to nothing halfway across the band. Rare -- it needs a store
        // released backwards -- and the release attitude is the better answer when it happens.
        return Vec.Len2(eased) > 1e-9 ? Vec.Unit(eased) : fallback;
    }
}
