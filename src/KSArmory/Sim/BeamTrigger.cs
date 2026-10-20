namespace KSArmory;

/// <summary>
/// When a beam weapon is lasing, on simulated time: a press of the trigger holds it on for one shot's
/// dwell, automatic fire holds it on for as long as it keeps asking, and either stops the moment the
/// director is not on target or the chemicals are gone.
/// </summary>
internal sealed class BeamTrigger
{
    private double _dwellLeft;

    /// <summary>Whether the beam is commanded on this step.</summary>
    public bool Lasing { get; private set; }

    /// <summary>Seconds of the trigger's dwell still to run.</summary>
    public double DwellLeft => _dwellLeft;

    /// <summary>One press: a whole dwell from now, replacing what was left of the last one.</summary>
    public void Press(double shotSeconds) => _dwellLeft = Math.Max(shotSeconds, 0.0);

    public void Stop()
    {
        _dwellLeft = 0.0;
        Lasing = false;
    }

    /// <summary>
    /// Advances one step and decides whether the beam is on for it. The dwell runs down only while it
    /// is lasing, so a press made while the director is still slewing waits for it rather than being
    /// spent on nothing.
    /// </summary>
    public bool Step(double dt, bool autoWants, bool mayLase)
    {
        Lasing = mayLase && (autoWants || _dwellLeft > 0.0);
        if (Lasing && _dwellLeft > 0.0) _dwellLeft = Math.Max(0.0, _dwellLeft - Math.Max(dt, 0.0));
        return Lasing;
    }
}
