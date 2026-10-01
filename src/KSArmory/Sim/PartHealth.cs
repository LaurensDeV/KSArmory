using System.Runtime.CompilerServices;

namespace KSArmory;

/// <summary>
/// How much of each part's strength is left, from one for an untouched part to nothing for one
/// that breaks.
///
/// <para>A hit costs its <see cref="BlastDamage.Share"/> times the scale, and the share is exactly
/// one where the part fails, so at a scale of one a fresh part breaks exactly where it did before
/// parts had health. What the ledger adds is that a load short of that is not forgotten: two
/// bursts at half the breaking load break the part between them.</para>
///
/// <para>Keyed on the engine's own part object, held weakly, so a part that leaves the world
/// takes its entry with it and nothing has to prune the ledger.</para>
/// </summary>
internal sealed class PartHealth
{
    /// <summary>The ledger the game runs on; a test makes its own.</summary>
    public static PartHealth World { get; } = new();

    private readonly ConditionalWeakTable<object, StrongBox<double>> _taken = new();

    /// <summary>What is left of a part, one for a part nothing has touched.</summary>
    public double Of(object part)
    {
        ArgumentNullException.ThrowIfNull(part);

        return _taken.TryGetValue(part, out StrongBox<double>? taken)
            ? (taken.Value >= 1.0 - Rounding ? 0.0 : Math.Clamp(1.0 - taken.Value, 0.0, 1.0))
            : 1.0;
    }

    /// <summary>
    /// Takes <paramref name="share"/> times <paramref name="scale"/> off a part, and answers whether
    /// it has nothing left — also true for a part already spent, since one still on its craft is
    /// one the engine has yet to break.
    ///
    /// <para><b>No one hit costs more than a whole share.</b> A shell bursting against the skin
    /// loads it hundreds of times over, so uncapped a tenth of that still breaks the part at once and
    /// the scale does nothing against a gun. At a scale of one the cap changes nothing, because a
    /// whole share already breaks a fresh part.</para>
    /// </summary>
    public bool Hit(object part, double share, double scale)
    {
        ArgumentNullException.ThrowIfNull(part);

        double cost = Math.Min(share, 1.0) * scale;
        StrongBox<double> taken = _taken.GetValue(part, static _ => new StrongBox<double>(0.0));
        if (double.IsFinite(cost) && cost > 0.0) taken.Value += cost;

        return taken.Value >= 1.0 - Rounding;
    }

    // Ten hits at a tenth sum to 0.9999999999999999, and must break the part.
    private const double Rounding = 1.0e-9;
}
