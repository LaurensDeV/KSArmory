namespace KSArmory;

/// <summary>
/// Where a mount's gun can point without its barrels or its line of fire meeting the craft it is
/// on: a grid of bearing against elevation, swept once from the craft's own geometry
/// (<see cref="TravelSweep"/>), and read by <see cref="Turret"/> as a traverse arc and a band of
/// elevation at each bearing.
///
/// <para>Bearing column <c>i</c> is <c>i</c> steps round from forward, covering a whole turn;
/// elevation row <c>j</c> runs from the profile's lowest elevation to its highest.</para>
/// </summary>
public sealed class TravelMap
{
    private readonly bool[,] _clear;
    private readonly int[] _floorRow;
    private readonly int[] _ceilingRow;

    public int Bearings { get; }
    public int Elevations { get; }
    public double MinElevationRad { get; }
    public double MaxElevationRad { get; }
    public double BearingStepRad => Math.Tau / Bearings;

    /// <summary>The arc round forward the traverse may cover, or null for a whole turn.</summary>
    public (double Lo, double Hi)? Arc { get; }

    public TravelMap(bool[,] clear, double minElevationRad, double maxElevationRad, double restElevationRad)
    {
        _clear = clear;
        Bearings = clear.GetLength(0);
        Elevations = clear.GetLength(1);
        MinElevationRad = minElevationRad;
        MaxElevationRad = maxElevationRad;

        _floorRow = new int[Bearings];
        _ceilingRow = new int[Bearings];
        int rest = RowNearest(restElevationRad);
        for (int i = 0; i < Bearings; i++) (_floorRow[i], _ceilingRow[i]) = Band(i, rest);

        Arc = ArcRoundForward();
    }

    public double ElevationOfRow(int j)
        => Elevations <= 1 ? MinElevationRad
                           : MinElevationRad + (MaxElevationRad - MinElevationRad) * j / (Elevations - 1);

    public bool IsClear(int bearing, int elevation) => _clear[bearing, elevation];

    /// <summary>
    /// The band of elevation the gun may take at a bearing. Between two columns, the narrower of
    /// the two, so a gun turning between samples never enters what either of them found blocked.
    /// </summary>
    public (double Floor, double Ceiling) BandAt(double bearingRad)
    {
        double at = Mod(bearingRad, Math.Tau) / BearingStepRad;
        int a = (int)Math.Floor(at) % Bearings;
        int b = (a + 1) % Bearings;

        bool usableA = _floorRow[a] >= 0, usableB = _floorRow[b] >= 0;
        if (!usableA && !usableB) return (MinElevationRad, MaxElevationRad);

        int floor = Math.Max(usableA ? _floorRow[a] : int.MinValue, usableB ? _floorRow[b] : int.MinValue);
        int ceiling = Math.Min(usableA ? _ceilingRow[a] : int.MaxValue, usableB ? _ceilingRow[b] : int.MaxValue);
        if (ceiling < floor) ceiling = floor;

        return (ElevationOfRow(floor), ElevationOfRow(ceiling));
    }

    private int RowNearest(double elevationRad)
    {
        if (Elevations <= 1 || !(MaxElevationRad > MinElevationRad)) return 0;
        double t = (elevationRad - MinElevationRad) / (MaxElevationRad - MinElevationRad);
        return Math.Clamp((int)Math.Round(t * (Elevations - 1)), 0, Elevations - 1);
    }

    // The clear run of rows containing the rest elevation, or the one nearest it. (-1, -1) for a
    // column with nothing clear at all.
    private (int Floor, int Ceiling) Band(int i, int rest)
    {
        int seed = -1;
        for (int d = 0; d < Elevations && seed < 0; d++)
        {
            if (rest - d >= 0 && _clear[i, rest - d]) seed = rest - d;
            else if (rest + d < Elevations && _clear[i, rest + d]) seed = rest + d;
        }
        if (seed < 0) return (-1, -1);

        int lo = seed, hi = seed;
        while (lo > 0 && _clear[i, lo - 1]) lo--;
        while (hi < Elevations - 1 && _clear[i, hi + 1]) hi++;
        return (lo, hi);
    }

    private (double, double)? ArcRoundForward()
    {
        if (_floorRow[0] < 0) return (0.0, 0.0);

        int up = 0;
        while (up < Bearings - 1 && _floorRow[(up + 1) % Bearings] >= 0) up++;
        if (up == Bearings - 1) return null;

        int down = 0;
        while (_floorRow[(Bearings - down - 1) % Bearings] >= 0) down++;

        return (-down * BearingStepRad, up * BearingStepRad);
    }

    internal static double Mod(double x, double m) => ((x % m) + m) % m;
}
