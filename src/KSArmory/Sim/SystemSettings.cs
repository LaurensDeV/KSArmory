namespace KSArmory;

/// <summary>
/// One system's settings, flattened so they can be written down and read back.
///
/// <para>Not <see cref="SystemConfig"/> serialised directly, so a field added or renamed there is not
/// a file-format change. Defaults match <c>SystemConfig</c>'s, so an older file loads a new setting at
/// its default rather than at zero, which for <c>MissilesEnabled</c> would disarm the system.</para>
/// </summary>
public sealed class SystemSettings
{
    public bool AutoEngage { get; set; }
    public bool ProtectControlledVehicle { get; set; } = true;

    public bool ChaseRounds { get; set; }
    public bool MissilesEnabled { get; set; } = true;
    public bool GunsEnabled { get; set; } = true;
    public bool BeamEnabled { get; set; } = true;
    public int RoundsPerTarget { get; set; } = 2;
    public bool MouseAim { get; set; }
    public bool TurretTracking { get; set; } = true;
    public bool TurretManual { get; set; }
    public float TurretManualBearingDeg { get; set; }
    public float TurretManualElevationDeg { get; set; } = 55f;
    public bool TurretSpin { get; set; }
    public bool SearchRadarStopped { get; set; }
    public bool RadarSilent { get; set; }
    public bool DrawBombSight { get; set; } = true;
    public bool ScopeOpen { get; set; }
    public float ScopeRangeMetres { get; set; }

    public string? OwnTeam { get; set; }
    public bool EngageUnknown { get; set; } = true;
    public bool EngageNeutral { get; set; }
    public bool ProtectFriendly { get; set; } = true;
    public List<string> AlliedTeams { get; set; } = [];
    public List<string> NeutralTeams { get; set; } = [];

    public static SystemSettings From(SystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new SystemSettings
        {
            AutoEngage = config.AutoEngage,
            ProtectControlledVehicle = config.ProtectControlledVehicle,
            ChaseRounds = config.ChaseRounds,
            MissilesEnabled = config.MissilesEnabled,
            GunsEnabled = config.GunsEnabled,
            BeamEnabled = config.BeamEnabled,
            RoundsPerTarget = config.RoundsPerTarget,
            MouseAim = config.MouseAim,
            TurretTracking = config.TurretTracking,
            TurretManual = config.TurretManual,
            TurretManualBearingDeg = config.TurretManualBearingDeg,
            TurretManualElevationDeg = config.TurretManualElevationDeg,
            TurretSpin = config.TurretSpin,
            SearchRadarStopped = config.SearchRadarStopped,
            RadarSilent = config.RadarSilent,
            DrawBombSight = config.DrawBombSight,
            ScopeOpen = config.ScopeOpen,
            ScopeRangeMetres = config.ScopeRangeMetres,

            OwnTeam = config.Iff.OwnTeam,
            EngageUnknown = config.Iff.EngageUnknown,
            EngageNeutral = config.Iff.EngageNeutral,
            ProtectFriendly = config.Iff.ProtectFriendly,
            AlliedTeams = [.. config.Iff.AlliedTeams],
            NeutralTeams = [.. config.Iff.NeutralTeams],
        };
    }

    /// <summary>
    /// Puts every team this system names back on the session's roster of team names.
    ///
    /// <para>The names are session-wide and memberships per system, so without this a reload classifies
    /// every contact Unknown, which is engaged by default.</para>
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

    /// <summary>
    /// Puts these settings onto a system. A director's are not here: it keeps its own
    /// <see cref="OpticConfig"/>, crewed per part.
    /// </summary>
    public void ApplyTo(SystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.AutoEngage = AutoEngage;
        config.ProtectControlledVehicle = ProtectControlledVehicle;
        config.ChaseRounds = ChaseRounds;
        config.MissilesEnabled = MissilesEnabled;
        config.GunsEnabled = GunsEnabled;
        config.BeamEnabled = BeamEnabled;
        config.RoundsPerTarget = Math.Max(1, RoundsPerTarget);
        config.MouseAim = MouseAim;
        config.TurretTracking = TurretTracking;
        config.TurretManual = TurretManual;
        config.TurretManualBearingDeg = TurretManualBearingDeg;
        config.TurretManualElevationDeg = TurretManualElevationDeg;
        config.TurretSpin = TurretSpin;
        config.SearchRadarStopped = SearchRadarStopped;
        config.RadarSilent = RadarSilent;
        config.DrawBombSight = DrawBombSight;
        config.ScopeOpen = ScopeOpen;
        config.ScopeRangeMetres = ScopeRangeMetres;

        config.Iff.OwnTeam = OwnTeam;
        config.Iff.EngageUnknown = EngageUnknown;
        config.Iff.EngageNeutral = EngageNeutral;
        config.Iff.ProtectFriendly = ProtectFriendly;

        config.Iff.AlliedTeams.Clear();
        foreach (string t in AlliedTeams ?? []) config.Iff.AlliedTeams.Add(t);

        config.Iff.NeutralTeams.Clear();
        foreach (string t in NeutralTeams ?? []) config.Iff.NeutralTeams.Add(t);
    }

    /// <summary>Whether two settings differ, so nothing is written when nothing has changed.</summary>
    public bool Differs(SystemSettings other)
    {
        if (other is null) return true;

        return AutoEngage != other.AutoEngage
               || ProtectControlledVehicle != other.ProtectControlledVehicle
               || ChaseRounds != other.ChaseRounds
               || MissilesEnabled != other.MissilesEnabled
               || GunsEnabled != other.GunsEnabled
               || BeamEnabled != other.BeamEnabled
               || RoundsPerTarget != other.RoundsPerTarget
               || MouseAim != other.MouseAim
               || TurretTracking != other.TurretTracking
               || TurretManual != other.TurretManual
               || Math.Abs(TurretManualBearingDeg - other.TurretManualBearingDeg) > 1e-3f
               || Math.Abs(TurretManualElevationDeg - other.TurretManualElevationDeg) > 1e-3f
               || TurretSpin != other.TurretSpin
               || SearchRadarStopped != other.SearchRadarStopped
               || RadarSilent != other.RadarSilent
               || DrawBombSight != other.DrawBombSight
               || ScopeOpen != other.ScopeOpen
               || Math.Abs(ScopeRangeMetres - other.ScopeRangeMetres) > 1e-3f
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
