using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which way a launcher must hold for one tube to throw its round along the line every round is
/// meant to leave on.
///
/// <para>Canted tubes each throw on their own vector, and one aim correction cannot remove that: the
/// launcher turns between releases instead. No shipped launcher is canted; a weapon pack may be. For
/// uncanted tubes the rotation is the identity.</para>
/// </summary>
internal static class ReleasePointing
{
    /// <summary>
    /// The line every round is meant to leave along: the mean of the tube axes.
    ///
    /// <para>Symmetric cants cancel, so this is the launcher's own axis, the direction the aim
    /// correction assumes a round is thrown along.</para>
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

    /// <param name="tubeAxisAtNominal">
    /// Where that tube points at the <em>nominal</em> attitude, latched once. Measured live and applied
    /// to a fixed attitude, the loop settles at half the cant.
    /// </param>
    public static doubleQuat Repoint(double3 tubeAxisAtNominal, double3 referenceAxis)
        => Vec.RotationFromTo(tubeAxisAtNominal, referenceAxis);

    /// <summary>
    /// That rotation applied to an attitude command.
    ///
    /// <para>Both halves are turned together, so this rotates the commanded frame and never has to
    /// decide which body axis is the nose.</para>
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

    /// <summary>
    /// The same turn, built from where the tube is <em>now</em> and applied to where the launcher
    /// is <em>now</em> — rather than from a latched axis applied to the attitude it was told to
    /// hold.
    ///
    /// <para>A latched axis is measured at the actual attitude and the held command is the asked-for
    /// one, so mixing them leaves the standing pointing error in the answer. And KSA's flight computer
    /// tracks pointing only, so the roll drifts and carries a canted tube round a cone of twice the
    /// cant; rebuilding from the live axis every cycle closes that loop (<c>docs/MIRV-NEXT.md</c> item 5).</para>
    ///
    /// <para>Applied to the live attitude, the turn's fixed point is the tube on the line, not half the
    /// cant. Only the direction matters: the roll is carried so the command is a pose at all.</para>
    /// </summary>
    /// <param name="noseAxisNow">
    /// The launcher's own axis as it is now — the mean of its live tube axes, which is what the
    /// cants cancel to.
    /// </param>
    public static bool TryAimTubeFromHere(double3 tubeAxisNow, double3 noseAxisNow,
                                          double3 referenceAxis, double3 heldRoll,
                                          out double3 direction, out double3 roll)
        => TryAimTube(tubeAxisNow, referenceAxis, noseAxisNow, heldRoll, out direction, out roll);

    public static double OffReferenceRadians(double3 tubeAxisNow, double3 referenceAxis)
        => Vec.AngleBetween(tubeAxisNow, referenceAxis);
}
