namespace KSArmory;

/// <summary>
/// A cyclic gun's firing state: how much belt is left, where it is in a burst, and when the next
/// round is due.
///
/// <para>Not a <see cref="Magazine"/>: a gun cycles its barrels against one belt, so what matters is a
/// rate, a burst length and a pause between bursts.</para>
/// </summary>
internal sealed class GunChannel
{
    /// <summary>Rounds left in the belt.</summary>
    public int Ammo { get; private set; }

    /// <summary>Rounds still owed on the burst in progress. Zero between bursts.</summary>
    public int BurstRemaining { get; private set; }

    /// <summary>
    /// How many rounds the next burst fires in place of the profile's, once; zero for the profile's.
    /// </summary>
    public int NextBurstRounds { get; set; }

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
    /// <para>A count, because at 2500 rounds/minute a 100 ms frame owes four rounds; one per step caps
    /// the gun at the frame rate.</para>
    ///
    /// <para><paramref name="wantToFire"/> only starts bursts, so a flickering track does not stutter the
    /// gun; <paramref name="mayContinue"/> false ends a burst and starts the gap.</para>
    /// </summary>
    public int Step(double dt, bool wantToFire, LauncherProfile profile, bool mayContinue = true)
    {
        if (!(dt > 0.0) || !double.IsFinite(dt)) return 0;

        // Negative is time the gun owes, which lets one step deliver several rounds.
        Cooldown -= dt;

        if (BurstRemaining > 0 && !mayContinue)
        {
            BurstRemaining = 0;
            Cooldown = Math.Max(Cooldown, profile.GunBurstGapSeconds);
            return 0;
        }

        if (BurstRemaining <= 0)
        {
            if (!wantToFire || Ammo <= 0 || Cooldown > 0.0)
            {
                // Idling banks no credit, or a gun that waited empties its belt into one frame.
                if (Cooldown < 0.0) Cooldown = 0.0;
                return 0;
            }
            BurstRemaining = Math.Max(1, NextBurstRounds > 0 ? NextBurstRounds : profile.GunBurstRounds);
            NextBurstRounds = 0;
        }

        double interval = profile.GunRoundInterval;
        if (!(interval > 0.0)) return 0;

        int fired = 0;
        // A round due exactly at the step's end belongs to the next; the tolerance absorbs the residue
        // (-0.1 plus four 0.025 steps is -1.4e-17), which would otherwise run the gun 25% fast.
        while (Cooldown < -1e-9 && BurstRemaining > 0 && Ammo > 0)
        {
            fired++;
            Ammo--;
            BurstRemaining--;
            Cooldown += interval;
        }

        // A burst cannot outlive the belt, or Firing stays true for the session with nothing fired.
        if (Ammo <= 0) BurstRemaining = 0;

        if (BurstRemaining <= 0 && fired > 0)
        {
            Cooldown = Math.Max(Cooldown, profile.GunBurstGapSeconds);
        }

        return fired;
    }
}
