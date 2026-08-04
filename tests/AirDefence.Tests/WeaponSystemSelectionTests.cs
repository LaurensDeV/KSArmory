using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// Selecting between weapon systems, tested against registries with <em>several</em> entries.
///
/// <para>The mod ships one launcher, one round and one sensor, and every assertion in
/// <see cref="ArsenalTests"/> is therefore trivially satisfied: with one candidate, "picked the
/// right one" and "picked the only one" are indistinguishable, and the fallback in
/// <c>Arsenal.Named</c> returns element zero, which <em>is</em> the answer. None of it can fail
/// while the registry has one element, so none of it is guarding anything yet.</para>
///
/// <para>These use the internal registry overloads to put three of each in play. See
/// <c>docs/MODULARITY.md</c>.</para>
/// </summary>
public class WeaponSystemSelectionTests
{
    private static LauncherProfile Launcher(string id, string munition, string sensor,
                                            float maxElevation = 82f, float forwardArc = 50f) => new()
    {
        PartId = id,
        DisplayName = id,
        Munition = munition,
        Sensor = sensor,
        TubeOffsets = [new double3(1, 0, 0), new double3(1, 0, 0.4)],
        MaxElevationDeg = maxElevation,
        ForwardArcDeg = forwardArc,
    };

    private static MunitionProfile Munition(string name) =>
        new() { Name = name, DisplayName = name };

    private static SensorProfile Sensor(string name) =>
        new() { Name = name, DisplayName = name };

    private static readonly IReadOnlyList<LauncherProfile> ThreeLaunchers =
    [
        Launcher("Mod_Prefab_Alpha", "round-a", "set-a"),
        Launcher("Mod_Prefab_Bravo", "round-b", "set-b"),
        Launcher("Mod_Prefab_Charlie", "round-c", "set-c"),
    ];

    private static readonly IReadOnlyList<MunitionProfile> ThreeMunitions =
        [Munition("round-a"), Munition("round-b"), Munition("round-c")];

    private static readonly IReadOnlyList<SensorProfile> ThreeSensors =
        [Sensor("set-a"), Sensor("set-b"), Sensor("set-c")];

    // ---- Lookup with real alternatives ---------------------------------

    /// <summary>
    /// Every entry must be reachable, not just the first. A lookup that ignored its argument and
    /// returned element zero passes the shipping suite for the two middle cases.
    /// </summary>
    [Theory]
    [InlineData("Mod_Prefab_Alpha", 0)]
    [InlineData("Mod_Prefab_Bravo", 1)]
    [InlineData("Mod_Prefab_Charlie", 2)]
    public void EveryRegisteredLauncherIsReachableByItsPartId(string partId, int expected)
    {
        Assert.Same(ThreeLaunchers[expected], Arsenal.LauncherForPart(ThreeLaunchers, partId));
    }

    [Fact]
    public void APartFromAnotherModMatchesNothing()
    {
        Assert.Null(Arsenal.LauncherForPart(ThreeLaunchers, "SomeOtherMod_Prefab_Thing"));
        Assert.Null(Arsenal.LauncherForPart(ThreeLaunchers, null));
        Assert.Null(Arsenal.LauncherForPart(ThreeLaunchers, ""));
        Assert.Null(Arsenal.LauncherForPart([], "Mod_Prefab_Alpha"));
    }

    [Theory]
    [InlineData("round-a", 0)]
    [InlineData("round-b", 1)]
    [InlineData("round-c", 2)]
    public void EveryRegisteredMunitionIsReachableByName(string name, int expected)
    {
        Assert.Same(ThreeMunitions[expected], Arsenal.Named(ThreeMunitions, name, m => m.Name));
    }

    /// <summary>
    /// The fallback is real, and with more than one entry it is finally distinguishable from a
    /// successful match. It is deliberate — a launcher naming a round that does not exist is a
    /// typo in Arsenal, not a reason for the game to fall over mid-flight — but it is also silent,
    /// which is worth knowing when a new system fires the wrong round.
    /// </summary>
    [Fact]
    public void AnUnknownNameQuietlyFallsBackToTheFirstEntry()
    {
        Assert.Same(ThreeMunitions[0], Arsenal.Named(ThreeMunitions, "no such round", m => m.Name));
        Assert.Same(ThreeSensors[0], Arsenal.Named(ThreeSensors, "no such sensor", s => s.Name));
    }

    // ---- Switching between systems -------------------------------------

    /// <summary>
    /// All three profiles must move together. A launcher left pointing at the previous system's
    /// round is a wrong-weapon bug with no error attached to it.
    /// </summary>
    [Fact]
    public void SelectingASystemMovesTheLauncherRoundAndSensorTogether()
    {
        var config = new Config();

        config.Select(ThreeLaunchers[2], ThreeMunitions, ThreeSensors);
        Assert.Same(ThreeLaunchers[2], config.Launcher);
        Assert.Equal("round-c", config.Munition.Name);
        Assert.Equal("set-c", config.Sensor.Name);

        config.Select(ThreeLaunchers[0], ThreeMunitions, ThreeSensors);
        Assert.Same(ThreeLaunchers[0], config.Launcher);
        Assert.Equal("round-a", config.Munition.Name);
        Assert.Equal("set-a", config.Sensor.Name);
    }

    [Fact]
    public void SwitchingBackAndForthNeverLeavesAMixedPairing()
    {
        var config = new Config();

        for (int i = 0; i < 12; i++)
        {
            LauncherProfile launcher = ThreeLaunchers[i % ThreeLaunchers.Count];
            config.Select(launcher, ThreeMunitions, ThreeSensors);

            Assert.Equal(launcher.Munition, config.Munition.Name);
            Assert.Equal(launcher.Sensor, config.Sensor.Name);
        }
    }

    /// <summary>
    /// The turret is a single shared drive that every profile reconfigures, so switching systems
    /// must overwrite <em>every</em> limit. One left behind means a launcher silently flying the
    /// previous system's travel — and since both values are plausible, nothing would look wrong.
    /// </summary>
    [Fact]
    public void SwitchingSystemsLeavesNoStaleTurretLimit()
    {
        var turret = new Turret();

        LauncherProfile wide = Launcher("Mod_Prefab_Wide", "round-a", "set-a", maxElevation: 85f, forwardArc: 60f);
        LauncherProfile narrow = Launcher("Mod_Prefab_Narrow", "round-a", "set-a", maxElevation: 40f, forwardArc: 10f);

        wide.ConfigureTurret(turret);
        narrow.ConfigureTurret(turret);

        Assert.Equal(float.DegreesToRadians(narrow.MaxElevationDeg), turret.MaxElevationRad, 9);
        Assert.Equal(float.DegreesToRadians(narrow.ForwardArcDeg), turret.ForwardArcRad, 9);
        Assert.Equal(float.DegreesToRadians(narrow.MinElevationDeg), turret.MinElevationRad, 9);
        Assert.Equal(float.DegreesToRadians(narrow.ForwardMinElevationDeg), turret.ForwardMinElevationRad, 9);
    }

    /// <summary>
    /// Selecting a system must actually change how the drive behaves, not merely which numbers are
    /// stored. A launcher that cannot elevate past 40 degrees must refuse to.
    /// </summary>
    [Fact]
    public void ASelectedSystemsTravelLimitsGovernTheDrive()
    {
        var turret = new Turret();
        Launcher("Mod_Prefab_Narrow", "round-a", "set-a", maxElevation: 40f).ConfigureTurret(turret);

        // Straight up, well past the 40 degree ceiling.
        turret.Track(new double3(1, 0, 0));
        for (int i = 0; i < 600; i++) turret.Update(1.0 / 60.0, 5.0, 5.0);

        Assert.True(turret.ElevationRad <= float.DegreesToRadians(40f) + 1e-9,
            $"turret reached {double.RadiansToDegrees(turret.ElevationRad):F1} degrees past a 40 degree limit");
    }

    // ---- The shape a fixed launcher takes -------------------------------

    /// <summary>
    /// A launcher that does not train is the same profile with nothing to animate. The registry
    /// must accept it and it must still resolve a round and a sensor — this is the shape a static
    /// site or a rocket-mounted rail takes.
    /// </summary>
    [Fact]
    public void AFixedLauncherResolvesItsWeaponsLikeAnyOther()
    {
        LauncherProfile rail = Launcher("Mod_Prefab_FixedRail", "round-b", "set-b");
        var registry = new List<LauncherProfile>(ThreeLaunchers) { rail };

        Assert.Null(rail.TurretMarker);
        Assert.Null(rail.PodsMarker);
        Assert.Null(rail.RadarMarker);

        Assert.Same(rail, Arsenal.LauncherForPart(registry, rail.PartId));

        var config = new Config();
        config.Select(rail, ThreeMunitions, ThreeSensors);
        Assert.Equal("round-b", config.Munition.Name);
        Assert.Equal("set-b", config.Sensor.Name);
    }

    /// <summary>
    /// A fixed launcher's rounds leave on the loft fallback rather than along a tube, and that
    /// path has to produce a usable launch — <see cref="FireGeometry"/> is what a launcher with
    /// nothing that moves depends on entirely.
    /// </summary>
    [Fact]
    public void AFixedLauncherStillProducesAUsableLaunchDirection()
    {
        LauncherProfile rail = Launcher("Mod_Prefab_FixedRail", "round-a", "set-a");
        rail.LaunchAlongTube = false;

        double3 boresight = new(0, 0, 1);
        double3 launchPos = new(0, 0, 0);
        double3 targetPos = new(4000, 0, 200);

        double3 direction = FireGeometry.LaunchDirection(
            alongTube: false, tubeAxis: Vec.Zero, launchPos, targetPos, boresight, rail.LaunchLoft);

        Assert.Equal(1.0, Vec.Len(direction), 9);

        // Toward the target, and lofted above the direct line rather than fired flat at it.
        Assert.True(Vec.Dot(direction, Vec.Unit(targetPos - launchPos)) > 0.9, "launch is not toward the target");
        Assert.True(Vec.Dot(direction, boresight) > Vec.Dot(Vec.Unit(targetPos - launchPos), boresight),
            "a launcher that cannot aim did not loft");
    }
}
