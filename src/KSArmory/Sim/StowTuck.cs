namespace KSArmory;

/// <summary>
/// A parked roll-nod ball nodding on past its rest so its windows face into the nose, and nodding
/// back before the head may slew anywhere else. At the head's own slew rate both ways.
/// </summary>
public sealed class StowTuck
{
    /// <summary>How far past its aim the ball is nodded now (rad).</summary>
    public double Rad { get; private set; }

    /// <summary>Whether the pointing drive may move: never while the ball is still tucked.</summary>
    public bool DriveFree => Rad <= 0.0;

    /// <param name="parkedAndSettled">Resting with nothing to look at, and already at the rest aim.</param>
    public void Step(double dt, bool parkedAndSettled, double tuckRad, double rateRadPerSec)
    {
        if (!(dt > 0.0) || !double.IsFinite(dt)) return;

        double step = Math.Max(0.0, rateRadPerSec) * dt;
        double target = parkedAndSettled ? Math.Max(0.0, tuckRad) : 0.0;
        Rad = Rad < target ? Math.Min(target, Rad + step) : Math.Max(target, Rad - step);
    }

    public void Reset() => Rad = 0.0;
}
