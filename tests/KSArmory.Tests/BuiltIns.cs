using System.Runtime.CompilerServices;

namespace KSArmory.Tests;

/// <summary>
/// The weapons this mod ships as data, read out of `src/KSArmory/KSArmory/Weapons.xml` and
/// registered before any test runs.
///
/// <para><b>The suite exercises the shipped file, not a copy of it.</b> A weapon that has moved
/// out of <see cref="Arsenal"/> would otherwise leave every registry-wide invariant — that each
/// launcher names a registered round, that each is also a recognised component — quietly covering
/// one weapon fewer, with nothing failing to say so.</para>
///
/// <para>Registered rather than merely parsed, so `Catalogue` in a test holds what the game holds
/// and the lookups behave identically.</para>
/// </summary>
public static class BuiltIns
{
    [ModuleInitializer]
    internal static void Register()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "KSArmory.sln"))) at = at.Parent;
        if (at is null) throw new InvalidOperationException("cannot find the repository root");

        string file = Path.Combine(at.FullName, "src", "KSArmory", "KSArmory", "Weapons.xml");
        PackResult result = Armoury.Register(File.ReadAllText(file), PackReader.BuiltInSource);

        if (!result.Complete)
        {
            throw new InvalidOperationException(
                $"the shipped definitions were refused: {string.Join("; ", result.Faults)}");
        }
    }

    public static LauncherProfile PantsirS1 => Catalogue.LauncherForPart("KSArmory_Prefab_Launcher6")!;
    public static MunitionProfile Missile57E6 => Catalogue.MunitionNamed("57E6");
    public static MunitionProfile Cannon30Mm => Catalogue.MunitionNamed("30MM");
    public static SensorProfile SearchRadar1Rs1 => Catalogue.SensorNamed("1RS1");
}
