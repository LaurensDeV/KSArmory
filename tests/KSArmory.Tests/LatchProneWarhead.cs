using System.Reflection;

namespace KSArmory.Tests;

/// <summary>
/// A warhead whose preferred coast step sits above the step its coast actually runs at, as a
/// specimen for the tests that are about the warp latch.
///
/// <para>The shipped Mk 21 no longer does: it asks for 160 ms, which is under the ~185 ms an 8x
/// coast gives, so <see cref="WarpPolicy"/> engages on every frame and the received step is the
/// profile's choice. At 225 ms it engaged on none of them, and the 96 ms it was meant to receive
/// arrived only on the shots where a stray frame happened to trip it — see
/// <c>docs/MIRV-NEXT.md</c> item 7e, which priced that lottery across 38 flown shots.</para>
///
/// <para><b>The mechanism it exercises is still general and still shipped.</b>
/// <c>Sim/WarpPolicy.cs</c> names no round, and any profile whose preferred step is above its own
/// coast step is in this state — including every round that names none and falls back to
/// <c>MaxFaithfulStepSeconds</c>. Pointing these tests at the shipped profile would have quietly
/// stopped measuring the latch the moment the Mk 21 was fixed, which is the trap
/// <see cref="CantedRing"/> was written to avoid.</para>
/// </summary>
internal static class LatchProneWarhead
{
    /// <summary>
    /// The Mk 21 as it was up to the fix, kept as the latch-prone specimen.
    ///
    /// <para>Copied member by member rather than field by field. <see cref="MunitionProfile"/> is a
    /// sealed class, so assigning it hands back the shipped instance and setting anything on it
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
