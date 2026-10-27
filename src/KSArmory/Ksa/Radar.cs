using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Search-and-track radar. Sweeps a cone about the system's boresight and classifies
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
    /// <para>Owned by the system that fitted it rather than read through <c>Config</c>.</para>
    /// </summary>
    /// <remarks>
    /// Zero range until a launcher is adopted, never the first registered profile's reach.
    /// </remarks>
    public SensorProfile Sensor { get; set; } = SensorProfile.None;

    private SensorProfile _sensor => Sensor;

    /// <summary>Live tracks, highest priority first. Rebuilt every scan.</summary>
    public List<Track> Tracks { get; } = [];

    /// <summary>The track currently designated for engagement, if any.</summary>
    public Track? Locked { get; private set; }

    /// <summary>
    /// The best contact on scope, whether or not it may be engaged. What an <em>instrument</em>
    /// follows, as against <see cref="Locked"/>, which is what a weapon may shoot.
    ///
    /// <para>The same as <see cref="Locked"/> whenever there is a threat; they differ over a contact
    /// that will never close.</para>
    /// </summary>
    public Track? Watched { get; private set; }

    private object? _watchedHandle;

    /// <summary>
    /// What the operator picked from the track list, as an <see cref="IContact.Handle"/>, cleared
    /// when that contact leaves it. An object rather than a craft: a contact need not be one.
    /// </summary>
    public object? ManualDesignation { get; set; }

    /// <summary>
    /// What a <see cref="SensorProfile.CuedOnly"/> set has been handed to hold, as an
    /// <see cref="IContact.Handle"/>. Every other contact is left out of its scan.
    /// </summary>
    public object? Cue { get; set; }

    /// <summary>
    /// Craft the last scan discarded because the planet was in the way.
    ///
    /// <para>Counted so a set that sees nothing can say why.</para>
    /// </summary>
    public int MaskedByTerrain { get; private set; }

    /// <summary>
    /// Craft the last scan lost to a nuclear fireball's ionised air, counted for the same reason:
    /// a set that has gone blind reads as a broken one unless it says why.
    /// </summary>
    public int MaskedByBurst { get; private set; }

    /// <summary>
    /// Contacts in an <see cref="SensorProfile.Infrared"/> set's cone the last scan found too cool to
    /// see at their range, counted for the same reason.
    /// </summary>
    public int TooFaint { get; private set; }

    /// <summary>Contacts the last scan lost to chaff beside them in the notch. See <see cref="ChaffNotch"/>.</summary>
    public int LostToChaff { get; private set; }

    private readonly ChaffNotch _notch = new();

    /// <summary>Whether chaff has taken this contact's track and the set is still hunting for it.</summary>
    public bool ChaffBroke(object? handle) => _notch.IsBroken(handle);

    /// <summary>
    /// Pieces of craft this mod's warheads broke up, left out of the picture this scan, counted for
    /// the same reason. See <see cref="KSArmory.Wreckage"/>.
    /// </summary>
    public int IgnoredWreckage { get; private set; }

    // Held between scans so a contact's dwell time survives track rebuilds.
    private readonly Dictionary<object, double> _dwell = new();

    // Each contact's velocity history, carried between scans the same way. Two maps swapped each
    // scan, so what was not seen this scan is dropped and starts again if it comes back.
    private Dictionary<object, AccelerationEstimate> _acceleration = new(ReferenceEqualityComparer.Instance);
    private Dictionary<object, AccelerationEstimate> _accelerationNext = new(ReferenceEqualityComparer.Instance);
    // An index over Tracks, refilled every scan. Reference equality because a handle is an
    // identity: a contact type overriding Equals would fold two contacts onto one track.
    private readonly Dictionary<object, Track> _byHandle = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The live track for a target handle, or null if this set is not holding it.
    ///
    /// <para>O(1) because fire control asks once per round in the air.</para>
    /// </summary>
    public Track? TrackFor(object? handle) =>
        handle is not null && _byHandle.TryGetValue(handle, out Track? track) ? track : null;

    /// <param name="boresight">Unit vector the radar is pointed along, in Ecl.</param>
    /// <param name="dt">Seconds since the previous scan.</param>
    /// <param name="airborne">
    /// Rounds somebody else has in the air, through the same threat model and IFF as a craft.
    /// </param>
    public void Scan(Vehicle platform, double3 boresight, double dt,
                     IReadOnlyList<IContact>? airborne = null)
    {
        Tracks.Clear();
        _byHandle.Clear();
        MaskedByTerrain = 0;
        MaskedByBurst = 0;
        TooFaint = 0;
        LostToChaff = 0;
        IgnoredWreckage = 0;
        _notch.BeginScan(dt);

        // A silent set sees nothing: going quiet against an anti-radiation round costs the site its
        // picture. A passive set answers false and is unaffected.
        if (_sensor.Emits && _policy.RadarSilent)
        {
            _dwell.Clear();
            _acceleration.Clear();
            Locked = null;
            return;
        }

        double3 originEcl = KsaWorld.PositionEcl(platform);
        double3 originVel = KsaWorld.VelocityEcl(platform);

        // Once per scan: every contact is measured against the body under the set, not under it.
        KsaWorld.MeanSphereUnder(originEcl, out double3 groundCentre, out double groundRadius);

        IReadOnlyList<Vehicle> world = KsaWorld.Vehicles;

        for (int i = 0; i < world.Count; i++)
        {
            Vehicle candidate = world[i];

            if (ReferenceEquals(candidate, platform)) continue;
            if (_sensor.CuedOnly && !ReferenceEquals(candidate, Cue)) continue;
            if (_policy.ProtectControlledVehicle && ReferenceEquals(candidate, KsaWorld.ControlledVehicle)) continue;

            if (KsaWorld.Wreckage.IsPiece(KsaWorld.DisplayName(candidate)))
            {
                IgnoredWreckage++;
                continue;
            }

            Consider(new VehicleContact(candidate), platform, originEcl, originVel, boresight, dt,
                     groundCentre, groundRadius);
        }

        if (airborne is not null)
        {
            for (int i = 0; i < airborne.Count; i++)
            {
                // A set does not watch its own platform's salvo leave: a round clearing the tubes
                // is metres away and would hold the top of the priority list. IFF does not cover
                // this, because a friendly contact is still tracked.
                if (ReferenceEquals(airborne[i].LaunchedFrom, platform)) continue;
                if (_sensor.CuedOnly && !ReferenceEquals(airborne[i].Handle, Cue)) continue;

                Consider(airborne[i], platform, originEcl, originVel, boresight, dt, groundCentre, groundRadius);
            }
        }

        _notch.EndScan();

        (_acceleration, _accelerationNext) = (_accelerationNext, _acceleration);
        _accelerationNext.Clear();

        _dwell.Clear();
        foreach (Track t in Tracks)
        {
            _dwell[t.Contact.Handle] = t.HeldSeconds;
            _byHandle[t.Contact.Handle] = t;
        }

        ThreatModel.SortByPriority(Tracks);

        UpdateLock();
    }

    private void Consider(IContact contact, Vehicle platform, double3 originEcl, double3 originVel,
                          double3 boresight, double dt, double3 groundCentre, double groundRadius)
    {
        if (!contact.IsAlive) return;

        // What the panel's flag put it on, and nothing else: a craft's name says what it is called,
        // not whose it is.
        string? team = contact.DeclaredTeam;
        Allegiance allegiance = _policy.Iff.Classify(team);

        double3 targetPos = contact.PositionEcl;
        double3 targetVel = contact.VelocityEcl;

        // The engine's accelerometer, unless how the velocity has actually changed says it is wrong:
        // an impulse or a tumbling frame. See AccelerationEstimate.
        if (!_acceleration.Remove(contact.Handle, out AccelerationEstimate? history)) history = new AccelerationEstimate();
        history.Add(targetVel, dt);
        _accelerationNext[contact.Handle] = history;
        double3 acceleration = history.Believe(contact.AccelerationEcl);
        if (_sensor.HorizonMasking
            && KsaWorld.IsOccluded(originEcl, targetPos, _sensor.TerrainMarginMetres, out _))
        {
            MaskedByTerrain++;
            return;
        }

        double height = groundRadius > 0.0
            ? Vec.Len(targetPos - groundCentre) - groundRadius
            : double.PositiveInfinity;

        var signature = new ThreatModel.ContactSignature(contact.MeanRadius, height, contact.IsMunition);

        if (!ThreatModel.TryAssess(targetPos - originEcl, targetVel - originVel,
                                   boresight, _sensor, signature, out var a)) return;

        if (_sensor.Infrared
            && !InfraredSearch.Sees(_sensor, contact.HeatKwPerSr, originEcl, targetPos,
                                    KsaWorld.AirDensityRatioAt(platform, originEcl),
                                    KsaWorld.AirDensityRatioAt(platform, targetPos),
                                    groundCentre, groundRadius))
        {
            TooFaint++;
            return;
        }

        // Chaff in the notch, which takes the track rather than hiding the contact: dropping it here is
        // what resets its dwell, so the lock has to mature again once it is found.
        bool wasBroken = _notch.IsBroken(contact.Handle);
        if (_notch.Hides(_sensor, contact.Handle, originEcl, originVel, targetPos, targetVel,
                         RadarSignature.CrossSectionFor(contact.MeanRadius), Countermeasures.Live))
        {
            LostToChaff++;
            if (!wasBroken) Log.Info($"{_sensor.DisplayName} lost {contact.DisplayName} to chaff in the notch");
            return;
        }

        // A fireball's ionised air absorbs a beam that crosses it; asked only of a transmitting set.
        if (_sensor.Emits && _config.NuclearBlackout && NuclearClouds.BlacksOut(originEcl, targetPos))
        {
            MaskedByBurst++;
            return;
        }

        // The skyline last of all the rejects: every sample is a height-map fetch.
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
            AccelerationEcl = acceleration,
            DragShape = contact.DragShape,
            Range = a.Range,
            ClosingSpeed = a.ClosingSpeed,
            ClosestApproach = a.ClosestApproach,
            TimeToClosestApproach = a.TimeToClosestApproach,
            HeldSeconds = _dwell.GetValueOrDefault(contact.Handle) + dt,
            IsThreat = (a.IsThreat || _sensor.CuedOnly) && !contact.IsDebris && _policy.Iff.MayEngage(allegiance),
            Team = team,
            Allegiance = allegiance,
        });
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
                Watched = designated;
                _watchedHandle = designated.Contact.Handle;
                return;
            }
            ManualDesignation = null;
        }

        int first = ThreatModel.IndexOfFirstThreat(Tracks);
        Locked = first >= 0 ? Tracks[first] : null;

        // What an instrument should look at: the best thing on scope whether or not it is a threat,
        // held until another is clearly better (Sim/WatchChoice.cs).
        int fresh = Locked is not null ? first : Tracks.Count > 0 ? 0 : -1;
        int held = _watchedHandle is null ? -1 : Tracks.FindIndex(t => ReferenceEquals(t.Contact.Handle, _watchedHandle));
        int watched = WatchChoice.Choose(Tracks, fresh, held);

        Watched = watched >= 0 ? Tracks[watched] : null;
        _watchedHandle = Watched?.Contact.Handle;
    }

    /// <summary>True when the locked contact has been held long enough to shoot at.</summary>
    public bool HasFiringSolution => IsFiringSolution(Locked);

    /// <summary>Whether a track is a threat held long enough to shoot at, whether or not it is the lock.</summary>
    public bool IsFiringSolution(Track? track) =>
        track is not null && track.IsThreat && track.HeldSeconds >= _sensor.LockSeconds;

    public void Reset()
    {
        Tracks.Clear();
        _byHandle.Clear();
        _dwell.Clear();
        _acceleration.Clear();
        Locked = null;
        Watched = null;
        _watchedHandle = null;
        ManualDesignation = null;
    }
}
