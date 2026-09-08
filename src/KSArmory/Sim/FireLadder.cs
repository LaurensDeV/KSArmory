namespace KSArmory;

/// <summary>
/// Everything the fire ladder reads that is not already a shared profile, sampled at one instant.
///
/// <para>Gathered into one value rather than asked for one call at a time, because the ladder's
/// whole meaning is <em>which gate says no first</em>: a rung answered from a later instant than
/// the one above it can report a reason that was never true at any single moment.</para>
/// </summary>
internal readonly record struct FireConditions
{
    /// <summary>The system is mounted on a craft.</summary>
    public required bool HasPlatform { get; init; }

    /// <summary>Its launcher part resolved on that craft.</summary>
    public required bool IsOperational { get; init; }

    /// <summary>
    /// It has tubes, and therefore takes the missile rungs rather than the belt's.
    ///
    /// <para>The two are alternatives, not a filter: a gun-only launcher's magazine is empty by
    /// construction, so running it down the missile rungs reports "out of rounds" forever while
    /// its cannon are audibly firing.</para>
    /// </summary>
    public required bool HasTubes { get; init; }

    public required bool MagazineEmpty { get; init; }
    public required double ReloadSeconds { get; init; }
    public required int Ammo { get; init; }
    public required double SalvoSeconds { get; init; }
    public required bool BeltEmpty { get; init; }

    public required bool HasFiringSolution { get; init; }
    public required int TrackCount { get; init; }

    /// <summary>The tube drives have settled.</summary>
    public required bool IsLaid { get; init; }

    /// <summary>The gun drives have. Separate because the two weapons share only the traverse.</summary>
    public required bool GunsAreLaid { get; init; }

    /// <summary>The turret is laid on the cannon's ballistic lead rather than on the target.</summary>
    public required bool RingIsOnGunLead { get; init; }

    /// <summary>The turret is laid where the operator is pointing rather than at the target.</summary>
    public required bool RingIsOnCursor { get; init; }

    /// <summary>Rounds leave along the tube, so where it points decides where they go.</summary>
    public required bool LaunchAlongTube { get; init; }

    /// <summary>What the set is holding, or null.</summary>
    public required TrackState? Locked { get; init; }

    /// <summary>
    /// The locked contact is transmitting. False for anything that cannot carry a set at all — a
    /// round in the air, or a designated coordinate.
    /// </summary>
    public required bool LockedIsEmitting { get; init; }

    /// <summary>What to call it, for the one reason that names its target.</summary>
    public required string LockedName { get; init; }
}

/// <summary>
/// A reason a system is not shooting, and whether it stops the operator as well as fire control.
///
/// <para><b>Not every gate binds the trigger.</b> Some are about whether the round can leave the
/// rail at all — no lock, empty, still slewing — and the manual path refuses those too. Others are
/// automatic fire's <em>economy</em>: don't spend a round on something out of reach, don't put a
/// third on a target already committed to. An operator asking for a close shot has decided to take
/// it, and the trigger has always let them. Reporting that as <c>Holding fire</c> beside a button
/// that then works is the panel describing a refusal that never happens.</para>
/// </summary>
internal readonly record struct FireHold(string Reason, bool BindsTrigger);

/// <summary>
/// Why a system is not shooting, as the first gate that says no.
///
/// <para>Every gate in fire control returns quietly, so an unarmed system, one with no lock, one
/// still settling and one whose drives the engine refused all look identical from outside. Naming
/// the first rung that answers is the difference between reading the panel and reading the
/// source.</para>
///
/// <para><em>Auto-engage is deliberately not a rung.</em> It decides whether fire control shoots on
/// its own, not whether a round can leave the rail, and no manual fire path consults it. Reporting
/// it here stops the ladder at the one switch that blocks nothing the operator asked for, hiding
/// every gate below it from the panel beside the trigger.</para>
/// </summary>
internal static class FireLadder
{
    /// <summary>
    /// The first reason this system is holding fire, or null if it is not.
    ///
    /// <para>In order, and the order is the fire sequence's — a rung may only be asked once
    /// everything above it has been satisfied, because several are meaningless otherwise. The
    /// settling rungs are the clearest case: a launcher with no firing solution has nothing to be
    /// settled <em>onto</em>.</para>
    /// </summary>
    public static FireHold? Holding(in FireConditions now, SystemConfig policy, MunitionProfile munition)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(munition);

        if (!now.HasPlatform) return Binding("no platform");

        // The platform was answered above, so this is the launcher and nothing else.
        if (!now.IsOperational) return Binding("no launcher resolved on this craft");

        if (now.HasTubes && now.MagazineEmpty && now.ReloadSeconds > 0.0)
        {
            return Binding($"reloading ({now.ReloadSeconds:F0} s)");
        }

        if (!policy.Armed) return Binding("safe -- master arm is off");

        if (now.HasTubes)
        {
            if (!policy.MissilesEnabled) return Binding("missiles are switched off");
            if (!munition.Powered) return Binding("a store is released by hand, not auto-engaged");
            if (now.Ammo <= 0) return Binding("out of rounds");
            if (now.SalvoSeconds > 0.0) return Binding("between salvos");
        }
        else
        {
            if (!policy.GunsEnabled) return Binding("cannon are switched off");
            if (now.BeltEmpty) return Binding("belt empty");
        }

        if (!now.HasFiringSolution)
        {
            return Binding(now.TrackCount == 0
                               ? "nothing detected"
                               : $"no firing solution yet ({now.TrackCount} track(s))");
        }

        // Each weapon settles on its own gear, so a system with no pods must not be asked whether
        // its pods have stopped moving.
        if (now.HasTubes)
        {
            if (!now.IsLaid) return Binding("drives still settling");
            if (!FireGate.MissilesMayFire(now.RingIsOnGunLead, now.LaunchAlongTube))
            {
                return Binding("the cannon has the bearing");
            }

            // The operator owns the ring, so an automatic launch would leave along the cursor and
            // turn onto whatever the radar locked. Held rather than re-aimed: the cursor is a
            // deliberate command, and taking the ring back to shoot would fight the player.
            if (!FireGate.MissilesMayFire(now.RingIsOnCursor, now.LaunchAlongTube))
            {
                return Binding("the cursor has the bearing");
            }
        }
        else if (!now.GunsAreLaid)
        {
            return Binding("drives still settling");
        }

        if (now.Locked is not { } locked) return Binding("no lock");

        // An anti-radiation round has to be pointed at something radiating, and that is a gate on
        // *launching* rather than only on homing. Emission is read in flight and nowhere before it,
        // so a shell closing at 956 m/s -- which takes the top of a list ranked by time to closest
        // approach, ahead of the site that fired it -- can be locked and shot at by a weapon with
        // no way to see it. The round then flies straight past everything.
        if (munition.Guidance == GuidanceMode.AntiRadiation && !now.LockedIsEmitting)
        {
            return Binding($"{now.LockedName} is not radiating");
        }

        if (!ThreatModel.MayEngage(locked, policy.Iff)) return Binding("target is not engageable (IFF)");

        // Economy, not capability: another round on this target would be spent rather than wasted,
        // and an operator who asks for it has decided that. The trigger does not consult it.
        if (!ThreatModel.HasSalvoCapacity(locked, policy.RoundsPerTarget)) return AutoOnly("salvo committed");

        if (!ThreatModel.InEngagementEnvelope(locked, munition))
        {
            // With the numbers: "out of reach" reads as too far, and the usual cause is a target
            // that came inside the minimum instead.
            //
            // Auto-engage's own, for the same reason as the salvo count above. The floor is about a
            // round still boosting and the ceiling about one expiring short, and neither stops the
            // shot happening -- so the trigger fires and the round flies, which is what made this
            // line read as a fault.
            return AutoOnly($"target out of reach ({locked.Range / 1000.0:F1} km, envelope "
                            + $"{munition.MinRange / 1000f:F1}-"
                            + $"{munition.MaxRange / 1000f:F1} km)");
        }

        return null;
    }

    // Stops the round leaving the rail, so it stops the operator too.
    private static FireHold Binding(string reason) => new(reason, BindsTrigger: true);

    // Stops fire control shooting on its own, and nothing else.
    private static FireHold AutoOnly(string reason) => new(reason, BindsTrigger: false);
}
