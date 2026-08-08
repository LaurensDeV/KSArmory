namespace KSArmory;

/// <summary>
/// One battery's settings, flattened so they can be written down and read back.
///
/// <para>A separate type from <see cref="SystemConfig"/> rather than serialising that directly.
/// <c>SystemConfig</c> is what the panel edits and the fire control reads; it gains fields freely
/// and holds an <see cref="IffPolicy"/> with collections behind properties. Persisting it as-is
/// would make every field a file-format decision, and a rename would silently drop a setting
/// someone had chosen.</para>
///
/// <para>Everything here is a plain field with a default that matches <c>SystemConfig</c>'s, so a
/// file written by an older version loads with the new settings at their defaults rather than at
/// zero — which for <c>MissilesEnabled</c> or <c>TurretTracking</c> would silently disarm half a
/// battery.</para>
/// </summary>
public sealed class SystemSettings
{
    public bool Armed { get; set; }
    public bool AutoEngage { get; set; }
    public bool ProtectControlledVehicle { get; set; } = true;

    public bool ChaseRounds { get; set; }
    public bool MissilesEnabled { get; set; } = true;
    public bool GunsEnabled { get; set; } = true;
    public int RoundsPerTarget { get; set; } = 2;
    public bool MouseAim { get; set; }
    public bool TurretTracking { get; set; } = true;
    public bool TurretManual { get; set; }
    public float TurretManualBearingDeg { get; set; }
    public float TurretManualElevationDeg { get; set; } = 55f;
    public bool TurretSpin { get; set; }
    public bool SearchRadarStopped { get; set; }

    public string? OwnTeam { get; set; }
    public bool EngageUnknown { get; set; } = true;
    public bool EngageNeutral { get; set; }
    public bool ProtectFriendly { get; set; } = true;
    public List<string> AlliedTeams { get; set; } = [];
    public List<string> NeutralTeams { get; set; } = [];

    /// <summary>Reads a battery's current settings.</summary>
    public static SystemSettings From(SystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new SystemSettings
        {
            Armed = config.Armed,
            AutoEngage = config.AutoEngage,
            ProtectControlledVehicle = config.ProtectControlledVehicle,
            ChaseRounds = config.ChaseRounds,
            MissilesEnabled = config.MissilesEnabled,
            GunsEnabled = config.GunsEnabled,
            RoundsPerTarget = config.RoundsPerTarget,
            MouseAim = config.MouseAim,
            TurretTracking = config.TurretTracking,
            TurretManual = config.TurretManual,
            TurretManualBearingDeg = config.TurretManualBearingDeg,
            TurretManualElevationDeg = config.TurretManualElevationDeg,
            TurretSpin = config.TurretSpin,
            SearchRadarStopped = config.SearchRadarStopped,

            OwnTeam = config.Iff.OwnTeam,
            EngageUnknown = config.Iff.EngageUnknown,
            EngageNeutral = config.Iff.EngageNeutral,
            ProtectFriendly = config.Iff.ProtectFriendly,
            AlliedTeams = [.. config.Iff.AlliedTeams],
            NeutralTeams = [.. config.Iff.NeutralTeams],
        };
    }

    /// <summary>
    /// Puts these settings onto a battery.
    ///
    /// <para><see cref="SystemConfig.OpticViewport"/> is deliberately not carried: it names a
    /// viewport index in the session that saved it, and restoring it would point a new session's
    /// camera at a window that may not exist.</para>
    /// </summary>
    /// <summary>
    /// Puts every team this system names back on the session's roster of team names.
    ///
    /// <para>The names are session-wide and the memberships are per system, so only half of a
    /// two-sided world survives a reload on its own: each system remembers it is on "Red" and the
    /// world has forgotten that "Red" is a team. Every contact then classifies Unknown, and
    /// engaging the unknown is permissive by default, so a carefully divided world comes back as
    /// a free-for-all with the panel still showing the old allegiances.</para>
    /// </summary>
    public void DeclareTeams(List<string> teamNames)
    {
        Declare(teamNames, OwnTeam);
        foreach (string t in AlliedTeams) Declare(teamNames, t);
        foreach (string t in NeutralTeams) Declare(teamNames, t);
    }

    private static void Declare(List<string> into, string? team)
    {
        if (string.IsNullOrWhiteSpace(team)) return;
        if (into.Contains(team, StringComparer.OrdinalIgnoreCase)) return;

        into.Add(team);
    }

    public void ApplyTo(SystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.Armed = Armed;
        config.AutoEngage = AutoEngage;
        config.ProtectControlledVehicle = ProtectControlledVehicle;
        config.ChaseRounds = ChaseRounds;
        config.MissilesEnabled = MissilesEnabled;
        config.GunsEnabled = GunsEnabled;
        config.RoundsPerTarget = Math.Max(1, RoundsPerTarget);
        config.MouseAim = MouseAim;
        config.TurretTracking = TurretTracking;
        config.TurretManual = TurretManual;
        config.TurretManualBearingDeg = TurretManualBearingDeg;
        config.TurretManualElevationDeg = TurretManualElevationDeg;
        config.TurretSpin = TurretSpin;
        config.SearchRadarStopped = SearchRadarStopped;

        config.Iff.OwnTeam = OwnTeam;
        config.Iff.EngageUnknown = EngageUnknown;
        config.Iff.EngageNeutral = EngageNeutral;
        config.Iff.ProtectFriendly = ProtectFriendly;

        config.Iff.AlliedTeams.Clear();
        foreach (string t in AlliedTeams ?? []) config.Iff.AlliedTeams.Add(t);

        config.Iff.NeutralTeams.Clear();
        foreach (string t in NeutralTeams ?? []) config.Iff.NeutralTeams.Add(t);
    }

    /// <summary>
    /// Whether two settings differ, so nothing is written when nothing has changed.
    /// </summary>
    public bool Differs(SystemSettings other)
    {
        if (other is null) return true;

        return Armed != other.Armed
               || AutoEngage != other.AutoEngage
               || ProtectControlledVehicle != other.ProtectControlledVehicle
               || ChaseRounds != other.ChaseRounds
               || MissilesEnabled != other.MissilesEnabled
               || GunsEnabled != other.GunsEnabled
               || RoundsPerTarget != other.RoundsPerTarget
               || MouseAim != other.MouseAim
               || TurretTracking != other.TurretTracking
               || TurretManual != other.TurretManual
               || Math.Abs(TurretManualBearingDeg - other.TurretManualBearingDeg) > 1e-3f
               || Math.Abs(TurretManualElevationDeg - other.TurretManualElevationDeg) > 1e-3f
               || TurretSpin != other.TurretSpin
               || SearchRadarStopped != other.SearchRadarStopped
               || !string.Equals(OwnTeam, other.OwnTeam, StringComparison.Ordinal)
               || EngageUnknown != other.EngageUnknown
               || EngageNeutral != other.EngageNeutral
               || ProtectFriendly != other.ProtectFriendly
               || !SameTeams(AlliedTeams, other.AlliedTeams)
               || !SameTeams(NeutralTeams, other.NeutralTeams);
    }

    private static bool SameTeams(List<string>? a, List<string>? b)
    {
        a ??= [];
        b ??= [];
        if (a.Count != b.Count) return false;

        foreach (string t in a)
        {
            if (!b.Contains(t, StringComparer.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
