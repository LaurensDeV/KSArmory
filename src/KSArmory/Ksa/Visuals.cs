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

    // Shells in the diagnostic overlay. What a player sees is the particle tracer; this is the
    // line that says where the simulation thinks the round actually is, which is not the same
    // claim and is worth being able to check separately.
    private static readonly float4 TracerColour = new(1.0f, 0.72f, 0.18f, 1.0f);
    private const int TracerSegments = 4;

    // Every shell in the air, not just the traced ones. Warm and dim: it must read as a stream of
    // rounds without competing with the tracers running through it.
    private static readonly float4 ShellColour = new(0.95f, 0.78f, 0.45f, 0.75f);

    // How long a shell is drawn, along its own flight.
    //
    // A round is not a point and drawing it as one is what makes it a ball: at 1100 m/s it crosses
    // 18 m in a single frame, so what a camera records is a streak and what a sphere shows is a
    // marble hanging in the air. Roughly one frame of travel, which is the length the blur would
    // actually have.
    private const double ShellStreakMetres = 14.0;
    private static readonly float4 LoadedTubeColour = new(0.45f, 1.0f, 0.5f, 0.9f);
    private static readonly float4 SpentTubeColour = new(0.3f, 0.3f, 0.32f, 0.6f);
    private static readonly float4 CpaColour = new(0.6f, 0.4f, 1.0f, 0.7f);
    private static readonly float4 TurretColour = new(0.4f, 1.0f, 0.9f, 0.8f);
    private static readonly float4 NorthColour = new(1.0f, 1.0f, 1.0f, 0.9f);
    private static readonly float4 ArrayColour = new(0.5f, 1.0f, 0.6f, 0.9f);

    public static void Draw(IWeaponSystemView battery, Config config)
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

        if (config.DrawRadarVolume) DrawSearchVolume(origin, battery.Boresight, clockRef, battery.Sensor, config);
        if (config.DrawTracks) DrawTracks(battery, origin, config);
        if (config.DrawMissiles) DrawRounds(battery, config);
        if (battery.Launcher is not null && config.DrawTubeMarkers) DrawLoadedTubes(battery, config, origin);
        if (config.DrawTurretFacing) DrawTurretFacing(battery);
        if (config.DrawBearingReference) DrawBearingReference(battery);
    }

    /// <summary>
    /// The shells themselves, drawn whether or not the diagnostic overlay is on.
    ///
    /// <para>A tracer is one round in nineteen, which is how a belt is loaded and what
    /// <see cref="TracerTrail"/> can afford: an emitter is held for its shell's whole flight and
    /// there are eight of them against a hundred and fifty rounds in the air. Without this the
    /// other eighteen are drawn as nothing, and a firing CIWS reads as a handful of bright streaks
    /// through empty sky rather than as a stream of fire.</para>
    ///
    /// <para>A short segment along each shell's own flight, not a point and not a trail. A point
    /// reads as a ball because a round moves further between frames than any believable radius; a
    /// trail back to the muzzle reads as a laser. One frame of travel is what a camera would have
    /// caught.</para>
    /// </summary>
    public static void DrawShellStream(WeaponSystem system)
    {
        if (system.Rounds.Count == 0 || system.Platform is not { } platform) return;
        if (!KsaWorld.BeginDraw(platform, system.PlatformEcl)) return;

        foreach (IProjectile round in system.Rounds)
        {
            // Negative tube numbers mark the cannon; the magazine owns zero and up, and a missile
            // has a real subpart body of its own.
            if (!RoundLabel.IsGunRound(round.Tube) || round.State != RoundState.Flying) continue;

            // Local, never VelocityEcl: the latter carries 29.8 km/s of ecliptic motion and would
            // lay every streak along the same direction whatever the gun did.
            double3 along = Vec.Unit(round.VelocityLocal);
            if (!Vec.IsFinite(along)) continue;

            // Never longer than the round has actually flown. The streak is drawn backwards from
            // the shell, so a full-length one on a round that has just left the barrel starts 14 m
            // behind the muzzle, which is inside the mount and out the other side of it.
            double streak = Math.Min(ShellStreakMetres, Vec.Len(round.TravelSinceLaunch));
            if (streak < 0.1) continue;

            double3 nose = KsaWorld.AnchorEgo + round.OffsetFromPlatform;
            KsaWorld.DrawLineEgo(nose - (along * streak), nose, ShellColour);
        }
    }

    // Wireframe cone: boresight, a rim, and ribs out to the rim.
    private static void DrawSearchVolume(double3 origin, double3 boresight, double3 clockRef,
                                         SensorProfile sensor, Config config)
    {
        const int ribs = 12;

        // Drawing the cone at its true range makes it useless to look at: 20 km of thin lines
        // all radiating from one point, converging at the horizon. Draw a scaled-down shape
        // near the craft instead - it shows the *direction and angle* the radar covers, which
        // is what the overlay is for. The range readout lives in the panel.
        double range = Math.Min(sensor.Range, config.ConeDisplayMetres);
        double half = sensor.ConeHalfAngleRad;

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

    private static void DrawTracks(IWeaponSystemView battery, double3 origin, Config config)
    {
        foreach (Track track in battery.Radar.Tracks)
        {
            bool isLock = ReferenceEquals(track, battery.Radar.Locked);
            float4 colour = isLock ? LockColour : track.IsThreat ? ThreatColour : TrackColour;

            float marker = (float)Math.Clamp(track.Range * 0.02, 12.0, 220.0);

            // Ask the engine where this vehicle is being drawn rather than deriving it, so the
            // marker sits on the craft and not on its analytic position.
            if (config.DrawTrackMarkers && track.Contact.TryDrawEgo(out double3 trackEgo))
                KsaWorld.DrawSphereEgo(trackEgo, marker, colour);

            if (!track.IsThreat && !isLock) continue;

            // End the line on the engine's position for the contact, not its analytic one, so
            // it touches the craft rather than pointing near it.
            if (KsaWorld.TryEclToEgo(origin, out double3 originEgo) && track.Contact.TryDrawEgo(out double3 endEgo))
            {
                KsaWorld.DrawLineEgo(originEgo, endEgo, colour);
            }

            // Where this contact passes the launcher, if it holds course. Opt-in: the prediction
            // can land kilometres from anything on screen, where it just looks like a stray dot.
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
    private static void DrawLoadedTubes(IWeaponSystemView battery, Config config, double3 origin)
    {
        LauncherProfile profile = battery.Profile;
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
                     && battery.TubesResolved
                     && KsaWorld.HasAnchor
                     && LauncherPart.TryGetTubeMuzzlesEgo(platform, battery.PodsPart ?? battery.Launcher, profile,
                                                       KsaWorld.AnchorEgo, muzzles);

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
    private static void DrawTurretFacing(IWeaponSystemView battery)
    {
        if (battery.Platform is not { } platform) return;

        double bearing = battery.Turret.BearingRad;
        double elevation = battery.Turret.ElevationRad;
        double horizontal = Math.Cos(elevation);
        double3 facingPart = new(Math.Sin(elevation),
                                 horizontal * Math.Cos(bearing),
                                 horizontal * Math.Sin(bearing));

        // Through the launcher's own mounting, not the vehicle's alone. The barrels are drawn in
        // the part frame; a line converted from the vehicle frame is out by the part's rotation,
        // which on a stack mount is a half turn, pointing the line the opposite way to the gun it
        // reports.
        if (battery.Launcher is not { } launcher) return;
        if (!LauncherPart.TryLauncherDirectionEcl(platform, launcher, facingPart, out double3 facingEcl)) return;

        double3 from = battery.MountEcl + battery.Boresight * 3.2;
        KsaWorld.DrawLineEcl(from, from + facingEcl * 45.0, TurretColour);
    }

    // North, and where the scope believes each face of the array is looking.
    //
    // The one thing that settles whether the scope agrees with the vehicle. Every step from the
    // array's angle to a compass bearing is somewhere a sign or a handedness can invert, and the
    // wrong one draws a sweep that turns the opposite way to the dish while agreeing with it twice
    // a revolution -- which reads as an offset rather than a reversal. Drawn beside the dish, that
    // stops being a matter of watching carefully.
    private static void DrawBearingReference(IWeaponSystemView battery)
    {
        if (battery.Platform is not { } platform) return;
        if (KsaWorld.ParentBody(platform) is not { } body) return;

        MapFrame? built;
        try
        {
            built = MapFrame.TryAt(body.GetPositionEcl(), battery.MountEcl, body.GetRotationAxisCce());
        }
        catch
        {
            return;      // no frame here, so no bearing to draw one against
        }

        if (built is not { } frame) return;

        double3 from = battery.MountEcl + battery.Boresight * 3.2;

        // North itself, long and white, so everything else is read against it.
        KsaWorld.DrawLineEcl(from, from + (frame.North * 60.0), NorthColour);

        int faces = battery.Profile.SearchRadarFaces;
        if (faces <= 0) return;

        double3 forward = frame.ToLocalDirection(platform.Asmb2Ego * new double3(0, 1, 0));
        if (!Vec.IsFinite(forward)) return;

        double heading = ScopeGeometry.BearingRad(forward.X, forward.Y);
        double array = battery.Turret.BearingRad + battery.RadarSpinRad;

        Span<double> bearings = stackalloc double[ScopeGeometry.MaxSweepFaces];
        int count = ScopeGeometry.SweepBearings(heading, array, faces, bearings);

        // Back out of a compass bearing into the world, which is the same conversion the scope
        // makes on its face -- so a line that disagrees with the dish is the scope disagreeing.
        for (int i = 0; i < count; i++)
        {
            double3 along = (frame.North * Math.Cos(bearings[i])) + (frame.East * Math.Sin(bearings[i]));
            KsaWorld.DrawLineEcl(from, from + (along * 48.0), ArrayColour);
        }
    }

    private static void DrawRounds(IWeaponSystemView battery, Config config)
    {
        // A 6 m tracer sphere swallows the real 3 m round bodies, so it is only drawn when there
        // is nothing better to show, or when asked for.
        bool haveBodies = battery.RoundBodiesWork && battery.RoundBodyCount > 0;

        foreach (IProjectile round in battery.Rounds)
        {
            // Rounds are stored as platform-relative offsets, so they draw straight off the
            // anchor with no absolute-position arithmetic to go stale.
            double3 roundEgo = KsaWorld.AnchorEgo + round.OffsetFromPlatform;

            // What is actually on screen, measured where it is actually drawn.
            //
            // The same comparison made in Detonate is a frame stale: that runs in the frame
            // hook, while the draw anchor is established here in the GUI hook, so it carries a
            // whole step of ecliptic motion, ~660 m, and reads as a rendering error. Here the
            // anchor and the target's draw position come from the same pass, so a difference is
            // real. It should be the miss distance, not hundreds of metres.
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

            // A shell gets only the last few segments. Its trail holds 32 points, which at
            // 1100 m/s is close to 600 m of line, and drawn back to the muzzle it reads as a beam
            // rather than as a round.
            int from = RoundLabel.IsGunRound(round.Tube)
                           ? Math.Max(1, round.TrailOffsets.Count - TracerSegments)
                           : 1;

            for (int i = from; i < round.TrailOffsets.Count; i++)
            {
                KsaWorld.DrawLineEgo(
                    KsaWorld.AnchorEgo + round.TrailOffsets[i - 1],
                    KsaWorld.AnchorEgo + round.TrailOffsets[i],
                    RoundLabel.IsGunRound(round.Tube) ? TracerColour : TrailColour);
            }

            if (round.TrailOffsets.Count > 0)
                KsaWorld.DrawLineEgo(KsaWorld.AnchorEgo + round.TrailOffsets[^1], roundEgo, TrailColour);

            // Seeker line to whatever it is chasing.
            if (round.TargetRef is KSA.Vehicle target && KsaWorld.TryVehicleEgo(target, out double3 tgtEgo))
                KsaWorld.DrawLineEgo(roundEgo, tgtEgo, LockColour);
        }
    }
}
