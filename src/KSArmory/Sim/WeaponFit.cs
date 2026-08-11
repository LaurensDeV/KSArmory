namespace KSArmory;

/// <summary>
/// Which of a battery's magazines and switches an armament draws on.
///
/// <para>Carried by the description rather than asked about by whoever reads it: the panel
/// enumerates armaments and never tests one. It exists because the count a battery keeps and the
/// switch that lets an armament engage are separately named members rather than a lookup, so
/// something has to pair them up.</para>
/// </summary>
public enum ArmamentKind
{
    /// <summary>Rounds that leave a tube one at a time, each flown by the guidance model.</summary>
    Tubes,

    /// <summary>Rounds fed from a belt through barrels that cycle rather than empty, flown
    /// ballistically.</summary>
    Belt,
}

/// <summary>
/// One armament a weapons system is fitted with, as an operator needs it described: what to call
/// it, what it throws, how much of it there is and whether it comes back.
/// </summary>
public readonly record struct Armament
{
    public required ArmamentKind Kind { get; init; }

    /// <summary>Names its status row, its enable switch and its tuning section.</summary>
    public required string Label { get; init; }

    /// <summary>Registry key of the round it throws, for the reader to resolve and tune.</summary>
    public required string Munition { get; init; }

    /// <summary>Rounds a full load holds.</summary>
    public required int Capacity { get; init; }

    /// <summary>Seconds to replenish. Zero means what it carries is all there is.</summary>
    public required float ReloadSeconds { get; init; }

    /// <summary>
    /// Whether the guidance numbers reach this armament's rounds. Tubes are flown as
    /// interceptors and the belt ballistically, which is the battery's choice of flight model
    /// rather than anything the round declares.
    /// </summary>
    public bool Steers => Kind == ArmamentKind.Tubes;

    public bool Reloads => ReloadSeconds > 0f;

    /// <summary>How much is left against a full load.</summary>
    public string Tally(int remaining) => $"{remaining}/{Capacity}";

    /// <summary>One armament's line of a status readout.</summary>
    public string Describe(int remaining, bool firing)
        => firing ? $"{Label}: {Tally(remaining)} FIRING" : $"{Label}: {Tally(remaining)}";

    /// <summary>
    /// The battery switch that lets an armament of this kind engage, by reference so a tick box
    /// can drive it.
    ///
    /// <para>Static, and keyed on the kind rather than on an instance, because a caller reading
    /// armaments out of a list holds copies and a reference into one of those would be a
    /// reference to a temporary.</para>
    /// </summary>
    public static ref bool EnabledIn(SystemConfig policy, ArmamentKind kind)
    {
        if (kind == ArmamentKind.Belt) return ref policy.GunsEnabled;
        return ref policy.MissilesEnabled;
    }
}

/// <summary>
/// What one weapons system is fitted with, and what it can be told to do.
///
/// <para><b>This is what the panel reads instead of testing profile fields.</b> It enumerates
/// <see cref="Armaments"/> and asks the rest as questions, so a launcher with no tubes, nothing
/// that trains, or no sensor at all describes itself without anything upstream knowing which of
/// the three it is.</para>
///
/// <para>Built on demand rather than cached: the profiles are tuned live by reference, so a fit
/// kept from one frame answers for the load the system started with.</para>
/// </summary>
public sealed class WeaponFit
{
    /// <summary>Everything this system shoots, in the order it is presented.</summary>
    public required IReadOnlyList<Armament> Armaments { get; init; }

    /// <summary>Something has to be laid before it may fire.</summary>
    public required bool Aims { get; init; }

    /// <summary>It has a traverse to report a bearing from.</summary>
    public required bool Traverses { get; init; }

    /// <summary>It has an assembly that elevates.</summary>
    public required bool Elevates { get; init; }

    /// <summary>It carries a search array that turns.</summary>
    public required bool SweepsASearchArray { get; init; }

    /// <summary>
    /// It finds its own targets. False for a launcher that is aimed and fired by hand, which has
    /// no scope, no track list and nothing to tune on a sensor.
    /// </summary>
    public required bool Searches { get; init; }

    /// <summary>Whether any armament is flown by the guidance model.</summary>
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
    /// Most rounds one target can be given at once. A salvo comes out of the tubes, so a system
    /// with none has no salvo to size.
    /// </summary>
    public int SalvoCapacity => FirstOf(ArmamentKind.Tubes)?.Capacity ?? 0;

    /// <summary>The first armament of a kind, or null when none of that kind is fitted.</summary>
    /// <summary>
    /// Whether a row named <paramref name="displayName"/> describes this fit's armament of that
    /// kind — which is how a panel tells a crewed part's row from a second part's.
    ///
    /// <para>Here rather than in the panel because it is the only part of that question that can be
    /// tested, and it is the part that was wrong: a provided row is declared as a
    /// <em>munition's</em> DisplayName, so it has to be matched against the munition, resolved from
    /// the registry. Matching <see cref="Armament.Label"/> compares it to the belt's heading —
    /// "Cannon" against "2A38M 30 mm cannon" — which never agrees, and reports a working gun as
    /// not run.</para>
    /// </summary>
    public bool Describes(ArmamentKind kind, string displayName)
        => FirstOf(kind) is { } arm
           && string.Equals(Arsenal.MunitionNamed(arm.Munition).DisplayName, displayName,
                            StringComparison.Ordinal);

    public Armament? FirstOf(ArmamentKind kind)
    {
        for (int i = 0; i < Armaments.Count; i++)
        {
            if (Armaments[i].Kind == kind) return Armaments[i];
        }
        return null;
    }

    /// <summary>Reads a launcher and the set feeding it as the description above.</summary>
    /// <summary>
    /// Whether anything this system releases falls to the ground on its own.
    ///
    /// <para>Not "has no missiles". A gun has none either, and a ballistic pipper over a cannon is
    /// a ring in the wrong place with nothing to say so. What the sight needs is a store that the
    /// terrain stops, which is exactly <see cref="MunitionProfile.HitsTerrain"/>.</para>
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

        return new WeaponFit
        {
            Armaments = armaments,
            Aims = launcher.Trains,
            Traverses = launcher.TurretMarker is not null,
            Elevates = launcher.PodsMarker is not null || launcher.GunsMarker is not null,
            SweepsASearchArray = launcher.RadarMarker is not null,

            // A set with no range detects nothing, which is the only way to declare "no sensor"
            // while every launcher still names one.
            Searches = sensor.Range > 0f,

            Drops = Drop(armaments),
        };
    }

    /// <summary>
    /// Rounds a full launcher holds: a deep magazine's depth, otherwise one per tube. The same
    /// two numbers <c>Magazine.Resize</c> reads, so the panel counts down from what the magazine
    /// was actually filled with.
    /// </summary>
    // Whether any armament throws something the ground stops. Resolved here rather than in the
    // panel so a profile field is never the thing a control is gated on -- an unknown munition
    // name answers no, which draws one control fewer rather than throwing at a tick box.
    private static bool Drop(List<Armament> armaments)
    {
        for (int i = 0; i < armaments.Count; i++)
        {
            if (Arsenal.MunitionNamed(armaments[i].Munition) is { HitsTerrain: true }) return true;
        }

        return false;
    }

    public static int MagazineCapacity(LauncherProfile launcher)
        => launcher.MagazineDepth > launcher.TubeCount ? launcher.MagazineDepth : launcher.TubeCount;
}
