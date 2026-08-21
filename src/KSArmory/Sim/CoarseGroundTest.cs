using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A ground test that reuses its last answer while the round is nowhere near the ground.
///
/// <para>For the sight rather than for a round in flight. A round samples the surface once a frame,
/// or once a sub-step if its profile asks, which is tens of lookups at most;
/// <see cref="BombSight"/> flies a whole trajectory inside one frame and so pays a terrain lookup
/// per integration step — up to <see cref="BombSight.MaxSteps"/> of them for one pipper, several
/// times a second.</para>
///
/// <para>Almost all of that is spent kilometres above the ground, where the answer cannot change
/// the outcome: the surface is not reachable this step whatever it says. So the sample is reused
/// until either the round comes within <see cref="NearMetres"/> of the cached surface or it has
/// travelled <see cref="ResampleMetres"/> since the sample was taken. The first keeps the terminal
/// phase — the part that decides where the bomb lands — sampled exactly as densely as before. The
/// second is what stops a stale sample from the release point being trusted after the ground has
/// risen underneath it.</para>
/// </summary>
internal sealed class CoarseGroundTest(IGroundTest inner) : IGroundTest
{
    /// <summary>Within this of the cached surface, every step samples again.</summary>
    public const double NearMetres = 600.0;

    /// <summary>And regardless of height, once it has moved this far from where it last sampled.</summary>
    public const double ResampleMetres = 250.0;

    private bool _have;
    private double3 _sampledAt;
    private double3 _centre;
    private double _radius;

    /// <summary>Samples taken, and samples avoided. Diagnostic only.</summary>
    public int Sampled { get; private set; }

    /// <inheritdoc />
    public bool TryGround(double3 positionEcl, out double3 centreEcl, out double radius)
    {
        if (_have && !MustResample(positionEcl))
        {
            centreEcl = _centre;
            radius = _radius;
            return true;
        }

        Sampled++;
        _have = inner.TryGround(positionEcl, out _centre, out _radius);
        _sampledAt = positionEcl;

        centreEcl = _centre;
        radius = _radius;
        return _have;
    }

    /// <summary>Forgets the cached sample, so a fresh trajectory starts by asking.</summary>
    public void Reset()
    {
        _have = false;
        Sampled = 0;
    }

    // Near the surface, or far from where the surface was last looked at.
    private bool MustResample(double3 positionEcl)
        => Vec.Len(positionEcl - _centre) - _radius <= NearMetres
           || Vec.Len(positionEcl - _sampledAt) >= ResampleMetres;
}
