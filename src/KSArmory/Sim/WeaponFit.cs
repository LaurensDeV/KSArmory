namespace KSArmory;

/// <summary>
/// Which of a system's magazines and switches an armament draws on — the pairing of a count and an
/// enable switch that are separately named members rather than a lookup.
/// </summary>
public enum ArmamentKind
{
    /// <summary>Rounds that leave a tube one at a time, each flown by the guidance model.</summary>
    Tubes,

    /// <summary>Rounds fed from a belt, flown ballistically.</summary>
    Belt,

    /// <summary>A high-energy beam held on what its director looks at; what it carries is seconds
    /// of lasing.</summary>
    Beam,
}

public readonly record struct Armament
{
    public required ArmamentKind Kind { get; init; }

    /// <summary>Names its status row, its enable switch and its tuning section.</summary>
    public required string Label { get; init; }

    /// <summary>Registry key of the round it throws, for the reader to resolve and tune.</summary>
    public required string Munition { get; init; }

    /// <summary>Rounds a full load holds, or seconds of lasing for a beam.</summary>
    public required int Capacity { get; init; }

    /// <summary>Seconds to replenish. Zero means what it carries is all there is.</summary>
    public required float ReloadSeconds { get; init; }

    /// <summary>
    /// Whether the guidance numbers reach this armament's rounds. Both terms: a belt is built as a
    /// <c>Slug</c> and never reads <see cref="MunitionProfile.Guidance"/>, whose default is not None.
    /// </summary>
    public bool Steers => Kind == ArmamentKind.Tubes
                          && Catalogue.TryMunitionNamed(Munition) is { Guidance: not GuidanceMode.None };

    /// <summary>
    /// Whether this armament's round is a missile, which is what the seeker and boost numbers describe.
    /// Narrower than <see cref="Steers"/>: a tail kit steers a fall on the navigation numbers alone.
    /// </summary>
    public bool Powered => Kind == ArmamentKind.Tubes && Catalogue.TryMunitionNamed(Munition) is { Powered: true };

    public bool Reloads => ReloadSeconds > 0f;

    public string Tally(int remaining)
        => Kind == ArmamentKind.Beam ? $"{remaining} s/{Capacity} s" : $"{remaining}/{Capacity}";

    public string Describe(int remaining, bool firing)
        => firing ? $"{Label}: {Tally(remaining)} FIRING" : $"{Label}: {Tally(remaining)}";

    /// <summary>
    /// The switch that lets an armament of this kind engage, by reference for a tick box. Keyed on the
    /// kind, because an armament read out of a list is a copy and a reference into it a temporary.
    /// </summary>
    public static ref bool EnabledIn(SystemConfig policy, ArmamentKind kind)
    {
        if (kind == ArmamentKind.Belt) return ref policy.GunsEnabled;
        if (kind == ArmamentKind.Beam) return ref policy.BeamEnabled;
        return ref policy.MissilesEnabled;
    }
}

/// <summary>
/// What one weapons system is fitted with: what the panel reads instead of testing profile fields.
/// Built on demand, not cached, because the profiles are tuned live.
/// </summary>
public sealed class WeaponFit
{
    public required IReadOnlyList<Armament> Armaments { get; init; }

    /// <summary>Something has to be laid before it may fire.</summary>
    public required bool Aims { get; init; }

    public required bool Traverses { get; init; }

    public required bool Elevates { get; init; }

    public required bool SweepsASearchArray { get; init; }

    /// <summary>False for a launcher aimed and fired by hand: no scope, no track list, no sensor to tune.</summary>
    public required bool Searches { get; init; }

    public bool Powered
    {
        get
        {
            for (int i = 0; i < Armaments.Count; i++)
            {
                if (Armaments[i].Powered) return true;
            }
            return false;
        }
    }

    public bool Steers
    {
        get
        {
            for (int i = 0; i < Armaments.Count; i++)
            {
                if (Armaments[i].Steers) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Whether fire control can pick a target and shoot without being told to — the split
    /// <see cref="FireLadder"/> makes: a belt or beam does, tubes only with a powered round.
    /// </summary>
    public bool AutoEngages
    {
        get
        {
            for (int i = 0; i < Armaments.Count; i++)
            {
                if (Armaments[i].Kind is ArmamentKind.Belt or ArmamentKind.Beam) return true;
                if (Armaments[i].Powered) return true;
            }
            return false;
        }
    }

    /// <summary>Most rounds one target can be given at once; zero without tubes.</summary>
    public int SalvoCapacity => FirstOf(ArmamentKind.Tubes)?.Capacity ?? 0;

    /// <summary>
    /// Whether a row named <paramref name="displayName"/> describes this fit's armament of that kind.
    /// Matched on the munition's DisplayName, which is how a row is declared, never on
    /// <see cref="Armament.Label"/>: "Cannon" against "2A38M 30 mm cannon" never agrees.
    /// </summary>
    public bool Describes(ArmamentKind kind, string displayName)
        => kind != ArmamentKind.Beam
           && FirstOf(kind) is { } arm
           && string.Equals(Catalogue.TryMunitionNamed(arm.Munition)?.DisplayName, displayName,
                            StringComparison.Ordinal);

    public Armament? FirstOf(ArmamentKind kind)
    {
        for (int i = 0; i < Armaments.Count; i++)
        {
            if (Armaments[i].Kind == kind) return Armaments[i];
        }
        return null;
    }

    /// <summary>
    /// Whether anything this system releases is stopped by the terrain
    /// (<see cref="MunitionProfile.HitsTerrain"/>), which is what the bomb sight needs — not "has no missiles".
    /// </summary>
    public required bool Drops { get; init; }

    public static WeaponFit Of(LauncherProfile launcher, SensorProfile sensor)
    {
        List<Armament> armaments = new(2);

        if (launcher.TubeCount > 0)
        {
            armaments.Add(new Armament
            {
                Kind = ArmamentKind.Tubes,
                Label = launcher.TubeArmamentLabel,
                Munition = launcher.Munition,
                Capacity = MagazineCapacity(launcher),
                ReloadSeconds = launcher.ReloadSeconds,
            });
        }

        if (launcher.HasCannon)
        {
            armaments.Add(new Armament
            {
                Kind = ArmamentKind.Belt,
                Label = launcher.GunArmamentLabel,
                Munition = launcher.GunMunition!,
                Capacity = launcher.GunAmmo,
                ReloadSeconds = launcher.GunReloadSeconds,
            });
        }

        if (launcher.FiresBeam && Catalogue.OpticForPart(launcher.PartId)?.Beam is { } beam)
        {
            armaments.Add(new Armament
            {
                Kind = ArmamentKind.Beam,
                Label = launcher.BeamArmamentLabel,
                Munition = "",
                Capacity = (int)Math.Round(beam.ChemicalSeconds),
                ReloadSeconds = 0f,
            });
        }

        return new WeaponFit
        {
            Armaments = armaments,
            Aims = launcher.Trains,
            Traverses = launcher.TurretMarker is not null,
            Elevates = launcher.PodsMarker is not null || launcher.GunsMarker is not null,
            SweepsASearchArray = launcher.RadarMarker is not null,

            // Every launcher names a set, so a range of zero is how "no sensor" is declared.
            Searches = sensor.Range > 0f,

            Drops = Drop(armaments),
        };
    }

    private static bool Drop(List<Armament> armaments)
    {
        for (int i = 0; i < armaments.Count; i++)
        {
            if (armaments[i].Kind == ArmamentKind.Beam) continue;
            if (Catalogue.TryMunitionNamed(armaments[i].Munition) is { HitsTerrain: true }) return true;
        }

        return false;
    }

    /// <summary>
    /// The armament a trigger set to <paramref name="wanted"/> fires: that one if the launcher has
    /// it, otherwise the first it has, in the order <see cref="Of"/> lists them.
    /// </summary>
    public static ArmamentKind TriggerOn(LauncherProfile launcher, ArmamentKind wanted)
    {
        bool tubes = launcher.TubeCount > 0, belt = launcher.HasCannon, beam = launcher.FiresBeam;

        if (wanted == ArmamentKind.Tubes && tubes) return ArmamentKind.Tubes;
        if (wanted == ArmamentKind.Belt && belt) return ArmamentKind.Belt;
        if (wanted == ArmamentKind.Beam && beam) return ArmamentKind.Beam;

        return tubes ? ArmamentKind.Tubes : belt ? ArmamentKind.Belt : beam ? ArmamentKind.Beam : ArmamentKind.Belt;
    }

    /// <summary>
    /// Rounds a full launcher holds, from the same two numbers <c>Magazine.Resize</c> reads, so the panel
    /// counts down from what the magazine was filled with.
    /// </summary>
    public static int MagazineCapacity(LauncherProfile launcher)
        => launcher.MagazineDepth > launcher.TubeCount ? launcher.MagazineDepth : launcher.TubeCount;
}
