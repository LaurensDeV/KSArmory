using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Moving a point between an instant inside the step and the world sample at the step's end.
///
/// <para>Samples arrive at the step's end and a round bursts part-way through it, so
/// <c>sinceSample</c> is negative. Near a planet a step of its ~30 km/s is hundreds of metres, and a
/// sign slipped either way doubles the error rather than removing it — which is why this is one
/// function rather than an expression retyped at each site.</para>
/// </summary>
public static class InFrame
{
    /// <summary>A point sampled at the step's end, carried back to <paramref name="sinceSample"/>.</summary>
    public static double3 AtBurst(double3 atSample, double3 velocityEcl, double sinceSample)
        => atSample + (velocityEcl * sinceSample);

    /// <summary>A point at <paramref name="sinceSample"/>, carried forward to the step's end.</summary>
    public static double3 AtSample(double3 atBurst, double3 velocityEcl, double sinceSample)
        => atBurst - (velocityEcl * sinceSample);
}
