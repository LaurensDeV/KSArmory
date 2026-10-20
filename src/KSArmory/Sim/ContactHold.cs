namespace KSArmory;

/// <summary>
/// Whether a craft rests on something, held through the instants a bounce or a roll lifts it: KSA's contact flips
/// 2-6 times a second on a rocket sitting on a slope, and a gun's lay swaps the acceleration it leads with on every
/// flip.
/// </summary>
internal sealed class ContactHold
{
    /// <summary>How long after its last contact a craft still counts as resting.</summary>
    public const double HoldSeconds = 1.0;

    private double _sinceContact = double.PositiveInfinity;

    public bool Rests { get; private set; }

    public bool Update(bool touching, double stepSeconds)
    {
        _sinceContact = touching ? 0.0 : _sinceContact + Math.Max(stepSeconds, 0.0);
        Rests = _sinceContact <= HoldSeconds;
        return Rests;
    }

    public void Reset()
    {
        _sinceContact = double.PositiveInfinity;
        Rests = false;
    }
}
