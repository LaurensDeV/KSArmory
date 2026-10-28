namespace KSArmory;

/// <summary>
/// Parts on fire, and what each costs its part per second until it breaks.
///
/// <para>A fire starts on a hit to a part holding propellant, with the munition's
/// <see cref="MunitionProfile.IgnitionChance"/> per fragment or strike, and only in air. It burns the part
/// at a rate drawn between <see cref="SlowestPerSecond"/> and <see cref="FastestPerSecond"/>, so a part
/// on fire from whole fails in 20 to 100 s -- the aircraft-fire figures in <c>docs/DAMAGE-PLAN.md</c>.
/// It goes out when the air does, and is forgotten when its part leaves the world.</para>
/// </summary>
internal sealed class PartFire
{
    public const double SlowestPerSecond = 0.01;
    public const double FastestPerSecond = 0.05;

    /// <summary>
    /// The share of fires that go up at once rather than burn: a tank's vapour space lit, which the
    /// engine then explodes as it does any part breaking, by what the tank still holds. An estimate.
    /// </summary>
    public const double FlashoverChance = 0.2;

    /// <summary>The ledger the game runs on; a test makes its own.</summary>
    public static PartFire World { get; } = new();

    private readonly Dictionary<object, double> _burning = new(ReferenceEqualityComparer.Instance);

    public int Count => _burning.Count;

    public IEnumerable<object> Burning => _burning.Keys;

    public bool IsBurning(object part) => _burning.ContainsKey(part);

    /// <summary>The chance at least one of <paramref name="hits"/> lights a part, each at <paramref name="perHit"/>.</summary>
    public static double IgnitionChance(int hits, double perHit)
    {
        if (hits <= 0 || !(perHit > 0.0)) return 0.0;

        return 1.0 - Math.Pow(1.0 - Math.Min(perHit, 1.0), hits);
    }

    /// <summary>
    /// Rolls for a fire on a part that took <paramref name="hits"/>; true when one started. A part
    /// already burning is not lit twice.
    /// </summary>
    public bool TryIgnite(object part, int hits, double perHit, Random dice)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(dice);

        if (_burning.ContainsKey(part)) return false;
        if (dice.NextDouble() >= IgnitionChance(hits, perHit)) return false;

        _burning[part] = SlowestPerSecond + (dice.NextDouble() * (FastestPerSecond - SlowestPerSecond));
        return true;
    }

    /// <summary>What <paramref name="seconds"/> of burning costs this part, or zero for one not on fire.</summary>
    public double Cost(object part, double seconds)
        => seconds > 0.0 && _burning.TryGetValue(part, out double rate) ? rate * seconds : 0.0;

    public void PutOut(object part) => _burning.Remove(part);

    public void Clear() => _burning.Clear();
}
