using System.Reflection;

namespace KSArmory.Tests;

/// <summary>
/// A warhead whose preferred coast step sits above the step its coast actually runs at, as a
/// specimen for the tests that are about the warp latch.
///
/// <para>The shipped Mk 21 does not: it asks for 180 ms, under the ~185 ms an 8x coast gives, so
/// <see cref="WarpPolicy"/> engages on every frame. At 225 ms it engages on none, and the step it is
/// meant to receive arrives only when a stray frame trips it — <c>docs/MIRV-NEXT.md</c> item 7e.</para>
///
/// <para><b>The mechanism it exercises is still general and still shipped.</b>
/// <c>Sim/WarpPolicy.cs</c> names no round, and any profile whose preferred step is above its own
/// coast step is in this state — including every round that names none and falls back to
/// <c>MaxFaithfulStepSeconds</c>. Pointed at the shipped profile, these tests would not measure the
/// latch at all, which is the trap <see cref="CantedRing"/> was written to avoid.</para>
/// </summary>
internal static class LatchProneWarhead
{
    /// <summary>
    /// The Mk 21 with a 225 ms preferred step, the latch-prone specimen.
    ///
    /// <para>Copied member by member. <see cref="MunitionProfile"/> is a class, so assigning it hands
    /// back the shipped instance and setting anything on it
    /// would retune every Mk 21 in the process — including the ones other suites are flying.</para>
    /// </summary>
    public static MunitionProfile Profile
    {
        get
        {
            MunitionProfile source = DeorbitShot.Warhead;
            MunitionProfile copy = new() { Name = source.Name, DisplayName = source.DisplayName };

            foreach (FieldInfo f in typeof(MunitionProfile).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                f.SetValue(copy, f.GetValue(source));
            }

            foreach (PropertyInfo p in typeof(MunitionProfile).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.CanRead && p.CanWrite) p.SetValue(copy, p.GetValue(source));
            }

            copy.PreferredStepSeconds = 0.225f;
            return copy;
        }
    }
}
