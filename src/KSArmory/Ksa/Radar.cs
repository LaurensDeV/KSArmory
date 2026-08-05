using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Search-and-track radar. Sweeps a cone about the battery's boresight and classifies
/// contacts as threats using their closest point of approach rather than raw closing
/// speed, so a target crossing the site is engaged just as readily as one flying at it.
/// </summary>
internal sealed class Radar(Config config, BatteryConfig policy)
{
    private readonly Config _config = config;
    private readonly BatteryConfig _policy = policy;

    /// <summary>
    /// What this set can see.
    ///
    /// <para>Owned by the battery that fitted it rather than read through <c>Config</c>: with more
    /// than one battery alive a shared field is whichever system resolved last. Live tuning still
    /// works, because profiles are shared instances.</para>
    /// </summary>
    public SensorProfile Sensor { get; set; } = Arsenal.SearchRadar1Rs1;

    private SensorProfile _sensor => Sensor;
    private readonly List<Vehicle> _scratch = [];

    /// <summary>Live tracks, highest priority first. Rebuilt every scan.</summary>
    public List<Track> Tracks { get; } = [];

    /// <summary>The track currently designated for engagement, if any.</summary>
    public Track? Locked { get; private set; }

    /// <summary>Set when the operator picks a target by hand; clears on lock loss.</summary>
    public Vehicle? ManualDesignation { get; set; }

    // Held between scans so a contact's dwell time survives track rebuilds.
    private readonly Dictionary<Vehicle, double> _dwell = new();

    /// <summary>
    /// Rebuilds the track list from the current world state.
    /// </summary>
    /// <param name="platform">The vehicle carrying the battery.</param>
    /// <param name="boresight">Unit vector the radar is pointed along, in Ecl.</param>
    /// <param name="dt">Seconds since the previous scan.</param>
    public void Scan(Vehicle platform, double3 boresight, double dt)
    {
        Tracks.Clear();

        double3 originEcl = KsaWorld.PositionEcl(platform);
        double3 originVel = KsaWorld.VelocityEcl(platform);

        KsaWorld.CollectVehicles(_scratch);

        foreach (Vehicle candidate in _scratch)
        {
            if (ReferenceEquals(candidate, platform)) continue;
            if (_config.ProtectControlledVehicle && ReferenceEquals(candidate, KsaWorld.ControlledVehicle)) continue;

            // Classified before the geometry, so a friendly still appears on the panel as a
            // contact and is simply never handed to fire control.
            string? team = TeamOf(candidate);
            Allegiance allegiance = _policy.Iff.Classify(team);

            double3 targetPos = KsaWorld.PositionEcl(candidate);
            double3 targetVel = KsaWorld.VelocityEcl(candidate);

            // Relative motion, so the ecliptic frame's huge common position and velocity cancel
            // before any of the geometry runs. The maths itself lives in Sim/ and is tested.
            if (!ThreatModel.TryAssess(targetPos - originEcl, targetVel - originVel,
                                       boresight, _sensor, out var a)) continue;

            Tracks.Add(new Track
            {
                Vehicle = candidate,
                PositionEcl = targetPos,
                VelocityEcl = targetVel,
                Range = a.Range,
                ClosingSpeed = a.ClosingSpeed,
                ClosestApproach = a.ClosestApproach,
                TimeToClosestApproach = a.TimeToClosestApproach,
                HeldSeconds = _dwell.GetValueOrDefault(candidate) + dt,
                IsThreat = a.IsThreat && _policy.Iff.MayEngage(allegiance),
                Team = team,
                Allegiance = allegiance,
            });
        }

        // Refresh dwell bookkeeping, dropping anything we no longer see.
        _dwell.Clear();
        foreach (Track t in Tracks) _dwell[t.Vehicle] = t.HeldSeconds;

        ThreatModel.SortByPriority(Tracks);

        UpdateLock();
    }

    // KSA has no team field, so the craft's name is the only assignment available without extra
    // UI. Longest match wins, so "Red Team" beats "Red" when both are listed.
    private string? TeamOf(Vehicle v)
    {
        if (_config.TeamNames.Count == 0) return null;

        string name = KsaWorld.DisplayName(v);
        string? best = null;

        foreach (string team in _config.TeamNames)
        {
            if (string.IsNullOrWhiteSpace(team)) continue;
            if (name.Contains(team, StringComparison.OrdinalIgnoreCase)
                && (best is null || team.Length > best.Length))
            {
                best = team;
            }
        }
        return best;
    }

    private void UpdateLock()
    {
        // An operator designation wins as long as the contact is still on scope.
        if (ManualDesignation is not null)
        {
            Track? designated = Tracks.Find(t => ReferenceEquals(t.Vehicle, ManualDesignation));
            if (designated is not null)
            {
                Locked = designated;
                return;
            }
            ManualDesignation = null;
        }

        int first = ThreatModel.IndexOfFirstThreat(Tracks);
        Locked = first >= 0 ? Tracks[first] : null;
    }

    /// <summary>True when the locked contact has been held long enough to shoot at.</summary>
    public bool HasFiringSolution =>
        Locked is not null && Locked.IsThreat && Locked.HeldSeconds >= _sensor.LockSeconds;

    public void Reset()
    {
        Tracks.Clear();
        _dwell.Clear();
        Locked = null;
        ManualDesignation = null;
    }
}
