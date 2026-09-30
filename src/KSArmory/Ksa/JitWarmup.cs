using System.Reflection;
using System.Runtime.CompilerServices;
using KSA;

namespace KSArmory;

/// <summary>
/// Compiles the kill path before anything is shot, on a thread of its own.
///
/// <para>The first kill of a session ran it cold: on the Mk 42, 45 ms of one frame against 8-14 ms for
/// every kill after. This takes the mod's detonation from 12.8 ms to 2.6 and the engine's destroy from
/// 28 to 23 -- the rest of that is KSA building its debris vehicles for the first time, which no amount
/// of compiling reaches: warming all of KSA.dll left it at 21.9.</para>
/// </summary>
internal static class JitWarmup
{
    // The engine's side of a kill: KsaWorld.Destroy is Universe.DestroyVehicleFromEvent, which spawns
    // the destruction explosion and sheds up to twelve pieces as vehicles of their own.
    private static readonly Type[] EngineTypes =
    [
        typeof(Universe), typeof(PartFailure), typeof(ExplosionSystem), typeof(ExplosionVolumeSystem),
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
                // A method the runtime will not compile ahead is compiled on first call, as before.
            }
        }

        return compiled;
    }
}
