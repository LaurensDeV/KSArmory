using System.Runtime.CompilerServices;

namespace KSArmory.Tests;

/// <summary>
/// The weapons this mod ships as data, read out of every file in `src/KSArmory/KSArmory/` and
/// registered before any test runs.
///
/// <para><b>The suite exercises the shipped files, not a copy of them, through the game's own
/// <see cref="CoreDefinitions"/>.</b> Read from a copy, a weapon added to one of those files would leave
/// every registry-wide invariant — that each launcher names a registered round, that each is also a
/// recognised component — quietly covering one weapon fewer, with nothing failing to say so.</para>
///
/// <para>Name lookups throw on a miss from here on. A weapon renamed in the move would otherwise
/// come back as whichever round registered first, and every test asking for it would fly that
/// instead.</para>
/// </summary>
public static class BuiltIns
{
    [ModuleInitializer]
    internal static void Register()
    {
        Catalogue.MissingNameThrows = true;

        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "KSArmory.sln"))) at = at.Parent;
        if (at is null) throw new InvalidOperationException("cannot find the repository root");

        string folder = Path.Combine(at.FullName, "src", "KSArmory", PackScan.FolderName);
        List<(string, string)> files =
            [.. Directory.GetFiles(folder, PackScan.FilePattern)
                         .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];

        foreach (CoreFileResult file in CoreDefinitions.Register(files))
        {
            if (!file.Result.Complete)
            {
                throw new InvalidOperationException(
                    $"the shipped {file.FileName} was refused: {string.Join("; ", file.Result.Faults)}");
            }
        }
    }

    public static LauncherProfile PantsirS1 => Catalogue.LauncherForPart("KSArmory_Prefab_Launcher6")!;
    public static MunitionProfile Missile57E6 => Catalogue.MunitionNamed("57E6");
    public static MunitionProfile Cannon30Mm => Catalogue.MunitionNamed("30MM");
    public static SensorProfile SearchRadar1Rs1 => Catalogue.SensorNamed("1RS1");

    public static LauncherProfile SidewinderRail => Catalogue.LauncherForPart("KSArmory_Prefab_SidewinderRail")!;
    public static LauncherProfile AmraamRail => Catalogue.LauncherForPart("KSArmory_Prefab_AmraamRail")!;
    public static LauncherProfile HarmRail => Catalogue.LauncherForPart("KSArmory_Prefab_HarmRail")!;
    public static MunitionProfile Missile9J => Catalogue.MunitionNamed("AIM9J");
    public static MunitionProfile Missile120C => Catalogue.MunitionNamed("AIM120C");
    public static MunitionProfile MissileAgm88 => Catalogue.MunitionNamed("AGM88");
    public static SensorProfile SeekerHeadAim9 => Catalogue.SensorNamed("AIM9SEEK");
    public static SensorProfile SeekerHeadAim120 => Catalogue.SensorNamed("AIM120SEEK");
    public static SensorProfile SeekerHeadAgm88 => Catalogue.SensorNamed("AGM88SEEK");

    public static LauncherProfile Ciws => Catalogue.LauncherForPart("KSArmory_Prefab_Ciws")!;
    public static LauncherProfile Mk42 => Catalogue.LauncherForPart("KSArmory_Prefab_Mk42")!;
    public static LauncherProfile M197 => Catalogue.LauncherForPart("KSArmory_Prefab_M197")!;
    public static MunitionProfile Cannon20Mm => Catalogue.MunitionNamed("20MM");
    public static MunitionProfile Shell5In54 => Catalogue.MunitionNamed("5IN54");
    public static SensorProfile SearchRadarVps2 => Catalogue.SensorNamed("VPS2");
    public static SensorProfile FireControlMk68 => Catalogue.SensorNamed("MK68");
    public static SensorProfile TurretSight => Catalogue.SensorNamed("TSU");

    public static OpticProfile EoDirector => Catalogue.OpticForPart("KSArmory_Prefab_Optic")!;
    public static OpticProfile Litening => Catalogue.OpticForPart("KSArmory_Prefab_Litening")!;
    public static SearchSetProfile Irst => Catalogue.SearchSetForPart("KSArmory_Prefab_Irst")!;
    public static SensorProfile EoSensor => Catalogue.SensorNamed("EO");
    public static SensorProfile PodSensor => Catalogue.SensorNamed("Litening");
    public static SensorProfile InfraredSearchSet => Catalogue.SensorNamed("Irst");

    public static DispenserProfile Ale47Flare => Catalogue.DispenserForPart("KSArmory_Prefab_Ale47Flare")!;
    public static DispenserProfile Ale47Chaff => Catalogue.DispenserForPart("KSArmory_Prefab_Ale47Chaff")!;
    public static DecoyProfile FlareMju7 => Catalogue.DecoyNamed("MJU7");
    public static DecoyProfile ChaffRr170 => Catalogue.DecoyNamed("RR170");

    public static OpticProfile Yal1Turret => Catalogue.OpticForPart("KSArmory_Prefab_Yal1Turret")!;
    public static LauncherProfile Yal1Beam => Catalogue.LauncherForPart("KSArmory_Prefab_Yal1Turret")!;
    public static SensorProfile BeamDirectorSensor => Catalogue.SensorNamed("Yal1BeamDirector");

    public static LauncherProfile MirvBus => Catalogue.LauncherForPart("KSArmory_Prefab_MirvBus")!;
    public static LauncherProfile NukeRack => Catalogue.LauncherForPart("KSArmory_Prefab_NukeRack")!;
    public static MunitionProfile ReentryVehicleMk21 => Catalogue.MunitionNamed("MK21");
    public static MunitionProfile NukeB61 => Catalogue.MunitionNamed("B61");
    public static SensorProfile BusDesignation => Catalogue.SensorNamed("MIRVBUS");
    public static SensorProfile BombSight => Catalogue.SensorNamed("BOMBSIGHT");

    public static LauncherProfile Lau10 => Catalogue.LauncherForPart("KSArmory_Prefab_Lau10")!;
    public static LauncherProfile ZuniPod6 => Catalogue.LauncherForPart("KSArmory_Prefab_ZuniPod6")!;
    public static MunitionProfile RocketZuni => Catalogue.MunitionNamed("ZUNI");
    public static SensorProfile RocketSight => Catalogue.SensorNamed("ZUNISIGHT");
}
