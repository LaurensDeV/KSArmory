using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The shipped definitions file, read and compared field by field against the C# it is replacing.
///
/// <para>This is the gate that makes moving the built-ins out of <see cref="Arsenal"/> a
/// translation rather than a rewrite. Until every weapon is through it, the two live side by side
/// and this says whether they still agree.</para>
/// </summary>
public class BuiltInDefinitionsTests
{
    private static readonly PackContents Shipped = Read();

    private static PackContents Read()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "KSArmory.sln"))) at = at.Parent;
        Assert.NotNull(at);

        string file = Path.Combine(at.FullName, "src", "KSArmory", "KSArmory", "Weapons.xml");
        Assert.True(File.Exists(file), $"no definitions file at {file}");

        // Read against empty registries: everything the file references, it must also declare.
        return PackReader.Read(File.ReadAllText(file), PackReader.BuiltInSource, [], []);
    }

    private static T Named<T>(IReadOnlyList<T> from, string name, Func<T, string> key)
        => Assert.Single(from, x => key(x) == name);

    [Fact]
    public void TheFileIsAcceptedWhole()
    {
        Assert.Empty(Shipped.Faults);
    }

    /// <summary>
    /// The built-ins keep bare keys. Qualifying them would rename every reference a saved setting
    /// or a third-party pack already holds — <c>KSArmory:30MM</c> resolves to the same round, and
    /// that is the point.
    /// </summary>
    [Fact]
    public void TheBuiltInsAreNotQualifiedWithTheModsOwnName()
    {
        foreach (MunitionProfile round in Shipped.Munitions) Assert.DoesNotContain(':', round.Name);
        foreach (SensorProfile set in Shipped.Sensors) Assert.DoesNotContain(':', set.Name);
    }

    [Fact]
    public void TheRoundsMatchTheProfilesTheyReplace()
    {
        foreach (MunitionProfile was in new[] { Arsenal.Missile57E6, Arsenal.Cannon30Mm })
        {
            MunitionProfile now = Named(Shipped.Munitions, was.Name, m => m.Name);

            Assert.Equal(was.DisplayName, now.DisplayName);
            Assert.Equal(was.BodyMarker, now.BodyMarker);
            Assert.Equal(was.FinMarker, now.FinMarker);
            Assert.Equal(was.Guidance, now.Guidance);
            Assert.Equal(was.LaunchSpeed, now.LaunchSpeed);
            Assert.Equal(was.BoostSeconds, now.BoostSeconds);
            Assert.Equal(was.BoostAccel, now.BoostAccel);
            Assert.Equal(was.MaxFlightSeconds, now.MaxFlightSeconds);
            Assert.Equal(was.MinRange, now.MinRange);
            Assert.Equal(was.MaxRange, now.MaxRange);
            Assert.Equal(was.DragK, now.DragK, 9);
            Assert.Equal(was.FuseRadius, now.FuseRadius);
            Assert.Equal(was.FuseArmSeconds, now.FuseArmSeconds);
            Assert.Equal(was.ChargeKg, now.ChargeKg);
            Assert.Equal(was.HitsTerrain, now.HitsTerrain);

            // The derived numbers, because those are what actually reach a round in flight.
            Assert.Equal(was.LethalRadius, now.LethalRadius, 9);
            Assert.Equal(was.BlastRadius, now.BlastRadius, 9);
            Assert.Equal(was.Powered, now.Powered);
            Assert.Equal(was.Steers, now.Steers);
        }
    }

    [Fact]
    public void TheSearchSetMatchesTheProfileItReplaces()
    {
        SensorProfile was = Arsenal.SearchRadar1Rs1;
        SensorProfile now = Named(Shipped.Sensors, was.Name, s => s.Name);

        Assert.Equal(was.DisplayName, now.DisplayName);
        Assert.Equal(was.Range, now.Range);
        Assert.Equal(was.ConeDeg, now.ConeDeg);
        Assert.Equal(was.BoresightSource, now.BoresightSource);
        Assert.Equal(was.ThreatRadius, now.ThreatRadius);
        Assert.Equal(was.LockSeconds, now.LockSeconds);
        Assert.Equal(was.Emits, now.Emits);
    }

    /// <summary>
    /// The hard one, and the reason this weapon was converted first: twelve tubes, four muzzles,
    /// five markers, five pivots and two reference elevations, every one of them generated.
    /// </summary>
    [Fact]
    public void ThePantsirMatchesTheProfileItReplaces()
    {
        LauncherProfile was = Arsenal.PantsirS1;
        LauncherProfile now = Named(Shipped.Launchers, was.PartId, l => l.PartId);

        Assert.Equal(was.DisplayName, now.DisplayName);
        Assert.Equal(was.Munition, now.Munition);
        Assert.Equal(was.Sensor, now.Sensor);
        Assert.Equal(was.GunMunition, now.GunMunition);

        Assert.Equal(was.TurretMarker, now.TurretMarker);
        Assert.Equal(was.PodsMarker, now.PodsMarker);
        Assert.Equal(was.RadarMarker, now.RadarMarker);
        Assert.Equal(was.GunsMarker, now.GunsMarker);
        Assert.Equal(was.OpticBaseMarker, now.OpticBaseMarker);
        Assert.Equal(was.SearchRadarFaces, now.SearchRadarFaces);

        Assert.Equal(was.TubeCount, now.TubeCount);
        for (int i = 0; i < was.TubeCount; i++)
        {
            Assert.True(Vec.Len(was.Tubes[i].Position - now.Tubes[i].Position) < 1e-9,
                        $"tube {i}: {was.Tubes[i].Position} vs {now.Tubes[i].Position}");
            Assert.Equal(was.Tubes[i].HasOwnDirection, now.Tubes[i].HasOwnDirection);
        }

        Assert.Equal(was.GunMuzzles.Length, now.GunMuzzles.Length);
        for (int i = 0; i < was.GunMuzzles.Length; i++)
        {
            Assert.True(Vec.Len(was.GunMuzzles[i] - now.GunMuzzles[i]) < 1e-9, $"muzzle {i}");
        }

        foreach ((double a, double b) in new[]
        {
            (was.TurretPivot.X, now.TurretPivot.X), (was.TurretPivot.Y, now.TurretPivot.Y),
            (was.PodPivotFromTurret.X, now.PodPivotFromTurret.X),
            (was.RadarPivotFromTurret.X, now.RadarPivotFromTurret.X),
            (was.OpticBaseFromTurret.Z, now.OpticBaseFromTurret.Z),
            (was.GunPivotFromTurret.X, now.GunPivotFromTurret.X),
            (was.MuzzleForwardOffset, now.MuzzleForwardOffset),
            (was.TubeRingRadius, now.TubeRingRadius),
        })
        {
            Assert.Equal(a, b, 9);
        }

        // Written in degrees rather than as a rounded radian constant, so these agree to the
        // tolerance validate-parts.py already checks them at rather than exactly. The XML is the
        // more faithful of the two: 22 degrees is what the model was built to.
        Assert.Equal(was.GunReferenceElevationRad, now.GunReferenceElevationRad, 4);
        Assert.Equal(was.PodReferenceElevationRad, now.PodReferenceElevationRad, 4);
    }

    [Fact]
    public void ThePantsirBringsItsComponentRowWithIt()
    {
        ComponentProfile row = Assert.Single(
            Shipped.Components, c => c.PartId == Arsenal.PantsirS1.PartId);

        Assert.Equal(WeaponRole.Launcher, row.Role);
        Assert.Contains(row.Provides, p => p.Role == WeaponRole.FireControl);
        Assert.Contains(row.Provides, p => p.Role == WeaponRole.Gun);
        Assert.Contains(row.Provides, p => p.Role == WeaponRole.Sensor);
    }
}
