using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A craft's aerodynamic force (m²) and moment about its centre of mass (m³) per unit of dynamic pressure,
/// in its own body frame, against the direction the air flows past it -- and how each changes with the
/// body rate over the airspeed, which is the engine's rate damping -- sampled over a sphere of directions.
///
/// <para>The samples sit on an icosphere and are resampled once, linearly within its triangles, onto a
/// 5° latitude-longitude grid read back bilinearly: the lead flies a target forward tens of thousands of
/// steps a solve, and a lookup then costs the same whatever the sample count.</para>
/// </summary>
public sealed class AeroTable
{
    private const int LonCells = 72;
    private const int LatRows = 37;
    private const double CellDeg = 5.0;

    // Which samples each grid point lies between, and how much of each: the same for every table on
    // one sphere, so worked out once a level and not on the frame a table completes.
    private static readonly Dictionary<int, (double3[] Directions, (int A, int B, int C, double U, double V, double W)[] Grid)> Spheres = [];

    // Per direction: force, moment, then the force's and the moment's change per unit of rate over
    // airspeed (rad/m) about each body axis.
    public const int Channels = 8;

    private readonly double3[] _directions;
    private readonly (int A, int B, int C, double U, double V, double W)[] _weights;
    private readonly double3[] _samples;
    private readonly bool[] _set;
    private double3[]? _grid;
    private int _filled;

    /// <param name="levels">How often the icosahedron is subdivided: 2 gives 162 directions 16° apart, 3 gives 642 at 8°.</param>
    public AeroTable(int levels)
    {
        lock (Spheres)
        {
            if (!Spheres.TryGetValue(levels, out var sphere))
            {
                sphere = Build(levels);
                Spheres[levels] = sphere;
            }

            (_directions, _weights) = sphere;
        }

        _samples = new double3[_directions.Length * Channels];
        _set = new bool[_directions.Length];
    }

    public int Count => _directions.Length;

    public int Filled => _filled;

    public bool IsComplete => _grid is not null;

    public double3 Direction(int index) => _directions[index];

    /// <summary>A force alone, with no moment and no rate damping.</summary>
    public void Set(int index, double3 forcePerQ) => Set(index, [forcePerQ, Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero, Vec.Zero]);

    /// <summary>Every channel of one direction, in the order <see cref="Channels"/> describes.</summary>
    public void Set(int index, ReadOnlySpan<double3> channels)
    {
        if (!_set[index]) _filled++;
        _set[index] = true;
        channels.Slice(0, Channels).CopyTo(_samples.AsSpan(index * Channels, Channels));
    }

    /// <summary>Resamples the filled directions onto the grid; a table is read only once sealed.</summary>
    public void Seal()
    {
        if (_filled < _directions.Length) return;

        var grid = new double3[_weights.Length * Channels];
        for (int i = 0; i < _weights.Length; i++)
        {
            (int a, int b, int c, double u, double v, double w) = _weights[i];
            for (int k = 0; k < Channels; k++)
            {
                grid[(i * Channels) + k] = (_samples[(a * Channels) + k] * u) + (_samples[(b * Channels) + k] * v)
                                           + (_samples[(c * Channels) + k] * w);
            }
        }

        _grid = grid;
    }

    /// <summary>The force per unit dynamic pressure for air flowing along <paramref name="flowBody"/>, a unit body-frame direction, the body not turning.</summary>
    public double3 ForcePerQ(double3 flowBody) => Channel(flowBody, 0);

    /// <summary>
    /// Force and moment per unit dynamic pressure for air along <paramref name="flowBody"/> with the body
    /// turning at <paramref name="ratePerSpeed"/>, its rates over the airspeed (rad/m).
    /// </summary>
    public (double3 Force, double3 Moment) Loads(double3 flowBody, double3 ratePerSpeed)
    {
        double3 force = Channel(flowBody, 0), moment = Channel(flowBody, 1);
        for (int axis = 0; axis < 3; axis++)
        {
            double rate = axis == 0 ? ratePerSpeed.X : axis == 1 ? ratePerSpeed.Y : ratePerSpeed.Z;
            if (rate == 0.0) continue;
            force += Channel(flowBody, 2 + axis) * rate;
            moment += Channel(flowBody, 5 + axis) * rate;
        }

        return (force, moment);
    }

    private double3 Channel(double3 flowBody, int k)
    {
        if (_grid is null) return Vec.Zero;

        double lat = double.RadiansToDegrees(Math.Asin(Math.Clamp(flowBody.Z, -1.0, 1.0)));
        double lon = double.RadiansToDegrees(Math.Atan2(flowBody.Y, flowBody.X));
        if (lon < 0.0) lon += 360.0;

        double r = Math.Clamp((lat + 90.0) / CellDeg, 0.0, LatRows - 1.000001);
        double c = lon / CellDeg;
        int r0 = (int)r, c0 = (int)c % LonCells, c1 = (c0 + 1) % LonCells;
        double fr = r - r0, fc = c - Math.Floor(c);

        double3 low = Lerp(_grid[(((r0 * LonCells) + c0) * Channels) + k], _grid[(((r0 * LonCells) + c1) * Channels) + k], fc);
        double3 high = Lerp(_grid[((((r0 + 1) * LonCells) + c0) * Channels) + k],
                            _grid[((((r0 + 1) * LonCells) + c1) * Channels) + k], fc);
        return Lerp(low, high, fr);
    }

    // Each grid point linear within the triangle the ray along it passes through, so every sample is met exactly.
    private static (double3[], (int, int, int, double, double, double)[]) Build(int levels)
    {
        (double3[] points, (int A, int B, int C)[] faces) = Icosphere(levels);

        // A ray can only pass through a triangle whose centre is within the triangle's own reach of it.
        var centres = new double3[faces.Length];
        double widest = 1.0;
        for (int f = 0; f < faces.Length; f++)
        {
            (int a, int b, int c) = faces[f];
            centres[f] = Vec.Unit(points[a] + points[b] + points[c]);
            widest = Math.Min(widest, Math.Min(Vec.Dot(centres[f], points[a]),
                                               Math.Min(Vec.Dot(centres[f], points[b]), Vec.Dot(centres[f], points[c]))));
        }

        var grid = new (int, int, int, double, double, double)[LatRows * LonCells];
        for (int row = 0; row < LatRows; row++)
        {
            for (int col = 0; col < LonCells; col++)
            {
                double3 at = FromLatLon(-90.0 + (row * CellDeg), col * CellDeg);
                for (int f = 0; f < faces.Length; f++)
                {
                    if (Vec.Dot(at, centres[f]) < widest - 1e-9) continue;

                    (int a, int b, int c) = faces[f];
                    double3 pa = points[a], pb = points[b], pc = points[c];
                    double det = Vec.Dot(pa, Vec.Cross(pb, pc));
                    double u = Vec.Dot(at, Vec.Cross(pb, pc)) / det;
                    double v = Vec.Dot(pa, Vec.Cross(at, pc)) / det;
                    double w = Vec.Dot(pa, Vec.Cross(pb, at)) / det;
                    if (u < -1e-12 || v < -1e-12 || w < -1e-12) continue;

                    double sum = u + v + w;
                    grid[(row * LonCells) + col] = (a, b, c, u / sum, v / sum, w / sum);
                    break;
                }
            }
        }

        return (points, grid);
    }

    /// <summary>Unit directions evenly over the sphere, and the triangles between them: an icosahedron subdivided <paramref name="levels"/> times (12, 42, 162, 642).</summary>
    public static (double3[] Points, (int A, int B, int C)[] Faces) Icosphere(int levels)
    {
        double t = (1.0 + Math.Sqrt(5.0)) / 2.0;
        var points = new List<double3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int i = 0; i < points.Count; i++) points[i] = Vec.Unit(points[i]);

        var faces = new List<(int A, int B, int C)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2),
            (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5),
            (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };

        for (int level = 0; level < levels; level++)
        {
            var midpoints = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                (int, int) key = a < b ? (a, b) : (b, a);
                if (midpoints.TryGetValue(key, out int index)) return index;
                points.Add(Vec.Unit((points[a] + points[b]) * 0.5));
                midpoints[key] = points.Count - 1;
                return points.Count - 1;
            }

            var next = new List<(int, int, int)>(faces.Count * 4);
            foreach ((int a, int b, int c) in faces)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.Add((a, ab, ca));
                next.Add((b, bc, ab));
                next.Add((c, ca, bc));
                next.Add((ab, bc, ca));
            }

            faces = next;
        }

        return (points.ToArray(), faces.ToArray());
    }

    private static double3 FromLatLon(double latDeg, double lonDeg)
    {
        double lat = double.DegreesToRadians(latDeg), lon = double.DegreesToRadians(lonDeg);
        return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    private static double3 Lerp(double3 a, double3 b, double t) => a + ((b - a) * t);
}
