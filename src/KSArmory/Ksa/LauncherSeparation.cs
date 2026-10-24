using KSA;

namespace KSArmory;

/// <summary>
/// Letting a launcher off the stack that carried it up.
///
/// <para>A launcher that points itself between rounds would otherwise turn the spent booster too.</para>
///
/// <para><b>A property of the part, not of the craft</b>: the only question is whether there is a
/// decoupler on the joint holding the launcher on. With none it deploys attached.</para>
/// </summary>
internal static class LauncherSeparation
{
    /// <summary>
    /// Whether the next stage the player has queued is the one that lets this launcher off.
    ///
    /// <para>KSA fires the first sequence that has not been activated and has parts in it.</para>
    ///
    /// <para>False when the launcher has no decoupler, when nothing is left to stage, or when the list
    /// cannot be read: not knowing answers with the one that lets the rocket fly.</para>
    /// </summary>
    public static bool NextStageSeparates(Vehicle? craft, Part? launcher)
    {
        if (craft is null || !TryFind(launcher, out Decoupler decoupler)) return false;

        try
        {
            // Derived data is rebuilt once a frame, before the solvers run, so a tree split after
            // that holds the old list until the next frame unless asked for it.
            craft.Parts.EnsureDerived(DerivedData.Sequences);
            ReadOnlySpan<Sequence> sequences = craft.Parts.SequenceList.Sequences;

            for (int i = 0; i < sequences.Length; i++)
            {
                Sequence sequence = sequences[i];
                if (sequence.Activated || sequence.Parts.IsEmpty) continue;

                ReadOnlySpan<Part> parts = sequence.Parts;

                for (int p = 0; p < parts.Length; p++)
                {
                    if (ReferenceEquals(parts[p], decoupler.Parent)) return true;
                }

                return false;
            }
        }
        catch (Exception e)
        {
            Log.Error("could not read the staging list", e);
        }

        return false;
    }

    /// <summary>
    /// How many stages with parts are still to fire after the one that lets this launcher off, or zero when it is last,
    /// has fired, or cannot be read. More than zero is a stack that will drop its rounds before its burn is done.
    /// </summary>
    public static int StagesAfterTheRelease(Vehicle? craft, Part? launcher)
    {
        if (craft is null || !TryFind(launcher, out Decoupler decoupler)) return 0;

        try
        {
            craft.Parts.EnsureDerived(DerivedData.Sequences);
            ReadOnlySpan<Sequence> sequences = craft.Parts.SequenceList.Sequences;
            int after = -1;

            for (int i = 0; i < sequences.Length; i++)
            {
                Sequence sequence = sequences[i];
                if (sequence.Activated || sequence.Parts.IsEmpty) continue;

                if (after >= 0) after++;
                else if (Holds(sequence, decoupler)) after = 0;
            }

            return Math.Max(after, 0);
        }
        catch (Exception e)
        {
            Log.Error("could not read the staging list", e);
            return 0;
        }
    }

    /// <summary>
    /// Move the stage that lets this launcher off to the end of the staging list, as dragging it there in KSA's own list
    /// does. Only when that stage holds the joint alone: moving it moves everything in it, and an engine moved with it
    /// would wait for the release.
    /// </summary>
    /// <returns>Whether it moved; <paramref name="why"/> says what happened either way.</returns>
    public static bool TryMoveReleaseLast(Vehicle? craft, Part? launcher, out string why)
    {
        why = "";
        if (craft is null || !TryFind(launcher, out Decoupler decoupler))
        {
            why = "the launcher has no joint to move";
            return false;
        }

        try
        {
            craft.Parts.EnsureDerived(DerivedData.Sequences);
            SequenceList list = craft.Parts.SequenceList;
            ReadOnlySpan<Sequence> sequences = list.Sequences;

            for (int i = 0; i < sequences.Length; i++)
            {
                Sequence sequence = sequences[i];
                if (sequence.Activated || !Holds(sequence, decoupler)) continue;

                if (sequence.Parts.Length != 1)
                {
                    why = $"stage {sequence.Number} fires {sequence.Parts.Length} parts with the release, and moving it would move them too";
                    return false;
                }

                int from = sequence.Number;
                list.MoveSequence(sequence, null);
                why = $"the release was stage {from}; it is now the last";
                return true;
            }

            why = "the release is not in the staging list";
            return false;
        }
        catch (Exception e)
        {
            why = $"the staging list could not be changed ({e.Message})";
            return false;
        }
    }

    private static bool Holds(Sequence sequence, Decoupler decoupler)
    {
        ReadOnlySpan<Part> parts = sequence.Parts;
        for (int p = 0; p < parts.Length; p++)
        {
            if (ReferenceEquals(parts[p], decoupler.Parent)) return true;
        }

        return false;
    }

    /// <summary>
    /// The decoupler on the joint that holds this launcher on, if there is one.
    ///
    /// <para>Only that joint: further up the tree is the interstage, which drops the whole upper stage
    /// with the rounds still aboard.</para>
    /// </summary>
    public static bool TryFind(Part? launcher, out Decoupler found)
    {
        found = null!;
        if (launcher is null) return false;

        try
        {
            Part part = launcher.FullPart;

            // On the launcher itself: it declares a decoupler on its own mounting connector.
            if (TryOnPart(part, part, out found)) return true;

            // Or on whatever it is mounted to, provided the joint is the one holding the launcher.
            // A decoupler further up is somebody else's staging.
            Part? below = part.TreeParent;
            return below is not null && TryOnPart(below, part, out found);
        }
        catch
        {
            // A part tree mid-rebuild reads as "no decoupler", which is the answer that changes
            // nothing rather than the one that fires something.
            found = null!;
            return false;
        }
    }

    /// <summary>Whether the launcher could let go, asked before the shot rather than during it.</summary>
    public static bool CanSeparate(Part? launcher) => TryFind(launcher, out Decoupler d) && d.IsEnabled;

    /// <summary>
    /// Fires the module directly, never the staging sequence, which fires whatever the player has
    /// next. The split is deferred through the engine's input buffer, so it lands the following frame.
    /// </summary>
    public static bool Separate(Vehicle? craft, Part? launcher)
    {
        if (craft is null || !KsaWorld.IsAlive(craft)) return false;
        if (!TryFind(launcher, out Decoupler decoupler) || !decoupler.IsEnabled) return false;

        try
        {
            decoupler.SetIsActive(craft, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryOnPart(Part carrier, Part launcher, out Decoupler found)
    {
        found = null!;

        foreach (Decoupler decoupler in carrier.Modules.Get<Decoupler>())
        {
            if (!decoupler.IsEnabled) continue;
            if (decoupler.Connector.Connection is not { } connection) continue;

            // The joint has to be the one the launcher hangs on. A decoupler on the same part
            // facing the other way is the stage below's business.
            Part far = connection.OtherPart(decoupler.Connector.ConnectionPart);
            Part near = decoupler.Connector.ConnectionPart;

            if (!ReferenceEquals(far.FullPart, launcher) && !ReferenceEquals(near.FullPart, launcher))
            {
                continue;
            }

            found = decoupler;
            return true;
        }

        return false;
    }
}
