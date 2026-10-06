namespace KSArmory;

/// <summary>
/// A rotary cannon's barrel cluster: spun up to one barrel per round while it fires, run down
/// after. Drawn only, and on simulated time, so a pause stops it where it is.
/// </summary>
public sealed class GunRotor
{
    public double AngleRad { get; private set; }

    public double RateRadPerSec { get; private set; }

    /// <summary>The cluster's rate at full fire: every barrel fires once a turn.</summary>
    public static double FiringRateRadPerSec(LauncherProfile profile)
        => profile.GunMuzzles.Length > 0
            ? profile.GunRoundsPerMinute / profile.GunMuzzles.Length * (Math.Tau / 60.0)
            : 0.0;

    public void Update(double dt, bool firing, double firingRateRadPerSec,
                       double spinUpSeconds, double spinDownSeconds)
    {
        if (!(dt > 0.0)) return;

        double target = firing ? firingRateRadPerSec : 0.0;
        double seconds = firing ? spinUpSeconds : spinDownSeconds;
        double step = seconds > 0.0 ? firingRateRadPerSec / seconds * dt : double.PositiveInfinity;

        RateRadPerSec += Math.Clamp(target - RateRadPerSec, -step, step);
        AngleRad = Turret.WrapPi(AngleRad + RateRadPerSec * dt);
    }
}
