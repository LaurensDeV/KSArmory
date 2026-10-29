namespace KSArmory;

/// <summary>
/// Settings that belong to the session rather than to any one system: the roster of team names,
/// what gets drawn, how much is logged.
///
/// <para>Whatever two launchers in one world could disagree about is <see cref="SystemConfig"/>'s; how a
/// weapon performs is its profiles'.</para>
/// </summary>
public sealed class Config
{
    // No launcher, round or sensor here: a session-wide selection gives every reader whichever
    // installation updated last. Catalogue.LoadoutFor pairs them per system.

    /// <summary>Play a rocket motor while a round is boosting.</summary>
    public bool MotorSound = true;

    /// <summary>Volume of that motor, before the engine's own distance and pressure falloff.</summary>
    public float MotorVolume = 0.7f;

    /// <summary>The cannon you can hear while they are firing.</summary>
    public bool CannonSound = true;

    /// <summary>Volume of that gun, before the engine's own distance and pressure falloff.</summary>
    public float CannonVolume = 0.8f;

    /// <summary>
    /// The rate of the gun the shared cannon loop was recorded from, in rounds per minute.
    ///
    /// <para>Playback pitch is the gun's own rate over this, clamped; the CIWS plays it untouched.</para>
    /// </summary>
    public float CannonReferenceRpm = 4500f;

    /// <summary>
    /// Draw a plume at the nozzle while a round burns.
    ///
    /// <para>Held-open emitters come from a shared pool, so a large salvo can starve the game's other
    /// particles.</para>
    /// </summary>
    public bool MotorPlume = true;

    /// <summary>
    /// Lay a smoke trail behind a round while its motor burns.
    ///
    /// <para>A segment lives <b>1200 seconds</b>, a global engine setting a mod cannot shorten, and
    /// segments are capped at 16,384 per body, evicted oldest-first, from the same budget as a mushroom
    /// cloud's.</para>
    /// </summary>
    public bool MotorSmoke = true;

    /// <summary>
    /// Multiplier on how wide that trail is drawn, against the round's own size.
    ///
    /// <para>A look: the engine reaches the expanded radius within five seconds, so this is the trail's
    /// width for almost all of its life.</para>
    /// </summary>
    public float MotorSmokeWidth = 1f;

    /// <summary>
    /// Show the little floating button that reopens the panel.
    ///
    /// <para>The menu-bar entry appends to KSA's own bar, which is ImGui behaviour rather than a hook, so
    /// this stays on by default, with <b>ModMenu</b> too: the control to restore it is inside the shut
    /// panel.</para>
    /// </summary>
    public bool FloatingPanelButton = true;

    /// <summary>
    /// Draw the mushroom clouds a nuclear burst leaves standing.
    ///
    /// <para>Costs about <b>6-8 ms a frame</b> while they stand (fourteen shots, 2026-09-09-walk2), so a
    /// ballistic scenario turns it off (<c>Ksa/ScenarioRunner.cs</c>). It changes nothing a burst does.</para>
    /// </summary>
    public bool NuclearClouds = true;


    /// <summary>
    /// Which sound to use, by <c>ModLibrary</c> Id. Null takes Core's engine loop, which resolves
    /// on every install; a mod-supplied Id only resolves once that asset actually ships.
    /// </summary>
    public string? MotorSoundId;

    /// <summary>
    /// Hold the world's timewarp down while rounds are in the air, and give it back when they land
    /// (<see cref="WarpPolicy"/>). Off, rounds under heavy warp lag the world and miss.
    /// </summary>
    public bool LimitWarpInFlight = true;

    /// <summary>
    /// Take a rocket's spent ascent stages out of the world once they are clear of it, rather than
    /// leaving them to fall.
    ///
    /// <para><b>It buys frame time, which is the only thing that buys simulation rate</b>: about 2.0 ms a
    /// vehicle (<c>docs/METRE-LEVEL.md</c> §5b). <b>Off by default, because it destroys things in the
    /// player's world</b>; on for a scripted batch. The half a MIRV bus dropped is never taken
    /// (<see cref="StageDisposal"/>).</para>
    /// </summary>
    public bool DisposeSpentStages;

    /// <summary>
    /// Break individual parts off a craft a warhead went off near, rather than destroying the
    /// whole craft or nothing at all.
    ///
    /// <para>Each part is judged on its own distance and strength (<see cref="BlastDamage"/>), and the
    /// engine's fragment guard decides when that destroys the craft. Off is binary kills at the lethal
    /// radius, the way back if fragments cost too much frame time.</para>
    /// </summary>
    public bool DamageIndividualParts = true;

    /// <summary>
    /// How much of a part's health a burst takes, as a multiple of its <see cref="BlastDamage.Share"/>
    /// (<see cref="PartHealth"/>): at one a fresh part breaks where the sweep breaks it, at a tenth the
    /// burst has to land ten times. No one hit costs more than a whole share.
    /// </summary>
    public float DamageScale = 1f;

    /// <summary>
    /// Whether a shell that strikes a hull leaves a hole there, soot and all — see
    /// <see cref="HoleLook"/>. Painted by the cloud pass, so nothing without it.
    /// </summary>
    public bool BulletHoles = true;

    /// <summary>
    /// Whether a holed tank leaks — see <see cref="TankLeak"/>: the liquid settles against the
    /// acceleration the craft feels, so a craft on its side drains from its lower holes and a hole above
    /// the liquid leaks nothing; coasting, the liquid floats and any hole leaks as often as it is under it.
    /// </summary>
    public bool TankLeaks = true;

    /// <summary>
    /// Paintball: a hit marks what it struck with the shooter's paint and harms nothing -- no part damage,
    /// no dents or push, no fire or leak, nothing destroyed, and no round in the air shot down.
    /// </summary>
    public bool Paintball;

    /// <summary>A health bar over every part of every craft near the camera, and over any damaged part further out.</summary>
    public bool DrawPartHealth;

    /// <summary>
    /// Where the warheads are going, and how far the bus can still move them, drawn on KSA's own
    /// ground-track map.
    /// </summary>
    public bool DrawGroundTrackMarks;

    /// <summary>
    /// Nine crosses at round coordinates on that map, for checking the projection against the
    /// ground they name. Not against KSA's grid lines, which are spread across what is visible
    /// rather than drawn at round degrees.
    /// </summary>
    public bool DrawGroundTrackCalibration;

    /// <summary>
    /// Whether a nuclear fireball blacks out the radar beams that cross it, for as long as the air
    /// it ionised stays hot — see <see cref="FireballBlackout"/>. Only sets that transmit are
    /// blinded. Off, a burst is something radar looks straight through.
    /// </summary>
    public bool NuclearBlackout = true;

    /// <summary>
    /// Whether a burst high in the air sends out its red wave (<see cref="RedWave"/>) -- a faint red
    /// sphere through the thermosphere for minutes. One full-screen dispatch while it lasts.
    /// </summary>
    public bool RedWave = true;

    /// <summary>
    /// Count rounds per <em>craft</em> rather than per weapon when deciding whether a target has
    /// had enough.
    ///
    /// <para>Per weapon, two rails each fire a full salvo at one target (<see cref="TargetAllocation"/>).
    /// Off is a real choice: two launchers fitted to put four rounds on a target.</para>
    /// </summary>
    public bool ShareTargetsAcrossWeapons = true;

    /// <summary>
    /// The teams declared this session, in the order a switcher row's flag steps through them.
    ///
    /// <para>Only the names: which team a craft is on is <see cref="TeamRoster"/>'s, set by the flag.</para>
    /// </summary>
    public readonly List<string> TeamNames = [];

    /// <summary>
    /// Each team's rules of engagement, by name; see <see cref="KSArmory.TeamRules"/>. Not saved on
    /// their own: every system carries its team's copy, so a team takes them back from the first of
    /// its systems a save restores (<see cref="AdoptRules"/>).
    /// </summary>
    public readonly Dictionary<string, TeamRules> TeamRules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The rules for a craft on no team.</summary>
    public readonly TeamRules NoTeamRules = new();

    /// <summary>A team's rules, made on first asking; <see cref="NoTeamRules"/> for no team.</summary>
    public TeamRules RulesFor(string? team)
    {
        if (string.IsNullOrWhiteSpace(team)) return NoTeamRules;
        if (TeamRules.TryGetValue(team, out TeamRules? rules)) return rules;

        rules = new TeamRules();
        TeamRules[team] = rules;
        return rules;
    }

    /// <summary>A restored system's settings become its team's, if the team has none yet this session.</summary>
    public void AdoptRules(IffPolicy restored)
    {
        if (string.IsNullOrWhiteSpace(restored.OwnTeam) || TeamRules.ContainsKey(restored.OwnTeam)) return;

        RulesFor(restored.OwnTeam).TakeFrom(restored);
    }

    /// <summary>Takes a team out of every team's rules, and drops its own.</summary>
    public void ForgetRules(string team)
    {
        TeamRules.Remove(team);
        NoTeamRules.Forget(team);
        foreach (TeamRules rules in TeamRules.Values) rules.Forget(team);
    }

    /// <summary>Click the world to set off a warhead there. Off by default: on, a click is an explosion.</summary>
    public bool BurstTool;

    /// <summary>
    /// Draw the ring showing where the next burst would go and how far it would kill.
    ///
    /// <para>At the <em>lethal</em> radius; hidden by itself for the flash after each burst, so this switch
    /// is for turning it off for good.</para>
    /// </summary>
    public bool BurstMarker = true;

    /// <summary>
    /// How strongly the nuclear cloud draws, as a fraction of full.
    ///
    /// <para>Zero skips the cloud and the burst's sky, and still paints the rings, the holes and the
    /// tracers, which have no other way onto the screen.</para>
    /// </summary>
    public float ShaderPass = 1f;

    /// <summary>
    /// How bright a gun round with no tracer is drawn in daylight, against a tracer's 24: a faint grey
    /// streak so the stream between tracers reads. It fades with the sun (<see cref="TracerLook.Daylight"/>).
    /// Zero draws only the tracers.
    /// </summary>
    public float BallRoundBrightness = 1.5f;

    /// <summary>
    /// Explosive charge for a hand-fired burst (kg), the same figure a round carries.
    /// </summary>
    public float BurstChargeKg = 20f;

    /// <summary>
    /// Make that burst a nuclear one, dialled in kilotons rather than kilograms.
    ///
    /// <para>Only the dial's unit changes: the cloud grows itself over <see cref="MushroomCloud.ThresholdKg"/>.</para>
    /// </summary>
    public bool BurstNuclear;

    /// <summary>
    /// Yield for that (kt), from the B61's lowest setting to Tsar Bomba's 50 Mt.
    ///
    /// </summary>
    public float BurstYieldKt = 0.3f;

    /// <summary>
    /// How far above the clicked ground that burst goes off (m). Zero is a surface burst; Tsar
    /// Bomba went off about 4,000 m up.
    /// </summary>
    public float BurstHeightMetres;

    /// <summary>Pick a craft up with one click and set it down with the next.</summary>
    public bool MoveCraftWithMouse;


    /// <summary>
    /// Periodically dump the system's world view to the log: every loaded vehicle with the numbers the
    /// radar filters on, plus the render-frame state.
    /// </summary>
    public bool DiagnosticDump;

    /// <summary>Seconds between diagnostic dumps.</summary>
    public float DiagnosticIntervalSeconds = 3f;

    /// <summary>
    /// Log developer detail, which a release build leaves out because it runs to hundreds of lines per
    /// engagement; this turns it on without a different build.
    /// </summary>
    public bool VerboseLog;

    /// <summary>
    /// Follow one released warhead all the way down, beside <see cref="ImpactPredictor"/> re-flown
    /// from wherever it has got to. See <c>Ksa/WarheadTrace.cs</c>.
    ///
    /// <para>A measurement: whether the warhead leaves its release probe smoothly or in a step. Needs
    /// <see cref="VerboseLog"/> for the per-frame half.</para>
    /// </summary>
    public bool TraceWarhead;

    /// <summary>
    /// Sweep a seated round's fins continuously, as a guided store does on power-up. Drawn only.
    /// </summary>
    public bool FinTestSweep;

    /// <summary>
    /// Draw the world overlay at all — search volume, tracks, round tracers, drive facing.
    ///
    /// <para>Diagnostic drawing, of what the mod thinks; rounds keep their bodies with it off.</para>
    /// </summary>
    public bool DrawOverlays;

    /// <summary>
    /// Draw the overlay only for the system the panel is showing, rather than for every one.
    ///
    /// <para>Off to compare two sites at once.</para>
    /// </summary>
    public bool DrawOverlayForFocusedOnly = true;

    public bool DrawRadarVolume = true;
    public bool DrawTracks = true;
    public bool DrawMissiles = true;

    /// <summary>
    /// Draw a line along where the drives think they are pointing.
    ///
    /// <para>Its own switch: it separates "the maths is wrong" from "the engine ignored the write".</para>
    /// </summary>
    public bool DrawTurretFacing = true;

    /// <summary>
    /// Diagnostic: a line to the north, and one along each face of the search array as the scope
    /// believes it is pointing.
    ///
    /// <para>Settles whether the scope agrees with the vehicle: a sign slip in carrying the array's angle
    /// draws a plausible sweep turning the wrong way.</para>
    /// </summary>
    public bool DrawBearingReference;

    /// <summary>
    /// Diagnostic: hold the chase camera still through its transition instead of flying it onto
    /// the round. It still takes the view and keeps the round in the middle of it, it simply does
    /// not travel.
    ///
    /// <para>A discriminator: separates the camera's travel from the scene juddering against it.</para>
    /// </summary>
    public bool FreezeChaseTransition;

    /// <summary>
    /// Bracket every weapons system on screen, with an arrow at the edge for one out of view.
    ///
    /// </summary>
    public bool DrawSystemMarkers = true;

    /// <summary>
    /// Bracket what the selected weapon is engaging, closing the brackets as the lock matures.
    ///
    /// <para>Not part of <see cref="DrawOverlays"/>: a lock is playing, not debugging.</para>
    /// </summary>
    public bool DrawLockCue = true;

    /// <summary>
    /// Set off KSA's own explosion where a warhead goes off, its flash and sound included.
    ///
    /// <para>Not part of <see cref="DrawOverlays"/>: this is the engagement itself.</para>
    /// </summary>
    public bool DrawExplosions = true;

    /// <summary>
    /// Draw a sphere on each contact, scaled with range, so up close it is a large ball over the craft.
    /// </summary>
    public bool DrawTrackMarkers;

    /// <summary>
    /// Draw a tracer sphere on each round in flight, on top of the round's own body.
    ///
    /// The sphere is bigger than the missile; it is for a round a few pixels across, or for where the
    /// simulation thinks a round is when the body looks wrong.
    /// </summary>
    public bool DrawRoundMarkers;

    /// <summary>
    /// Draw a marker on each tube, green for loaded and grey for spent.
    /// </summary>
    public bool DrawTubeMarkers;

    /// <summary>
    /// Place a real subpart body on each round in flight.
    ///
    /// <para>Off falls back to tracer spheres, a separate path (gizmo anchor in Ecl), which isolates a
    /// fault to the subpart transform.</para>
    /// </summary>
    public bool UseRoundBodies = true;

    /// <summary>
    /// Draw a marker at each threat's predicted closest point of approach — where it will pass
    /// the system if it holds course; with a 40 s horizon it can sit kilometres from anything visible.
    /// </summary>
    public bool DrawClosestApproach;

    /// <summary>
    /// How long the drawn search cone is (m). Cosmetic: the detection range is <see cref="SensorProfile.Range"/>.
    /// </summary>
    public float ConeDisplayMetres = 2500f;

    /// <summary>A copy of every setting as it stands now, to be put back with <see cref="Restore"/>.</summary>
    public Config Snapshot() => (Config)MemberwiseClone();

    /// <summary>
    /// Puts back every setting <paramref name="snapshot"/> held. Readonly fields are left alone: a
    /// snapshot shares them by reference, so there is nothing to put back.
    /// </summary>
    public void Restore(Config snapshot)
    {
        foreach (System.Reflection.FieldInfo field in typeof(Config).GetFields(
                     System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.NonPublic))
        {
            if (!field.IsInitOnly) field.SetValue(this, field.GetValue(snapshot));
        }
    }
}
