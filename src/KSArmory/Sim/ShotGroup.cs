namespace KSArmory;

/// <summary>What a salvo came to, and whether that is a pass.</summary>
/// <param name="Said">
/// The verdict in words, with the bar in it. A pass or fail with no number beside it cannot be
/// argued with, and arguing with it is the only useful thing to do with a scripted shot's verdict.
/// </param>
internal readonly record struct ShotVerdict(bool Pass, string Said);

/// <summary>
/// Where a salvo landed, as the numbers a verdict is read off.
///
/// <para><b>Scored on the worst warhead, not the mean.</b> Every fault this exists to catch puts a
/// common offset on the whole group — an untrimmed separation, a late release, a prediction flown
/// through the wrong air — so for those a mean says what a worst says. What a mean cannot see is
/// the one case it does not cover: a group that scattered either side of the target averages to a
/// shot nobody fired.</para>
///
/// <para><b>A warhead that never arrived counts against the shot.</b> One still in the air when the
/// clock ran out, and one that expired short, are both rounds that were paid for and delivered
/// nothing — scoring only what landed turns a bus that threw five warheads away into a perfect shot
/// with one hit.</para>
/// </summary>
internal sealed class ShotGroup
{
    private readonly List<double> _misses = [];

    /// <summary>How many warheads left the tubes.</summary>
    public int Released { get; private set; }

    /// <summary>How many arrived somewhere that could be measured.</summary>
    public int Arrived => _misses.Count;

    public double Best => _misses.Count == 0 ? double.NaN : _misses.Min();

    public double Worst => _misses.Count == 0 ? double.NaN : _misses.Max();

    public double Mean => _misses.Count == 0 ? double.NaN : _misses.Average();

    /// <summary>How far apart the two ends of the group are — the part no single aim can remove.</summary>
    public double Spread => _misses.Count < 2 ? 0.0 : Worst - Best;

    public void Release() => Released++;

    /// <summary>
    /// One warhead's impact, as its distance from the aim point.
    ///
    /// <para>A miss that cannot be measured is not recorded rather than being recorded as zero: an
    /// unreadable impact and a direct hit are the two things that must never be confused, and one
    /// of them is what a missing number looks like. It then counts as one that never arrived, which
    /// is the honest reading of an outcome nobody can see.</para>
    /// </summary>
    public void Arrive(double missMetres)
    {
        if (double.IsFinite(missMetres) && missMetres >= 0.0) _misses.Add(missMetres);
    }

    public ShotVerdict Judge(double barMetres)
    {
        string bar = $"bar {barMetres / 1000.0:F1} km on the worst of the group";

        if (Released == 0) return new ShotVerdict(false, $"nothing was released ({bar})");

        string flown = $"{Arrived} of {Released} arrived";

        if (Arrived < Released)
        {
            return new ShotVerdict(false, $"{flown}, {Released - Arrived} never did ({bar})");
        }

        return new ShotVerdict(
            Worst <= barMetres,
            $"{flown}; worst {Worst / 1000.0:F2} km, best {Best / 1000.0:F2} km, "
            + $"mean {Mean / 1000.0:F2} km, spread {Spread / 1000.0:F2} km ({bar})");
    }
}
