using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which way a bus must hold for one tube to throw its warhead along the line every warhead is
/// meant to leave on.
///
/// <para>Tubes are canted — a MIRV bus's six sit six degrees off its own axis at six clock
/// positions — so each warhead is ejected on its own vector and they scatter. There is one aim for
/// all of them, so no aim correction can remove it: the bus has to turn between releases and put
/// each tube in turn on the same line. That is what a real post-boost vehicle does.</para>
/// </summary>
internal static class BusPointing
{
    /// <summary>
    /// The line every warhead is meant to leave along: the mean of the tube axes.
    ///
    /// <para>Their cants cancel in the mean by construction, so this is the launcher's own axis and
    /// is the direction the aim correction already assumes a round is thrown along.</para>
    /// </summary>
    public static double3 ReferenceAxis(ReadOnlySpan<double3> tubeAxes)
    {
        double3 sum = Vec.Zero;

        for (int i = 0; i < tubeAxes.Length; i++)
        {
            double3 axis = Vec.Unit(tubeAxes[i]);
            if (!axis.Equals(Vec.Zero)) sum += axis;
        }

        return Vec.Unit(sum);
    }

    /// <summary>
    /// The rotation, in whatever frame the axes are given in, that carries one tube onto the
    /// reference.
    /// </summary>
    /// <param name="tubeAxisAtNominal">
    /// Where that tube points while the bus holds its <em>nominal</em> attitude, latched once.
    ///
    /// <para>Measuring it live instead makes a unity-gain loop whose fixed point sits at exactly
    /// half the cant: the correction shrinks as the tube approaches the line, so it converges,
    /// looks settled, and leaves half the error behind.</para>
    /// </param>
    public static doubleQuat Repoint(double3 tubeAxisAtNominal, double3 referenceAxis)
        => Vec.RotationFromTo(tubeAxisAtNominal, referenceAxis);

    /// <summary>
    /// That rotation applied to an attitude command.
    ///
    /// <para>Both halves are turned together, which is what makes this a <em>rotation of</em> the
    /// commanded frame rather than a new frame built here. Building one means deciding which body
    /// axis is the nose, and getting that wrong is a vehicle holding a perfectly steady attitude
    /// ninety degrees from the one asked for.</para>
    /// </summary>
    public static bool TryAimTube(double3 tubeAxisAtNominal, double3 referenceAxis,
                                  double3 heldDirection, double3 heldRoll,
                                  out double3 direction, out double3 roll)
    {
        direction = heldDirection;
        roll = heldRoll;

        if (!Vec.IsFinite(tubeAxisAtNominal) || !Vec.IsFinite(referenceAxis)) return false;
        if (!Vec.IsFinite(heldDirection) || !Vec.IsFinite(heldRoll)) return false;

        doubleQuat turn = Repoint(tubeAxisAtNominal, referenceAxis);

        double3 turnedDirection = turn * heldDirection;
        double3 turnedRoll = turn * heldRoll;

        if (!Vec.IsFinite(turnedDirection) || !Vec.IsFinite(turnedRoll)) return false;

        // The aiming frame is built from the cross of these two, so a command that has lost its
        // second direction is not a pose at all.
        if (Vec.Len2(Vec.Cross(turnedRoll, turnedDirection)) <= 0.0) return false;

        direction = turnedDirection;
        roll = turnedRoll;
        return true;
    }

    /// <summary>How far a tube still is off the line, in radians.</summary>
    public static double OffReferenceRadians(double3 tubeAxisNow, double3 referenceAxis)
        => Vec.AngleBetween(tubeAxisNow, referenceAxis);
}
