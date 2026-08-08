using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Search-and-track radar. Sweeps a cone about the battery's boresight and classifies
/// contacts as threats using their closest point of approach rather than raw closing
/// speed, so a target crossing the site is engaged just as readily as one flying at it.
/// </summary>
internal sealed class Radar(Config config, SystemConfig policy)
{
    private readonly Config _config = config;
    private readonly SystemConfig _policy = policy;

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
    /// <summary>
    /// What the operator picked from the track list, as an <see cref="IContact.Handle"/>. An
    /// object rather than a craft: a contact need not be one.
    /// </summary>
    public object? ManualDesignation { get; set; }

    /// <summary>
    /// Craft the last scan discarded because the planet was in the way.
    ///
    /// <para>Counted rather than dropped quietly: a battery that suddenly sees nothing looks
    /// broken, and this is the difference between "nothing is flying" and "everything is behind
    /// the world".</para>
    /// </summary>
    public int MaskedByTerrain { get; private set; }

    // Held between scans so a contact's dwell time survives track rebuilds.
    private readonly Dictionary<object, double> _dwell = new();

    /// <summary>
    /// Rebuilds the track list from the current world state.
    /// </summary>
    /// <param name="platform">The vehicle carrying the battery.</param>
    /// <param name="boresight">Unit vector the radar is pointed along, in Ecl.</param>
    /// <param name="dt">Seconds since the previous scan.</param>
    /// <param name="airborne">
    /// Contacts that are not craft -- rounds somebody else has in the air. Assessed through the
    /// same threat model and the same IFF as anything else: a sensor should not care what kind of
    /// thing it is looking at, only where it is going and whose it is.
    /// </param>
    public void Scan(Vehicle platform, double3 boresight, double dt,
                     IReadOnlyList<IContact>? airborne = null)
    {
        Tracks.Clear();
        MaskedByTerrain = 0;

        double3 originEcl = KsaWorld.PositionEcl(platform);
        double3 originVel = KsaWorld.VelocityEcl(platform);

        KsaWorld.CollectVehicles(_scratch);

        foreach (Vehicle candidate in _scratch)
        {
            if (ReferenceEquals(candidate, platform)) continue;
            if (_policy.ProtectControlledVehicle && ReferenceEquals(candidate, KsaWorld.ControlledVehicle)) continue;

            Consider(new VehicleContact(candidate), originEcl, originVel, boresight, dt);
        }

        if (airborne is not null)
        {
            for (int i = 0; i < airborne.Count; i++) Consider(airborne[i], originEcl, originVel, boresight, dt);
        }

        // Refresh dwell bookkeeping, dropping anything we no longer see.
        _dwell.Clear();
        foreach (Track t in Tracks) _dwell[t.Contact.Handle] = t.HeldSeconds;

        ThreatModel.SortByPriority(Tracks);

        UpdateLock();
    }

    // KSA has no team field, so the craft's name is the only assignment available without extra
    // UI. Longest match wins, so "Red Team" beats "Red" when both are listed.
    // One contact, through the same geometry, masking and IFF a craft gets. Anything that only a
    // craft can answer is already behind IContact, so there is nothing here that knows the
    // difference.
    private void Consider(IContact contact, double3 originEcl, double3 originVel,
                          double3 boresight, double dt)
    {
        if (!contact.IsAlive) return;

        string? team = TeamOf(contact.TeamKey);
        Allegiance allegiance = _policy.Iff.Classify(team);

        double3 targetPos = contact.PositionEcl;
        double3 targetVel = contact.VelocityEcl;

        if (_sensor.HorizonMasking
            && KsaWorld.IsOccluded(originEcl, targetPos, _sensor.TerrainMarginMetres, out _))
        {
            MaskedByTerrain++;
            return;
        }

        if (!ThreatModel.TryAssess(targetPos - originEcl, targetVel - originVel,
                                   boresight, _sensor, out var a)) return;

        Tracks.Add(new Track
        {
            Contact = contact,
            PositionEcl = targetPos,
            VelocityEcl = targetVel,
            Range = a.Range,
            ClosingSpeed = a.ClosingSpeed,
            ClosestApproach = a.ClosestApproach,
            TimeToClosestApproach = a.TimeToClosestApproach,
            HeldSeconds = _dwell.GetValueOrDefault(contact.Handle) + dt,
            IsThreat = a.IsThreat && _policy.Iff.MayEngage(allegiance),
            Team = team,
            Allegiance = allegiance,
        });
    }

    private string? TeamOf(string name)
    {
        if (_config.TeamNames.Count == 0) return null;

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
            Track? designated = Tracks.Find(t => ReferenceEquals(t.Contact.Handle, ManualDesignation));
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
