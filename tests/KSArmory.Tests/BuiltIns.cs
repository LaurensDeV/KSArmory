using System.Runtime.CompilerServices;

namespace KSArmory.Tests;

/// <summary>
/// The weapons this mod ships as data, read out of every file in `src/KSArmory/KSArmory/` and
/// registered before any test runs.
///
/// <para><b>The suite exercises the shipped files, not a copy of them, through the game's own
/// <see cref="CoreDefinitions"/>.</b> A weapon that has moved out of <see cref="Arsenal"/> would
/// otherwise leave every registry-wide invariant — that each launcher names a registered round, that
/// each is also a recognised component — quietly covering one weapon fewer, with nothing failing to
/// say so.</para>
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
}
