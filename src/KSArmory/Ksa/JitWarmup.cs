using System.Reflection;
using System.Runtime.CompilerServices;
using KSA;

namespace KSArmory;

/// <summary>
/// Compiles the kill path before anything is shot, on a thread of its own. What it buys, and what
/// it cannot: docs/BLOCKED-ON-KSA.md, "The first kill of a session pays for KSA's destroy running cold".
/// </summary>
internal static class JitWarmup
{
    // The engine's side of a kill, Universe.DestroyVehicleFromEvent. Delete the day KSA's first
    // destroy runs warm; docs/BLOCKED-ON-KSA.md has the check.
    private static readonly Type[] EngineTypes =
    [
        typeof(Universe), typeof(PartFailure), typeof(ExplosionSystem), typeof(ExplosionVolumeManager),
        typeof(ExplosionVolumeSpawner), typeof(ExplosionVolumeStore),
        typeof(ExplosionFlashSystem), typeof(Vehicle),
    ];

    public static void Start()
    {
        Task.Run(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int compiled = 0;

            foreach (Type type in typeof(JitWarmup).Assembly.GetTypes()) compiled += Prepare(type);
            foreach (Type type in EngineTypes) compiled += Prepare(type);

            Log.Info($"compiled {compiled} method(s) ahead of the first shot in {clock.Elapsed.TotalMilliseconds:F0} ms");
        });
    }

    private static int Prepare(Type type)
    {
        if (type.ContainsGenericParameters) return 0;

        int compiled = 0;
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                 | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (MethodBase method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
        {
            if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null) continue;

            try
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                compiled++;
            }
            catch
            {
                // Compiled on first call instead.
            }
        }

        return compiled;
    }
}
