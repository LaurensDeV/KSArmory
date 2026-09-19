using System;
using System.Reflection;

namespace KSArmory.Tests;

/// <summary>
/// A copy of a shipped munition profile with one thing changed.
///
/// <para><see cref="MunitionProfile"/> is a sealed class, so assigning it hands back the shipped
/// instance and setting anything on it would retune every round of that kind in the process —
/// including the ones other suites are flying, in whatever order the runner happens to pick.</para>
///
/// <para>Copied member by member rather than field by field so a new member cannot be silently
/// left at its default in the copy: that failure is invisible, because the copy still flies.</para>
/// </summary>
internal static class MunitionVariant
{
    public static MunitionProfile Of(MunitionProfile source, Action<MunitionProfile> change)
    {
        MunitionProfile copy = new() { Name = source.Name, DisplayName = source.DisplayName };

        foreach (FieldInfo f in typeof(MunitionProfile).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            f.SetValue(copy, f.GetValue(source));
        }

        foreach (PropertyInfo p in typeof(MunitionProfile).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanRead && p.CanWrite) p.SetValue(copy, p.GetValue(source));
        }

        change(copy);
        return copy;
    }
}
