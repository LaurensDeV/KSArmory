using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Finds an optical director on a craft and resolves its two subparts.
///
/// <para>Separate from <see cref="LauncherPart"/> because a director is not launcher gear. It is
/// a part in its own right, on any craft, with or without a weapon anywhere near it — so it is
/// found by walking the craft rather than by looking inside a launcher.</para>
/// </summary>
internal static class OpticPart
{
    /// <summary>
    /// Every director on a vehicle, in part order, appended to <paramref name="into"/>.
    ///
    /// <para>Part order rather than the <see cref="Part"/> reference is what a head keys on: KSA
    /// rebuilds the part tree during staging and docking, and the ordinal survives that.</para>
    /// </summary>
    public static void FindAll(Vehicle vehicle, List<(Part Part, OpticProfile Profile)> into)
    {
        into.Clear();
        try
        {
            ReadOnlySpan<Part> parts = vehicle.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] is { } part && Arsenal.OpticForPart(part.Id) is { } profile)
                {
                    into.Add((part, profile));
                }
            }
        }
        catch
        {
            // Part tree can be mid-rebuild during staging or docking.
        }
    }

    /// <summary>The nth director on a vehicle, or null once that many are no longer fitted.</summary>
    public static (Part Part, OpticProfile Profile)? FindNth(Vehicle vehicle, int ordinal,
                                                             List<(Part, OpticProfile)> scratch)
    {
        if (ordinal < 0) return null;

        FindAll(vehicle, scratch);
        return ordinal < scratch.Count ? scratch[ordinal] : null;
    }

    /// <summary>The gimballed head, which is the only thing on a director that moves.</summary>
    public static Part? FindHead(Part director, OpticProfile profile)
        => FindSubPart(director, profile.HeadMarker);

    /// <summary>The fixed flange and mast.</summary>
    public static Part? FindBase(Part director, OpticProfile profile)
        => FindSubPart(director, profile.BaseMarker);

    private static Part? FindSubPart(Part director, string? marker)
    {
        if (marker is null) return null;

        try
        {
            ReadOnlySpan<Part> subParts = director.SubParts;
            for (int i = 0; i < subParts.Length; i++)
            {
                if (subParts[i] is { } sub && sub.Id is { } id
                    && id.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return sub;
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"optic subpart '{marker}': {e.Message}");
        }

        return null;
    }

    /// <summary>
    /// Turns the head to look along <paramref name="aimPartFrame"/>.
    ///
    /// <para>False when the engine refuses the write, which is latched by the caller: a head that
    /// has stopped moving must not go on claiming to be on target, or the sight paints a settled
    /// bracket over a picture pointing somewhere else.</para>
    /// </summary>
    public static bool TryApplyAim(Part head, OpticProfile profile, double3 aimPartFrame)
    {
        try
        {
            DrivePose pose = OpticGeometry.Pose(profile, aimPartFrame);

            head.Asmb2ParentAsmb = pose.Rotation;
            head.PositionParentAsmb = pose.Position;

            // Part caches its matrices; without this the new value is stored and ignored.
            head.ResetCachedPosMatrixValues();

            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"optic head transform: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Where the head is looking from and along what, both in Ecl.
    ///
    /// <para><paramref name="platformEcl"/> is the caller's, so the eye can be paired with a
    /// platform sample from whichever instant the caller is working in — the engine's own frame
    /// pass uses a different one from the mod's, and mixing them is a frame of the planet's
    /// motion. See <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
    /// </summary>
    public static bool TryViewEcl(Vehicle platform, Part director, OpticProfile profile,
                                  double3 aimPartFrame, double3 platformEcl,
                                  out double3 eyeEcl, out double3 forwardEcl)
    {
        eyeEcl = forwardEcl = Vec.Zero;

        double3 eyePartFrame = OpticGeometry.EyePartFrame(profile, aimPartFrame);

        if (!LauncherPart.TryPartPointEcl(platform, director, eyePartFrame, platformEcl, out eyeEcl))
        {
            return false;
        }

        return LauncherPart.TryLauncherDirectionEcl(platform, director, aimPartFrame, out forwardEcl);
    }
}
