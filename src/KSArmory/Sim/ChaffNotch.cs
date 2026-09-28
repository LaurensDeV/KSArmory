using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Whether a tracking radar has lost a target to chaff, which is what notching is.
///
/// <para>A tracking set holds its target in a range gate and a velocity gate. A target flying square to
/// the line of sight has almost no Doppler, and neither has chaff that has stopped in the air; with a
/// bigger chaff return in the same resolution cell, the set cannot tell the two apart and its gates
/// walk onto the cloud. Off the beam the chaff's Doppler is hundreds of m/s from the target's, and
/// nothing happens. Both conditions are geometry, so the break is decided rather than rolled.</para>
///
/// <para>A broken track takes <see cref="SensorProfile.ChaffReacquireSeconds"/> to be found again. A set
/// with an optical channel then holds it on the camera for as long as the target stays in the notch,
/// whatever chaff follows, so the same pass does not break it twice; one without can be broken again at
/// once. Only leaving the notch lets the next pass work.</para>
/// </summary>
internal sealed class ChaffNotch
{
    /// <summary>How close chaff must be to the target to share its resolution cell, in metres.</summary>
    public const double CellMetres = 150.0;

    private sealed class Break
    {
        public double Since;
        public bool HeldOptically;
    }

    private readonly Dictionary<object, Break> _breaks = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);
    private double _clock;

    /// <summary>Whether the set is still hunting for a target chaff took from it.</summary>
    public bool IsBroken(object? handle)
        => handle is not null && _breaks.TryGetValue(handle, out Break? b) && !b.HeldOptically;

    /// <summary>Starts a scan <paramref name="dt"/> after the last.</summary>
    public void BeginScan(double dt)
    {
        if (double.IsFinite(dt) && dt > 0.0) _clock += dt;
        _seen.Clear();
    }

    /// <summary>Forgets every contact the scan did not ask about.</summary>
    public void EndScan()
    {
        if (_breaks.Count == 0) return;

        List<object>? gone = null;
        foreach (object handle in _breaks.Keys)
        {
            if (!_seen.Contains(handle)) (gone ??= []).Add(handle);
        }

        if (gone is not null) foreach (object h in gone) _breaks.Remove(h);
    }

    /// <summary>
    /// Whether the set loses this contact this scan. Every position and velocity is a sample of one
    /// instant, so the frame's motion cancels in each difference.
    /// </summary>
    public bool Hides(SensorProfile sensor, object handle, double3 originEcl, double3 originVel,
                      double3 targetEcl, double3 targetVel, double targetCrossSection,
                      IReadOnlyList<Decoy> decoys)
    {
        _seen.Add(handle);
        if (!(sensor.ChaffNotchMps > 0f)) return false;

        bool inNotch = InTheNotch(sensor, originEcl, originVel, targetEcl, targetVel);
        bool confused = inNotch
                        && ChaffOutshines(sensor, originEcl, originVel, targetEcl, targetCrossSection, decoys);

        if (_breaks.TryGetValue(handle, out Break? held))
        {
            if (held.HeldOptically)
            {
                if (inNotch) return false;
                _breaks.Remove(handle);
            }
            else if (_clock - held.Since < sensor.ChaffReacquireSeconds)
            {
                return true;
            }
            else if (inNotch && sensor.OpticalBackup)
            {
                // Found again while still in the notch: the camera holds it from here.
                held.HeldOptically = true;
                return false;
            }
            else
            {
                _breaks.Remove(handle);
            }
        }

        if (!confused) return false;

        _breaks[handle] = new Break { Since = _clock };
        return true;
    }

    private static bool InTheNotch(SensorProfile sensor, double3 originEcl, double3 originVel,
                                   double3 positionEcl, double3 velocityEcl)
        => Math.Abs(Signature.ClosingSpeed(originEcl, originVel, positionEcl, velocityEcl)) < sensor.ChaffNotchMps;

    private static bool ChaffOutshines(SensorProfile sensor, double3 originEcl, double3 originVel,
                                       double3 targetEcl, double targetCrossSection, IReadOnlyList<Decoy> decoys)
    {
        for (int i = 0; i < decoys.Count; i++)
        {
            Decoy d = decoys[i];
            if (d.Profile.Kind != DecoyKind.Chaff) continue;
            if (Vec.Len(d.PositionEcl - targetEcl) > CellMetres) continue;
            if (d.Signature <= targetCrossSection) continue;
            if (InTheNotch(sensor, originEcl, originVel, d.PositionEcl, d.VelocityEcl)) return true;
        }

        return false;
    }
}
