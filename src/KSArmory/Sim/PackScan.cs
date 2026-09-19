namespace KSArmory;

/// <summary>What an installed mod offers KSArmory, and whether it can be taken.</summary>
public enum PackAvailability
{
    /// <summary>Not a weapon pack. Almost every mod, and not worth a word.</summary>
    NothingToRead,

    /// <summary>A pack, installed and enabled.</summary>
    Ready,

    /// <summary>
    /// A pack whose mod is switched off in the manifest. Reported rather than registered: KSA has
    /// not loaded its parts, so its launchers would name parts nothing declares — but silence here
    /// is indistinguishable from not having installed it, and KSA writes a newly discovered mod
    /// into the manifest disabled without saying so.
    /// </summary>
    Disabled,
}

/// <summary>
/// Where KSArmory looks for weapon definitions somebody else shipped.
///
/// <para><b>A mod is a directory, so another mod's KSArmory content sits in a folder named after
/// this mod inside it.</b> That is the same reasoning that puts system settings at
/// <c>saves/&lt;save&gt;/KSArmory/systems.json</c>, and it buys the same things: several mods can do
/// this without agreeing on filenames, and uninstalling the pack takes its weapons with it.</para>
///
/// <para>This is a <em>convention</em>, not a list. KSArmory holds no record of which packs exist
/// and never learns the name of one — it looks in the same place inside every mod and reads what
/// is there.</para>
/// </summary>
public static class PackScan
{
    /// <summary>The folder inside another mod that KSArmory reads.</summary>
    public const string FolderName = "KSArmory";

    /// <summary>Definition files within it. Several are allowed; one per weapon is fine.</summary>
    public const string FilePattern = "*.xml";

    public static PackAvailability Of(bool enabled, int definitionFiles)
        => definitionFiles <= 0 ? PackAvailability.NothingToRead
           : enabled ? PackAvailability.Ready
           : PackAvailability.Disabled;
}
