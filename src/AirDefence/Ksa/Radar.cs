using Brutal.Numerics;
using KSA;

namespace AirDefence;

/// <summary>
/// Search-and-track radar. Sweeps a cone about the battery's boresight and classifies
/// contacts as threats using their closest point of approach rather than raw closing
/// speed, so a target crossing the site is engaged just as readily as one flying at it.
/// </summary>
internal sealed class Radar(Config config)
{
    private readonly Config _config = config;

    /// <summary>
    /// What this set can see. Read through the config each time rather than captured, so that
    /// re-selecting the weapon system — or tuning it from the panel — takes effect immediately.
    /// </summary>
    private SensorProfile _sensor => _config.Sensor;
    private readonly List<Vehicle> _scratch = [];

    /// <summary>Live tracks, highest priority first. Rebuilt every scan.</summary>
    public List<Track> Tracks { get; } = [];

    /// <summary>The track currently designated for engagement, if any.</summary>
    public Track? Locked { get; private set; }

    /// <summary>Set when the operator picks a target by hand; clears on lock loss.</summary>
    public Vehicle? ManualDesignation { get; set; }

    /// <summary>Held between scans so a contact's dwell time survives track rebuilds.</summary>
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
        double coneCos = Math.Cos(_sensor.ConeHalfAngleRad);
        double rangeSq = (double)_sensor.Range * _sensor.Range;

        KsaWorld.CollectVehicles(_scratch);

        foreach (Vehicle candidate in _scratch)
        {
            if (ReferenceEquals(candidate, platform)) continue;
            if (_config.ProtectControlledVehicle && ReferenceEquals(candidate, KsaWorld.ControlledVehicle)) continue;

            double3 targetPos = KsaWorld.PositionEcl(candidate);
            double3 r = targetPos - originEcl;
            double rangeSquared = Vec.Len2(r);
            if (rangeSquared > rangeSq || rangeSquared < 1.0) continue;

            // Inside the search cone?
            if (Vec.Dot(Vec.Unit(r), boresight) < coneCos) continue;

            double3 targetVel = KsaWorld.VelocityEcl(candidate);
            double3 v = targetVel - originVel;

            double relSpeed = Vec.Len(v);
            if (relSpeed < _sensor.MinTargetSpeed) continue;

            double range = Math.Sqrt(rangeSquared);

            // Closest point of approach against the battery, assuming both hold course.
            double tCa = Vec.TimeOfClosestApproach(r, v, _sensor.ThreatHorizonSeconds);
            double cpa = Vec.Len(r + v * tCa);

            double held = _dwell.GetValueOrDefault(candidate) + dt;

            var track = new Track
            {
                Vehicle = candidate,
                PositionEcl = targetPos,
                VelocityEcl = targetVel,
                Range = range,
                ClosingSpeed = -Vec.Dot(v, Vec.Unit(r)),
                ClosestApproach = cpa,
                TimeToClosestApproach = tCa,
                HeldSeconds = held,
            };

            // A threat either will pass close enough to matter, or is already inside the bubble.
            track.IsThreat = cpa <= _sensor.ThreatRadius || range <= _sensor.ThreatRadius;

            Tracks.Add(track);
        }

        // Refresh dwell bookkeeping, dropping anything we no longer see.
        _dwell.Clear();
        foreach (Track t in Tracks) _dwell[t.Vehicle] = t.HeldSeconds;

        Tracks.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));

        UpdateLock();
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

        Locked = Tracks.Find(t => t.IsThreat);
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
