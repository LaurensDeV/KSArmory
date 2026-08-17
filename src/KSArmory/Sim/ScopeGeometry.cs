using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A plan-position indicator, as coordinates on a round face.
///
/// <para>Geometry only — no drawing, no ImGui, no radar. The same split <see cref="Reticle"/> makes,
/// and for the same reason: where a blip belongs is arithmetic that a test can settle, and what
/// colour it is drawn in is not.</para>
///
/// <para>The face is a unit disc: the centre is <c>(0, 0)</c> and the rim is at radius one, so a
/// caller scales by however many pixels it has. North is <b>up the screen</b>, which is negative Y
/// in screen coordinates — the one conversion worth getting wrong once and never again.</para>
/// </summary>
public static class ScopeGeometry
{
    /// <summary>Compass bearing of a contact, in radians, from a local east/north offset.</summary>
    ///
    /// <remarks>
    /// Compass convention rather than mathematical: zero is north and it increases clockwise
    /// through east. <c>Atan2(east, north)</c> rather than <c>Atan2(north, east)</c> is the whole
    /// of it, and swapping them silently mirrors the scope about its north–south line — which
    /// looks plausible in every screenshot and puts every contact on the wrong side.
    /// </remarks>
    public static double BearingRad(double east, double north)
    {
        double bearing = Math.Atan2(east, north);
        return bearing < 0.0 ? bearing + Math.Tau : bearing;
    }

    /// <summary>Ground range to a contact, from the same offset. Horizontal only.</summary>
    ///
    /// <remarks>
    /// A PPI is a <em>plan</em> position indicator: it shows the ground track, so a contact
    /// directly overhead belongs at the centre however high it is. Using slant range instead puts
    /// an aircraft passing over the site out at its own altitude, which reads as a contact that
    /// never quite arrives.
    /// </remarks>
    public static double GroundRange(double east, double north) => Math.Sqrt((east * east) + (north * north));

    /// <summary>Whether a contact is past the rim at this range setting.</summary>
    public static bool Beyond(double range, double scopeRange)
        => !(scopeRange > 0.0) || range > scopeRange;

    /// <summary>
    /// Where a contact sits on the face, in unit coordinates about its centre.
    ///
    /// <para>Anything past the rim is clamped <em>to</em> the rim rather than dropped, so a contact
    /// closing from outside the range setting is visible on the bearing it is coming from. The
    /// caller draws it differently — see <see cref="Beyond"/> — so "out there, that way" is never
    /// read as "there".</para>
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

    /// <summary>Most faces a scope will draw a trace for, so a caller can size a buffer.</summary>
    public const int MaxSweepFaces = 8;

    /// <summary>
    /// Where each radiating face of the array is pointing, as compass bearings.
    ///
    /// <para>Off the array's own angle, not a clock. A stopped array therefore draws a stopped
    /// sweep, which is the honest reading: the trace says "this set is scanning", and a set whose
    /// array has been halted — or whose drive the engine refused — is not.</para>
    ///
    /// <para><b>The array's angle is subtracted, not added, and that is not a taste.</b> The part
    /// turns about its own +X with <c>X × Y = Z</c>, so a positive angle carries its forward toward
    /// +Z. <see cref="MapFrame"/> builds <c>north = up × east</c>, making east/north/up right-handed
    /// — and in such a triad <c>up × forward</c> is <em>minus</em> east. So the part's +Z is west of
    /// its forward, a rising angle walks the array anticlockwise round the compass, and a bearing
    /// that adds it draws a sweep turning the opposite way to the dish on the vehicle. The two agree
    /// twice a revolution, which is exactly often enough to look like a phase problem rather than a
    /// sign one.</para>
    ///
    /// <para><paramref name="faces"/> is how many sides of the array radiate. One is an ordinary
    /// set; the Pantsir's wedge is two, half a turn apart, and its picture therefore refreshes
    /// twice a revolution. Any count works and they are spread evenly, so a three-face set needs
    /// no new concept.</para>
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

    /// <summary>Range rings drawn inside the rim, as fractions of the face radius.</summary>
    ///
    /// <para>The rim itself is not one of them: it is the range setting and is drawn as the edge,
    /// so a ring on top of it is a second line saying the same thing.</para>
    public static readonly float[] Rings = [0.25f, 0.5f, 0.75f];

    /// <summary>What one ring is worth at a range setting, for a legend that reads in kilometres.</summary>
    public static double RingRange(double scopeRange, int ring)
        => ring < 0 || ring >= Rings.Length ? 0.0 : scopeRange * Rings[ring];
}
