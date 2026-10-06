namespace KSArmory;

/// <summary>
/// What a mod depending on KSArmory calls to add its weapons.
///
/// <para><b>This is the whole code surface</b>, and it is deliberately three members wide. It is
/// for the pack that ships an assembly: a pack with no code needs none of it, because
/// <c>InstalledPacks</c> reads a <c>KSArmory/</c> folder in every installed mod and registers what
/// it finds through the same <see cref="Register"/>. A pack with code is an ordinary StarMap mod
/// that declares <c>ModDependencies = [ { ModId = "KSArmory" } ]</c> and calls this from its own
/// <c>[StarMapBeforeMain]</c>. StarMap holds it back until this mod is up and shares this assembly
/// with it, both by default.</para>
///
/// <para><b>Text, not profiles.</b> Constructing a <see cref="LauncherProfile"/> means referencing
/// <c>Brutal.Core.Numerics</c> for <c>double3</c>, which is RocketWerkz's to distribute and not
/// ours — so a pack that builds one inherits this repository's whole assembly problem. A pack that
/// passes a string needs <c>KSArmory.dll</c> and <c>StarMap.API.dll</c> and nothing else, and the
/// loader ships the second. Keep it that way.</para>
/// </summary>
public static class Armoury
{
    /// <summary>The definition schema this build reads. A pack states it in its file.</summary>
    public static int Schema => PackReader.Schema;

    /// <summary>
    /// Whether there is still time to register. Open from load until the roster is built; a pack
    /// calling from <c>[StarMapBeforeMain]</c> or <c>[StarMapImmediateLoad]</c> is always in time.
    /// </summary>
    public static bool IsOpen => Catalogue.IsOpen;

    /// <summary>
    /// Read a pack's definitions and register what survives.
    /// </summary>
    /// <param name="definitions">The contents of a weapon-definition file.</param>
    /// <param name="source">What the pack calls itself. It supplies this; nothing looks it up,
    /// and it is what keeps two packs' identically named rounds apart.</param>
    public static PackResult Register(string definitions, string source)
        => Catalogue.Register(
            PackReader.Read(definitions, source, Catalogue.Munitions, Catalogue.Sensors));
}
