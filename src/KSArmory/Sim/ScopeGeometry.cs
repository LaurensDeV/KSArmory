using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A plan-position indicator, as coordinates on a round face.
///
/// <para>Geometry only, as <see cref="Reticle"/> is. The face is a unit disc centred on
/// <c>(0, 0)</c>, and north is up the screen, which is negative Y.</para>
/// </summary>
public static class ScopeGeometry
{
    /// <summary>Compass bearing of a contact, in radians, from a local east/north offset.</summary>
    ///
    /// <remarks>
    /// Zero is north, increasing clockwise through east. Swapping the <c>Atan2</c> arguments mirrors
    /// the scope about its north-south line, which looks plausible in every screenshot.
    /// </remarks>
    public static double BearingRad(double east, double north)
    {
        double bearing = Math.Atan2(east, north);
        return bearing < 0.0 ? bearing + Math.Tau : bearing;
    }

    /// <summary>Ground range to a contact, from the same offset. Horizontal only.</summary>
    ///
    /// <remarks>
    /// A plan position indicator shows the ground track, so a contact directly overhead belongs at
    /// the centre however high it is.
    /// </remarks>
    public static double GroundRange(double east, double north) => Math.Sqrt((east * east) + (north * north));

    /// <summary>Whether a contact is past the rim at this range setting.</summary>
    public static bool Beyond(double range, double scopeRange)
        => !(scopeRange > 0.0) || range > scopeRange;

    /// <summary>
    /// Where a contact sits on the face, in unit coordinates about its centre.
    ///
    /// <para>Anything past the rim is clamped to it rather than dropped, so it shows on its bearing;
    /// the caller marks it with <see cref="Beyond"/>.</para>
    /// </summary>
    public static float2 Plot(double bearingRad, double range, double scopeRange)
    {
        if (!double.IsFinite(bearingRad) || !double.IsFinite(range) || !(scopeRange > 0.0))
        {
            return default;
        }

        double unit = Math.Clamp(range / scopeRange, 0.0, 1.0);

        // North up the screen, which is -Y; east to the right, which is +X.
        return new float2((float)(unit * Math.Sin(bearingRad)),
                          (float)(-unit * Math.Cos(bearingRad)));
    }

    public const int RangeStepCount = 3;

    /// <summary>
    /// The range settings a set's scope steps between: all of its reach, half and a quarter.
    ///
    /// <para>Off the set's own range, so the widest rim is where the set stops seeing. The full reach
    /// is rounded up to 100 m under 2 km and to a whole kilometre above. Ordered nearest first.</para>
    /// </summary>
    public static int RangeSteps(double sensorRange, Span<float> into)
    {
        if (into.Length < RangeStepCount) return 0;

        double quantum = sensorRange < 2000.0 ? 100.0 : 1000.0;
        double full = double.IsFinite(sensorRange) && sensorRange > 0.0
            ? Math.Ceiling(sensorRange / quantum) * quantum
            : 5000.0;

        into[0] = (float)(full / 4.0);
        into[1] = (float)(full / 2.0);
        into[2] = (float)full;
        return RangeStepCount;
    }

    /// <summary>
    /// The step a stored setting lands on: the nearest by ratio, or the widest for zero.
    ///
    /// <para>By ratio because the steps are a factor of two apart. Zero is the whole reach, which an
    /// untouched installation opens at.</para>
    /// </summary>
    public static float SnapRange(float stored, ReadOnlySpan<float> steps)
    {
        if (steps.IsEmpty) return stored;
        if (!(stored > 0f) || !float.IsFinite(stored)) return steps[^1];

        float best = steps[0];
        double bestGap = double.MaxValue;
        foreach (float step in steps)
        {
            double gap = Math.Abs(Math.Log(stored / step));
            if (gap < bestGap)
            {
                best = step;
                bestGap = gap;
            }
        }

        return best;
    }

    /// <summary>Most faces a scope will draw a trace for, so a caller can size a buffer.</summary>
    public const int MaxSweepFaces = 8;

    /// <summary>
    /// Where each radiating face of the array is pointing, as compass bearings.
    ///
    /// <para>Off the array's own angle, not a clock, so a stopped or refused array draws a stopped
    /// sweep.</para>
    ///
    /// <para>The angle is subtracted: the part turns about +X with <c>X × Y = Z</c>, and in
    /// <see cref="MapFrame"/>'s right-handed east/north/up the part's +Z is west of its forward, so a
    /// rising angle turns the array anticlockwise. Added, the sweep agrees with the dish only twice a
    /// revolution.</para>
    ///
    /// <para><paramref name="faces"/> radiating sides are spread evenly; the Pantsir's wedge is two.</para>
    /// </summary>
    /// <param name="headingRad">Compass bearing of the craft's own forward.</param>
    /// <param name="arrayRad">The array's angle in the part's frame — its traverse plus its spin.</param>
    /// <param name="into">Filled with one bearing per face; the count returned says how many.</param>
    public static int SweepBearings(double headingRad, double arrayRad, int faces, Span<double> into)
    {
        if (faces <= 0 || into.IsEmpty) return 0;
        if (!double.IsFinite(headingRad) || !double.IsFinite(arrayRad)) return 0;

        int count = Math.Min(faces, into.Length);
        double step = Math.Tau / count;

        for (int i = 0; i < count; i++)
        {
            double bearing = (headingRad - arrayRad + (i * step)) % Math.Tau;
            into[i] = bearing < 0.0 ? bearing + Math.Tau : bearing;
        }

        return count;
    }

    /// <summary>What a blip on the scope is, which decides the symbol drawn for it.</summary>
    public enum Blip
    {
        /// <summary>A craft whose side is known and not hostile: friendly or neutral. Drawn X.</summary>
        Known,

        /// <summary>A craft on a side this set is at war with. Drawn as a red triangle with its heading.</summary>
        Hostile,

        /// <summary>A craft whose side is not known, and so a potential threat. Drawn as a triangle.</summary>
        Unknown,

        /// <summary>Somebody's round in the air. Drawn M.</summary>
        Missile,
    }

    /// <summary>
    /// Which symbol a contact gets.
    ///
    /// <para>A round outranks every side. Emission is a mark beside the symbol
    /// (<see cref="Emitting"/>), so a hostile that switches its set on is still drawn hostile.</para>
    /// </summary>
    public static Blip SymbolFor(bool isRound, Allegiance side)
        => isRound ? Blip.Missile
         : side == Allegiance.Hostile ? Blip.Hostile
         : side == Allegiance.Unknown ? Blip.Unknown
         : Blip.Known;

    /// <summary>The mark a transmitting contact carries beside its symbol.</summary>
    ///
    /// <remarks>
    /// Read off the same source the anti-radiation path homes on, so the two cannot disagree about
    /// who is transmitting.
    /// </remarks>
    public const string Emitting = "R";

    /// <summary>The mark a contact an infrared search set holds carries below its symbol.</summary>
    public const string Heat = "IR";

    /// <summary>Range rings inside the rim, as fractions of the face radius; the rim is drawn as the edge.</summary>
    public static readonly float[] Rings = [0.25f, 0.5f, 0.75f];

    /// <summary>What one ring is worth at a range setting, for a legend that reads in kilometres.</summary>
    public static double RingRange(double scopeRange, int ring)
        => ring < 0 || ring >= Rings.Length ? 0.0 : scopeRange * Rings[ring];
}
