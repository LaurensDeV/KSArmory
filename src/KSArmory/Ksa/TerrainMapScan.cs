using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A cached square of terrain heights around a craft, sampled off the engine's height field.
///
/// <para><b>The cache is the whole design.</b> One refresh is <c>Cells²</c> height-field lookups —
/// 4096 at the default — and <see cref="SensorProfile.TerrainSamples"/> defaults to zero because
/// that per-frame cost has never been measured. So this pays it on movement and on a timer
/// instead, the same way the bomb sight re-solves a few times a second rather than per frame. The
/// map is a picture of the ground; the ground does not move.</para>
///
/// <para>Heights are kept as metres above the mean sphere, and cells the field would not answer for
/// are left <em>unknown</em> rather than zero — reading zero from an unreadable field puts a whole
/// square at sea level with nothing to say it did.</para>
/// </summary>
internal sealed class TerrainMapScan
{
    /// <summary>Cells across, both ways.</summary>
    public int Cells { get; private set; }

    /// <summary>Metres across the square this grid was sampled at.</summary>
    public double Span { get; private set; }

    /// <summary>The frame it was sampled in, or null if it has never been sampled.</summary>
    public MapFrame? Frame { get; private set; }

    /// <summary>Height above the mean sphere per cell (m), row-major from the south-west.</summary>
    public double[] Height { get; private set; } = [];

    /// <summary>False where the field made no claim.</summary>
    public bool[] Known { get; private set; } = [];

    /// <summary>The lowest and highest known cell (m), for scaling the relief.</summary>
    public double Lowest { get; private set; }

    public double Highest { get; private set; }

    /// <summary>How many cells the last refresh could not read.</summary>
    public int Unknown { get; private set; }

    /// <summary>Wall-clock seconds the last refresh took, which is the number worth watching.</summary>
    public double LastScanMs { get; private set; }

    private double3 _sampledAt;

    // Frames since the last scan, not simulated seconds. The rule that fire control runs on
    // simulated time is about *integrating* the world; this is a cache refresh, and terrain
    // streams in on the engine's own schedule whether the world is paused or not -- so a paused
    // game should still pick it up.
    private int _sinceScan;

    /// <summary>Throws the grid away, so the next update samples from scratch.</summary>
    public void Invalidate() => Frame = null;

    // Roughly ten seconds at 60 fps, which is often enough for terrain streaming in.
    private const int RefreshFrames = 600;

    /// <summary>
    /// Re-samples if the anchor has moved far enough, the span has changed, or enough frames have
    /// passed. Called once a frame while the map is open, and not at all while it is shut.
    /// </summary>
    public void Update(Celestial? body, double3 anchorEcl, double span, int cells)
    {
        if (body is null || !(span > 0.0) || cells < 2) return;

        _sinceScan++;

        bool stale = Frame is null
                     || Math.Abs(span - Span) > 1e-6
                     || cells != Cells
                     || _sinceScan >= RefreshFrames
                     || Vec.Len(anchorEcl - _sampledAt) > TerrainMap.RefreshDistance(span);

        if (!stale) return;

        double3 centre;
        double3 axis;
        try
        {
            centre = body.GetPositionEcl();
            axis = body.GetRotationAxisCce();
        }
        catch (Exception e)
        {
            Log.Warn($"map: cannot read the body's frame -- {e.Message}");
            return;
        }

        if (MapFrame.TryAt(centre, anchorEcl, axis) is not { } frame)
        {
            // At a pole, where there is no bearing. Nothing to draw rather than a rose pointing
            // at an arbitrary perpendicular.
            Frame = null;
            return;
        }

        Sample(new TerrainHeights(body), frame, span, cells);

        _sampledAt = anchorEcl;
        _sinceScan = 0;
    }

    private void Sample(ITerrainHeights heights, MapFrame frame, double span, int cells)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        if (Height.Length != cells * cells)
        {
            Height = new double[cells * cells];
            Known = new bool[cells * cells];
        }

        double lowest = double.MaxValue;
        double highest = double.MinValue;
        int unknown = 0;

        for (int j = 0; j < cells; j++)
        {
            double north = TerrainMap.CellOffset(j, cells, span);

            for (int i = 0; i < cells; i++)
            {
                double east = TerrainMap.CellOffset(i, cells, span);
                int at = j * cells + i;

                if (heights.TryHeight(frame.DirectionAt(east, north), out double metres))
                {
                    Height[at] = metres;
                    Known[at] = true;
                    lowest = Math.Min(lowest, metres);
                    highest = Math.Max(highest, metres);
                }
                else
                {
                    Known[at] = false;
                    unknown++;
                }
            }
        }

        Frame = frame;
        Span = span;
        Cells = cells;
        Unknown = unknown;
        Lowest = lowest == double.MaxValue ? 0.0 : lowest;
        Highest = highest == double.MinValue ? 0.0 : highest;

        LastScanMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                     * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        Log.Debug($"map: {cells}x{cells} over {span:F0} m in {LastScanMs:F1} ms, "
                  + $"{unknown} cell(s) unreadable, relief {Lowest:F0}-{Highest:F0} m");
    }

    /// <summary>Metres per cell, which is what the relief shading is measured against.</summary>
    public double MetresPerCell => Cells > 0 ? Span / Cells : 0.0;

    /// <summary>The height at a cell, or null where the field made no claim.</summary>
    public double? At(int i, int j)
    {
        if (i < 0 || j < 0 || i >= Cells || j >= Cells) return null;

        int at = j * Cells + i;

        return Known[at] ? Height[at] : null;
    }

    /// <summary>
    /// The relief shading at a cell, from its neighbours. Null where it or any neighbour is
    /// unknown, so an edge of readable terrain fades out rather than shading against a cliff that
    /// is really the end of the data.
    /// </summary>
    public double? ReliefAt(int i, int j)
    {
        double? here = At(i, j);
        if (here is null) return null;

        double west = At(i - 1, j) ?? here.Value;
        double east = At(i + 1, j) ?? here.Value;
        double south = At(i, j - 1) ?? here.Value;
        double north = At(i, j + 1) ?? here.Value;

        return TerrainMap.Relief(west, east, south, north, MetresPerCell);
    }
}
