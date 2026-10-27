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

    // Where the simulation thinks a shell is, a separate claim from where the tracer is drawn.
    private static readonly float4 TracerColour = new(1.0f, 0.72f, 0.18f, 1.0f);
    private const int TracerSegments = 4;

    private static readonly float4 ShellColour = new(0.95f, 0.78f, 0.45f, 0.75f);

    // Roughly one frame of travel at 1100 m/s, the length a camera's blur would have.
    private const double ShellStreakMetres = 14.0;
    private static readonly float4 LoadedTubeColour = new(0.45f, 1.0f, 0.5f, 0.9f);
    private static readonly float4 SpentTubeColour = new(0.3f, 0.3f, 0.32f, 0.6f);
    private static readonly float4 CpaColour = new(0.6f, 0.4f, 1.0f, 0.7f);
    private static readonly float4 TurretColour = new(0.4f, 1.0f, 0.9f, 0.8f);
    private static readonly float4 NorthColour = new(1.0f, 1.0f, 1.0f, 0.9f);
    private static readonly float4 ArrayColour = new(0.5f, 1.0f, 0.6f, 0.9f);

    private static readonly float4 FlareColour = new(1.0f, 0.95f, 0.8f, 1.0f);
    private static readonly float4 ChaffColour = new(0.7f, 0.75f, 0.85f, 0.8f);

    // The COIL's 1.315 um is invisible; drawn so the operator can see what is being lased.
    private static readonly float4 BeamColour = new(1.0f, 0.35f, 0.15f, 0.9f);
    private static readonly float4 BurnColour = new(1.0f, 0.85f, 0.5f, 1.0f);

    public static void DrawBeam(OpticalHead head)
    {
        if (!head.BeamOn || head.Platform is not { } craft || !KsaWorld.IsAlive(craft)) return;
        if (!KsaWorld.TryVehicleEgo(craft, out double3 craftEgo)) return;

        double3 from = craftEgo + (head.BeamFromEcl - head.PlatformEcl);
        double3 to = craftEgo + (head.BeamToEcl - head.PlatformEcl);
        KsaWorld.DrawLineEgo(from, to, BeamColour);
        KsaWorld.DrawSphereEgo(to, head.Burning is null ? 0.4f : 0.8f, BurnColour);
    }

    /// <summary>
    /// Placed off the craft that threw it: both are end-of-step samples, so their difference
    /// carries none of the frame's motion.
    /// </summary>
    public static void DrawDecoys(IReadOnlyList<Decoy> decoys)
    {
        for (int i = 0; i < decoys.Count; i++)
        {
            Decoy d = decoys[i];
            if (d.DroppedBy is not KSA.Vehicle craft || !KsaWorld.IsAlive(craft)) continue;
            if (!KsaWorld.TryVehicleEgo(craft, out double3 craftEgo)) continue;

            double share = d.Profile.PeakSignature > 0f ? d.Signature / d.Profile.PeakSignature : 0.0;
            float radius = (float)(1.0 + (4.0 * share));

            KsaWorld.DrawSphereEgo(craftEgo + (d.PositionEcl - KsaWorld.PositionEcl(craft)), radius,
                                   d.Profile.Kind == DecoyKind.Flare ? FlareColour : ChaffColour);
        }
    }

    public static void Draw(IWeaponSystemView system, Config config)
    {
        if (system.Platform is null) return;

        // Everything is an offset from the platform's render position: absolute Ecl lands on the
        // analytic orbit position, visibly beside a landed craft.
        if (!KsaWorld.BeginDraw(system.Platform, system.PlatformEcl)) return;

        double3 origin = system.MountEcl;

        // The overlay is clocked to the craft's own forward axis. Left to an arbitrary
        // perpendicular it turns with the planet under a boresight that is local "up".
        double3 clockRef = Vec.Zero;
        if (system.Launcher is { } launcher)
        {
            LauncherPart.TryLauncherDirectionEcl(system.Platform, launcher, new double3(0, 1, 0),
                                                 out clockRef);
        }

        if (config.DrawRadarVolume) DrawSearchVolume(origin, system.Boresight, clockRef, system.Sensor, config);
        if (config.DrawTracks) DrawTracks(system, origin, config);
        if (config.DrawMissiles) DrawRounds(system, config);
        if (system.Launcher is not null && config.DrawTubeMarkers) DrawLoadedTubes(system, config, origin);
        if (config.DrawTurretFacing) DrawTurretFacing(system);
        if (config.DrawBearingReference) DrawBearingReference(system);
    }

    /// <summary>
    /// The shells as lines, when <see cref="ShellTracers"/>' pass is off or would not build.
    /// </summary>
    public static void DrawShellStream(WeaponSystem system)
    {
        if (ShellTracers.Painting) return;
        if (system.Rounds.Count == 0 || system.Platform is not { } platform) return;
        if (!KsaWorld.BeginDraw(platform, system.PlatformEcl)) return;

        foreach (IProjectile round in system.Rounds)
        {
            // A shell drawn as a body needs no streak.
            if (!RoundLabel.IsGunRound(round.Tube) || round.State != RoundState.Flying
                || system.ShellDrawnAsBody(round))
            {
                continue;
            }

            // Never VelocityEcl, which carries 29.8 km/s of ecliptic motion.
            double3 along = Vec.Unit(round.VelocityLocal);
            if (!Vec.IsFinite(along)) continue;

            // Drawn backwards, so never longer than the round has flown, or it starts inside the mount.
            double streak = Math.Min(ShellStreakMetres, Vec.Len(round.TravelSinceLaunch));
            if (streak < 0.1) continue;

            double3 nose = KsaWorld.AnchorEgo + round.OffsetFromPlatform;
            KsaWorld.DrawLineEgo(nose - (along * streak), nose, ShellColour);
        }
    }

    private static void DrawSearchVolume(double3 origin, double3 boresight, double3 clockRef,
                                         SensorProfile sensor, Config config)
    {
        const int ribs = 12;

        // Scaled down near the craft: at true range the cone is lines converging at the horizon.
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

    private static void DrawTracks(IWeaponSystemView system, double3 origin, Config config)
    {
        foreach (Track track in system.Radar.Tracks)
        {
            bool isLock = ReferenceEquals(track, system.Radar.Locked);
            float4 colour = isLock ? LockColour : track.IsThreat ? ThreatColour : TrackColour;

            float marker = (float)Math.Clamp(track.Range * 0.02, 12.0, 220.0);

            // The engine's drawn position, not the analytic one.
            if (config.DrawTrackMarkers && track.Contact.TryDrawEgo(out double3 trackEgo))
                KsaWorld.DrawSphereEgo(trackEgo, marker, colour);

            if (!track.IsThreat && !isLock) continue;

            if (KsaWorld.TryAnchoredEgo(origin, out double3 originEgo) && track.Contact.TryDrawEgo(out double3 endEgo))
            {
                KsaWorld.DrawLineEgo(originEgo, endEgo, colour);
            }

            if (config.DrawClosestApproach)
            {
                double3 cpa = track.PositionEcl + (track.VelocityEcl - KsaWorld.VelocityEcl(system.Platform!))
                                                  * track.TimeToClosestApproach;
                KsaWorld.DrawSphereEcl(cpa, marker * 0.4f, CpaColour);
            }
        }
    }

    private static void DrawLoadedTubes(IWeaponSystemView system, Config config, double3 origin)
    {
        LauncherProfile profile = system.Profile;
        int spent = profile.TubeCount - system.Ammo;

        // Through the pods subpart, not the turret: the offsets are from the trunnion, and only the
        // pods elevate. Through the turret the markers follow the traverse and not the elevation.
        Span<double3> muzzles = stackalloc double3[profile.TubeCount];
        bool exact = system.Platform is { } platform
                     && system.TubesResolved
                     && KsaWorld.HasAnchor
                     && LauncherPart.TryGetTubeMuzzlesEgo(platform, system.PodsPart ?? system.Launcher, profile,
                                                       KsaWorld.AnchorEgo, muzzles);

        for (int tube = 0; tube < profile.TubeCount; tube++)
        {
            float4 colour = tube < spent ? SpentTubeColour : LoadedTubeColour;

            if (exact)
            {
                KsaWorld.DrawSphereEgo(muzzles[tube], 0.16f, colour);
            }
            else if (KsaWorld.TryAnchoredEgo(LauncherPart.MuzzleEcl(profile, origin, system.Boresight, tube), out double3 ego))
            {
                KsaWorld.DrawSphereEgo(ego, 0.16f, colour);
            }
        }
    }

    // Where the drive thinks it points: a line on target with the mesh elsewhere means the engine
    // ignored the transform write, not that the maths is wrong.
    private static void DrawTurretFacing(IWeaponSystemView system)
    {
        if (system.Platform is not { } platform) return;

        double bearing = system.Turret.BearingRad;
        double elevation = system.Turret.ElevationRad;
        double horizontal = Math.Cos(elevation);
        double3 facingPart = new(Math.Sin(elevation),
                                 horizontal * Math.Cos(bearing),
                                 horizontal * Math.Sin(bearing));

        // Through the launcher's mounting: from the vehicle frame alone it is out by the part's
        // rotation, a half turn on a stack mount.
        if (system.Launcher is not { } launcher) return;
        if (!LauncherPart.TryLauncherDirectionEcl(platform, launcher, facingPart, out double3 facingEcl)) return;

        double3 from = system.MountEcl + system.Boresight * 3.2;
        KsaWorld.DrawLineEcl(from, from + facingEcl * 45.0, TurretColour);
    }

    // Where the scope believes each array face looks, beside the dish: an inverted sign agrees
    // with it twice a revolution, which reads as an offset rather than a reversal.
    private static void DrawBearingReference(IWeaponSystemView system)
    {
        if (system.Platform is not { } platform) return;
        if (KsaWorld.ParentBody(platform) is not { } body) return;

        MapFrame? built;
        try
        {
            built = MapFrame.TryAt(body.GetPositionEcl(), system.MountEcl, body.GetRotationAxisCce());
        }
        catch
        {
            return;      // no frame here, so no bearing to draw one against
        }

        if (built is not { } frame) return;

        double3 from = system.MountEcl + system.Boresight * 3.2;

        KsaWorld.DrawLineEcl(from, from + (frame.North * 60.0), NorthColour);

        int faces = system.Profile.SearchRadarFaces;
        if (faces <= 0) return;

        double3 forward = frame.ToLocalDirection(platform.Asmb2Ego * new double3(0, 1, 0));
        if (!Vec.IsFinite(forward)) return;

        double heading = ScopeGeometry.BearingRad(forward.X, forward.Y);
        double array = system.Turret.BearingRad + system.RadarSpinRad;

        Span<double> bearings = stackalloc double[ScopeGeometry.MaxSweepFaces];
        int count = ScopeGeometry.SweepBearings(heading, array, faces, bearings);

        // The scope's own conversion, so a line that disagrees with the dish is the scope disagreeing.
        for (int i = 0; i < count; i++)
        {
            double3 along = (frame.North * Math.Cos(bearings[i])) + (frame.East * Math.Sin(bearings[i]));
            KsaWorld.DrawLineEcl(from, from + (along * 48.0), ArrayColour);
        }
    }

    private static void DrawRounds(IWeaponSystemView system, Config config)
    {
        // The marker sphere swallows the real round bodies, so it is drawn only without them or when asked.
        bool haveBodies = system.RoundBodiesWork && system.RoundBodyCount > 0;

        foreach (IProjectile round in system.Rounds)
        {
            double3 roundEgo = KsaWorld.AnchorEgo + round.OffsetFromPlatform;

            // Here the anchor and the target's draw position share a pass, so the separation is real;
            // made in the frame hook it carries a step of ecliptic motion.
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

            // A shell gets the last few segments: 32 points 0.05 s apart is ~1.8 km at 1100 m/s.
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

            if (round.TargetRef is KSA.Vehicle target && KsaWorld.TryVehicleEgo(target, out double3 tgtEgo))
                KsaWorld.DrawLineEgo(roundEgo, tgtEgo, LockColour);
        }
    }
}
