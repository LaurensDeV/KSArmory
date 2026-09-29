using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which cells of a <see cref="TravelMap"/> to test, and in what order: every
/// <see cref="Coarse"/>-th cell first, then only the cells whose surrounding coarse cells disagree.
/// A cell among coarse neighbours that all agree takes their answer untested, so something smaller
/// than the coarse spacing in both directions can be missed.
///
/// <para>Handed out one cell at a time, so a caller can stop after a budget of tests and carry on
/// next frame.</para>
/// </summary>
public sealed class TravelSweep
{
    public const int Coarse = 2;

    /// <summary>How far along the bore the line of fire is tested past the muzzle (m).</summary>
    public const double LineOfFireMetres = 30.0;

    private readonly bool?[,] _cells;
    private readonly Queue<(int, int)> _queue = new();
    private bool _fine;

    public int Bearings { get; }
    public int Elevations { get; }
    public int Tested { get; private set; }
    public bool Done => _fine && _queue.Count == 0;

    public TravelSweep(int bearings, int elevations)
    {
        if (bearings % Coarse != 0) throw new ArgumentException($"bearings must divide by {Coarse}", nameof(bearings));
        Bearings = bearings;
        Elevations = Math.Max(1, elevations);
        _cells = new bool?[Bearings, Elevations];

        for (int i = 0; i < Bearings; i += Coarse)
            for (int j = 0; j < Elevations; j++)
                if (IsCoarseRow(j)) _queue.Enqueue((i, j));
    }

    /// <summary>A sweep sized for one profile: a column every 5 deg, a row about every 5 deg.</summary>
    public static TravelSweep For(LauncherProfile profile)
    {
        double span = Math.Max(0.0, profile.MaxElevationDeg - profile.MinElevationDeg);
        return new TravelSweep(72, (int)Math.Ceiling(span / 5.0) + 1);
    }

    public bool TryNext(out int bearing, out int elevation)
    {
        if (_queue.Count == 0 && !_fine) PlanFine();

        if (_queue.TryDequeue(out (int, int) cell))
        {
            (bearing, elevation) = cell;
            return true;
        }

        (bearing, elevation) = (-1, -1);
        return false;
    }

    public void Record(int bearing, int elevation, bool clear)
    {
        _cells[bearing, elevation] = clear;
        Tested++;
    }

    public TravelMap Result(double minElevationRad, double maxElevationRad, double restElevationRad)
    {
        var clear = new bool[Bearings, Elevations];
        for (int i = 0; i < Bearings; i++)
            for (int j = 0; j < Elevations; j++)
                clear[i, j] = _cells[i, j] ?? true;
        return new TravelMap(clear, minElevationRad, maxElevationRad, restElevationRad);
    }

    private bool IsCoarseRow(int j) => j % Coarse == 0 || j == Elevations - 1;

    private void PlanFine()
    {
        _fine = true;
        for (int i = 0; i < Bearings; i++)
        {
            for (int j = 0; j < Elevations; j++)
            {
                if (_cells[i, j] is not null) continue;

                int i0 = i - i % Coarse, i1 = (i0 + Coarse) % Bearings;
                int j0 = j, j1 = j;
                while (!IsCoarseRow(j0)) j0--;
                while (!IsCoarseRow(j1)) j1++;

                bool? a = _cells[i0, j0];
                if (a is { } agreed && _cells[i1, j0] == agreed && _cells[i0, j1] == agreed && _cells[i1, j1] == agreed)
                {
                    _cells[i, j] = agreed;
                }
                else
                {
                    _queue.Enqueue((i, j));
                }
            }
        }
    }

    /// <summary>
    /// What is tested at one pose, in the launcher part's frame: from the trunnion to the mean of the
    /// muzzles, then on along the bore for <see cref="LineOfFireMetres"/>.
    /// </summary>
    public static (double3 Start, double3 End)[] Probes(LauncherProfile profile, double bearingRad, double elevationRad)
    {
        DrivePose gun = TubeGeometry.GunPose(profile, bearingRad, elevationRad);

        double3 mean = Vec.Zero;
        foreach (double3 muzzle in profile.GunMuzzles) mean += muzzle;
        if (profile.GunMuzzles.Length > 0) mean *= 1.0 / profile.GunMuzzles.Length;

        double3 muzzleAt = gun.Position + gun.Rotation * mean;
        double3 bore = TubeGeometry.GunAxisPartFrame(profile, gun.Rotation);

        return [(gun.Position, muzzleAt), (muzzleAt, muzzleAt + bore * LineOfFireMetres)];
    }
}
