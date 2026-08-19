using KSA;

namespace KSArmory;

/// <summary>
/// Letting a launcher off the stack that carried it up.
///
/// <para>A launcher that deploys its rounds one at a time wants to point itself between them, and a
/// spent booster is both the mass it has to turn and the lever arm that throws each round as it
/// turns. Separating first is what a real post-boost vehicle does, and it is worth two to three
/// orders of magnitude of angular acceleration.</para>
///
/// <para><b>Whether a launcher can do this is a property of the part, not of the craft.</b> The
/// question asked here is only ever "is there a decoupler on the joint holding my launcher on" —
/// so a launcher that declares its own separates itself, one bolted to a stock decoupler separates
/// at that, and one with neither simply deploys attached. Nothing here names a weapon.</para>
/// </summary>
internal static class LauncherSeparation
{
    /// <summary>
    /// The decoupler on the joint that holds this launcher on, if there is one.
    ///
    /// <para>Deliberately only that joint. Walking further up the tree finds the interstage, and
    /// firing that drops the whole upper stage rather than releasing the launcher — with the rounds
    /// still aboard, on a trajectory nobody solved.</para>
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
    /// Fire it.
    ///
    /// <para>The module directly, never the staging sequence: that fires whatever the player has
    /// next, which on somebody's craft is anything at all. The split is deferred through the
    /// engine's input buffer, so it lands on the following frame.</para>
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
