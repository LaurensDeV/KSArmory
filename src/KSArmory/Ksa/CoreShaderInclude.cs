using System.IO;

namespace KSArmory;

/// <summary>
/// Writes the one line that lets this mod's shader reach KSA's own shader library.
///
/// <para><b>An absolute include resolves and a relative one cannot.</b> shaderc resolves an include
/// against the requesting file, and Core's shaders sit under the install while a mod's sit under the
/// player's Documents — there is no relative path between the two trees. An absolute one compiles,
/// probed in flight, and the header's own relative includes then resolve inside the Core tree as
/// they always did.</para>
///
/// <para><b>It cannot be committed, because it is the player's install.</b> So it is written beside
/// the mod's own shaders at load, pointing at their copy. Nothing of RocketWerkz's is redistributed
/// — the same arrangement as the build reading their assemblies from wherever the game is.</para>
///
/// <para>Written every load rather than once, because the path it holds is only true of the install
/// that wrote it. A stale one fails as a shader that will not compile, which is not a sentence
/// anybody can read.</para>
/// </summary>
internal static class CoreShaderInclude
{
    // Where this mod's shaders live, relative to the mod folder, and what the generated header is
    // called. KSArmoryCloud.comp includes it by this name and never knows the path inside it.
    private const string Folder = "Shaders";
    private const string Generated = "CoreAtmosphere.glsl";

    // What it points at, under the game's own Content root.
    private static readonly string[] Wanted =
        ["Content", "Core", "Shaders", "Atmosphere", "AtmosphereLuts.glsl"];

    /// <summary>Whether the header was written and names a file that is there.</summary>
    public static bool Available { get; private set; }

    /// <summary>
    /// Writes it, next to the mod's own shaders.
    ///
    /// <para><paramref name="modFolder"/> is where this mod was loaded from. Returns false and says
    /// why rather than throwing: without it the cloud still draws, on the approximated sky.</para>
    /// </summary>
    public static bool Write(string modFolder)
    {
        Available = false;

        try
        {
            // The game's own working directory is its install root, which is what makes the Content
            // path resolvable without anybody being asked where the game is.
            string core = Path.GetFullPath(Path.Combine(Wanted));

            if (!File.Exists(core))
            {
                Log.Warn($"core shaders: '{core}' is not there, so the cloud keeps its approximated "
                         + "sky; KSA's own atmosphere needs the game's shader tree");
                return false;
            }

            string shaders = Path.Combine(modFolder, Folder);
            if (!Directory.Exists(shaders))
            {
                Log.Warn($"core shaders: no '{shaders}' to write into");
                return false;
            }

            // Forward slashes whatever the platform: this is a GLSL include, not a path the shell
            // will ever see, and a backslash in one is an escape.
            string include = core.Replace('\\', '/');

            File.WriteAllText(Path.Combine(shaders, Generated),
                              "// Generated at load. Points at this machine's own KSA install;\n"
                              + "// nothing of the game's is copied here. See Ksa/CoreShaderInclude.cs.\n"
                              + $"#include \"{include}\"\n");

            Available = true;
            Log.Info($"core shaders: atmosphere available from {include}");

            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"core shaders: could not write the include ({e.Message}); the cloud keeps its "
                     + "approximated sky");
            return false;
        }
    }
}
