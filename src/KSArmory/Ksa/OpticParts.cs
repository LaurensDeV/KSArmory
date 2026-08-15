using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Finds an optical director on a craft and resolves the subparts it is built from.
///
/// <para>Separate from <see cref="LauncherPart"/> because a director is not launcher gear. It is
/// a part in its own right, on any craft, with or without a weapon anywhere near it — so it is
/// found by walking the craft rather than by looking inside a launcher.</para>
/// </summary>
internal static class OpticParts
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

    /// <summary>The flange and mast the head turns on.</summary>
    public static Part? FindBase(Part director, OpticProfile profile)
        => FindSubPart(director, profile.BaseMarker);

    /// <summary>
    /// The outer roll gimbal, or null on a head that has none — which is every head but a pod's.
    /// </summary>
    public static Part? FindRoll(Part director, OpticProfile profile)
        => FindSubPart(director, profile.RollMarker);

    /// <summary>
    /// Where the base has ended up, in the director part's frame.
    ///
    /// <para><b>Read off the engine rather than worked out.</b> Whatever carries the base — a
    /// turret's traverse, a hinge, an arm — has already written this transform by the time a head
    /// is driven, so taking it as found is what lets a head ride anything without being taught
    /// what any of it is. Reconstructing the pose instead would mean naming the mover.</para>
    ///
    /// <para><see cref="MountFrame.Fixed"/> when there is no base subpart or the engine will not
    /// answer, which is the pose a director bolted to a hull has anyway — so the fallback is the
    /// common case rather than a guess.</para>
    /// </summary>
    public static MountFrame MountOf(Part director, OpticProfile profile)
    {
        try
        {
            return FindBase(director, profile) is { } mount
                ? new MountFrame(mount.PositionParentAsmb, mount.Asmb2ParentAsmb)
                : MountFrame.Fixed;
        }
        catch (Exception e)
        {
            Log.Warn($"optic mount: {e.Message}");
            return MountFrame.Fixed;
        }
    }

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
    public static bool TryApplyAim(Part head, OpticProfile profile, MountFrame mount,
                                   double3 aimPartFrame)
        => TryApplyPose(head, OpticGeometry.Pose(profile, mount, aimPartFrame), "head");

    /// <summary>
    /// Turns the outer roll gimbal to the roll the head's aim implies.
    ///
    /// <para>Written from the same aim as the head rather than tracked separately, which is what
    /// keeps the window flush in the shell: the two are one decomposition of one rotation, and
    /// deriving them from different instants would show as the window standing off its aperture.</para>
    /// </summary>
    public static bool TryApplyRoll(Part roll, OpticProfile profile, MountFrame mount,
                                    double3 aimPartFrame)
        => TryApplyPose(roll, OpticGeometry.RollPose(profile, mount, aimPartFrame), "roll gimbal");

    private static bool TryApplyPose(Part body, DrivePose pose, string what)
    {
        try
        {
            body.Asmb2ParentAsmb = pose.Rotation;
            body.PositionParentAsmb = pose.Position;

            // Part caches its matrices; without this the new value is stored and ignored.
            body.ResetCachedPosMatrixValues();

            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"optic {what} transform: {e.Message}");
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
                                  MountFrame mount, double3 aimPartFrame, double3 platformEcl,
                                  out double3 eyeEcl, out double3 forwardEcl)
    {
        eyeEcl = forwardEcl = Vec.Zero;

        double3 eyePartFrame = OpticGeometry.EyePartFrame(profile, mount, aimPartFrame);

        if (!LauncherPart.TryPartPointEcl(platform, director, eyePartFrame, platformEcl, out eyeEcl))
        {
            return false;
        }

        return LauncherPart.TryLauncherDirectionEcl(platform, director, aimPartFrame, out forwardEcl);
    }
}
