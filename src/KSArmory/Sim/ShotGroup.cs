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
            // Three places, not two. F2 in km is a 10 m quantum, which is a fifth of the group
            // docs/METRE-LEVEL.md rung C is gated on and larger than the whole spread term -- it
            // reported every group of 2026-08-26 as 0.00 or 0.01 km and scored two arms a LOSS on
            // the rounding. Metres would read better and would change what the unit means in every
            // log already written; the parser takes [\d.]+ either way.
            $"{flown}; worst {Worst / 1000.0:F3} km, best {Best / 1000.0:F3} km, "
            + $"mean {Mean / 1000.0:F3} km, spread {Spread / 1000.0:F3} km ({bar})");
    }
}

/// <summary>
/// Where a salvo that went to several places landed — one <see cref="ShotGroup"/> per target,
/// judged on its own and reported together.
///
/// <para><b>A split group has no spread.</b> <see cref="ShotGroup.Spread"/> is the part of a miss
/// no single aim can remove, which is a statement about warheads that share an aim. Warheads
/// deliberately sent twenty kilometres apart have a spread of twenty kilometres, and that measures
/// the plan rather than the shot — so nothing here reports one across targets. Each target keeps
/// its own, and the flight's verdict carries none.</para>
///
/// <para><b>A set of one is the group's own verdict, by the group's own call.</b> Every accuracy
/// number this project owns is measured on the single-target shot, so that path is not written a
/// second time here: <see cref="Judge"/> returns <c>_groups[0].Judge(bar)</c> and
/// <see cref="ShotGroup"/> is untouched.
/// <c>ShotBoardTests.OneTargetIsWordForWordTheGroupsOwnVerdict</c> pins it.</para>
///
/// <para><b>The targets are the ones that were asked for, not the ones that were reached.</b> A
/// stop the walk could not pay for releases nothing, and seeding from the request is what turns
/// that into a failure naming the target rather than a target nobody notices is missing.</para>
/// </summary>
internal sealed class ShotBoard
{
    private readonly ShotGroup[] _groups;

    public ShotBoard(int targets)
    {
        _groups = new ShotGroup[Math.Max(1, targets)];
        for (int i = 0; i < _groups.Length; i++) _groups[i] = new ShotGroup();
    }

    /// <summary>How many places this salvo was sent to.</summary>
    public int Targets => _groups.Length;

    /// <summary>Whether the salvo was split at all — what decides whether anything reports per target.</summary>
    public bool Split => _groups.Length > 1;

    /// <summary>
    /// The group for one target.
    ///
    /// <para>An index outside the set is clamped rather than refused: a round nobody could
    /// attribute still has to be scored somewhere, and dropping it would turn an attribution fault
    /// into a shot that reads better than it was. A single-target flight has one index, so the
    /// clamp is the only thing that can ever happen there.</para>
    /// </summary>
    public ShotGroup For(int target) => _groups[Math.Clamp(target, 0, _groups.Length - 1)];

    /// <summary>How many warheads left the tubes, over every target.</summary>
    public int Released
    {
        get
        {
            int total = 0;
            foreach (ShotGroup group in _groups) total += group.Released;
            return total;
        }
    }

    /// <summary>How many arrived somewhere that could be measured, over every target.</summary>
    public int Arrived
    {
        get
        {
            int total = 0;
            foreach (ShotGroup group in _groups) total += group.Arrived;
            return total;
        }
    }

    /// <summary>One target's own verdict, which is what the line reporting it is written from.</summary>
    public ShotVerdict JudgeTarget(int target, double barMetres) => For(target).Judge(barMetres);

    /// <summary>
    /// The flight's verdict: a pass only when every target's group is one.
    ///
    /// <para>No mean, no best and no spread across targets. Those three describe a group that
    /// shares an aim, and printing them for a split salvo would put a number in the very shape
    /// <c>tools/shot-report.py</c> reads a single-target group off — which is how a walk would come
    /// to be scored as a twenty-kilometre miss.</para>
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

            // Not `reached > worst`: the first finite reading has to beat a NaN, and every
            // comparison against NaN is false.
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
