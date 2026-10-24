using System.Collections.Generic;
using System.IO;

namespace KSArmory;

/// <summary>
/// Writes the include that lets this mod's shader reach KSA's own shader library, at every load.
/// <para>Absolute, because shaderc resolves an include against the requesting file and Core's tree and a
/// mod's have no relative path between them. It names the player's install, so it is never committed.</para>
/// </summary>
internal static class CoreShaderInclude
{
    // Under a folder called Content: KSA's shader hot reload keys every include on the text after the
    // first "Content" in its path, and one with none logs an error on every compile.
    private const string Folder = "Shaders";
    private const string Subfolder = "Content";
    private const string Generated = "CoreAtmosphere.glsl";

    // What it points at, under the game's own Content root. Global.glsl declares the set KSA binds
    // at 0 for every compute pass -- the lighting block and the ambient LUT among it -- and
    // AtmosphereLuts.glsl is what reads them. TextureSet.glsl is KSA's bindless textures, which the
    // weather's coverage maps are read out of.
    private static readonly string[][] Wanted =
    [
        ["Content", "Core", "Shaders", "Common", "Global.glsl"],
        ["Content", "Core", "Shaders", "Atmosphere", "AtmosphereLuts.glsl"],
        ["Content", "Core", "Shaders", "Common", "TextureSet.glsl"],
    ];

    /// <summary>Whether the header was written and names a file that is there.</summary>
    public static bool Available { get; private set; }

    /// <summary>
    /// Returns false and logs why rather than throwing: without it the cloud still draws, on the
    /// approximated sky.
    /// </summary>
    public static bool Write(string modFolder)
    {
        Available = false;

        try
        {
            // The game's own working directory is its install root, which is what makes the Content
            // path resolvable without anybody being asked where the game is.
            List<string> found = [];

            foreach (string[] parts in Wanted)
            {
                string core = Path.GetFullPath(Path.Combine(parts));

                if (!File.Exists(core))
                {
                    Log.Warn($"core shaders: '{core}' is not there, so the cloud keeps its "
                             + "approximated sky; KSA's own atmosphere needs the game's shader tree");
                    return false;
                }

                found.Add(core);
            }

            string shaders = Path.Combine(modFolder, Folder);
            if (!Directory.Exists(shaders))
            {
                Log.Warn($"core shaders: no '{shaders}' to write into");
                return false;
            }

            // Forward slashes whatever the platform: these are GLSL includes, not paths the shell
            // will ever see, and a backslash in one is an escape.
            string body = "// Generated at load. Points at this machine's own KSA install;\n"
                          + "// nothing of the game's is copied here. See Ksa/CoreShaderInclude.cs.\n";

            foreach (string core in found) body += $"#include \"{core.Replace('\\', '/')}\"\n";

            string into = Directory.CreateDirectory(Path.Combine(shaders, Subfolder)).FullName;
            File.WriteAllText(Path.Combine(into, Generated), body);

            Available = true;
            Log.Info($"core shaders: atmosphere available from {Path.GetDirectoryName(found[0])}");

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
