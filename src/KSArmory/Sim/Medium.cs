using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What the stuff a round is flying through does to it — the two terms every round has, whatever
/// flies it.
///
/// <para>Shared by <see cref="Interceptor"/> and <see cref="Slug"/> because they are the same
/// physics, not because the code looked alike: a guided round differs from a shell by having a
/// motor and a seeker, and in nothing about how air or water pushes on it. A third kind of round
/// inherits both terms by asking for them rather than by being copied from one of these two.</para>
///
/// <para>Every argument is measured in the <b>local</b> frame — against the ground the round is
/// flying over, never the ecliptic. See <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
/// </summary>
internal static class Medium
{
    /// <summary>
    /// Gravity as a round in this medium feels it.
    ///
    /// <para>A round denser than what surrounds it still sinks; one at its neutral density neither
    /// sinks nor rises. <see cref="MunitionProfile.NeutralDensityRatio"/> of zero switches the whole
    /// term off, so a round that only ever flies in air behaves exactly as it would without it.</para>
    /// </summary>
    public static double3 Buoyancy(double3 gravity, MunitionProfile munition, double densityRatio)
    {
        ArgumentNullException.ThrowIfNull(munition);

        return munition.NeutralDensityRatio > 0f
            ? gravity * (1.0 - (densityRatio / munition.NeutralDensityRatio))
            : gravity;
    }

    /// <summary>
    /// The longest step a round can be integrated across while there is air worth resolving.
    ///
    /// <para>A munition's own <see cref="MunitionProfile.MaxFaithfulStepSeconds"/> is about fusing
    /// — how far a round may move before it steps over its own proximity radius. Entry is a
    /// different problem with a different answer: air density falls off on a scale height of a few
    /// kilometres and a re-entering round crosses that in seconds, so a step sized for fusing flies
    /// it through air that is nothing like what is there. Flown at a 170 ms step, six warheads
    /// landed <b>381 km</b> beyond where the same shot puts them at 17 ms.</para>
    /// </summary>
    public const double FaithfulStepInAir = 0.05;

    /// <summary>Below this the air cannot move the answer within one step, whatever the step.</summary>
    public const double NoticeableDensity = 1e-4;

    /// <summary>
    /// The drag deceleration, as a vector to <b>subtract</b> from a round's acceleration.
    ///
    /// <para>Quadratic in airspeed, so a coasting round bleeds speed instead of holding it, and
    /// scaled by the medium's density so one profile is right on the pad, climbing out, in orbit
    /// and submerged. <see cref="MunitionProfile.DragK"/> is the sea-level-air value, which is why
    /// the ratio is 1.0 there.</para>
    ///
    /// <para>Zero in a vacuum, at rest, or for a round that declares no drag — the guards are here
    /// rather than at the call sites so that a round cannot be given a NaN direction by dividing
    /// out a zero speed.</para>
    /// </summary>
    public static double3 Drag(double3 localVelocity, MunitionProfile munition, double densityRatio)
    {
        ArgumentNullException.ThrowIfNull(munition);

        double airspeed = Vec.Len(localVelocity);

        if (munition.DragK <= 0f || airspeed <= 1e-6 || densityRatio <= 0.0) return Vec.Zero;

        return localVelocity * (munition.DragK * airspeed * densityRatio);
    }
}
