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
    /// <para>Alternatives, not a filter: a gun-only launcher's magazine is empty by construction.</para>
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

    /// <summary>
    /// A round leaving the tube now could steer onto the locked contact — false only for a seeker
    /// round with the contact past its gimbal limit, which the shot itself refuses.
    /// </summary>
    public required bool LockedWithinSeeker { get; init; }

    /// <summary>How far the locked contact is off the tube, in degrees, for the reason that says so.</summary>
    public required double LockedOffTubeDeg { get; init; }

    /// <summary>What to call it, for the one reason that names its target.</summary>
    public required string LockedName { get; init; }

    /// <summary>
    /// A craft the operator named, which the trigger shoots in place of the set's own pick, or null.
    /// <see cref="Locked"/> is then its track, and null while the set cannot see it.
    /// </summary>
    public required string? DesignatedName { get; init; }

    /// <summary>The beam's own state when the armament asked about is a beam, else null.</summary>
    public BeamConditions? Beam { get; init; }
}

/// <summary>What the director firing a beam says about it, sampled with the rest.</summary>
internal readonly record struct BeamConditions
{
    /// <summary>A director on the launcher's part was found to fire it.</summary>
    public required bool HeadResolved { get; init; }

    public required double SecondsLeft { get; init; }

    /// <summary>The craft carrying it is in the way (MASK).</summary>
    public required bool Masked { get; init; }

    /// <summary>The director is settled on what it was cued at.</summary>
    public required bool OnTarget { get; init; }

    /// <summary>Cloud stops the beam short of the target.</summary>
    public required bool CloudInTheWay { get; init; }

    public required double ReachMetres { get; init; }
}

/// <summary>
/// A reason a system is not shooting, and whether it stops the operator as well as fire control.
///
/// <para><b>Not every gate binds the trigger.</b> Some stop the round leaving the rail at all, and the
/// manual path refuses those too; others are automatic fire's economy (reach, salvo count), which the
/// trigger does not consult.</para>
/// </summary>
internal readonly record struct FireHold(string Reason, bool BindsTrigger);

/// <summary>
/// Why a system is not shooting, as the first gate that says no.
///
/// <para>Every gate in fire control returns quietly, so this names the first that answers.
/// <em>Auto-engage is not a rung</em>: no manual path consults it, and as a rung it would hide every
/// gate below it.</para>
/// </summary>
internal static class FireLadder
{
    /// <summary>
    /// The first reason this system is holding fire, or null if it is not.
    ///
    /// <para>In the fire sequence's order: a rung is asked only once everything above it is satisfied,
    /// since a launcher with nothing locked has nothing to be settled onto.</para>
    /// </summary>
    public static FireHold? Holding(in FireConditions now, SystemConfig policy, MunitionProfile munition)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(munition);

        if (!now.HasPlatform) return Binding("no platform");

        // The platform was answered above, so this is the launcher and nothing else.
        if (!now.IsOperational) return Binding("no launcher resolved on this craft");

        if (now.Beam is { } beam) return BeamHolding(now, beam, policy);

        if (now.HasTubes && now.MagazineEmpty && now.ReloadSeconds > 0.0)
        {
            return Binding($"reloading ({now.ReloadSeconds:F0} s)");
        }

        if (now.HasTubes)
        {
            if (!policy.MissilesEnabled) return Binding("missiles are switched off");
            if (now.Ammo <= 0) return Binding("out of rounds");
            if (now.SalvoSeconds > 0.0) return Binding("between salvos");

            // A store is released onto a designation or onto nothing, so no rung below applies.
            // Automatic fire never drops one: UpdateFireControl refuses it on its own.
            if (!munition.Powered) return null;
        }
        else
        {
            if (!policy.GunsEnabled) return Binding("cannon are switched off");
            if (now.BeltEmpty) return Binding("belt empty");

            // A burst goes where the guns are laid, so the drives are all the trigger waits for.
            // Every rung below is automatic fire choosing a target.
            if (!now.GunsAreLaid) return Binding("drives still settling");
        }

        bool tubes = now.HasTubes;

        // Nothing to shoot at, asked before the drives: with no lock they are parked, and settling
        // is measured against nothing. A lock that is not yet a solution is asked about further
        // down, because the drives are on it and the trigger will fire at it.
        if (!now.HasFiringSolution && now.Locked is null)
        {
            return Targeting(tubes, now.DesignatedName is { } named ? $"{named} is not on the radar"
                                    : now.TrackCount == 0 ? "nothing detected"
                                    : $"no firing solution yet ({now.TrackCount} track(s))");
        }

        // Each weapon settles on its own gear, so a system with no pods must not be asked whether
        // its pods have stopped moving.
        if (tubes)
        {
            if (!now.IsLaid) return Binding("drives still settling");
            if (!FireGate.MissilesMayFire(now.RingIsOnGunLead, now.LaunchAlongTube))
            {
                return Binding("the cannon has the bearing");
            }

            // The operator owns the ring; held rather than taking it back, which would fight the player.
            if (!FireGate.MissilesMayFire(now.RingIsOnCursor, now.LaunchAlongTube))
            {
                return Binding("the cursor has the bearing");
            }
        }

        if (now.Locked is not { } locked) return Targeting(tubes, "no lock");

        // A gate on launching, not only on homing: otherwise a shell heading the threat list is locked
        // and shot at by a weapon with no way to see it.
        if (munition.Guidance == GuidanceMode.AntiRadiation && !now.LockedIsEmitting)
        {
            return Targeting(tubes, $"{now.LockedName} is not radiating");
        }

        if (!ThreatModel.MayEngage(locked, policy.Iff)) return Targeting(tubes, "target is not engageable (IFF)");

        // Binding: the trigger fires at the lock, and the shot refuses a seeker released off-axis
        // because it would never steer. Only a launcher that cannot train stays here for long.
        if (tubes && !now.LockedWithinSeeker)
        {
            return Binding($"{now.LockedName} is {now.LockedOffTubeDeg:F0} deg off the tube, past the "
                           + $"seeker's {munition.SeekerFovDeg:F0} deg - point the launcher at it");
        }

        // A contact that is not closing, or not yet held for the lock time. The trigger fires at
        // whatever is locked, which is what locking a passer-by with a shift-click is for.
        if (!now.HasFiringSolution)
        {
            return AutoOnly($"no firing solution yet ({now.TrackCount} track(s))");
        }

        // Economy, not capability: another round on this target would be spent rather than wasted,
        // and an operator who asks for it has decided that. The trigger does not consult it.
        if (!ThreatModel.HasSalvoCapacity(locked, policy.RoundsPerTarget)) return AutoOnly("salvo committed");

        if (!ThreatModel.InEngagementEnvelope(locked, munition))
        {
            // With the numbers, since the usual cause is a target inside the minimum. Auto-engage's
            // own: neither bound stops the trigger's shot.
            return AutoOnly($"target out of reach ({locked.Range / 1000.0:F1} km, envelope "
                            + $"{munition.MinRange / 1000f:F1}-"
                            + $"{munition.MaxRange / 1000f:F1} km)");
        }

        return null;
    }

    // A beam goes where its director looks, as a burst goes where the guns are laid: everything about
    // the target is automatic fire's, and nothing is rationed per target or bounded below in range.
    private static FireHold? BeamHolding(in FireConditions now, BeamConditions beam, SystemConfig policy)
    {
        if (!beam.HeadResolved) return Binding("no beam director resolved on this part");
        if (!policy.BeamEnabled) return Binding("laser is switched off");
        if (beam.SecondsLeft <= 0.0) return Binding("out of laser chemicals");
        if (beam.Masked) return Binding("own craft in the way (MASK)");

        if (now.Locked is not { } locked)
        {
            return AutoOnly(now.DesignatedName is { } named ? $"{named} is not on the director's set"
                            : now.TrackCount == 0 ? "nothing detected"
                            : "no lock");
        }

        if (!beam.OnTarget) return Binding("director still slewing");
        if (!ThreatModel.MayEngage(locked, policy.Iff)) return AutoOnly("target is not engageable (IFF)");
        if (!now.HasFiringSolution) return AutoOnly($"no firing solution yet ({now.TrackCount} track(s))");
        if (beam.CloudInTheWay) return AutoOnly("cloud between the beam and the target");

        if (locked.Range > beam.ReachMetres)
        {
            return AutoOnly($"target out of reach ({locked.Range / 1000.0:F0} km, beam "
                            + $"{beam.ReachMetres / 1000.0:F0} km)");
        }

        return null;
    }

    // Stops the round leaving the rail, so it stops the operator too.
    private static FireHold Binding(string reason) => new(reason, BindsTrigger: true);

    // Stops fire control shooting on its own, and nothing else.
    private static FireHold AutoOnly(string reason) => new(reason, BindsTrigger: false);

    // A missile needs a target to leave the rail; a burst goes where the guns are laid, so for a
    // gun everything about the target is automatic fire's.
    private static FireHold Targeting(bool tubes, string reason) => tubes ? Binding(reason) : AutoOnly(reason);
}
