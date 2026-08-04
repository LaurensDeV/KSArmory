namespace KSArmory;

/// <summary>
/// A cyclic gun's firing state: how much belt is left, where it is in a burst, and when the next
/// round is due.
///
/// <para>Nothing like <see cref="Magazine"/>, which tracks which tube holds what. A gun does not
/// empty tubes — it cycles two barrels against one belt, so what matters is a rate, a burst
/// length and a pause between bursts. Kept here rather than in the battery because the failure
/// modes are all arithmetic: a burst that never ends, a rate that outruns the frame, a belt that
/// goes negative.</para>
/// </summary>
internal sealed class GunChannel
{
    /// <summary>Rounds left in the belt.</summary>
    public int Ammo { get; private set; }

    /// <summary>Rounds still owed on the burst in progress. Zero between bursts.</summary>
    public int BurstRemaining { get; private set; }

    /// <summary>Seconds until the next round may leave.</summary>
    public double Cooldown { get; private set; }

    /// <summary>True while a burst is in progress, so the panel can show it.</summary>
    public bool Firing => BurstRemaining > 0;

    public bool IsEmpty => Ammo <= 0;

    public void Fill(int rounds) => Ammo = rounds < 0 ? 0 : rounds;

    public void Reset()
    {
        BurstRemaining = 0;
        Cooldown = 0.0;
    }

    /// <summary>
    /// Advances the gun and reports how many rounds left the barrels this step.
    ///
    /// <para>Returns a count rather than a bool because a step can be longer than the interval
    /// between rounds: at 2500 rounds/minute a round is due every 24 ms, and a 100 ms frame owes
    /// four. Firing one and discarding the rest silently caps the gun at the frame rate, which
    /// looks like the cannon being feeble rather than like the bug it is.</para>
    ///
    /// <para><paramref name="wantToFire"/> only starts bursts. A burst already begun runs to its
    /// end, so the gun does not stutter when a track flickers.</para>
    /// </summary>
    public int Step(double dt, bool wantToFire, LauncherProfile profile)
    {
        if (!(dt > 0.0) || !double.IsFinite(dt)) return 0;

        // Allowed to go negative: that is the time the gun owes, and it is what lets one step
        // deliver several rounds.
        Cooldown -= dt;

        if (BurstRemaining <= 0)
        {
            if (!wantToFire || Ammo <= 0 || Cooldown > 0.0)
            {
                // Idling banks no credit. Without this, a gun that waited a minute for a target
                // empties its belt into the first frame it is allowed to shoot.
                if (Cooldown < 0.0) Cooldown = 0.0;
                return 0;
            }
            BurstRemaining = Math.Max(1, profile.GunBurstRounds);
        }

        double interval = profile.GunRoundInterval;
        if (!(interval > 0.0)) return 0;

        int fired = 0;
        // Strictly negative, with a tolerance. A round due at exactly the end of this step belongs
        // to the next one, and accumulating the interval leaves a residue either side of zero —
        // -0.1 plus four steps of 0.025 lands at -1.4e-17, which without this fires a fifth round
        // every step and quietly runs the gun 25% fast.
        while (Cooldown < -1e-9 && BurstRemaining > 0 && Ammo > 0)
        {
            fired++;
            Ammo--;
            BurstRemaining--;
            Cooldown += interval;
        }

        // Between bursts the gun pauses, which is what makes it read as a gun rather than a hose.
        if (BurstRemaining <= 0 && fired > 0)
        {
            Cooldown = Math.Max(Cooldown, profile.GunBurstGapSeconds);
        }

        return fired;
    }
}
