namespace KSArmory;

/// <param name="Said">The verdict in words, with the bar in it.</param>
internal readonly record struct ShotVerdict(bool Pass, string Said);

/// <summary>
/// Where a salvo landed. Scored on the worst warhead, since a group scattered either side of the
/// target has a good mean; and a warhead that never arrived fails the shot.
/// </summary>
internal sealed class ShotGroup
{
    private readonly List<double> _misses = [];

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
    /// One warhead's distance from the aim point. One that cannot be measured is not recorded, never
    /// recorded as a direct hit, so it counts as one that never arrived.
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
            // F3: in km, F2 is a 10 m quantum, coarser than the groups it scores. Still km, because
            // every log already written is in km.
            $"{flown}; worst {Worst / 1000.0:F3} km, best {Best / 1000.0:F3} km, "
            + $"mean {Mean / 1000.0:F3} km, spread {Spread / 1000.0:F3} km ({bar})");
    }
}

/// <summary>
/// One <see cref="ShotGroup"/> per target asked for, not per target reached, so a stop the walk could
/// not pay for fails by name. No spread is reported across targets: that would measure the plan.
///
/// <para>A set of one is the group's own verdict, word for word
/// (<c>ShotBoardTests.OneTargetIsWordForWordTheGroupsOwnVerdict</c>).</para>
/// </summary>
internal sealed class ShotBoard
{
    private readonly ShotGroup[] _groups;

    public ShotBoard(int targets)
    {
        _groups = new ShotGroup[Math.Max(1, targets)];
        for (int i = 0; i < _groups.Length; i++) _groups[i] = new ShotGroup();
    }

    public int Targets => _groups.Length;

    public bool Split => _groups.Length > 1;

    /// <summary>
    /// The group for one target. An index outside the set is clamped rather than refused, so a round
    /// nobody could attribute is still scored.
    /// </summary>
    public ShotGroup For(int target) => _groups[Math.Clamp(target, 0, _groups.Length - 1)];

    public int Released
    {
        get
        {
            int total = 0;
            foreach (ShotGroup group in _groups) total += group.Released;
            return total;
        }
    }

    public int Arrived
    {
        get
        {
            int total = 0;
            foreach (ShotGroup group in _groups) total += group.Arrived;
            return total;
        }
    }

    public ShotVerdict JudgeTarget(int target, double barMetres) => For(target).Judge(barMetres);

    /// <summary>
    /// A pass only when every target's group is one. No mean, best or spread across targets: in the
    /// shape <c>tools/shot-report.py</c> reads, a walk would score as a twenty-kilometre miss.
    /// </summary>
    public ShotVerdict Judge(double barMetres)
    {
        if (_groups.Length == 1) return _groups[0].Judge(barMetres);

        bool pass = true;
        double worst = double.NaN;
        int worstAt = 0;
        List<int> failed = [];

        for (int i = 0; i < _groups.Length; i++)
        {
            if (!_groups[i].Judge(barMetres).Pass)
            {
                pass = false;
                failed.Add(i + 1);
            }

            double reached = _groups[i].Worst;

            // Not `reached > worst`: the first finite reading has to beat the NaN.
            if (double.IsFinite(reached) && !(worst >= reached))
            {
                worst = reached;
                worstAt = i;
            }
        }

        string bar = $"bar {barMetres / 1000.0:F1} km on the worst of each target's group";
        string wide = double.IsFinite(worst)
                          ? $"worst {worst / 1000.0:F3} km on target {worstAt + 1}"
                          : "nothing arrived anywhere";

        return new ShotVerdict(
            pass,
            $"{_groups.Length} targets, {Arrived} of {Released} arrived; {wide}"
            + (failed.Count > 0 ? $"; {Named(failed)} failed" : "")
            + $" ({bar})");
    }

    private static string Named(List<int> targets)
    {
        if (targets.Count == 1) return $"target {targets[0]}";

        string all = string.Join(", ", targets.GetRange(0, targets.Count - 1));
        return $"targets {all} and {targets[^1]}";
    }
}
