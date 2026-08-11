using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Search-and-track radar. Sweeps a cone about the battery's boresight and classifies
/// contacts as threats using their closest point of approach rather than raw closing
/// speed, so a target crossing the site is engaged just as readily as one flying at it.
/// </summary>
internal sealed class Radar(Config config, ISensorPolicy policy)
{
    private readonly Config _config = config;
    private readonly ISensorPolicy _policy = policy;

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

    /// <summary>
    /// The best contact on scope, whether or not it may be engaged. What an <em>instrument</em>
    /// follows, as against <see cref="Locked"/>, which is what a weapon may shoot.
    ///
    /// <para>The same as <see cref="Locked"/> whenever there is a threat, so a sight and a gun
    /// agree about the thing that matters. They part company over a contact that will never close:
    /// the gun is right to ignore it and the sight is right to watch it.</para>
    /// </summary>
    public Track? Watched { get; private set; }

    /// <summary>
    /// What the operator picked from the track list, as an <see cref="IContact.Handle"/>, cleared
    /// when that contact leaves it. An object rather than a craft: a contact need not be one.
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

        // Once per scan, not once per contact. Only the clutter floor reads it, and the body every
        // contact is measured against is the one under the set rather than the one under each of
        // them -- a set does not see a target against a different planet's ground.
        KsaWorld.MeanSphereUnder(originEcl, out double3 groundCentre, out double groundRadius);

        KsaWorld.CollectVehicles(_scratch);

        foreach (Vehicle candidate in _scratch)
        {
            if (ReferenceEquals(candidate, platform)) continue;
            if (_policy.ProtectControlledVehicle && ReferenceEquals(candidate, KsaWorld.ControlledVehicle)) continue;

            Consider(new VehicleContact(candidate), originEcl, originVel, boresight, dt,
                     groundCentre, groundRadius);
        }

        if (airborne is not null)
        {
            for (int i = 0; i < airborne.Count; i++)
            {
                // A set does not watch its own platform's salvo leave. The mirror of the rule
                // above for craft, and needed for the same reason: a round clearing the tubes is
                // metres away and closes nothing, so it takes the top of the priority list and
                // holds it for the round's whole flight. IFF will not do this — allegiance decides
                // what may be *engaged*, and a friendly contact is still tracked.
                //
                // On a launcher carrying a director this is the difference between a sight that
                // watches the target and one that follows every round out to 30 km.
                if (ReferenceEquals(airborne[i].LaunchedFrom, platform)) continue;

                Consider(airborne[i], originEcl, originVel, boresight, dt, groundCentre, groundRadius);
            }
        }

        // Refresh dwell bookkeeping, dropping anything no longer seen.
        _dwell.Clear();
        foreach (Track t in Tracks) _dwell[t.Contact.Handle] = t.HeldSeconds;

        ThreatModel.SortByPriority(Tracks);

        UpdateLock();
    }

    // One contact, through the same geometry, masking and IFF a craft gets. Anything that only a
    // craft can answer is already behind IContact, so there is nothing here that knows the
    // difference.
    private void Consider(IContact contact, double3 originEcl, double3 originVel,
                          double3 boresight, double dt, double3 groundCentre, double groundRadius)
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

        double height = groundRadius > 0.0
            ? Vec.Len(targetPos - groundCentre) - groundRadius
            : double.PositiveInfinity;

        var signature = new ThreatModel.ContactSignature(contact.MeanRadius, height);

        if (!ThreatModel.TryAssess(targetPos - originEcl, targetVel - originVel,
                                   boresight, _sensor, signature, out var a)) return;

        // The skyline, and last of all the rejects. Every sample is a height-map fetch, so it is
        // only worth spending on a contact that range, cone and the planet's own bulk have all
        // already let through.
        if (_sensor.HorizonMasking
            && KsaWorld.IsHiddenByTerrain(originEcl, targetPos, _sensor.TerrainSamples,
                                          _sensor.TerrainClearanceMetres, out _))
        {
            MaskedByTerrain++;
            return;
        }

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

    // KSA has no team field, so the craft's name is the only assignment available without extra
    // UI. Longest match wins, so "Red Team" beats "Red" when both are listed.
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

        // What an instrument should look at, which is not what a weapon may shoot. Tracks are
        // already sorted by priority, so the best thing on scope is the head of the list whether
        // or not it qualifies as a threat.
        //
        // A weapon is right to hold fire at something that will never come close; a sight pointed
        // at it is doing its job. Without this a director watches nothing at all whenever the only
        // contact is a passer-by, and lags the launcher whenever a threat is still maturing —
        // fire control reaches its own verdict first and the missiles leave before the picture
        // has moved.
        Watched = Locked ?? (Tracks.Count > 0 ? Tracks[0] : null);
    }

    /// <summary>True when the locked contact has been held long enough to shoot at.</summary>
    public bool HasFiringSolution =>
        Locked is not null && Locked.IsThreat && Locked.HeldSeconds >= _sensor.LockSeconds;

    public void Reset()
    {
        Tracks.Clear();
        _dwell.Clear();
        Locked = null;
        Watched = null;
        ManualDesignation = null;
    }
}
