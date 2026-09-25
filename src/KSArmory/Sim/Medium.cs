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
    /// it through air that is nothing like what is there. Measured on a 2,700 km deorbit against a
    /// 1 ms reference, the impact moves about <b>1.7 m per millisecond of frame</b> — smoothly and
    /// without a threshold, so this bounds an ordinary integration error rather than guarding
    /// against a cliff.</para>
    /// </summary>
    public const double FaithfulStepInAir = 0.05;

    /// <summary>Below this the air cannot move the answer within one step, whatever the step.</summary>
    public const double NoticeableDensity = 1e-4;

    /// <summary>
    /// What a density ratio is a multiple of: Earth's sea-level air, 1.225 kg/m³, as KSA declares it. One
    /// reference for every body, so a round is dragged by the air actually there — a fiftieth of it over a
    /// Mars-like surface and fifty times it over a Venus-like one.
    /// </summary>
    public const double ReferenceDensityKgPerM3 = 1.225;

    /// <summary>
    /// A round's drag constant from what it is: half the reference air's density, times its drag coefficient,
    /// times its frontal area, over its mass. Zero for anything not given.
    /// </summary>
    public static double DragK(double massKg, double calibreMm, double dragCoefficient)
    {
        if (!(massKg > 0.0) || !(calibreMm > 0.0) || !(dragCoefficient > 0.0)) return 0.0;

        double radius = calibreMm / 2000.0;
        return 0.5 * ReferenceDensityKgPerM3 * dragCoefficient * Math.PI * radius * radius / massKg;
    }

    /// <summary>
    /// Everything accelerating an unpowered round: the pull less its buoyancy, and its drag through the
    /// air at <paramref name="airVelocity"/>.
    /// </summary>
    public static double3 Coasting(double3 pull, double3 airVelocity, MunitionProfile munition, double densityRatio)
        => Buoyancy(pull, munition, densityRatio) - Drag(airVelocity, munition, densityRatio);

    /// <summary>
    /// What a store's parachute adds to its drag at an age since release: nothing before it opens,
    /// all of it once filled, and in between as the canopy fills. Sized off the sink rate it is
    /// given, so a canopy is described by what it does rather than by an area and a mass.
    /// </summary>
    public static double3 ChuteDrag(double3 localVelocity, MunitionProfile munition, double densityRatio,
                                    double age)
    {
        ArgumentNullException.ThrowIfNull(munition);
        if (!munition.HasChute || densityRatio <= 0.0) return Vec.Zero;

        double open = Math.Clamp((age - munition.ChuteOpensSeconds) / MunitionProfile.ChuteInflationSeconds,
                                 0.0, 1.0);
        if (open <= 0.0) return Vec.Zero;

        double sink = munition.ChuteSinkMetresPerSecond;
        double k = StandardGravity / (sink * sink);
        return localVelocity * (k * open * Vec.Len(localVelocity) * densityRatio);
    }

    /// <summary>The gravity a sink rate is quoted against.</summary>
    public const double StandardGravity = 9.80665;

    /// <summary>
    /// The drag deceleration, as a vector to <b>subtract</b> from a round's acceleration.
    ///
    /// <para>Quadratic in airspeed, so a coasting round bleeds speed instead of holding it, and
    /// scaled by the medium's density so one profile is right on the pad, climbing out, in orbit
    /// and submerged. <see cref="MunitionProfile.AppliedDragK"/> is the value in reference air, which is
    /// why the ratio is 1.0 there.</para>
    ///
    /// <para>Zero in a vacuum, at rest, or for a round that declares no drag — the guards are here
    /// rather than at the call sites so that a round cannot be given a NaN direction by dividing
    /// out a zero speed.</para>
    /// </summary>
    public static double3 Drag(double3 localVelocity, MunitionProfile munition, double densityRatio)
    {
        ArgumentNullException.ThrowIfNull(munition);

        double airspeed = Vec.Len(localVelocity);

        double k = munition.AppliedDragK;
        if (k <= 0.0 || airspeed <= 1e-6 || densityRatio <= 0.0) return Vec.Zero;

        return localVelocity * (k * airspeed * densityRatio);
    }
}
