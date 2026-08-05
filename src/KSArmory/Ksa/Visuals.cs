using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Draws the engagement with the engine's gizmo renderer: the search volume, tracks,
/// and rounds in flight. Everything is submitted in Ecl and converted camera-side.
/// </summary>
internal static class Visuals
{
    private static readonly float4 ConeColour = new(0.3f, 0.75f, 1.0f, 0.9f);
    private static readonly float4 TrackColour = new(1.0f, 0.78f, 0.2f, 0.9f);
    private static readonly float4 ThreatColour = new(1.0f, 0.32f, 0.2f, 1.0f);
    private static readonly float4 LockColour = new(1.0f, 0.1f, 0.1f, 1.0f);
    private static int _drawTrace;

    private static readonly float4 RoundColour = new(1.0f, 0.95f, 0.6f, 1.0f);
    private static readonly float4 TrailColour = new(0.8f, 0.8f, 0.85f, 0.45f);
    private static readonly float4 LoadedTubeColour = new(0.45f, 1.0f, 0.5f, 0.9f);
    private static readonly float4 SpentTubeColour = new(0.3f, 0.3f, 0.32f, 0.6f);
    private static readonly float4 CpaColour = new(0.6f, 0.4f, 1.0f, 0.7f);
    private static readonly float4 TurretColour = new(0.4f, 1.0f, 0.9f, 0.8f);

    public static void Draw(DefenceBattery battery, Config config)
    {
        if (battery.Platform is null) return;

        // Anchor to the platform's render position; everything else is drawn as an offset from
        // it. Converting absolute Ecl positions instead lands the overlay on the craft's
        // analytic orbit position, which for a landed craft is visibly beside the craft itself.
        if (!KsaWorld.BeginDraw(battery.Platform, battery.PlatformEcl)) return;

        double3 origin = battery.MountEcl;

        // The overlay is clocked to the craft's own forward axis. Left to an arbitrary
        // perpendicular it turns with the planet under a boresight that is local "up".
        double3 clockRef = Vec.Zero;
        if (battery.Launcher is { } launcher)
        {
            LauncherPart.TryLauncherDirectionEcl(battery.Platform, launcher, new double3(0, 1, 0),
                                                 out clockRef);
        }

        if (config.DrawRadarVolume) DrawSearchVolume(origin, battery.Boresight, clockRef, config);
        if (config.DrawTracks) DrawTracks(battery, origin, config);
        if (config.DrawMissiles) DrawRounds(battery, config);
        if (battery.Launcher is not null && config.DrawTubeMarkers) DrawLoadedTubes(battery, config, origin);
        if (config.DrawTurretFacing) DrawTurretFacing(battery);
    }

    // Wireframe cone: boresight, a rim, and ribs out to the rim.
    private static void DrawSearchVolume(double3 origin, double3 boresight, double3 clockRef,
                                         Config config)
    {
        const int ribs = 12;

        // Drawing the cone at its true range makes it useless to look at: 20 km of thin lines
        // all radiating from one point, converging at the horizon. Draw a scaled-down shape
        // near the craft instead - it shows the *direction and angle* the radar covers, which
        // is what you actually want to see. The range readout lives in the panel.
        double range = Math.Min(config.Sensor.Range, config.ConeDisplayMetres);
        double half = config.Sensor.ConeHalfAngleRad;

        double3 axisEnd = origin + boresight * range;
        KsaWorld.DrawLineEcl(origin, axisEnd, ConeColour);

        double3 u = Vec.PerpendicularTo(boresight, clockRef);
        double3 w = Vec.Cross(boresight, u);

        double rimDist = range * Math.Cos(half);
        double rimRadius = range * Math.Sin(half);
        double3 rimCentre = origin + boresight * rimDist;

        double3 previous = default;
        for (int i = 0; i <= ribs; i++)
        {
            double a = i * (Math.Tau / ribs);
            double3 point = rimCentre + (u * Math.Cos(a) + w * Math.Sin(a)) * rimRadius;

            if (i > 0) KsaWorld.DrawLineEcl(previous, point, ConeColour);
            if (i % 3 == 0) KsaWorld.DrawLineEcl(origin, point, ConeColour);

            previous = point;
        }
    }

    private static void DrawTracks(DefenceBattery battery, double3 origin, Config config)
    {
        foreach (Track track in battery.Radar.Tracks)
        {
            bool isLock = ReferenceEquals(track, battery.Radar.Locked);
            float4 colour = isLock ? LockColour : track.IsThreat ? ThreatColour : TrackColour;

            float marker = (float)Math.Clamp(track.Range * 0.02, 12.0, 220.0);

            // Ask the engine where this vehicle is being drawn rather than deriving it, so the
            // marker sits on the craft and not on its analytic position.
            if (config.DrawTrackMarkers && KsaWorld.TryVehicleEgo(track.Vehicle, out double3 trackEgo))
                KsaWorld.DrawSphereEgo(trackEgo, marker, colour);

            if (!track.IsThreat && !isLock) continue;

            // End the line on the engine's position for the contact, not its analytic one, so
            // it touches the craft rather than pointing near it.
            if (KsaWorld.TryEclToEgo(origin, out double3 originEgo) && KsaWorld.TryVehicleEgo(track.Vehicle, out double3 endEgo))
            {
                KsaWorld.DrawLineEgo(originEgo, endEgo, colour);
            }

            // Where this contact will pass us, if it holds course. Opt-in: the prediction can
            // land kilometres from anything on screen, where it just looks like a stray dot.
            if (config.DrawClosestApproach)
            {
                double3 cpa = track.PositionEcl + (track.VelocityEcl - KsaWorld.VelocityEcl(battery.Platform!))
                                                  * track.TimeToClosestApproach;
                KsaWorld.DrawSphereEcl(cpa, marker * 0.4f, CpaColour);
            }
        }
    }

    // Marks which tubes still hold a round. Rounds are fired in tube order, so the first TubeCount
    // - Ammo tubes are the spent ones.
    private static void DrawLoadedTubes(DefenceBattery battery, Config config, double3 origin)
    {
        LauncherProfile profile = config.Launcher;
        int spent = profile.TubeCount - battery.Ammo;

        // Prefer the part's own transform so the markers sit on the actual tubes rather than on
        // a correctly-sized ring at an arbitrary rotation.
        //
        // It has to be the *pods* subpart, not the turret: the offsets are measured from the
        // trunnion, and only the pods carry the elevation. Passing the turret here looks right
        // and very nearly is - the markers still follow the traverse, so they track left and
        // right correctly and simply refuse to go up and down.
        Span<double3> muzzles = stackalloc double3[profile.TubeCount];
        bool exact = battery.Platform is { } platform
                     && battery.PodsPart is { } pods
                     && KsaWorld.HasAnchor
                     && LauncherPart.TryGetTubeMuzzlesEgo(platform, pods, profile, KsaWorld.AnchorEgo, muzzles);

        for (int tube = 0; tube < profile.TubeCount; tube++)
        {
            float4 colour = tube < spent ? SpentTubeColour : LoadedTubeColour;

            if (exact)
            {
                KsaWorld.DrawSphereEgo(muzzles[tube], 0.16f, colour);
            }
            else if (KsaWorld.TryEclToEgo(LauncherPart.MuzzleEcl(profile, origin, battery.Boresight, tube), out double3 ego))
            {
                KsaWorld.DrawSphereEgo(ego, 0.16f, colour);
            }
        }
    }

    // A line along where the turret drive *thinks* it is pointing. Worth having beyond the
    // cosmetics: it separates "the slew maths is wrong" from "the engine ignored the transform
    // write". If the line sweeps onto the target and the mesh does not follow it, the maths is fine
    // and Asmb2ParentAsmb is not being honoured.
    private static void DrawTurretFacing(DefenceBattery battery)
    {
        if (battery.Platform is not { } platform) return;

        double bearing = battery.Turret.BearingRad;
        double elevation = battery.Turret.ElevationRad;
        double horizontal = Math.Cos(elevation);
        double3 facingPart = new(Math.Sin(elevation),
                                 horizontal * Math.Cos(bearing),
                                 horizontal * Math.Sin(bearing));

        if (!LauncherPart.TryDirectionFromPartFrame(platform, facingPart, out double3 facingEcl)) return;

        double3 from = battery.MountEcl + battery.Boresight * 3.2;
        KsaWorld.DrawLineEcl(from, from + facingEcl * 45.0, TurretColour);
    }

    private static void DrawRounds(DefenceBattery battery, Config config)
    {
        // A 6 m tracer sphere was the right size when it *was* the round. Now that the rounds
        // are real 3 m bodies, that sphere simply swallows them - so it is only drawn when
        // there is nothing better to show, or when asked for.
        bool haveBodies = battery.RoundBodiesWork && battery.RoundBodyCount > 0;

        foreach (IProjectile round in battery.Rounds)
        {
            // Rounds are stored as platform-relative offsets, so they draw straight off the
            // anchor with no absolute-position arithmetic to go stale.
            double3 roundEgo = KsaWorld.AnchorEgo + round.OffsetFromPlatform;

            // What is actually on screen, measured where it is actually drawn.
            //
            // The same comparison made in Detonate is a frame stale: that runs in the frame
            // hook, while the draw anchor is established here in the GUI hook, so it carried a
            // whole step of ecliptic motion - ~660 m - and reported it as a rendering error.
            // Here the anchor and the target's draw position come from the same pass, so a
            // difference is real. It should be the miss distance, not hundreds of metres.
            if (Log.Threshold <= Log.Level.Debug && ++_drawTrace % 30 == 0
                && round.TargetRef is Vehicle drawn && KsaWorld.IsAlive(drawn)
                && KsaWorld.TryVehicleEgo(drawn, out double3 targetEgo))
            {
                IProjectile r = round;
                double onScreen = Vec.Len(roundEgo - targetEgo);
                Log.Debug(() => $"draw t{r.Tube}: on-screen separation {onScreen:F1} m, " +
                                $"offset |{Vec.Len(r.OffsetFromPlatform):F0}| m, anchored={KsaWorld.HasAnchor}");
            }

            if (!haveBodies || config.DrawRoundMarkers)
            {
                KsaWorld.DrawSphereEgo(roundEgo, haveBodies ? 1.2f : 6f, RoundColour);
            }

            for (int i = 1; i < round.TrailOffsets.Count; i++)
            {
                KsaWorld.DrawLineEgo(
                    KsaWorld.AnchorEgo + round.TrailOffsets[i - 1],
                    KsaWorld.AnchorEgo + round.TrailOffsets[i],
                    TrailColour);
            }

            if (round.TrailOffsets.Count > 0)
                KsaWorld.DrawLineEgo(KsaWorld.AnchorEgo + round.TrailOffsets[^1], roundEgo, TrailColour);

            // Seeker line to whatever it is chasing.
            if (round.TargetRef is KSA.Vehicle target && KsaWorld.TryVehicleEgo(target, out double3 tgtEgo))
                KsaWorld.DrawLineEgo(roundEgo, tgtEgo, LockColour);
        }
    }
}
