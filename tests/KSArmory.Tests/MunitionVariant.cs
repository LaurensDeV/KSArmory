using System;
using System.Reflection;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="MunitionProfile"/> is a shared class instance, so changing a shipped one retunes it for every suite.
/// Copied by reflection so a new member cannot be left at its default in the copy, which would still fly.
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
