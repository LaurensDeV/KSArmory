using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The contact rule: whether a round runs into a body over one sub-step.
///
/// <para>One rule, used twice. A round tests it against what it was aimed at and against every
/// body it merely passes, and those two have to agree — a shell has no idea what it was fired at,
/// and something struck by accident is struck exactly as hard.</para>
/// </summary>
internal static class ContactSweep
{
    /// <summary>
    /// Whether the round comes within <paramref name="trigger"/> of the body during the step, and
    /// when.
    ///
    /// <para>Analytic closest approach rather than a distance sampled at the ends, so a fast round
    /// cannot step clean over a small target. That is what lets a fuse radius go to nearly zero
    /// and still mean contact: at 1100 m/s a shell crosses 18 m of a frame that is otherwise only
    /// measured at both ends.</para>
    /// </summary>
    /// <param name="separation">
    /// Body minus round at the start of the step, both at the <em>round's</em> epoch. A world
    /// sample is end-of-frame while the round is mid-step, and differencing across that carries a
    /// whole frame of the planet's motion — ~500 m at 60 fps, against a trigger measured in
    /// metres. See docs/FRAMES-AND-EPOCHS.md.
    /// </param>
    /// <param name="closingVelocity">The body's velocity relative to the round.</param>
    public static bool TryContact(double3 separation, double3 closingVelocity,
                                  double stepSeconds, double trigger,
                                  out double timeOfContact, out double missDistance)
    {
        timeOfContact = Vec.TimeOfClosestApproach(separation, closingVelocity, stepSeconds);
        missDistance = Vec.Len(separation + closingVelocity * timeOfContact);

        return missDistance <= trigger;
    }

    /// <summary>
    /// The same question for a round that has to <em>touch</em> what it hits: the sphere rejects,
    /// and then the hull decides.
    ///
    /// <para>The two phases are not alternatives. A sphere containing the mesh cannot produce a
    /// false negative, so running it first costs nothing and rejects almost everything — which
    /// matters, because the narrow phase walks triangles. It is also what stops a round at 1100
    /// m/s tunnelling, and the hull test inherits that: it is only ever asked about a step the
    /// round could plausibly have met something on.</para>
    /// </summary>
    /// <param name="hull">Null when nothing can answer, which leaves the sphere's verdict standing.</param>
    /// <param name="body">Opaque handle passed through to the hull test.</param>
    public static bool TryStrike(double3 separation, double3 closingVelocity, double stepSeconds,
                                 double fuseRadius, double bodyRadius,
                                 IHullTest? hull, object? body,
                                 out double timeOfContact, out double missDistance)
    {
        if (!TryContact(separation, closingVelocity, stepSeconds, fuseRadius + bodyRadius,
                        out timeOfContact, out missDistance))
        {
            return false;
        }

        if (hull is null) return true;

        // Relative travel: in a frame riding with the body it stands still for the step, which is
        // what makes a static segment the right query against a mesh sampled once.
        HullVerdict verdict = hull.Judge(body, separation, -closingVelocity * stepSeconds,
                                         out double fraction);

        if (verdict == HullVerdict.Missed) return false;
        if (verdict == HullVerdict.Unknown) return true;

        timeOfContact = Math.Clamp(fraction, 0.0, 1.0) * stepSeconds;
        missDistance = 0.0;
        return true;
    }

    /// <summary>
    /// Whether a round's step reaches a sphere, and where along the step it does.
    ///
    /// <para>What a hull test falls back on for a body with no geometry to cast against. Separate
    /// from <see cref="TryContact"/> because it answers in <em>fractions of the step</em> against a
    /// body held still for it, which is the frame a hull test works in.</para>
    /// </summary>
    /// <param name="separation">Body centre minus round position, at the round's own epoch.</param>
    /// <param name="travel">The round's displacement across the step, relative to the body.</param>
    public static bool TryReachSphere(double3 separation, double3 travel, double radius,
                                      out double fraction)
    {
        // A step is one unit long, so the horizon is 1 and the answer is already a fraction.
        fraction = Vec.TimeOfClosestApproach(separation, -travel, 1.0);

        return Vec.Len(separation - travel * fraction) <= radius;
    }
}
