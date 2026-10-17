using Brutal.Numerics;

namespace KSArmory;

/// <summary>One spin motor on a round's base, in the round's mesh frame: where its mouth is and which way it exhausts.</summary>
public readonly record struct SpinNozzle(double3 MouthBody, double3 ExhaustBody);

/// <summary>
/// The spin a round's own motors give it as it leaves -- <b>drawn only</b>: a released round is flown as a
/// point, so nothing here can move where it lands.
///
/// <para>The motors burn for <see cref="SpinUpSeconds"/> and the rate climbs evenly to
/// <see cref="RateDegPerSecond"/> across that burn, then holds. The roll is a closed form of the round's
/// age, so a frame of any length turns the body by exactly the difference between two ages.</para>
/// </summary>
public sealed class RoundSpin
{
    /// <summary>The held rate about the nose (+X of the mesh), signed: negative turns about -X.</summary>
    public required double RateDegPerSecond { get; init; }

    /// <summary>How long the motors burn, which is also how long the spin takes to reach its rate.</summary>
    public required double SpinUpSeconds { get; init; }

    public required SpinNozzle[] Nozzles { get; init; }

    /// <summary>How far the body has rolled about its nose since release, in radians.</summary>
    public double RollRadAt(double age)
    {
        if (!double.IsFinite(age) || age <= 0.0) return 0.0;

        double rate = double.DegreesToRadians(RateDegPerSecond);
        if (SpinUpSeconds <= 0.0) return rate * age;

        return age < SpinUpSeconds
                   ? 0.5 * rate * age * age / SpinUpSeconds
                   : rate * (age - (0.5 * SpinUpSeconds));
    }

    /// <summary>Whether the motors are still burning at this age.</summary>
    public bool Burning(double age) => age >= 0.0 && age < SpinUpSeconds;
}
