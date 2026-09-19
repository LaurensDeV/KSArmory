using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which way a launcher must hold for one tube to throw its round along the line every round is
/// meant to leave on.
///
/// <para>Tubes can be canted — sitting some degrees off the launcher's own axis at their several
/// clock positions — so each round is ejected on its own vector and they scatter. There is one aim
/// for all of them, so no aim correction can remove it: the launcher has to turn between releases
/// and put each tube in turn on the same line, which is what a real post-boost vehicle does. No
/// shipped launcher is canted; the shipped bus's six were straightened for exactly this reason, and
/// a weapon pack may still register one that is not.</para>
///
/// <para>It costs nothing on a launcher this does not describe. A single tube <em>is</em> the mean
/// of its own axes, so the rotation is the identity and everything below reduces to releasing when
/// the vehicle is steady.</para>
/// </summary>
internal static class ReleasePointing
{
    /// <summary>
    /// The line every round is meant to leave along: the mean of the tube axes.
    ///
    /// <para>Symmetric cants cancel in the mean, so this is the launcher's own axis and is the
    /// direction the aim correction already assumes a round is thrown along. One tube gives its own
    /// axis back, which is what makes the whole mechanism free for a launcher that does not
    /// scatter.</para>
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
    /// Where that tube points while the vehicle holds its <em>nominal</em> attitude, latched once.
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

    /// <summary>
    /// The same turn, built from where the tube is <em>now</em> and applied to where the launcher
    /// is <em>now</em> — rather than from a latched axis applied to the attitude it was told to
    /// hold.
    ///
    /// <para><b>Two frames, and they are not the same one.</b> A latched axis and the reference it
    /// averages to were measured at the launcher's <em>actual</em> attitude; the held command is
    /// the attitude it was <em>asked</em> for. Rotating the second by a turn built from the first
    /// leaves the difference between them — the vehicle's standing pointing error — in the answer,
    /// so the fixed point of the whole sequence is one pointing error off the line rather than on
    /// it.</para>
    ///
    /// <para><b>And the roll is nobody's.</b> KSA's flight computer tracks pointing only, so a
    /// launcher's roll about its own axis is free — which carries a canted tube right round a cone
    /// of twice the cant, at whatever rate the vehicle happens to be rolling. A latched axis is
    /// stale the moment that starts. Rebuilding the turn from the live axis every cycle is what
    /// closes the loop on it. <c>docs/MIRV-NEXT.md</c> item 5 has the engine's side.</para>
    ///
    /// <para>This is <em>not</em> the unity-gain trap <see cref="Repoint"/> warns about. That one
    /// applies a live turn to a <em>fixed</em> attitude, which halves as the tube approaches and
    /// settles at half the cant. Applying it to the live attitude has its fixed point exactly where
    /// the tube lies on the line, because the turn is the identity there and nowhere else.</para>
    ///
    /// <para><b>Only the direction is the answer.</b> The roll is carried through so the command is
    /// a pose at all — KSA's flight computer discards it, rebuilding the error as a pointing-only
    /// rotation, so the commanded roll reaches nothing. That is also the whole reason this has to be
    /// re-solved every cycle rather than latched: what the engine will not hold, it will not hold
    /// still.</para>
    /// </summary>
    /// <param name="noseAxisNow">
    /// The launcher's own axis as it is now — the mean of its live tube axes, which is what the
    /// cants cancel to.
    /// </param>
    public static bool TryAimTubeFromHere(double3 tubeAxisNow, double3 noseAxisNow,
                                          double3 referenceAxis, double3 heldRoll,
                                          out double3 direction, out double3 roll)
        => TryAimTube(tubeAxisNow, referenceAxis, noseAxisNow, heldRoll, out direction, out roll);

    /// <summary>How far a tube still is off the line, in radians.</summary>
    public static double OffReferenceRadians(double3 tubeAxisNow, double3 referenceAxis)
        => Vec.AngleBetween(tubeAxisNow, referenceAxis);
}
