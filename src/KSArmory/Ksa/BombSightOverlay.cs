using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The pipper: where a store released now would land, and the arc it would take there.
///
/// <para>Flown rather than solved — see <see cref="BombSight"/> — so the ring is wherever the bomb
/// will actually go, drag and terrain and all. The alternative is a tidier prediction than the
/// round obeys, which is a sight that lies at exactly the moment it matters.</para>
///
/// <para>Recomputed a few times a second rather than every frame. It is a few hundred integration
/// steps with a terrain sample in each, and nothing about a falling bomb changes fast enough for
/// the difference to show.</para>
/// </summary>
internal sealed class BombSightOverlay
{
    private const double RefreshSeconds = 0.25;

    // The integration step, which is emphatically not the refresh interval. At terminal velocity a
    // bomb crosses 55 m in a fifth of a second, and the ground is sampled once per step -- so a
    // coarse step quantises the impact point to that and the ring hops between two places.
    private const double IntegrationStep = 0.05;

    private const int ArcRibs = 48;

    private static readonly float4 ArcColour = new(1.0f, 0.75f, 0.15f, 1f);
    private static readonly float4 RingColour = new(1.0f, 0.45f, 0.10f, 1f);

    private readonly List<double3> _path = [];
    private readonly List<double3> _next = [];
    private double _sinceRefresh = RefreshSeconds;
    private bool _solved;
    private double3 _impactEcl;

    /// <summary>Forgets the solution, so a system that stops carrying a bomb stops drawing one.</summary>
    public void Clear()
    {
        _solved = false;
        _path.Clear();
    }

    public void Update(WeaponSystem battery, double dtSim)
    {
        if (battery.Platform is not { } platform || battery.Launcher is null) return;

        // Only a store that is released. A guided round goes where it is steered, so a ballistic
        // pipper would be an answer to the wrong question.
        if (battery.Munition.Guidance != GuidanceMode.None) { Clear(); return; }

        _sinceRefresh += Math.Abs(dtSim);
        if (_sinceRefresh < RefreshSeconds && _solved) return;
        _sinceRefresh = 0.0;

        if (!LauncherPart.TryGetTubeMuzzleEcl(platform, battery.Launcher, battery.PodsPart,
                                              battery.Profile, 0, battery.PlatformEcl,
                                              out double3 releaseEcl))
        {
            Clear();
            return;
        }

        double3 tubeEcl = LauncherPart.TryGetTubeAxisEcl(platform, battery.Launcher,
                                                         battery.PodsPart, battery.Profile, 0,
                                                         out double3 axis)
                              ? axis
                              : battery.Boresight;

        double3 craftVel = KsaWorld.VelocityEcl(platform);
        double3 groundVel = KsaWorld.GroundVelocityAt(platform, battery.PlatformEcl);

        // Into a scratch list, so a failed solve leaves the last good one on screen. A sight that
        // blanks for a frame reads as broken, and the answer it had a quarter of a second ago is
        // still very nearly right.
        bool ok = BombSight.TryPredict(
            releaseEcl,
            craftVel + tubeEcl * battery.Munition.LaunchSpeed,
            groundVel,
            battery.Munition,
            at => KsaWorld.GravityAt(platform, at),
            at => KsaWorld.MediumDensityRatioAt(platform, at),
            GroundTest.Shared,
            IntegrationStep,
            _next,
            out double3 impact);

        if (!ok) return;

        _path.Clear();
        _path.AddRange(_next);
        _impactEcl = impact;
        _solved = true;
    }

    public void Draw(WeaponSystem battery)
    {
        if (!_solved || _path.Count < 2) return;
        if (battery.Platform is not { } platform) return;
        if (!KsaWorld.BeginDraw(platform, battery.PlatformEcl)) return;

        // Every rib would be a line per 0.2 s of fall, which is hundreds on a high drop and
        // unreadable. Thinning keeps the arc the same shape and a fraction of the cost.
        int stride = Math.Max(1, _path.Count / ArcRibs);

        for (int i = stride; i < _path.Count; i += stride)
        {
            KsaWorld.DrawLineEcl(_path[i - stride], _path[i], ArcColour);
        }

        KsaWorld.DrawLineEcl(_path[^Math.Min(_path.Count, stride + 1)], _path[^1], ArcColour);

        // Draped on the terrain, so the ring reads as a place on the ground rather than a disc
        // floating over it.
        // Radial at the impact, which is what a ring lying on the ground is flat against. Taken
        // off gravity because that is the one direction the mod already resolves everywhere.
        double3 up = Vec.Unit(KsaWorld.GravityAt(platform, _impactEcl) * -1.0);
        if (Vec.Len2(up) < 0.5) return;

        // The store's own lethal radius, so what the ring circles is what the bomb reaches.
        double radius = Warhead.LethalRadius(battery.Munition.ChargeKg);

        KsaWorld.DrawCircleEcl(_impactEcl, up, radius, RingColour);
        KsaWorld.DrawCircleEcl(_impactEcl, up, radius * 0.15, RingColour, segments: 16);
    }
}
