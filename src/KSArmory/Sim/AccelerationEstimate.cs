using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A contact's acceleration checked against how its velocity actually changed over the last half
/// second.
///
/// <para>The engine's <c>AccelerationBody</c> is an accelerometer: the step's change of velocity less
/// gravity, over the step, impulses included. That makes it exact and current almost always, and wrong
/// when an impulse lands or a piece tumbles through a frame that is spinning — flown, 154–232 m off a
/// fragment whose velocity alone would have led it within 3 m.</para>
///
/// <para>The history is the median of the per-sample changes, taken per axis: a kick lasting a frame
/// or two is outvoted. But it lags by a quarter of its window, and a drone's drag changes over that —
/// used as the reading, it put every first shell 16 m behind its drone at 8.3 s. So it is the check,
/// not the reading: <see cref="Believe"/> keeps the accelerometer unless the two disagree by more than
/// that lag can explain.</para>
/// </summary>
internal sealed class AccelerationEstimate
{
    /// <summary>How far back the samples reach.</summary>
    public const double WindowSeconds = 0.5;

    /// <summary>
    /// How far the accelerometer may differ from the history before it is not believed (m/s²). A
    /// coasting drone's drag changes by about 0.3 m/s² across the lag; the fragment it missed by 232 m
    /// read about 7 wrong.
    /// </summary>
    public const double DisagreementLimit = 3.0;

    /// <summary>The accelerometer while the velocity history agrees with it, the history when it does not.</summary>
    public double3 Believe(double3 accelerometer)
    {
        bool known = TryEstimate(out double3 history);

        if (!Vec.IsFinite(accelerometer)) return known ? history : Vec.Zero;

        return known && Vec.Len(accelerometer - history) > DisagreementLimit ? history : accelerometer;
    }

    /// <summary>Fewer than this and there is no majority to outvote a kick with.</summary>
    public const int MinSamples = 5;

    private readonly Queue<(double Dt, double3 Acceleration)> _samples = new();
    private double _span;
    private double3 _lastVelocity;
    private bool _hasLast;

    /// <summary>Takes the contact's velocity as sampled now, <paramref name="dt"/> after the last.</summary>
    public void Add(double3 velocityEcl, double dt)
    {
        if (!Vec.IsFinite(velocityEcl)) return;

        if (_hasLast && dt > 0.0 && double.IsFinite(dt))
        {
            _samples.Enqueue((dt, (velocityEcl - _lastVelocity) * (1.0 / dt)));
            _span += dt;

            // Kept only while the rest still covers the window, so the window is always full.
            while (_samples.Count > MinSamples && _span - _samples.Peek().Dt >= WindowSeconds)
            {
                _span -= _samples.Dequeue().Dt;
            }
        }

        _lastVelocity = velocityEcl;
        _hasLast = true;
    }

    /// <summary>The median acceleration over the window, or false until there is enough to vote.</summary>
    public bool TryEstimate(out double3 acceleration)
    {
        acceleration = Vec.Zero;
        if (_samples.Count < MinSamples) return false;

        int n = _samples.Count;
        double[] x = new double[n], y = new double[n], z = new double[n];

        int i = 0;
        foreach ((_, double3 a) in _samples)
        {
            x[i] = a.X;
            y[i] = a.Y;
            z[i] = a.Z;
            i++;
        }

        acceleration = new double3(Median(x), Median(y), Median(z));
        return Vec.IsFinite(acceleration);
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        int mid = values.Length / 2;
        return values.Length % 2 == 1 ? values[mid] : 0.5 * (values[mid - 1] + values[mid]);
    }
}
