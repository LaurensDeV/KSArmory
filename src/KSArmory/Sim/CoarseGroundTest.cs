using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A ground test that reuses its last answer while the round is nowhere near the ground.
///
/// <para>For the sight: <see cref="BombSight"/> flies a whole trajectory inside one frame, a terrain
/// lookup per step, up to <see cref="BombSight.MaxSteps"/> of them several times a second.</para>
///
/// <para>Within <see cref="NearMetres"/> of the surface every step samples, so the terminal phase is
/// exact; <see cref="ResampleMetres"/> stops a sample from the release point being trusted after the
/// ground has risen under it.</para>
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

    /// <summary>Samples taken since the last <see cref="Reset"/>. Diagnostic only.</summary>
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

    private bool MustResample(double3 positionEcl)
        => Vec.Len(positionEcl - _centre) - _radius <= NearMetres
           || Vec.Len(positionEcl - _sampledAt) >= ResampleMetres;
}
