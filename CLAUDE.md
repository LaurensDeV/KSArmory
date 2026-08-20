# CLAUDE.md

A point-defence mod for **Kitten Space Agency** (KSA, RocketWerkz). A **Pantsir-S1** — search
radar, proportional-navigation interceptors, a proximity-fused warhead, twelve rounds in two pods
of six on an 8×8 chassis — and a **LAU-7 rail** carrying one AIM-9J, which surface-attaches to
anything and is the shipped example of a launcher with nothing that moves, a **LAU-128 rail**
carrying one AIM-120C, which is that same launcher with ten times the reach and the first whose
art was authored rather than generated, a **LAU-118 rail** carrying one AGM-88 HARM, which is the
one that cannot engage an aircraft at all and whose target has a say in whether it is one, a
**Mk 15 Phalanx CIWS** that stacks on a 3 m node and is the one with no missiles at all, and a
**B61 rack**, which is the one that neither aims nor fires: it lets a bomb go and the ground does
the rest. Two sights come with them, and they are the
same instrument on different mechanisms: an **EO director** on a mast, and a **Rafael LITENING
pod**, whose whole nose rolls about the pod's centreline while the sight nods within it.

## Read this first

**`docs/FRAMES-AND-EPOCHS.md` is the one to read before touching rounds, drawing or timing.**
A frame or epoch mismatch is multiplied by 29.8 km/s of ecliptic motion, and that file has the
engine's actual contract, the rules that follow from it, and how to tell the four failure shapes
apart.

**`docs/KSA-MODDING-NOTES.md` is the distilled result of reverse-engineering the game.** It has
the runtime, the loader contract, the type signatures, the reference frames and the gotchas.
Read it before touching anything KSA-facing — it will save you an hour of decompiling.

KSA has **no official code-modding API**. Everything is community tooling against a pre-release
game, so the API moves between builds.

## Comments and documentation

**Docs are part of the change, not a follow-up.** If a change makes a line in `CLAUDE.md`, a
`docs/` file, `README.md` or a comment untrue, fix it in the same commit. A stale line is worse
than a missing one: it is trusted, and nothing in a build fails when it goes wrong.

**Comment why, never what.** The code says what it does. A comment earns its place only when the
reason is not recoverable from reading it — an engine contract, a measured number, a constraint
imposed from somewhere else in the frame, an ordering that looks arbitrary and is not. Everything
else is noise that goes stale.

```csharp
// Anchor to the tube, not the orbit position: those differ by metres on a landed craft.   ok
double3 travelPart = asmb2Part * (ecl2Asmb * travelEcl);

// Convert the travel into the part frame.                                                 delete
double3 travelPart = asmb2Part * (ecl2Asmb * travelEcl);
```

**Keep them short.** A sentence or two. If a comment needs paragraphs, the explanation belongs in
`docs/` with a one-line pointer to it — that is what `docs/FRAMES-AND-EPOCHS.md` and
`docs/MODULARITY.md` are for.

**State the fact, not the history.** A comment says what is true now. It does not narrate what the
code used to do, what broke, when it was reported, or which commit fixed it — that belongs in git,
and the reasoning belongs in `docs/`. History in a comment ages badly, buries the invariant in
storytelling, and is unreadable to anyone who was not there.

```csharp
// elapsed is incremented after the step so the round's position and the back-dated
// target share an instant. Splitting them costs ~142 m at 29.8 km/s.                      ok

// elapsed used to be incremented first, which paired the round with the END of the
// sub-step. Reported from play as rounds appearing sideways; caught by
// ProjectileContractTests on its first run. See commit 6351118.                           delete
```

Both say why. Only the first will still be true and useful in a year.

**When in doubt, delete.** An unnecessary comment is not neutral: it is another thing that can
drift out of step with the code and mislead the next reader. Deleting one is a real improvement,
not a loss.

What stays is the *invariant* and its consequence — the ordering that looks arbitrary, the
measured number, the engine contract. What goes is how anyone came to know it.

## Committing

**Every commit message must be a [Conventional Commit](https://www.conventionalcommits.org/).**
This is not a style preference — semantic-release parses these to decide the next version, so a
message that does not parse silently produces no release and never appears in the changelog.

```
feat(turret): elevate the pods on their trunnions
fix(rounds): anchor bodies to the tube they left, not the orbit position
docs: write an install guide
refactor(sim): split launch geometry out of LauncherPart
```

| Type | Version effect |
| --- | --- |
| `feat`, `fix`, `perf`, `build`, `revert` | **patch** |
| `!` after the type, or a `BREAKING CHANGE:` footer | **major** — see the 1.0.0 note below before using this |
| `docs`, `refactor`, `test`, `chore`, `ci`, `style` | no release, and none of them appear in the changelog |
| a **minor** | never automatic — tag it by hand |

**The type says what a change is; it does not decide how big the version bump is.** `feat` cuts a
patch like everything else, so labelling something a feature is a changelog decision rather than a
release-size one. That split is deliberate: a mod's routine flow is features, enhancements and
fixes together, and bumping minor for each of them makes the middle digit a commit counter.

For calibration: a patch is the right size for a guidance overhaul, a new targeting UI and a dozen
fixes shipped together. A minor means more than that — a new weapon system, or a compatibility
milestone.

**A minor is a deliberate act.** When something genuinely lands — a new weapon system, a KSA
compatibility milestone — tag it:

```bash
git tag -a v0.9.0 -m "second weapon system"
git push origin v0.9.0
```

semantic-release reads the newest tag and carries on from it, so the next `fix` after that is
0.9.1. A tag is the only thing that anchors a version, which is why the first release needs one
too.

Scope is optional but useful; prefer the area touched — `turret`, `rounds`, `radar`, `sim`,
`model`, `ci`. Keep the subject in the imperative and under ~72 characters, and use the body to
say *why* when the reason is not obvious from the diff.

**`feat` still means a player can observe the difference in the shipped archive.** Capability that
nothing in `Arsenal.cs` or the panel yet uses is `refactor` — it becomes a feature in the commit
that uses it, which is also the first commit whose behaviour anyone can check. This matters for
the changelog rather than for the version now: a Features section listing things nobody can reach
is worse than a short one.

Scope is not consulted either: `feat(tools)` on a developer script is still a `feat` and still
cuts a release, publishing an archive identical to the last but for its version string. Developer
tooling is `chore`, `ci`, `test` or `refactor`. The
commit-msg hook warns when a `feat`/`fix`/`perf` commit touches nothing under `src/KSArmory/`;
it only warns, because a packaging fix in `tools/package.sh` genuinely changes what ships without
touching `src/` and no mechanical rule gets that right.

**No `Co-Authored-By` trailer, and no other attribution footer.** Commits carry the repository
owner's name and nothing else, whoever or whatever drafted them. This overrides any default to add
one.

Split unrelated work into separate commits rather than one large one: the changelog is generated
from these, so a commit that does three things describes none of them well.

**Commit to `dev`, not to `main`.** `main` is the release branch and a push to it cuts a release
and publishes to SpaceDock — see [CI and releases](#ci-and-releases). Everything lands on `dev`
first and rides to `main` in a merge when a release is wanted.

**Do not commit a behaviour fix as a fix until it has been verified in game.** Compiling, passing
the suite, and having a plausible mechanism are not evidence — this mod's hardest bugs live in
the gap between the maths and what KSA actually does, and that gap is only visible in flight.

So: **ship the diagnostic, not the guess.** Instrumentation that will find a cause is worth
committing — say that is what it is. A speculative fix labelled as a fix buries the real cause and
makes the history lie about what was wrong. If something is unverified, write that in the commit
message and leave the decision to the user.

And a regression test only counts **if it fails against the old code**. Check that it does, every
time. A test that advances the platform by exactly the `v*dt` it passes in cancels the error it
is meant to detect, so it passes against the broken code as readily as the fixed — which looks
like proof and is worth nothing.

**This is enforced.** `tools/check-commit-msg.sh` runs both as a local `commit-msg` hook
(`./tools/install-hooks.sh`, using `core.hooksPath` so hooks arrive with a pull) and as a CI job
over every commit in a push or PR. One script drives both, so they cannot drift apart. It skips
merges, reverts, `fixup!`/`squash!` and semantic-release's own `chore(release):` commit.

## Environment

- **KSA install**: `/mnt/c/Program Files/Kitten Space Agency` (Windows game, WSL dev)
- **KSA build these notes were taken against**: `2026.8.19.5261`
- The system `dotnet` is 8.0 and **cannot build this** — the mod targets **net10.0**
  (`error NETSDK1045`). A .NET 10 SDK is installed at `~/.dotnet`.
  **Use `tools/build.sh` / `tools/test.sh`**, which source `tools/env.sh` to fix PATH. Bare
  `dotnet` commands fail. In an interactive shell, `source tools/env.sh` once.

- `Import/` holds the game's assemblies and is **gitignored**. Repopulate with
  `./tools/sync-import.sh`. Nothing builds without it — though the build also finds a game
  install or a `ksa-game-assemblies` checkout on its own, see `Directory.Build.props`.
- **When KSA updates, four things have to move together**, not just `Import/`. See
  [After a KSA update](#after-a-ksa-update); getting it wrong makes CI build the mod against a
  different game from the one you are testing on, silently.
- **The game is launchable from WSL** — interop is enabled, so `tools/run.sh` starts
  `StarMap.exe` directly. `run.sh` finds it under the Windows user profile — override with
  `STARMAP_DIR`. It reads `./StarMapConfig.json` **relative to its own directory**, so it must be
  launched from there.
- **The mod writes its own log** to `<KSA user dir>/Logs/KSArmory.log`, readable from WSL;
  `./tools/ksa-user-dir.sh` prints that directory and `./tools/run.sh --attach` follows the log.
  `Console.WriteLine` only reaches stdout, and KSA's `KittenSpaceAgency.log` is written by its
  internal logger which mods cannot reach — so the mod's own file is the debugging channel.
  KSA's log is still the place to look for mod discovery and asset/XML errors.

## Commands

```bash
./tools/doctor.sh                          # can this machine build, test and run it? -- start here
./tools/check-all.sh                       # everything CI runs (~8 s); also the pre-push hook
./tools/build.sh                           # build the mod (handles the SDK PATH)
./tools/test.sh                            # guidance + fuse tests, no game needed
./tools/validate-parts.py                  # part XML, launch geometry, registered PartIds; runs in deploy.sh
./tools/pack-api.py --check                # has the API weapon packs bind to moved?
./tools/model/build.sh                     # rebuild every part's mesh and textures (needs Blender)
./tools/model/checkswept.py                # does any assembly pass through another in its travel?
./tools/check-boundary.sh                  # Sim/ must not reference KSA types
./tools/check-network.sh                   # the mod only reaches the network when Send is clicked
./tools/check-tunables.py                  # every setting has a control that reaches it
./tools/check-comments.sh                  # history in comments, XML docs on privates, ratios
./tools/check-docs.sh                      # layout table, API counts and KSA build vs reality
./tools/package.sh                         # release zip into dist/ -- no symbols, no game DLLs
./tools/deploy.sh                          # build and install into the KSA mods folder
./tools/run.sh                             # build, deploy, launch, show the mod's output
./tools/run.sh --attach                    # follow a game that's already running
./tools/scenario.sh head-on                # fly one engagement unattended and report pass/fail
./tools/scenario.sh mirv                   # ...or the whole ballistic shot, and score the group
./tools/ksa-user-dir.sh                    # where KSA keeps Logs/, mods/ and saves on this box
./tools/setup-starmap.sh                   # one-off: install StarMap and write its config
./tools/check-assemblies.sh --game         # has the installed game moved past the lock?
./tools/check-ksa-version.sh               # has RocketWerkz published a newer build?
./tools/api-surface.sh                     # record the KSA API this mod binds to
./tools/api-surface.sh --check             # ...and fail if the record is stale
./tools/decompile-assemblies.sh ../ksa-game-assemblies   # refresh the decompiled corpus
./tools/ksa-api-diff.sh ../ksa-game-assemblies           # which KSA changes hit this mod?
./tools/sync-import.sh                     # refresh Import/ -- NOT the whole story after a
                                           #   KSA update; see "After a KSA update" below

source tools/env.sh                        # then bare dotnet works in this shell
cd tools/apidump && dotnet run -- ../../Import members KSA.Vehicle   # inspect the game API
./tools/meshinfo.py "<KSA>/Content/Core/Meshes/CoreStructuralA_MeshAtlas.glb" Tube  # mesh bounds
```

## Layout

**The source is split by whether it touches KSA.** `Sim/` cannot; `Ksa/` does. That is not a
convention to remember — the test project links `Sim/**` wholesale and references no KSA
assembly, so a `using KSA;` under `Sim/` fails the test build. It also means a new file under
`Sim/` is tested the moment it exists, with no build plumbing to add.

| Path | What |
| --- | --- |
| **`src/KSArmory/Sim/`** | **no KSA types, linked into the tests wholesale** |
| `Sim/Arsenal.cs` | **the built-ins — add a weapon system here** |
| `Sim/Catalogue.cs` | **what the mod reads** — the built-ins plus anything else registered; a lookup taken against `Arsenal` sees only what shipped |
| `Sim/Armoury.cs` | **the whole public surface a weapon pack binds to** — two members, taking text rather than profiles so a pack needs no game assemblies |
| `Sim/PackReader.cs` | somebody else's weapon definitions, read into profiles — **text in, no file access**, so every refusal is testable headlessly |
| `Sim/PackScan.cs` | where KSArmory looks for somebody else's weapons — **a convention, not a list** |
| `Sim/PackAudit.cs` | whether what registered can actually be found — the half of validation that has to wait until the world has loaded |
| `Sim/IPartCatalogue.cs` | **the seam the audit asks what parts exist through** |
| `Sim/PackResult.cs` | what one pack got out of registering: how much stuck, and everything that did not |
| `Sim/PackContents.cs` | what reading one pack produced: what may register, and what may not |
| `Sim/PackFault.cs` | one definition that was refused, and the reason an author can act on |
| `Sim/WeaponSurvey.cs` | reads a weapons system off a craft the mod did not design |
| `Sim/LauncherProfile.cs` | one launch platform: part Id, tube geometry, drives |
| `Sim/MunitionProfile.cs` | one round: boost, guidance, fuse, warhead |
| `Sim/Warhead.cs` | explosive charge to lethal, blast and fireball radius |
| `Sim/SensorProfile.cs` | one sensor: range, cone, threat model |
| `Sim/OpticProfile.cs` | one optical head — its own part, or one a launcher carries |
| `Sim/OpticGeometry.cs` | where a director's head sits and how far it may look, **measured from the base it rides** |
| `Sim/OpticConfig.cs` | one director's own settings — where it looks, how far it zooms |
| `Sim/Config.cs` | session-wide settings — team names, drawing, logging |
| `Sim/SystemConfig.cs` | one installation's own settings — arm, engage, turret mode, IFF |
| `Sim/SystemSettings.cs` | those settings flattened, so they can be written down and read back |
| `Sim/IProjectile.cs` | **what everything in the air must be** — a weapon kind is an implementation, not a profile field |
| `Sim/Interceptor.cs` | guided round: proportional navigation, boost, fuse |
| `Sim/Slug.cs` | unguided kinetic round: ballistics and a contact fuse |
| `Sim/BlastSweep.cs` | how near a burst a body was, and what that does to it — shared by the sweep over craft and the one over rounds |
| `Sim/Medium.cs` | what the air or water a round flies through does to it — buoyancy and drag, shared by every round |
| `Sim/ContactSweep.cs` | the contact rule: whether a round runs into a body over one step |
| `Sim/IHullTest.cs` | **the seam a kinetic round asks whether it truly touched something** |
| `Sim/IGroundTest.cs` | where the ground is under a round, for the one round the terrain stops |
| `Sim/CoarseGroundTest.cs` | the sight's ground test, which skips the lookups a falling round cannot need |
| `Sim/MushroomCloud.cs` | the shape of a nuclear cloud over time, as offsets from the burst |
| `Sim/Magazine.cs` | which tubes hold a round, which fires next, what each body does |
| `Sim/RoundLabel.cs` | what to call a round in a line somebody reads — **the one place the tube field's sentinel is decoded**, because a shell has no tube |
| `Sim/TubeGeometry.cs` | tube positions and directions, pod and radar pose, body placement |
| `Sim/Turret.cs` | rate-limited traverse and elevation drives |
| `Sim/PointingDrive.cs` | a head that points rather than trains — two degrees of freedom, no axes of its own |
| `Sim/FireGeometry.cs` | launch direction and round-body orientation |
| `Sim/BodyAttitude.cs` | which way a round points, and how a released store noses over |
| `Sim/BombSight.cs` | where a store released now would land, flown rather than solved |
| `Sim/Lambert.cs` | the transfer between two points in a stated time — **the one thing solved rather than flown** |
| `Sim/BallisticBody.cs` | the planet an arc is flown around, and how it carries a point on its surface |
| `Sim/AimSite.cs` | a place on a world, as the thing a ballistic missile is aimed at |
| `Sim/BallisticArc.cs` | what a vehicle must be doing at burnout for the fall afterwards to arrive |
| `Sim/Kepler.cs` | where a coasting body will be later, in closed form — **so a search can ask thousands of times** |
| `Sim/AimFrame.cs` | which way is up for a vehicle told to point somewhere — **the roll a pointing command leaves undecided** |
| `Sim/OrbitPlane.cs` | how far off the plane a target sits, and what that costs — **the explanation for an inexplicable burn** |
| `Sim/BurnWindow.cs` | **when** to start burning, which is not the same question as how to fly it |
| `Sim/ImpactPredictor.cs` | where it would come down if the engines stopped now — flown, not solved |
| `Sim/AimCorrection.cs` | where to aim so it lands on the target — **the solver arrives at a point, a round stops at the ground** |
| `Sim/BoosterPerformance.cs` | what the stack can still do, as the four numbers guidance needs |
| `Sim/BurnoutGuidance.cs` | where to point and when to stop — velocity still to be gained |
| `Sim/AscentProfile.cs` | the schedule flown while there is air, and the limit that keeps the stack in one piece |
| `Sim/PostBoostAim.cs` | correcting the aim after the engines stop — **the trim is the actuator**, and holding the warheads to do it has a price. **Nothing is read off a bus whose nose is turning**: the prediction carries the kick along it |
| `Sim/IcbmProgram.cs` | **the flight** — pad to cutoff to release, as one phase machine |
| `Sim/BusTrim.cs` | putting the bus back on its solution after the split — **the only thing that can**, because the burn is over |
| `Sim/SeparationClearance.cs` | whether what let go has got far enough away to manoeuvre — **the shove is the separation**, so nulling it ends it |
| `Sim/ReleasePointing.cs` | which way a launcher must hold for one tube to throw along the line the others did |
| `Sim/ReleaseSequence.cs` | letting a magazine go one round at a time, each along that same line |
| `Sim/ShotRequest.cs` | where a scripted shot is aimed and the bar it is judged against — **text in**, so the harness's one line is testable headlessly |
| `Sim/ShotGroup.cs` | where a salvo landed, and whether that is a pass — **scored on the worst warhead**, and one that never arrived counts |
| `Sim/PlatformHandover.cs` | which craft a part went to, when a decoupler took it off the one carrying it — **one decision, every roster that follows a part** |
| `Sim/IcbmConfig.cs` | one installation's ballistic settings — armed, loft, arrival angle, ascent, staging, trim |
| `Sim/FinMixer.cs` | one steering command resolved into four blade deflections — **drawn only** |
| `Sim/FinTest.cs` | the built-in-test sweep a tail kit runs on the rack — **drawn only** |
| `Sim/FireGate.cs` | whether the launcher is pointing where it is about to shoot |
| `Sim/FireLadder.cs` | **why a system is not shooting** — the gates in order, and the first one that says no |
| `Sim/DriveStatus.cs` | which drives the engine is still accepting writes for, latched per channel |
| `Sim/GunChannel.cs` | the cannon's belt, burst position and next-round timing |
| `Sim/BallisticLead.cs` | where an unguided round must be aimed to arrive where the target will be |
| `Sim/Aimpoint.cs` | what a round is shooting at — craft, component or coordinate |
| `Sim/ThreatModel.cs` | CPA threat classification, priority, engagement envelope |
| `Sim/RadarSignature.cs` | how large a contact looks, and how far that lets the set see it |
| `Sim/TrackState.cs` | one contact, as the threat model sees it |
| `Sim/Iff.cs` | which side a contact is on, and whether it may be engaged |
| `Sim/LineOfSight.cs` | whether a body is between the viewer and something |
| `Sim/ITerrainHeights.cs` | **the seam a sensor looks over the real skyline through** |
| `Sim/TerrainMask.cs` | whether a ridge hides a contact, and how few samples that can cost |
| `Sim/TerrainMap.cs` | a local east/north frame on a body, and the square of ground drawn around it |
| `Sim/Picking.cs` | what the cursor's ray meets, and what is nearest it on screen |
| `Sim/Reticle.cs` | the gunner's sight as strokes on a screen — geometry only |
| `Sim/ScopeGeometry.cs` | the radar scope's face — where a blip belongs on it, in bearing and range |
| `Sim/LockCue.cs` | how far a lock has matured, as the number a closing bracket is drawn from |
| `Sim/SightPicture.cs` | where the sight's horizontal lies, and which way a contact off the glass went |
| `Sim/SightZoom.cs` | the head's magnification, as the field of view it asks a camera for |
| `Sim/CursorAim.cs` | cursor to viewport coordinates, and the bearing from a mount to what it points at |
| `Sim/WeaponFit.cs` | **what a weapons system is fitted with** — the panel asks this rather than testing profile fields |
| `Sim/WeaponSelection.cs` | stepping round a craft's weapons, wrapping — the half of the selector a test can reach |
| `Sim/StepGate.cs` | hands a simulation step out once and only once |
| `Sim/SmoothedStep.cs` | the step evened out, for the one consumer that wants a smooth clock |
| `Sim/SimClock.cs` | classifies a step: usable, paused, or too long to integrate |
| `Sim/WarpPolicy.cs` | holds timewarp down while rounds fly, and gives it back after |
| `Sim/OverrunLog.cs` | how much simulated time the clamp threw away, and whether that cost anything — **an empty sky loses nothing**, and a scene load is always one |
| `Sim/ChaseView.cs` | where to put a camera riding behind a round |
| `Sim/ViewClaim.cs` | who may hold the player's main view, and what that means for the loser |
| `Sim/OrbitAim.cs` | the orbit-camera angles that would point the view at something |
| `Sim/ReportDraft.cs` | a bug report or idea being written, and whether it is worth sending |
| `Sim/Vec.cs`, `Sim/DrawAnchor.cs` | vector helpers, the two-instant draw anchor |
| **`src/KSArmory/Ksa/`** | **everything that binds to the game** |
| `Ksa/KSArmoryMod.cs` | StarMap entry point and frame hooks |
| `Ksa/KsaWorld.cs` | most KSA contact is funnelled here — keep it that way |
| `Ksa/WeaponSystems.cs` | one system per weapon fitted, crewed with the craft and followed across a split |
| `Ksa/WeaponSystem.cs` | fire control, salvo logic, warhead effects, drives |
| `Ksa/WeaponSystemRoles.cs` | **the slices consumers take** — effects, sights and cameras get a role, not the whole system |
| `Ksa/Radar.cs` | cone search, CPA threat model, lock |
| `Ksa/LauncherPart.cs` | finds a registered launcher, resolves tubes and subparts |
| `Ksa/LauncherSeparation.cs` | the decoupler on the joint holding a launcher on — **a property of the part, not the craft** |
| `Ksa/OpticParts.cs` | finds a director on a craft, and turns its head |
| `Ksa/OpticalHead.cs` | **one director** — its own sensor, its own aim, no weapon involved |
| `Ksa/OpticalHeads.cs` | one head per director fitted, crewed with the craft and followed across a split |
| `Ksa/InstalledPacks.cs` | reads those folders and registers what is in them — **what lets a pack be assets only** |
| `Ksa/DeclaredParts.cs` | the part library as that seam — off `PartTemplate`, because the question is what was *declared*, not what is on a craft |
| `Ksa/HullTest.cs` | whether a round's step meets a craft's actual geometry, per triangle |
| `Ksa/GroundTest.cs` | the surface under a round, off the engine's own height field |
| `Ksa/TerrainHeights.cs` | one body's height field, sampled coarsely and many times per scan |
| `Ksa/TerrainMapScan.cs` | that height field as a cached grid — **the cost lives here**, so it is paid on movement rather than per frame |
| `Ksa/BombSightOverlay.cs` | the pipper: the impact ring and the arc down to it |
| `Ksa/IcbmComputer.cs` | **one craft's ballistic computer** — reads the world, runs the program, flies the rocket |
| `Ksa/IcbmComputers.cs` | one per craft this mod recognises a weapon on, crewed and forgotten with it |
| `Ksa/AttitudeHook.cs` | **the one place this mod patches the game** — the only window in which an attitude command survives |
| `Ksa/VehicleCommand.cs` | **the only place this mod flies somebody else's rocket** — attitude, throttle, ignition, staging |
| `Ksa/IcbmOverlay.cs` | the arc it is on and the ring it is aimed at |
| `Ksa/SiteDesignator.cs` | click the world to name where the warheads go — **a mode, not a button** |
| `Ksa/Ui/Ui.cs` | the panel's shell: system list, panes, and which system they read |
| `Ksa/Ui/UiSession.cs` | the world clock, and what the session draws and hears |
| `Ksa/Ui/UiSystem.cs` | one row per component: what each part is, sees and is doing |
| `Ksa/Ui/UiOptic.cs` | one director's rows — what it looks at, looks through, and will watch. **Reads no weapons system**, because a craft with a director and no armament has all of them |
| `Ksa/Ui/UiTuning.cs` | IFF, and the sensor, guidance and warhead numbers |
| `Ksa/Ui/UiDebug.cs` | test targets, moving craft, hand-fired bursts, the log |
| `Ksa/Ui/UiMap.cs` | the ground under a director as shaded relief, with what it can see marked on it |
| `Ksa/Ui/UiIcbm.cs` | the ballistic computer's pane — what it is aimed at, whether it will get there |
| `Ksa/Ui/UiScope.cs` | the radar scope: what the *set* holds, craft-centred and polar, on the Radar tab |
| `Ksa/Ui/UiWeapons.cs` | the weapon switcher — which of a craft's weapons the trigger is pointed at, with each one's ammo and arm state |
| `Ksa/Ui/UiReport.cs` | the one window behind **Report bug** and **Feedback** |
| `Ksa/Ui/ModMenuEntry.cs` | a copied attribute so ModMenu can list this mod — **wanted gone**, see `docs/BLOCKED-ON-KSA.md` |
| `Ksa/FeedbackClient.cs` | posts a report to the endpoint, off the frame thread |
| `Ksa/Visuals.cs` | gizmo rendering |
| `Ksa/Detonation.cs` | the fireball where a warhead goes off, through KSA's particle system |
| `Ksa/Fireball.cs` | the nuclear flash: one emissive sphere that blooms, and the light it casts |
| `Ksa/PlumeSmoke.cs` | smoke through the renderer KSA draws booster plumes with, one reflected field away |
| `Ksa/MotorSmoke.cs` | the trail a burning round leaves, through that same renderer — one cursor per round |
| `Ksa/NuclearClouds.cs` | the mushroom clouds standing in the world, walked with plume cursors |
| `Ksa/MotorSound.cs` | the rocket motor you can hear, one spatialised channel per burning round |
| `Ksa/MotorPlume.cs` | the flame at the nozzle, one pooled emitter per burning round |
| `Ksa/MuzzleFlash.cs` | the flash at the cannon's muzzles, one pooled emitter per firing system |
| `Ksa/GunSound.cs` | the cannon you can hear, one looping channel pitched by its fire rate |
| `Ksa/TracerTrail.cs` | tracers, an emitter riding a shell rather than thrown from the muzzle |
| `Ksa/Sight.cs` | paints the gunner's sight over the camera the optical head drives |
| `Ksa/SightCamera.cs` | borrows the main view to look through the optical head, and gives it back |
| `Ksa/Markers.cs` | on-screen brackets over every weapons system, labelled on hover or when pinned |
| `Ksa/LockCueOverlay.cs` | brackets on **what the selected weapon is engaging**, closing as the lock matures |
| `Ksa/RoundFollowable.cs` | a round, presented to the engine as something a camera can follow |
| `Ksa/ChaseHud.cs` | brackets around what a chased round is flying at |
| `Ksa/ChaseCamera.cs` | rides the main view behind a round, and gives it back |
| `Ksa/LevelHorizonController.cs` | KSA's fixed camera controller, with an up vector it does not otherwise offer |
| `Ksa/WatchCamera.cs` | nudges the main view round onto one system, then lets go |
| `Ksa/Contact.cs` | **what a sensor can hold** — a craft, or anything else that can be seen |
| `Ksa/RoundContact.cs` | somebody else's round in the air, as a thing a radar can see and a gun can shoot at |
| `Ksa/Track.cs` | one contact, with the kinematics the threat model reasons about |
| `Ksa/TestTarget.cs` | spawns drones to shoot at, from the panel |
| `Ksa/ScenarioRunner.cs` | flies a scripted scenario with nobody watching, and says what happened |
| `Ksa/BallisticScenario.cs` | the ballistic one of those — designate, arm, stage, and report what the warheads did |
| `Ksa/CraftMover.cs` | picks a craft up and sets it down elsewhere, from the panel |
| `Ksa/BurstTool.cs` | click the world to set off a warhead there, from the panel |
| `Ksa/Designator.cs` | click the world to shoot at that spot, with no target and no lock |
| `Ksa/TargetLock.cs` | shift-click anything to lock an installation onto it |
| `Ksa/Diagnostics.cs` | the periodic world dump — what the system can see and why |
| `Ksa/Build.cs` | what build this is, read off the assembly rather than written down |
| `Ksa/SettingsStore.cs` | per-craft settings across sessions, in JSON beside the log |
| `Ksa/Log.cs` | the mod's own log file, which is the only debugging channel it has |
| `src/KSArmory/KSArmory*.xml` | the parts, the warhead effects and one stock character — at the mod root, mirroring Core |
| `src/KSArmory/KSArmory/Weapons.xml` | **this mod's own weapons, as data** — read by `PackScan`'s convention like any pack's, not by KSA |
| `src/KSArmory/Meshes/`, `Textures/` | art. `KSArmory_MeshAtlas.glb` is generated — rebuild with `tools/model/build.sh`; every other atlas is **authored**, and its `.blend` is not in this repository |
| `src/KSArmory/Sounds/` | the explosions, generated by `tools/sounds.py`; the cannon, cut from a recording by `tools/cut-cannon.py` |
| `src/KSArmory/mod.toml` | serves as both the content-mod and StarMap manifest |
| `tests/KSArmory.Tests/` | links the KSA-free sources and flies engagements headlessly |
| `KSArmory.sln` | both projects, for editors only — every script builds a csproj directly |
| `infra/dns/` | ksarmory.com's Cloudflare records, as OpenTofu; see `infra/README.md` |
| `infra/services/` | Caddy and the feedback service on the VPS, same |
| `services/feedback/` | the endpoint that takes in-game bug reports and files them as issues |
| `tests/Feedback.Tests/` | its text rules and the log gate, needing neither the game nor the model |
| `tools/apidump/` | reflection dumper for the game assemblies |
| `tools/apisurface/` | reads the KSA API this mod binds to out of its own metadata |
| `docs/KSA-CAMERAS.md` | what the engine does with cameras and viewports, from the decompiled source |
| `docs/KSA-FRAME-ORDER.md` | **the engine's own frame order and what instant each sample belongs to**, from that same source — the evidence under `FRAMES-AND-EPOCHS.md`'s rules |
| `docs/KSA-TERRAIN.md` | **where the engine thinks the ground is** — the height field's resolution, what `accurate` buys, and the one place three surfaces disagree |
| `docs/KSA-API-SURFACE.md` | **generated** — the 429 members an upgrade has to preserve |
| `docs/PACK-API-SURFACE.md` | **generated** — the elements, attributes and members a weapon pack binds to |
| `docs/AUDIT-2026-08.md` | a review of where the code and tools mislead; the ranked list at the end is the backlog, and items come off it as they land |
| `docs/CODE-HEALTH.md` | **living** — the modularity and comment-hygiene backlog, ticked off as it lands |
| `docs/BLOCKED-ON-KSA.md` | **what the mod cannot build**, with the engine reason and what would unblock it |
| `docs/ICBM-GUIDANCE.md` | **the ballistic computer** — the algorithm, the frames, the cutoff, and what has not been flown |
| `docs/MIRV-NEXT.md` | **the backlog for the bus** — what separation costs, and what has to happen before re-pointing pays |
| `docs/ARRIVAL-ANGLE.md` | **what a steeper arrival is worth** — precision, impact speed and propellant against the angle a round comes in at, why seven degrees is the air's answer rather than the guidance's, and the control that asks for another |
| `docs/KINETIC-FLOOR.md` | **how accurate a round could possibly be** — the terms no amount of guidance work removes, and why the arrival angle is the whole lever |
| `docs/NUCLEAR-EFFECT.md` | which of KSA's four volumetric renderers a mod can reach, and what a mushroom cloud actually looks like |
| `docs/FROM-KSP-MODDING.md` | the concept map for anyone arriving from KSP part modding |
| `docs/MODULARITY.md` | how far the profile/registry split actually generalises, and the test gaps to close before widening it |
| `docs/WEAPON-TAXONOMY.md` | the same question from outside: which real weapon families share this data model, and which need a different one |
| `docs/BATTERY-SPLIT.md` | what `WeaponSystem` should be split into, what to call it instead, and in what order |
| `docs/WEAPON-PACKS.md` | **the pack author's reference** — the folder, the ten-line entry point, and every attribute a definition file may carry |
| `docs/EXTENSIBILITY.md` | **a plan, not a record** — how a weapon pack registers itself without this mod knowing it exists, and what such a pack could never express |
| `.claude/skills/upgrade-ksa/` | the whole KSA-update procedure, as a skill |
| `tools/meshinfo.py` | prints mesh bounds from a KSA `.glb` atlas |
| `tools/validate-parts.py` | checks asset Ids, texture paths, and launch geometry vs the mesh |
| `tools/pack-api.py` | records the API a weapon pack binds to, and fails when it moves — **the mirror of `api-surface.sh`**, because a pack lives in somebody else's repository and never builds here |
| `tools/repair-saves.py` | realigns saves written before a part lost a subpart |
| `tools/model/` | headless Blender scripts that generate the parts |
| `tools/model/pantsir.py` | the Pantsir, and the entry point that builds the whole atlas |
| `tools/model/sidewinder.py` | the LAU-7 rail and its AIM-9J, into that same atlas |
| `tools/model/ciws.py` | the Phalanx CIWS: a gun with no missiles, on a 3 m stack node |
| `tools/model/optic.py` | the EO director: the sight, as a part anything can carry |
| `tools/model/import-litening.py` | reframes the hand-modelled pod into what KSA reads |
| `tools/model/preview-glb.py` | renders any `.glb` from a few angles, so an authored asset can be judged before it is declared |
| `tools/model/preview.sh` | runs that from WSL, which is the only comfortable way: Blender is a Windows binary and wants Windows paths for the script *and* for everywhere it writes |
| `tools/model/checkmesh.py` | finds unpaired node/mesh names, zero-UV-area triangles and coplanar faces in a `.glb`; takes several at once, and `--compare` diffs two atlases by geometry *and* node transform |
| `tools/model/dilate-atlas.py` | fills the empty space around a baked atlas's islands from their nearest neighbour — **what a bake margin cannot do**, because a margin wide enough to survive mipmapping is wide enough to write one body's dilation over another's |
| `tools/model/checkswept.py` | sweeps the drives and reports any assembly passing through another |
| `tools/model/smokepuff.py` | the soft sprite the billboard smoke is drawn with |
| `tools/screenshot.sh` | captures the Windows screen; readable from here |
| `tools/scenario.sh` | drives one engagement or one ballistic shot end to end and exits pass/fail; screenshots on cue |
| `tools/sounds.py` | synthesises the explosion samples, and the fallback cannon behind `--synth-cannon` |
| `tools/cut-cannon.py` | cuts a gunfire recording into spin-up, loop and tail, on measured envelope boundaries |
| `tools/audio/` | the CC0 Phalanx recording the cannon is cut from, and its provenance |
| `tools/logo.py` | the Kessler Systems wordmark and icon, into `branding/` |
| `branding/` | the generated logo the README and SpaceDock point at |

## 3D model pipeline

**New art is authored in Blender over MCP, in a live session** — not written as a headless script
and not driven through the CLI. `.claude/skills/ksa-blender/SKILL.md` is the whole procedure; the
short version is that the addon drives the *open* document — screenshots of the viewport, scene
summaries, the bundled Blender docs, and Python execution as the last resort rather than the
interface — so the loop is build, look, adjust rather than emit and hope. **A human signs off on
the geometry before anything is unwrapped, baked or exported**, because everything past that point
is welded to the shape.

**The headless generator below still builds four parts and still has to keep working.** The
Pantsir, the CIWS, the LAU-7 rail and the EO director come out of `tools/model/pantsir.py` into one
atlas sharing one palette material. Keep it working; do not extend it. Everything from here to the
end of this section is about those four.

The nuclear rack is not among them: both its bodies are authored, into one atlas sharing one
unwrap. It stopped instancing the generated beam when the B61-12 turned out to hang from 30-inch
lugs where that beam's hooks are 14 inches apart — a MAU-12 carries both spacings, so the authored
rack carries both spacings.

**An authored asset's `.blend` is its source, and it is not in this repository** — what is committed
is the export. So a committed asset cannot be regenerated from a clean checkout, which is why
`checkmesh.py` and `validate-parts.py` matter more for authored art than for generated: they are the
only gate on something nobody can rebuild.

**An authored asset that obeys the contract needs no import step at all.** The suspension rail is
the demonstration: exported in part space with its origin on the mounting face, node names matching
mesh names, its own `_VM` twin and a `_ColPrim_` box carrying the collider, it is copied in as
exported and only declared. The pod needed a tool because its export did none of those things —
which is the difference the skill file exists to close.


Blender **5.2** is installed at
`/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe` and is driven entirely from
scripts — no viewport work. See `tools/model/README.md`; run `tools/model/smoketest.py` first
after any toolchain change.

```bash
BL="/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$BL" --background --python "$(wslpath -w tools/model/smoketest.py)" -- 'C:\Windows\Temp\out.png'
```

**The loop is**: build geometry → render a PNG → read the PNG here → adjust → repeat, with
`./tools/meshinfo.py` checking exported GLB bounds. Model work is therefore *visually iterable*
rather than blind, and the Pantsir was built through it.

`tools/model/README.md` has the full pipeline and the coordinate system. The traps worth
repeating here:

- Blender is a **Windows** binary, so `--python` needs `wslpath -w` and outputs want `C:\...`.
- Blender 5.2 has no `BLENDER_EEVEE_NEXT` — use `BLENDER_EEVEE`.
- **Every face needs UV area.** Collapsing a face's loops onto one swatch centre — the obvious
  way to use a palette atlas — gives a zero UV derivative, hence a zero-length tangent, hence
  `normalize()` → NaN, hence garbage shading. `NaN * 0` is still NaN, so a flat normal map does
  not save it. The vehicle sparkles. `project_to_swatch()` gives each face a small projected
  patch instead.
- **Never let two primitives share a face plane.** Coplanar faces z-fight. `box()` inflates
  every box by a skin *plus a per-box jitter* — a uniform skin only separates faces pointing at
  each other, and does nothing for two boxes whose outer faces both sit on the same constant.
  `cyl()` does not inflate at all, and radius alone will not save a coaxial pair: use a
  different facet count or a `cone()`.
- **The jitter runs off one seed, so moving a `box()` call reshuffles every box after it.** Adding
  or removing one is enough, and the damage lands somewhere else entirely — pushing two faces in
  an unrelated assembly onto the same plane. To change which group a primitive belongs to without
  disturbing anything, set `_group` around the existing call rather than moving the call.

**The atlas is not byte-reproducible.** Blender's exporter does not emit triangles in a stable
order, so a rebuild from unchanged sources gives a different file — same positions, normals and
UVs, permuted index buffer. `git status` showing it modified after a build therefore means
nothing. Ask `./tools/model/checkmesh.py <new> --compare <old>`, which compares the surface
rather than the bytes, and **revert the atlas** if it says the geometry is unchanged.

- **Two bodies can share a plane, and `checkmesh.py` alone will not see it.** It analyses one
  mesh at a time, so a turntable resting exactly on the cap of its mast z-fights like any other
  coincident pair and reports clean — worse when the pair spins, because the fight then rotates.
  The cross-body pass lives in `validate-parts.py`, because the atlas carries **no node
  transforms** and only the part XML knows where each body sits. It reads the subpart's
  `<Rotation>` as well as its `<Position>`, and has to: a round seated on a rail is placed with a
  quarter turn carrying its nose onto the tube axis, so a pass using position alone lays the body
  *across* the launcher and finds nothing, because nothing is in contact.
- **A render only shows the poses it was asked for.** Geometry defects hide at the other ones:
  pods that pass through the gun sponsons at the twelve o'clock positions, tubes through the APU
  box at bearing 50°. `tools/model/checkswept.py` sweeps the drives and reports the metres one
  assembly would have to move to leave another. It needs neither Blender nor the game — the atlas
  is a library of bodies in their own local frames, so any pose is reconstructible from it plus
  `muzzles.json`.
- **It sweeps every articulated vehicle, and a new one has to be added to `vehicles()` by hand.**
  A body set it does not name is simply not swept, and the tool still prints "clear" — a vehicle
  with a traverse and an elevating head can have no coverage at all. A tool that reads the first
  entry looks correct until there is a second, which is the shape of the launcher registry and
  the travel reader too. **When a weapon system stops being the only one, check what still
  assumes it is.**
- **A piece can come adrift and every other check still passes.** The mesh is clean, the pivots
  agree, nothing intersects — the part simply stops touching what carried it and hangs in the
  air. `checkswept.py` requires every primitive of the assembled vehicle to reach the chassis
  through overlap. Per-*body* connectivity is the wrong test: the cannon are legitimately two
  islands that never touch each other, and the fins are twelve.
- **A cover must not stand proud of what it covers.** A cap a few millimetres wider than its tube
  catches the light as a rim all the way round, and one with fewer facets makes that rim visibly
  polygonal. It is far too small to see in a preview. `checkswept.py` flags a *short* coaxial
  primitive slightly wider than the long one it caps — short is what separates a mistake from a
  design, because a booster stage is legitimately fatter than its sustainer.
- **Bearing cancels for any pair of bodies that both ride the turret.** The traverse is one rigid
  motion applied to the whole group, so pods-vs-guns, pods-vs-turret and guns-vs-turret are
  one-degree-of-freedom problems in elevation alone. That is a fact about the chain, not a
  sampling shortcut, and it is what keeps the sweep cheap.
- **Elevation turns about +Z, so a gap in Z holds at every pose.** Separating two turret-riding
  bodies in Z is the only separation that survives both drives; separating them in X or Y only
  works at the elevation you checked.

**Neither shows up in Blender's preview render**, so a clean preview proves nothing.
`./tools/model/checkmesh.py <atlas.glb>` catches both and exits non-zero — run it after any
model change. Both defects look identical in game (flickering white speckle), so *diagnose with
the checker, not by eye*: the symptom points at z-fighting whether the cause is coplanar faces
or degenerate UVs, and inflating geometry that is already fine fixes neither.

### Runtime part transforms work — the turret traverses and the pods elevate

**Writing a subpart's transform each frame moves it**, confirmed in game.

**Subparts are `Part` objects in their own right.** `Part.SubParts` is a `ReadOnlySpan<Part>`,
each with settable `Asmb2ParentAsmb` *and* `PositionParentAsmb` — so the launcher stays a single
part in the editor and still articulates.

What it depends on:

- **`ResetCachedPosMatrixValues()` must be called after the write.** `Part` caches
  `_matrixAsmb2Parent` and friends; without the reset the new value is stored and ignored.
- **A subpart rotates about its own mesh origin.** The turret and pod meshes are exported
  recentred on their pivots (`TURRET_PIVOT`, `POD_PIVOT` in `pantsir.py`) and put back with
  `<Position>` in the XML. Without that, a subpart swings round the chassis like a wrecking ball.
- **SubParts do not nest.** The asset XML places every `<SubPart>` against the `<Part>`, so the
  pods and the search array are *siblings* of the turret, not children. The mod composes the
  rotations itself and rewrites their `PositionParentAsmb` every frame — otherwise they would
  turn on the spot while the turret rotated out from under them.
- **The pods are modelled at their working elevation, not flat**, and runtime elevation is
  applied as a rotation *away* from that reference. A refused write then leaves the vehicle in
  a pose that looks right rather than with its tubes through the tracking radar.
- **`Vehicle.Asmb2Ego` doubles as world→part orientation.** Ego is a pure translation of Ecl, so
  for a *direction* the two agree exactly, and its conjugate turns a world bearing into the part
  frame the drives work in.

Tube offsets are emitted **pod-local**, so the launch markers ride both axes for free — and
`Visuals.DrawLoadedTubes` must resolve them through `PodsPart`, **not** `TurretPart`. Passing
the turret compiles, looks right, and is very nearly right: the markers still follow the
traverse and simply refuse to go up and down with the pods.

**The pods will not depress into the vehicle's own bodywork.** `Turret.DepressionFloorAt`
raises the elevation floor across a forward arc, where the pods would otherwise swing down
through the APU box behind the cab. It eases in across the arc edge, and is enforced against
the *current* bearing in `Update` so traversing into the forward sector lifts the pods on the
way round rather than on arrival. A flat depression limit everywhere would be simpler and
worse — off the beam the pods can legitimately come down to level, which is the shot against
anything skimming the horizon.

`Visuals` draws a cyan line along where the drives think they point, on `Config.DrawTurretFacing`
— its own switch rather than riding on the radar volume, because it is what separates "the maths
is wrong" from "the engine ignored the write" and is the one line worth keeping when everything
else is off.

The overlay is drawn for **one** system by default. There are as many overlays as there are crewed
systems, and four search cones around four craft is not four times as useful;
`Config.DrawOverlayForFocusedOnly` off draws them all, which is the case for comparing two sites.

Four moving pieces: chassis (fixed), turret (traverses), pods (traverse + elevate), and the
**search array**, a double-sided hexagonal wedge that turns continuously off the clock rather
than off the track — it is a search set, so it never stops and never aims. Its two hex faces are
clocked 30° apart because hexagons rotated alike put their flats on the same planes, and the
faces lean toward each other far enough for those planes to overlap.

The Pantsir's boresight is local "up" — its set sweeps a hemisphere regardless of where the tubes
are aimed, and the spinning array is cosmetic. That is its `SensorProfile`'s choice rather than a
limit: `BoresightMode` also offers `PartForward`, which the Sidewinder rail uses because a seeker
head looks where the rail points, and `TurretAxis`, which follows the traverse.

## Adding a weapon system

The mod is built around three profile types and a registry, so a new launcher, round or sensor
is **data plus art**, not new logic. No fire-control, guidance or drive code names the Pantsir —
it is named in `Sim/Arsenal.cs`, which is the registry and is meant to. Anything else naming it is
a doc comment citing what it was modelled on. **`tools/model/sidewinder.py` plus the
`SidewinderRail` entry in `Sim/Arsenal.cs` is the worked example** — a whole second system, and
neither of them is longer than a page.

1. **Model it.** A module beside `tools/model/sidewinder.py` exporting into the same atlas,
   called from `pantsir.py`'s `main()` — **after** everything already there, because the box
   jitter runs off one seed and inserting a primitive reshuffles every one drawn after it. Run
   `tools/model/checkmesh.py` and `tools/model/checkswept.py`; between them they catch the
   defects that are invisible in a preview render and obvious in game.
2. **Declare the part.** A `<SubPart>` per moving assembly plus a `<Part>` in
   `KSArmoryAssets.xml`, and a `<PartGameData>` with its colliders and mass.

   **A shipped part's subpart list is append-only.** KSA pairs a saved part with its current
   definition positionally, bounding the loop by the save and indexing the definition, so removing
   a `<SubPart>` throws `IndexOutOfRangeException` from inside `Popup.DrawAll` and **terminates the
   game** on every save holding that part. Adding and renaming are both free. Leave a stub to hold
   the count, or repair the saves with `tools/repair-saves.py`;
   `docs/KSA-MODDING-NOTES.md` has the loop.
3. **Register it — in `Sim/Arsenal.cs`, and in *both* registries.** One `LauncherProfile`, naming
   the munition and sensor it uses, with the geometry `build.sh` prints; add a `MunitionProfile`
   and a `SensorProfile` too if the round or the set differ. Then teach `validate-parts.py` to
   compare that geometry against `muzzles.json` — the generator emitting it and the profile
   holding it are the same numbers in two files, which is the shape that drifts.

   **And a `ComponentProfile` in `Arsenal.Components`.** The two registries are keyed on the same
   part Id and do different jobs: `Launchers` says how the thing shoots, `Components` is what
   `WeaponSurvey` reads to decide a craft is a weapons system *at all*. A launcher missing from
   `Components` loads, resolves its tubes, matches `LauncherForPart` and is then completely
   invisible — the panel says "no weapons systems" about a craft carrying it, and nothing appears
   in any log. `ArsenalTests.EveryRegisteredLauncherIsAlsoARecognisedComponent` fails against
   that state.
4. **Then nothing else.** `LauncherPart.Find` matches against every registered part Id, and the
   system selects whichever profile it finds. `ArsenalTests` checks the two registries agree and
   that each hangs together; `validate-parts.py` checks the geometry still matches the mesh, and
   that every registered `PartId` is declared in the XML — a profile naming a part that exists
   nowhere otherwise passes every gate and finds no launcher in game.

**A launcher need not carry missiles.** The CIWS declares `Tubes = []`, so `TubeCount` is zero and
the magazine holds nothing; it fires entirely through `GunMunition` and `GunMuzzles`. Neither
"every launcher has a tube" nor "every turret has pods" is an invariant. What is required is that
a launcher can shoot with *something*, and that a traverse carries something that moves with it.

**Its radome elevates with the gun, and that is the whole articulation.** The dome carries the
track antenna, which has to stay boresighted with the barrels, so the housing, the barrels and the
dome are one rigid body swinging on a trunnion between two cheeks that traverse. Splitting them —
dome held upright by the traverse, barrels elevating alone — reads as a mount that articulates in
a way no real one does. The clearance that makes it work is **a gap in Z**: elevation turns about
+Z, so the dome being narrower than the gap between the cheeks holds at every pose, and nothing
else does.

**And it stacks rather than surface-attaching.** A `<Connector>` with no `<Flags>` is a node
connector; `ToSurface` is the opt-in for radial. So the CIWS sits on top of any 3 m tank, decoupler
or adapter, and has one connector because nothing stacks on a gun.

**A part that surface-attaches cannot start a craft.** `IsAllowedAsRootPart` rejects a part if
*any* of its connectors is `ToSurface` or `FromSurface`, whatever tags it carries — so the choice
is one or the other, and it is why the Pantsir and the CIWS stack while the rail, the rack and the
director attach radially. A vehicle roots; a store rides. Nothing logs when it is wrong: the part
is simply greyed out while the editor is empty.

**And Core's `Radial` tag stops anything being mounted on the part carrying it**, because the
editor's face-snap target blacklist beats its whitelist. On a store that is right; on a launcher it
means no director can be fitted. `docs/KSA-MODDING-NOTES.md` has all three gates.

A launcher that does not train is the same `LauncherProfile` with `TurretMarker` and `PodsMarker`
left null — `Trains` is then false, the drives are skipped and `IsLaid` stays true, so fire
control cannot deadlock waiting for something that will never move.
`ArsenalTests.AFixedLauncherIsJustAProfileWithNothingThatMoves` pins that shape, and
`DriveFailureTests` pins the difference between that and a drive the engine refused.

**The mod reaches the network exactly once, and only because a player clicked Send.** There is no
version ping, no update check, no usage count — anything of that shape would be a request nobody
agreed to, arriving as a surprise in someone's firewall log. The click is the permission, which is
why there is no consent dialog to dismiss: the window says what will be sent before it is sent.
`tools/check-network.sh` enforces it textually, the way `check-boundary.sh` guards the Sim/Ksa
split, and it fails the build if any networking type appears outside `Ksa/FeedbackClient.cs`.

That is also why **the "report only against the latest version" rule lives on the server**. The mod
sends its version inside the report it was already sending, and the endpoint answers 426 with the
number needed. Asking the game to check whether it is current would mean a request at startup, for
a rule the server can apply for free.

**The panel is organised by what owns a thing, and there are four owners.** A control's home
follows from which one it belongs to, and that is the only rule needed to place a new one:

| Owner | Where it lives | Test |
| --- | --- | --- |
| the **session** | the settings window, off the main panel | two sites could not sensibly disagree — one screen, one pair of ears, one clock |
| a **component** | that part's row under **Components** | it describes or drives one part of one installation |
| the **installation** | the header strip above the tabs | it is about the whole system and no part of it — whether it is shooting, and what is in the air |
| a **shared profile** | the **Tuning** tab | it says what a Pantsir *is*, so editing it reaches every Pantsir in the world |

The last is why the tuning numbers did not move onto the component rows with everything else: a
slider under a named part on a named craft reads as belonging to that craft, and these do not. The
tab says so at the top, because it is the most surprising thing about it.

The header strip is above the tab bar rather than inside a row for a reason worth keeping: every
gate in fire control returns quietly, so an unarmed system, one with no lock, one still settling
and one whose drives the engine refused all look identical from outside. `Holding fire: <why>` is
the only thing that separates them, and it is no use behind a fold or on a tab nobody is looking
at.

**The master arm, auto-engage and FIRE are on that strip too, and belong there by the same test.**
Whether the installation is shooting is about the whole system and no part of it, so the
fire-control component row is the wrong home for it however sensible the part sounds — that row is
for what fire control *decides* once armed: rounds per target, what it will not shoot at, and the
mouse controls.

**Ownership says where a control lives; it does not license how deep.** The two rules are
independent, and obeying only the first is what buried the master arm and the sight three folds
down. So: a component row opens **expanded**, anything reached *during* an engagement — slow
motion, the target spawner, the log — is at most one window from the panel, and a control that is
the answer to "why is nothing happening" is never behind a disclosure triangle. A fold is for
detail nobody needs on the way past, which is what the manual-drive and IFF sub-nodes are.

**One field, one control.** `Config.DrawOverlays` had two, under two names with two explanations,
so toggling either silently moved the other. `check-tunables.py` cannot catch this — it asks that
every setting has *a* control, never that it has only one — so it is a review question. Where a
second surface genuinely helps, it reports the state and names where the switch is rather than
being a second switch.

**A control that opens a window is a button, never a tick box.** A checkmark reads as "this
setting is on", so a window arriving instead is unannounced and the tick says nothing about where
it went. Opening a window is an action; tick boxes are for state — armed, auto-engage, what to
draw, a tool being active. Tint the button if open/closed is worth showing.

**A setting nobody can reach is not a setting, and that is enforced.** The panel enumerates its
controls by hand, so a field added to `SensorProfile`, `MunitionProfile`, `SystemConfig`,
`OpticConfig` or `IcbmConfig` is read by the code, described in the docs, shipped in the archive,
and untouchable. Nothing fails and nothing appears in any log. `SensorProfile.HorizonMasking` and
`TerrainMarginMetres` shipped that way, with a whole section of `CHECKLIST.md` asking for them to be
toggled.

**And the list of scanned types has to grow with the mod**, which is the failure one level up:
`IcbmConfig` went unscanned for its whole first life, so a ballistic setting with no control would
have passed. Adding a settings type without adding it to `TUNABLE` puts every field on it back
outside the check, silently.

`tools/check-tunables.py` fails the build on it. Textual, like `check-boundary.sh`, and for the
same reason: the panel is under `Ksa/` and the test project cannot reference it. It requires a
**write** rather than a mention — every control is followed by a line reading the value back to
explain itself, so a check accepting any occurrence passes with the slider deleted and the
explanation left behind. Two escape hatches, both of which name their reason: `EXEMPT` for a
member no control could sensibly reach — generated geometry, a derived value, an identity string —
and `VIA` for one the panel drives through a helper, which names the helper so deleting the
control still fails.

**A weapon's discrimination fields are off at zero, and zero is the default.**
`ReferenceCrossSectionM2`, `NotchSpeed`, `ClutterFloorMetres` and `TerrainSamples` all start at
nothing, so a profile that says nothing about them behaves exactly as it did before they existed.
Each is a real capability with a real cost rather than an upgrade: a Doppler notch rejects clutter
*and* loses the target crossing exactly abeam, which is the one geometry `ThreatRadius` exists to
keep engageable, and a clutter floor of any size makes a short-range set useless at the job it is
for. Detection range goes as the **fourth** root of cross-section — a target a hundredth the size
is seen at a third of the range, not a hundredth — so a round is a far smaller target than the
craft that threw it with nothing having to know a round from a craft.

**A setting belongs to a system or to the session, and which one is the whole distinction.**
`SystemConfig` holds what can differ between two launchers in the same world — armed,
auto-engage, which weapons are live, turret mode, whether the craft
being flown is protected, and **the IFF policy**, because two sites on opposite sides is exactly
the case. `Config` holds what cannot: the
roster of team names, what gets drawn, how much is logged. The test to apply is not importance but
whether two sites could sensibly disagree — a name labels a craft the same way whoever is looking
at it, and what that name *means* is each system's own.

Weapon *performance* is neither: range, guidance and fuse live on the profiles, because two
Pantsirs on opposite sides of the map share a flight model and disagree about whether they are
armed.

**Every weapons system in the world runs its own fire control.** `Ksa/WeaponSystems.cs` crews a craft
the moment a survey recognises a part on it, pins the system there and forgets it when the craft
dies. Each carries its own `SystemConfig`, so arming one site, sending it a target or putting it
on a team says nothing about any other.

**A weapon system belongs to the installation running it, not to the session.** `WeaponSystem`
carries its own `Profile`, `Munition` and `Sensor`, paired by `Arsenal.LoadoutFor` when it
recognises the part; `Config` deliberately holds none of the three. A session-wide selection is
what makes two different systems in one world impossible — every reader outside a system's own
update gets whichever system resolved last, silently. The profiles are the shared `Arsenal`
instances, so panel tuning still reaches every system running that loadout, which is the intent.

**A craft carries one weapon system per launcher part, and the player picks between them.**
`WeaponSystems` keys on the craft *and* the launcher's ordinal, so two rails on one aircraft are
two weapons: each with its own magazine, drives, rounds in the air and arm switch. The selector on
the header strip chooses which one the panel and the trigger are pointed at, and `For(craft)` is
what returns it — which is why the sight, the chase camera and the manual trigger all followed the
selection without changing.

Re-pointing a single system at a different launcher would have been smaller and wrong: the
magazine is sized and filled per profile, so switching would refill it, and a player could drop,
switch, drop, switch back and find the bomb had returned. It is also why the settings key carries
the ordinal past the first — two racks sharing one entry would share an arm switch.

**What is still not general:** the roster is KSA-facing and unreachable from the test project, so
only `Sim/WeaponSelection.cs` — the stepping arithmetic — is covered. See `docs/MODULARITY.md`
change 2 for what that leaves unproven.

## CI and releases

Building needs KSA's own assemblies — `KSA.dll`, `Brutal.Core.Numerics.dll` and friends — and
the tests need them too, for `double3`. They are RocketWerkz's copyrighted files and **must
never be committed here or published anywhere**.

They live instead in the private repository **`LaurensDeV/ksa-game-assemblies`**, checked out by
CI with a **read-only deploy key** held in the `KSA_ASSEMBLIES_KEY` secret. Keeping your own
licensed copy privately is fine; publishing it is not. `tools/sync-assemblies.sh` refreshes the
mirror after a KSA update, refusing if a csproj references something it does not know to copy.
It mirrors the whole SDK by default — see *The mirror is a general KSA SDK* below for why, and
for the `--subset` flag that restores the nine assemblies this repository alone references.

`Directory.Build.props` resolves the folder in tiers, first match wins: `KSA_DLL_DIR` (what CI
sets), then `Import/`, then a sibling `ksa-game-assemblies` checkout, then the game install — so
a machine with any one of them needs no configuration.

### After a KSA update

**You will be told when this happens**, twice over:

- **Before the update is installed** — RocketWerkz publish the current build at
  `http://ksa-master1.rocketwerkz.com:8082/version`, which returns
  `{"Version": "...", "Url": "..."}`. `./tools/check-ksa-version.sh` compares it to the lock,
  and the `ksa-version.yml` workflow does the same daily and **opens an issue** when they
  diverge. It opens an issue rather than failing: nothing is broken when the game moves, there
  is just work to do, and a red cross on an unrelated commit says that badly.
- **After it is installed** — `./tools/build.sh` checks the install against the lock on every
  build, silent unless it has moved. `./tools/check-assemblies.sh --game` asks on demand.

The version check is deliberately *not* wired into `build.sh`: a network call on every build is
slow, breaks offline, and would be the first thing anyone disabled.

That check deliberately looks at the *install*, not at whatever the build resolved: `Import/` is
a copy, so it still matches the lock after a game update and would report all-clear. It also
compares only what the install ships — `StarMap.API.dll` comes from the loader, and would
otherwise report a KSA update every single run.

The assemblies exist in two places that drift apart silently: your `Import/`, and the
private repo CI compiles against. Update one and not the other and CI is building the mod
against a different game from the one you are testing against — which surfaces as behaviour
nobody can reproduce, not as an error.

`ksa-assemblies.lock` records the expected SHA-256 of each referenced assembly plus the game
build. It holds hashes and names only, so it is safe in a public repository. Both CI jobs check
the assemblies against it and fail if they disagree, and `sync-import.sh` reports the same thing
locally the moment you refresh.

The dance, in order — **or just run the `upgrade-ksa` skill, which is this written out with the
reasoning attached**:

```bash
./tools/sync-import.sh                                    # refresh Import/; it reports the drift
./tools/sync-assemblies.sh      ../ksa-game-assemblies    # the mirror's DLLs
./tools/decompile-assemblies.sh ../ksa-game-assemblies    # the mirror's sources
#   set current/KSA_BUILD, commit BOTH together, push there
./tools/ksa-api-diff.sh ../ksa-game-assemblies      # what actually broke — read this
./tools/check-assemblies.sh --update                # record the new digests
./tools/api-surface.sh                              # the surface moves if the fixes did
#   edit the `build` line in ksa-assemblies.lock, commit it here
```

Then **recheck `docs/BLOCKED-ON-KSA.md`**. It lists what this mod wants and cannot build, with the
engine reason for each; a KSA update is the only thing that changes any of them, and none will show
up in `ksa-api-diff.sh` because they are calls that do not happen rather than members that moved.

Do the private repo *before* pushing here, or CI fails on the lock it cannot satisfy yet.

**The compiler only finds half of it.** A renamed member is a build error you fix in seconds. A
member that keeps its name and signature and changes its *meaning* — a different reference
frame, different units, a reordered enum — compiles clean and is wrong in flight. That is what
the decompiled corpus is for, and `ksa-api-diff.sh` narrows it from 660,000 lines to the files
defining the 152 types this mod actually uses.

**The mirror is a general KSA SDK, not this mod's dependencies.** It carries all 35 RocketWerkz
first-party assemblies plus the loader and the game-shipped third-party — 44 in total, 12 MB —
so any KSA mod can build against it. `sync-assemblies.sh --subset` narrows it to the minimal set.
Per-mod pinning still comes from that mod's own `ksa-assemblies.lock`, which covers only what it
references, so the drift check stays exact without the mirror being narrow.

The build number is written down in three places, so update all three: `ksa-assemblies.lock`,
`current/KSA_BUILD` in the private repo, and the **KSA build** line under Environment above. The
lock is the one that is actually enforced; the other two are for humans reading.

CI is split the same way the source is:

- **`tooling` (hosted, always runs)** — everything that does not need the game, which is more
  than it sounds. `checkmesh.py` on the committed atlas catches the two defect classes that are
  invisible outside the game; `check-boundary.sh` guards the Sim/Ksa split textually;
  `palette.py` is re-run and the textures diffed, so hand-edited PNGs are caught before the next
  model build silently reverts them. Plus shellcheck, XML well-formedness and a check that no
  `.dll` is tracked.
- **`build` (hosted)** — the real build, the tests, `validate-parts.py` and the package,
  against the checked-out assemblies. If the secret is absent — a fork — the job skips with a
  notice instead of failing on something a contributor cannot fix.

shellcheck runs at `-S warning`. At the default level it flags every `source tools/env.sh` as
unfollowable, which it is, and CI would fail on nothing.

**Work happens on `dev`; `main` is the release branch.** Push to `dev` as often as you like — CI
runs on every branch and every pull request, so the build, the tests and all seventeen checks still
gate each push, and nothing is released. A release is then a deliberate act: merge `dev` into
`main`, and semantic-release cuts one release covering everything that accumulated.

Releasing on every push cuts a version per push — four in a day, each a patch on the one before.
A release is what a player downloads; it should be worth downloading.

**Merge, do not squash.** semantic-release reads the individual commits to build the changelog, so
a squash collapses a fortnight of features and fixes into one line and loses the notes. A merge
commit keeps every subject, and the release notes list them all.

**Versioning is automatic and commit messages are the input.** semantic-release runs on every
push to `main`, reads the Conventional Commits since the last tag, and cuts the release: version,
`CHANGELOG.md`, the `<Version>` in the csproj (via `tools/set-version.sh`), the tag and the
GitHub Release.

**The changelog is written for players, not for this repository.** Those same notes are what
SpaceDock shows, so only `feat`, `fix`, `perf` and `build` appear in it; a refactor or a docs
change tells a player nothing and is in `git log` for anyone who wants it. That is a reason to
label a commit by what a player can observe rather than by which files it touched. **Never edit a
version by hand** — it will be overwritten. `feat`/`fix`/`perf`/`build`/`revert` all cut a
**patch**, `!` or a `BREAKING CHANGE:` footer a major; `docs`, `chore`, `ci`, `test`, `style` and
`refactor` cut no release. A commit that does not parse is treated as no release, so a stray `wip`
cannot publish anything.

That workflow is in two jobs: the first decides the version and creates the release, the second
builds the archive, attaches it, and **publishes it to SpaceDock**. Both hosted. The release commit
carries `[skip ci]` so it does not retrigger CI.

SpaceDock needs three settings, and the step skips with a notice if any is missing — a fork cannot
have them, and the GitHub release is the real artefact either way:

| | |
| --- | --- |
| `SPACEDOCK_MOD_ID` | repository **variable** — the number in the mod's SpaceDock URL |
| `SPACEDOCK_USERNAME` | repository **variable** |
| `SPACEDOCK_PASSWORD` | repository **secret** |

The KSA version it claims compatibility with comes from `ksa-assemblies.lock`, so what SpaceDock
advertises cannot drift from the build CI actually enforced. `SPACEDOCK_GAME_VERSION` overrides it
for the case where SpaceDock does not list that build yet. It notifies followers on every upload.

Three things that will bite:

- **Branch protection on `main`** blocks the release commit unless the token can bypass it.
- **A shallow checkout** makes semantic-release believe every push is a first release — hence
  `fetch-depth: 0`.
- **A release with no prior tag is 1.0.0.** semantic-release reads "no tags" as "no releases" and
  the `<Version>` in the csproj has no bearing on it. Tags anchor everything, which is also how a
  minor is cut here — see the Committing section. Promotion to 1.0.0 is a deliberate
  `git tag -a v1.0.0`; a `BREAKING CHANGE:` footer would also do it, which is worth avoiding
  until it is meant.

**Releases** are `./tools/package.sh`, locally or from the release workflow — the archive is
identical either way. `./tools/publish-release.sh` does both halves from a machine with KSA:
build, then attach to the release semantic-release created. That is the fallback for when the
assemblies secret is unavailable, and it refuses rather than guessing if the tag or the release
does not exist.

**One archive covers Windows and Linux.** The mod is a portable `net10.0` assembly: no
`RuntimeIdentifier`, no P/Invoke, no Windows-only API. There is nothing to build twice, and
adding a per-platform build would produce two identical files. What *does* differ by platform:

- **Case sensitivity.** A mismatched filename loads on Windows and fails on Linux. The hosted CI
  job runs `validate-parts.py --offline` for exactly this — on Linux, where case matters — and
  it compares against the real directory listing rather than trusting `is_file()`.
- **Where KSA's user directory is.** `Log.cs` tries Documents, `XDG_DATA_HOME`, `~/.local/share`
  and `~/.config` in turn, only ever using one that already exists, and falls back to temp.
  `deploy.sh` searches the same set.
- **The loader.** StarMap ships `StarMap.exe` plus a portable `StarMap.dll`, so `dotnet
  StarMap.dll` is the Linux equivalent. *Not verified here* — this machine is WSL and runs the
  Windows build. Release builds carry **no debug symbols** (`DebugType=none` in the csproj)
and the log starts at `INFO` rather than `DEBUG`, because the developer detail runs to hundreds
of lines per engagement. `Log.Threshold` and the panel's **Verbose log** tick box put it back
without needing a different build, which is what you want from a bug report. The packaging
script refuses to ship a `.pdb` or any DLL that is not ours — that is the last gate before an
archive leaves the machine.

## Design decisions worth not re-litigating

**Rounds are simulated by this mod, not by KSA's vehicle physics.** They are drawn with
`GizmosRenderer` rather than being real part-based vehicles. This was deliberate: spawning real
vehicles needs a part template (GLB model, XML schema, and registering a module type into
engine-internal update lists that would require Harmony patching), and steering them means
writing kinematics from a worker-thread update. Self-simulating gives sub-frame accuracy for
free and cannot corrupt a save. The cost is that rounds look like tracer spheres with trails.
Swapping in real part-based missiles later means replacing `Visuals.DrawRounds` and the
integration in `Interceptor`, nothing else.

**Everything is computed in the ecliptic (`Ecl`) frame** and converted to camera-relative `Ego`
only at draw time. `Ego` is a pure translation of `Ecl`, so this is exact — see the notes.

**Threat classification uses closest point of approach, not closing speed.** That is what makes
targets *passing by* engageable and not just ones flying straight at the launcher.

**`Sim/` must stay free of KSA types**, and this enforces itself — see the Layout note. When
something KSA-facing turns out to have testable maths inside it, move the maths into `Sim/`
rather than leaving it unverifiable; `FireGeometry` is `LauncherPart`'s launch geometry moved out
for exactly that reason, and launch angle is only testable once it is there.

**And a `Sim/` entry point differences its own inputs.** It takes both frame-carrying terms —
`(shooterPos, shooterVel, targetPos, targetVel)` — never a `relativeVelocity` computed in `Ksa/`,
because that moves the subtraction carrying the whole frame contract to a call site no test
reaches. Test it for *invariance*: add the same velocity to both inputs, assert the answer does
not move. `docs/FRAMES-AND-EPOCHS.md` has why, and `BallisticLead` is where it bites hardest.

**Weapon performance lives on profiles, not in `Config`.** `Config` is the *player's* settings:
armed, auto-engage, what to draw. Range, guidance, fuse and launcher geometry belong to a
weapon system and vary per system, so they sit on `SensorProfile`, `MunitionProfile` and
`LauncherProfile`. The panel edits the profiles of whichever system it is showing, so live
tuning still works — it just tunes that system rather than the whole mod.

**The bomb sight is flown, not solved.** `Sim/BombSight.cs` steps the *same* `Slug` the bomb will
be, through the same gravity, the same air density and the same ground — so the ring sits wherever
the round will actually go, including anything about the flight model that is wrong. A closed form
exists only without drag, and this round's drag grows as it falls into thicker air; a sight derived
from a tidier model than the round obeys is a sight that lies at the moment it matters. It is
re-solved a few times a second rather than per frame, and the integration step is a *separate*
number from the refresh interval — sharing them puts 55 m of fall between terrain samples, and
the ring then hops between two places.

**The ballistic computer solves where to stop and flies everything else.**
`docs/ICBM-GUIDANCE.md` is the whole account; four things there are worth not re-deciding.

**Velocity-to-be-gained, re-solved against the vehicle's actual state.** The shot is exact at the
instant the difference reaches zero, whatever happened on the way — a wrong pitch programme, an
engine that underperforms, drag nobody modelled. A bad ascent costs propellant, not accuracy, which
is why there is no stored trajectory and no separate launch solver: the same call answers on the pad
and one second before cutoff.

**Cutting off is a timing problem, not a threshold one.** An engine stops on a frame boundary, and a
light upper stage at ten gravities changes its velocity by more in one frame than any sensible
tolerance — so waiting for the velocity still to gain to fall under a fixed number waits for
something that cannot happen, and the burn hunts until the stage is dry. The program is therefore
**stepped every frame and solved a few times a second**: the solve sets a countdown and the frame
runs it down. Same split as everywhere else in this mod between what is cheap and what is exact.

**Flight time is the parameter, and the arrival gets latched.** Parameterising by time is what
collapses a rotating target into a single solve. But the *cheapest* arc from the vehicle's current
state converges on the arc it is already flying, so a loft factor applied to that every cycle walks
the answer outward and the shot chases a trajectory running away from it — 162 km out at a 1.4 loft.
The cheapest time is carried out of the solver separately from the one flown, and the arrival time
is nailed down when closed-loop guidance takes over.

**Arrival angle is asked for with a bound, not with a nudge.** It is the dominant precision lever —
7.5° to 20° is eight times the velocity sensitivity and sixty-two times the immunity to a
drag-model error, which is the one term no correction loop can remove — and it used to be an output
of a delta-v minimisation. `Loft` is not a control for it and from orbit will *invert* it: raising
loft makes leaving now dearer too, so `BurnWindow` re-optimises the departure and defers to a cheap
flat window — 33.9° at loft 1.0 becoming **6.2°** at loft 1.8 on a 556 km shot.

So `IcbmConfig.MinArrivalAngleDeg` constrains the search instead: an arc arriving shallower costs
infinity, and the window search's "earliest affordable departure" becomes "earliest affordable
*satisfying* departure" with nothing else changed. **A predicate is idempotent where a multiplier is
not**, which is why the constrained flight time can be seeded straight back in and a lofted one
cannot. Where the two disagree the floor wins, nothing satisfying it is `IcbmReach.TooShallow`
rather than a silent graze, and it is **off at zero**, which is the default.
`docs/ARRIVAL-ANGLE.md` is the whole account. **Unflown.**

**Do not compete with KSA's own warp, and do not ask the world for more than the rounds do.**
Coming out of a warp-to-a-time is where this bites: KSA is still travelling when it reaches its
target, so a hold beginning there tries to brake the world from a thousand times in one frame, and
the first speed the policy computes from a step that size is nearly zero — measured as
`1213.07x -> 1.00x`, `held at 0.0x`, `(paused)`, then the burn abandoned for the world not running
slow enough. So the mod asks for nothing while an auto-warp runs and **ends the warp itself** when
the window is close, which resets the speed to something the hold can work from. And
`IcbmProgram.MaxFaithfulStep` is no tighter than a round's: the extra accuracy is a few hundred
metres and the cost is the whole shot.

**Pointing needs two directions, and the second one has a singularity.** KSA's aiming frame clocks
the roll to the planet, which has no answer when the nose points at it or away from it — and
*reverses* there rather than merely failing, because the side the planet is on has changed. A
vertical rise sits on it for its whole duration, so a roll re-derived each frame spins the vehicle
on its own axis. `Sim/AimFrame.cs` carries the reference forward instead: continuous by
construction, because it never asks again. Third time this mod has met this shape, after
`Vec.PerpendicularTo` and `OpticGeometry`.

**Attitude is driven for every phase that is doing something**, not only while an engine is lit — a
hold is an hour of being pointed at a burn, and after cutoff the bus keeps the line the warheads
leave along.

**The burn is exact when it ends, and two things then move the vehicle off it.** The frame the
cutoff landed on, and the decoupler that drops the spent stack — 7 kN against a six-tonne bus is
about 1.1 m/s, arriving *after* the last thing that could compensate for it, and measured in flight
as **3.5 km** between the one warhead that left before the split and the five that left after.
Nothing in the guidance can answer it, because the guidance is over. `Sim/BusTrim.cs` is the same
loop against a different actuator — re-solve to the **committed** arrival, thrust along the
difference on the attitude-control jets, stop when less than one frame of firing is left.

Three things about it are the decisions, and each cost a wrong version first. It resolves onto the
**vehicle's own control axes** rather than turning to point at the answer, because by the coast the
attitude *is* the release line and the dominant component is axial anyway — a decoupler pushes along
the joint. It fires **one direction at a time**, because the stop threshold is half a frame of a
thrust that is only measurable along the direction being fired, and a bus's lateral authority is
whatever its nozzle layout happened to give it. And it is a **precondition
of being ready to deploy** rather than a step inside the release sequence, which is what stops one
warhead leaving on the attached stack's solution and the rest on the shoved bus's.

**A nozzle serves a translation if its thrust lies within 60° of a control axis, and that is the
whole rule.** `ThrusterController.ComputeControlMap` thresholds the thrust direction at **0.5** per
axis — nothing about lever arms or the layout as a whole — so a bell clocked 45° between two axes
carries a flag for both. Two consequences neither obvious nor derivable from the part: the shipped
bus's *roll* jets, canted 29° radially inward, cleared that threshold and gave it lateral authority
nobody designed; and one radial bell per cluster is enough to serve every lateral direction, because
each is 45° between two of them. The torque flags are a separate threshold at 0.1 on a **normalized**
efficiency, `dot(thrust, unit(axis × r))`, so a jet with any lever arm at all is flagged and the
length of it changes nothing.

**The bus's declared mass sits at X = 0, and no nozzle station can straddle it.**
`<SolidSphereMass>` names no `<LocationAsmb>` and `AsmbTransformTemplate` defaults it to zero, so
KSA puts all 6,300 kg on the mounting face while the geometry's own centre is near X ≈ 1.4. Every
pad is at X 0.15–0.45, all on one side of it, so a translation jet's pitch/yaw torque cannot be
cancelled by pairing — which is why there is **one** radial jet per cluster rather than two. Warheads
leaving do not move it either: rounds are self-simulated, so nothing is removed from the vehicle.

**It joins the flight wherever the vehicle already is, and "when to burn" is its own question.**
The phase machine is entered by looking at the vehicle rather than by assuming a pad: low and still
means the launch sequence, dynamic pressure means an ascent already under way, above the air means
the only question left is *when*. That last one is `Sim/BurnWindow.cs`, which searches **departure
time** as well as flight time by coasting the state forward through `Sim/Kepler.cs`. Without it a
target the vehicle has just passed over has no affordable arc — forward the short way means
reversing the whole orbital velocity, the long way round goes through the planet — so a computer
that can only leave *now* takes an eleven-kilometre-a-second answer, burns the tank dry and lands on
the wrong continent. The same shot costs two hundred metres a second most of a revolution later.
Waiting is a fallback rather than an optimisation: it has to save kilometres a second, because the
thing being traded away is arriving — and the earliest window within a margin of the best wins,
because the cheapest departure in a day is not the one to want.

**The horizon is a day, and the planet is why.** A revolution turns the ground twenty-two degrees,
so within one orbit a target off the track stays off it and the only answer available is a plane
change costing kilometres a second; sixteen revolutions brings it under the track and the same shot
costs a deorbit. Searching a day properly would be thousands of solves, so the first revolution is
costed at every step — **phasing is invisible to geometry**, and a target just passed over is dead
in the plane and still unreachable — while the rest is scanned on the plane angle alone and only the
best few moments are solved for real. None of it fixes an inclination: a latitude the orbit never
reaches is not reached by waiting, which is what `Sim/OrbitPlane.cs` exists to say.

**A solve that can fail on some geometry needs an answer for that geometry, not a `false`.** A
latched arrival pins the transfer angle, and a pinned angle can land on the one case Lambert cannot
answer — two points opposite about the centre. Returning failure leaves the caller holding the
previous cycle's answer, which is **flying the burn open loop** with the velocity still to gain
frozen where it was. Measured at 9,904 km against 12 km with the fallback in place. The tell is a
readout that stops moving, which looks like stability.

**A guided burn holds timewarp down, for the same reason a round in the air does.** The engine
stops on a frame boundary, so the velocity left ungained is `accel x step x throttle` — 1.5 km of
miss at a one-second step, and *kilometres per second* at the 170-second steps high warp hands out.
So `IcbmProgram.MaxFaithfulStep` registers a burning computer with `WarpPolicy` alongside the
rounds, and a burn the world outran is **abandoned and reported** rather than flown into the wrong
ocean. The coast afterwards is not held: a coast is not being integrated by anything, and once the
warheads are away they are rounds, which the existing machinery already covers.

**The transfer solver is exact, and exact for the wrong thing.** It puts the arc through a *point*;
a round stops where the **ground** is. On a shallow arrival the arc covers about twelve
kilometres of ground per kilometre of height, so a target four kilometres up lands tens of
kilometres from a solution that is otherwise perfect — 47.9 km, measured, from a near-orbital
burnout 2,580 km out. The trajectory is not wrong; the asking is. So the aim carries a bias driven
by the flown prediction, the prediction is flown **against the real height field** rather than the
mean sphere, and the miss is scored against the **target** rather than the biased aim — scoring a
correction against itself reports a perfect shot however far the rounds land.

**And the prediction has to fly the warhead, not the bus.** The guidance reasons about a vehicle
that cuts off above the atmosphere, so a vacuum predictor is right for everything it was built for
and wrong for the one thing that has to arrive. Path length through air goes as `1/sin(γ)`, which
makes a **grazing deorbit arrival the worst case for drag rather than the mildest**: at ~5° a Mk 21
keeps a quarter of its speed and lands **54.6 km** short of the vacuum arc, measured through both
models from one cutoff state. `ImpactPredictor.Drag` goes through `Medium.Drag` — the same call the
round makes, because a prediction modelling drag its own way is a second flight model to keep in
step with the first — and the step comes down only where there is density, so a coast pays nothing.

**And the observer has to be reading the same clock as the thing it is scored against.** Mid-burn
the prediction departs from the *cutoff* state, seconds in the future, so `ImpactPredictor` un-carries
its impact into the body-fixed frame of that instant — while the target is known in the frame of
*now*. Comparing them measures the planet's turn over the rest of the burn and calls it miss. It is
not a bias that can be tuned out: it shrinks to nothing as cutoff arrives, so the correction chases a
ruler moving at ~400 m/s against ground moving at 465. Headless at 2,000 km it put a shot needing
**no correction at all** 191.6 km wrong; flown, closing it took the warheads from 11.25 km to
**5.35 km**. The same trap reaches `TerrainRadiusAt`, which samples the height field in the wrong
orientation for the same reason.

That is also the shape of how it hid for so long: **a correction loop can only remove what its
observer can see.** The aim correction reads the prediction, so a drag-free prediction converged,
reported zero, and the warheads went on falling 59 km short in flight. The loop was right and the
instrument was blind, which reads from outside exactly like a working feature.

**And the miss is not a monotonic function of the aim, so a loop that stops the first time it stops
improving stops in the wrong place.** Measured at 7,645 km: a best of 3.34 km banked, then a
**five**-cycle patch out to 5.89 km, and beyond it 1.73 km at a bias 38 km further on — 1.15 km of
flown miss against 15.74 for a loop that gave up inside the patch. Stopping early is not the
conservative choice, because stopping is what makes `AimCorrection.IsSteady` true and *that* commits
the arrival: the aim it kept is then judged against a different trajectory, and the 3.34 km it
stopped for measures 15.86 km one cycle later. Waiting costs cycles and nothing else — the best aim
is kept and reverted to either way, and `MaxMetres` bounds where the aim can wander meanwhile. So
`WorseBeforeStopping` is patient, bounded above by `IcbmProgram.LatchArrivalWithinSeconds`, past
which the arrival commits whatever the aim is doing.

**The miss is one product, and there is no floor under it.** Flown from the same cutoff position
with the *exact* required velocity, the integrator lands on the target to under a metre — so the
whole error is `velocity still to gain at cutoff x dMiss/dV`, and each half is worth knowing.
The sensitivity belongs to the trajectory (274 m per m/s at 1,400 km, 4,700 at 8,500 km) and the
frame rate is given, so **the only lever is the throttle**: an engine stops on a frame boundary, the
last frame adds `accel x step x throttle`, and coming back to a few per cent for the last couple of
seconds divides the miss by the same fraction. It is written against the throttle the vehicle
*reports having* rather than the one commanded, so a stack that cannot throttle gets the error it
would have had rather than a wrong cutoff.

**And a residual several times what one frame adds is not a rounding, so the throttle cannot reach
it.** Freezing the steering leaves whatever is square to the frozen line in the residual for good,
and `HoldDirectionBelow` is a fixed five metres a second — about ten frames at full thrust, and
*seconds* of them once the ramp has taken the throttle down. `IcbmProgram.HoldDirectionFrames`
counts the same limit in frames of the burn actually happening, capped by the old constant so full
thrust behaves as before. Measured headlessly across 90 shots: mean residual 0.065 → 0.018 m/s, and
the share square to the thrust line 71% → 6%. **A constant step cannot see it** — the fault is
driven by the solve moving between frames, so `IcbmFlightRig.StepJitter` is the fourth thing the rig
had to stop being better than the game at.

**A crossing search that stops on the first sample past the boundary is biased, not merely
imprecise.** `ImpactPredictor` accepted the first point below the ground, so a tolerance expressed
as a *time step* left the answer metres deep — which at 7 km/s on a shallow arc is tens of metres
downrange, **always** downrange, and reads exactly like guidance error. On the Moon, where arcs are
shallower still, it was kilometres. `CrossingToleranceMetres` bisects on how deep the answer is
instead: the thing that actually matters, and the same number on every body.

**It is `Cci`, and everything the ascent gates on is dynamic pressure.** A half-hour flight in the
ecliptic carries 54 million kilometres of the planet's own travel; a body's spin axis is exactly
`+Z` in its own `Cci`, so there is no obliquity term. And gating the guidance handover and the
angle-of-attack limiter on pressure rather than density or altitude is what makes a launch from the
Moon work without anything knowing the Moon has no air — thin air at two kilometres a second is
still kilopascals.

**Attitude is written from a Harmony prefix, and nothing else in this mod is patched.** KSA
double-buffers a vehicle's flight computer: `ApplyVehicleSolvers` writes the worker's result over
it, `ExecuteNextVehicleSolvers` snapshots it for the next worker, and *then* the GUI pass runs — so
a command written from any StarMap hook is not in the snapshot and is overwritten before anything
reads it. Measured over thousands of frames as `before Manual/None -> after Auto/Custom`, every one,
with the engine's own error angles at zero. `Vehicle.PrepareWorker` is the only thing inside that
window a mod can reach, which is why `Ksa/AttitudeHook.cs` prefixes it and why cairn5's
PoweredGuidance does the same.

**The rule this bends is about *private* methods, and the target is `public virtual`.**
`AttitudeHook.PinTheSignature` is never called and exists only to put the patched method in this
assembly's metadata, so `docs/KSA-API-SURFACE.md` tracks it and a KSA signature change is a build
error rather than a rocket that quietly stops steering. Harmony ships with StarMap, so this asks a
player to install nothing. **Nothing in the prefix may throw** — it runs inside the engine's frame
loop, where an exception is the game rather than a log line.

**`Ksa/VehicleCommand.cs` is the only place this mod flies somebody else's rocket**, and every write
in it is one the game already makes for itself: the flight computer's `Custom` attitude target,
which is how `PhysicsBubble` points a manoeuvring unit, and `Vehicle.ProcessInput`, which is what
the keyboard calls. Nothing is patched. The aiming rotation comes from KSA's own `GetTgt2Cci` rather
than being built here, because building one means guessing which body axis is the nose — and getting
that wrong is a vehicle holding a perfectly steady attitude ninety degrees from the one asked for.

**One computer per craft, not per launcher.** A craft can carry two rails and shoot them at
different things; it has exactly one trajectory, so a second computer aboard is a second autopilot
fighting the first for the same engines. That is the one place the ICBM roster and `WeaponSystems`
disagree about what to key on.

**A shot short of the propellant is flown and reported, not refused.** KSA reports the *running
stage's* engines, so how much a stack has left is only knowable one stage at a time — a launch gate
built on that number turns away every multi-stage rocket in the game. It flies, falls short, says by
how much, and holds its warheads: releasing on a trajectory known to fall short scatters them over
whatever is under the short fall.

**A round flies in the ground's frame, not the launcher's.** `KsaWorld.GroundVelocityAt` is the
parent body's own motion plus its spin at that radius, and it is what a round's airspeed, its drag
and the direction it points are all measured against. For a launcher standing still on the ground
that is the same number as the launching craft's velocity; a store released from something
**moving** is what separates the two. A round still *inherits* the craft's velocity at launch; it
does not measure its airspeed against it.

**Rounds are drawn as real subparts, anchored to the tube they left.** Twelve `Missile`
subparts, scaled to nothing until fired, with their transform written each frame. Two rules:

- **Anchor to the tube, add only the travel *since* launch.** `OffsetFromPlatform` is measured
  from the platform's *analytic* orbit position; a subpart is placed against the vehicle's
  *physics* origin. Those differ by metres on a landed craft — the same distinction
  `DrawAnchor` exists to preserve — so the absolute offset puts every round inside the
  search radar. `Interceptor.TravelSinceLaunch` is a difference between two positions in one
  frame, so it carries none of that.
- **Orient off `VelocityLocal`, never `VelocityEcl`.** The latter carries ~29.8 km/s of ecliptic
  motion and points every round the same way.

`RoundBodyAnchorTests` and `FireGeometryTests` hold both.

**A scenario cannot place a craft through the *system XML*, but the mod can place one itself.**
`LoadVehicleFromLibrary` in a system XML resolves through `DefaultVehicleSaves`, whose
`SaveFolderPath` is **hardcoded** to `Content/Core/defaultvehicles` under the game install — not
per-mod, and not writable without elevation. That door is shut.

`VehicleSaves` is a different registry and is not: its `SaveFolderPath` is the user's own
`Documents/Vehicles`, which is exactly where `tools/install-testcraft.sh` writes, and
`Refresh()`/`AsSpan()`, `UncompressedVehicleSave.FromDirectory`, `VehicleSave.Load(Viewport)` and
`Vehicle.GetInitialKinematicStateForLocation` — the launch menu's own pad-placement maths — are all
public. `TestTarget` already walks most of that chain to spawn drones.

**Not built, and two things are unknown before it is:** whether
`InputEvents.CreateVehicleBuffer` wants the frame ordering it gets from `VehicleLaunchMenu`, and
whether a craft spawned past `CrewAssignmentWindow.FillSeats` is controllable — fine for a drone,
untested for a vehicle that has to fly. `tools/scenario.sh mirv` therefore automates everything
after the craft is on the pad, and getting it there is still the operator's.

**A system mounts to the craft carrying the launcher part, and stays there.** It does not
follow the player's control: a system that re-homed onto whichever craft was being flown would
move onto the target the moment the player took its seat, and the target could then not be shot
at, because the kill path refuses to destroy its own platform — 22 m hits register as misses.
`PinPlatform` is how `WeaponSystems` mounts each system on creation, and nothing moves it after:
`ResolvePlatform` returns early for a pinned platform, so without that every system would elect
the craft being flown and they would all pile onto it.

**A round's drawn offset is `PositionEcl − platformEcl`, measured *after* the step against the
platform sample from the *same* frame, with no extrapolation.** Write the update index as `k`,
the platform sample as `Q(k)` and the round's position after its step as `P(k)`. Measured by a
probe in the frame hook, where both are produced, over thousands of frames:

```
( P(k) − P(k−1) ) − ( Q(k) − Q(k−1) )  ==  localVelocity * dt(k)      violated on 2 frames
```

So `P(k)` and `Q(k)` advance in lockstep, and `P(k) − Q(k)` therefore changes by exactly the
round's own flight each frame. That is the entire requirement, and it is why this form cannot
jitter.

**The phase is the whole story, and it is not what it looks like.** The platform sample arriving
at update `k` has already moved by `v * dt(k)` — the step *that same* update is given, not the
previous one. That follows from what `Universe.GetLastSimStep()` means: at frame `k` it reports
the step the engine has just finished applying, which is precisely the interval the sample moved
across.

Three forms are possible. Two pair mismatched instants, and both leak the same term:

| | Symptom |
| --- | --- |
| `P(k−1) − Q(k)` — measured before the step | the round's motion at frame `k−1` against the platform's at frame `k` |
| `P(k) − (Q(k) + frameVel*dt)` — extrapolated | re-projects `Q` by a `dt` that has already changed |
| **`P(k) − Q(k)` — after the step, no extrapolation** | **correct; confirmed in game** |

Each of the first two differences to `local*dt − v*dstep`. At ~29.8 km/s a 1 ms wobble in the
step is 30 m, and changing simulation speed swings the step by ~17 ms, which is **500 m in a
single frame** — measured at 507.37 m. Run side by side in flight the two wrong forms agree to
0.6 m: they share a cause rather than being alternatives.

**The tests advance the platform *before* the update**, and that ordering is what makes them mean
anything. Advancing it after — `Q(k+1) − Q(k) == v*dt(k)` — is indistinguishable with a constant
step and only separates when the step changes: a suite built that way advances the platform by
exactly the `v*dt` it passes in, the error cancels, and both wrong forms pass.
`RoundOffsetStabilityTests` and `FrameRegressionTests` hold the correct ordering, and every
offset test fails against both wrong forms.

`OffsetPhaseTests` holds the measurement and varies the step the way changing simulation speed
does, which is the case a constant-step test cannot see.

**The draw anchor uses two different instants on purpose.** `DrawAnchor.Ego` is sampled this
frame; `DrawAnchor.Ecl` is the platform position the geometry was measured against, one update
earlier. The difference between them *is* the frame's ecliptic motion (~500 m at 60 fps), and
differencing against the older reference is what cancels it. **Collapsing them into one sample
puts the entire overlay beside the craft.** `DrawAnchorTests` fails if they are collapsed into
one — read `DrawAnchor.cs` before touching it.

**Fire control runs on simulated time, never on player time.** StarMap's frame hook hands you
`currentPlayerTime` and a player-time delta, and both are deliberately ignored. Player time is
wall-clock, which is wrong twice over: it keeps running while the game is **paused**, so the
radar accumulates dwell, matures a firing solution and launches into a frozen world; and it
ignores **timewarp**, so at 10× the world moves ten times further per frame than the rounds do
and tracking falls apart. Fire control reads `KsaWorld.SimTimeSeconds` differenced by
`Sim/SimClock.cs` instead, which is `Universe.GetElapsedTime()` plus `Universe.IsPaused()`.

`SimClock` classifies steps it cannot integrate. `Interceptor` subdivides internally but clamps
at 64 sub-steps, so beyond `Interceptor.MaxFaithfulStep` (0.32 s) a round at 700 m/s starts
stepping over its own fuse radius, and `SimClock.Classify` answers `Skipped`.

**Past that step the world is slowed rather than the round being lied to.** `Sim/WarpPolicy.cs`
holds timewarp down while anything is in the air and gives the player's speed back when it lands
— so the unsimulatable state is prevented instead of being chosen between. The limit is a *step*,
not a warp factor: it is `MaxFaithfulStep / frameTime`, about 19× at 60 fps and lower on a slow
frame, and the policy calibrates off the step it was just handed rather than assuming a frame
rate.

**It is a control loop against an actuator that answers late and is shared with the player**, and
three rules follow from that:

- **Never judge a request on a step that predates it.** The step arriving on the frame a write
  takes effect still measures the interval *before* it, so dividing by it again reduces on top of
  a reduction already in flight — 30× to 9.9× to 3.2×, oscillating for the whole salvo.
  `SettleSteps` waits it out; `AStaleStepDoesNotReduceTheSpeedTwice` pins it.
- **Stop competing.** The player's warp control and KSA's auto-warp write the same field, and
  trading writes frame by frame is a loop neither side wins. After `OverridesBeforeYielding` the
  mod stands down for the rest of the salvo and says so — it is the guest.
- **A request never observed is a refusal.** KSA rejects a speed change outright while auto-warp
  runs, which is indistinguishable from a slow write until `FramesAwaitingWrite` have passed.
  Then the salvo is abandoned: a lost salvo the player is told about beats the silent
  alternative, which is a **124 km closest approach against 15–20 m unwarped**.

A player who moves the speed while it is held has overridden the mod, so the held value is not
restored over the top of a deliberate choice. `Config.LimitWarpInFlight` turns the whole thing
off, and then rounds lag the world at warp.

The clamp remains and still discards time: the frame that overran cannot be un-run, and the
policy only takes effect from the next one. What it stops is the next thousand frames doing the
same thing silently.

**A target's own behaviour decides whether it is a target, and that is what emission is for.**
`SensorProfile.Emits` says a set transmits, which is what kind of set it is; `SystemConfig.RadarSilent`
says whether it is transmitting *now*, which is the operator's. `GuidanceMode.AntiRadiation` is the
only thing that reads either — every other weapon sees `TargetState.Emitting` default to true and
behaves exactly as it did before the field existed.

Silence is a trade rather than a free defence: a silent set cannot be homed on **and cannot see**,
so `Radar.Scan` returns early. And it only saves a site that also *moves* — a round already in the
air carries on to where the emission last came from.

**That memory is a position *and the velocity it was seen with*, replayed on the round's own
clock.** Never a bare ecliptic coordinate: the velocity carries the planet's ~29.8 km/s, so
replaying it keeps the remembered spot on the ground it belongs to, where storing the point alone
is left behind by ~30 km per second of flight. Same rule as the draw anchor and the round bodies.
`AntiRadiationTests.TheRememberedEmissionCarriesTheFramesEclipticMotion` fails against the bare
point — and fails by never detonating at all, not by a near miss.

**Kills are binary.** KSA exposes no partial-damage model, only
`Universe.DestroyVehicleFromEvent`. `LethalRadius` destroys; between lethal and `BlastRadius`
the mod logs a near miss and the target survives.

**A shell has to touch what it kills; a warhead does not.** That difference is the weapon's
implementation, not a profile field — `Slug` asks `Sim/IHullTest.cs` and `Interceptor` never
does, so a proximity-fused missile keeps bursting near an airframe, which is what it is for.
The hook is deliberately *not* on `IProjectile`, because putting it there is an invitation to
wire it into the missile.

Three rules hold that together:

- **The sphere only rejects; the hull decides.** A craft's `MeanRadius` is the half-diagonal of
  its bounding box — a number built for orbital clearance margins, standing ten metres clear of
  a rocket's skin. Used as a contact radius it destroys things the shell visibly missed, at a
  miss distance of 8 m. It stays as the broad phase because a sphere containing the mesh
  cannot produce a false negative, and because it is what stops a round at 1100 m/s tunnelling.
- **A hull test that cannot answer falls back to the sphere, never to "no hit".** A craft the
  engine will not resolve would otherwise become silently bulletproof, which is worse than
  firing early and far harder to notice.
- **A round names what it struck.** Fire control decides what to shoot at; it does not decide
  what a shell in the air passes through. Scoring a strike on a bystander against the *target's*
  lethal range destroys something the round never reached.

**A round can be shot down, and that needed a path of its own rather than a wider blast.** A round
is not a `Vehicle`, so none of the machinery above reaches one: `ContactCandidates` collects craft,
`SampleTarget` refused anything that was not a craft, and the kill path calls
`DestroyVehicleFromEvent`. The result was a Pantsir that tracked an incoming missile, laid the
guns on its ballistic lead, fired the whole way in and could not have hit it at any aim — a
dead-centre shell tested against nothing and passed through. `IProjectile.ShootDown` is the only
way one ends, and `RoundState.ShotDown` is deliberately not `Detonated`: an intercepted round's
warhead never fired, and reading the two as one lets a missile splash the thing it was intercepted
over.

Three things follow, and the first is the one that bites:

- **The airborne sample is taken once for every system, before any of them steps.** A round is
  advanced by its own launcher's update, so a live reference is start-of-step or end-of-step
  depending on roster order — metres of closing motion, across a shell's fuse radius, decided by a
  dictionary's iteration order. `docs/FRAMES-AND-EPOCHS.md` has the rule.
- **A shell reaches a round only as its *designated* target, never as a bystander.**
  `ContactCandidates` is still craft-only on purpose: it is walked per round per sub-step, and a
  CIWS burst is 150 shells, so adding every round in the air to it multiplies that loop by the size
  of the salvo. The burst is aimed at the missile, which is the case that matters; the cost of the
  general one has never been measured, and CLAUDE.md's rule about unmeasured per-frame costs
  applies.
- **A missile intercepts by blast, a shell by touching** — the same split as everywhere else, and
  it falls out for free: the splash sweep now runs over rounds in the air as well as craft.

**A fired round does not belong to its launcher any more, so destroying the launcher does not
un-fire it.** A seeker head is aboard the round, and an anti-radiation round already carries the
emission it remembers — both are built to outlive what they were fired at, so outliving the
*shooter* costs them nothing. `WeaponSystem.GoLoose` hands such a system's rounds to the body they
are flying over and `WeaponSystems` keeps it, running nothing but `UpdateLoose`, until the last one
lands. `MunitionProfile.NeedsUplink` is the one thing that decides who survives: a command-link
round carries no seeker at all, so it is cut loose and coasts, which is what a command-link round
*is*. The guard for that is `Platform is null → no target`; writing it the other way round —
`&& Platform is not null` — skips the uplink check on a destroyed launcher and steers the round on
with nothing behind it.

Three rules hold it together:

- **The anchor becomes the parent body, and every offset moves at once.** `IProjectile.Reanchor`
  shifts the current offset, the launch offset and every trail point together. Only
  `OffsetFromPlatform` self-corrects on the next step, so a partial re-anchor looks right and draws
  the trail to where the launcher used to be — a planet's radius away. `ProjectileContractTests`
  holds it for every projectile, including that `TravelSinceLaunch` does not notice.
- **The roster holds a `Celestial` and a captured name, never the dead `Vehicle`.** That is this
  file's own rule about not keeping a destroyed craft reachable, and it is what bounds the
  lifetime to one flight rather than the session. The name carries the team as well as the label,
  so allegiance outlives the craft too.
- **A loose system runs no fire control.** No scan, no lay, no trigger — and that is a cost
  decision as much as a correctness one: the filter that stops a system shooting its own salvo
  walks every round in the world against every round it owns, which for a system whose rounds are
  *all* it has is the whole airborne list squared.

**A loose round keeps its plume and its tracer, and loses its body.** The body is a subpart of the
launching craft, so when that craft is destroyed there is nothing left to write a transform to —
that one is a KSA limit rather than a choice. The effects are not: every emitter this mod starts
sets `Context.Astronomical` and leaves `Context.Vehicle` null, so a plume has always hung on the
*body* rather than on the craft, and only the position lookup went through the launcher's part
tree. `IEffectSource.EffectBody` and `TryRoundEffectEcl` are that split made explicit — the drawn
body's position while there is one, and the round's own against its anchor once there is not, which
is exact rather than approximate precisely because there is no part to disagree with it.

So a shell keeps its tracer for its whole flight and a missile keeps its flame while the motor
burns. **A missile that has finished boosting is invisible**, having neither. The motor sound and
the diagnostic gizmo overlay are also not carried over, both because they convert through a
`Vehicle` to get camera-relative. `docs/CODE-HEALTH.md` has what closing those would take.

`Ksa/HullTest.cs` needs no camera: `Vehicle.GetMatrixAsmb2Ego` takes the frame origin as an
argument, so passing the round-relative separation puts the whole per-triangle cast in a
metres-scale frame centred on the round. What it is fed is the *analytic* position while the mesh
is drawn at the *physics* one; the verbose world dump prints that gap per craft, because it is
noise against a 22 m trigger and the entire error budget against a hull.

**A warhead is one number: `MunitionProfile.ChargeKg`.** Lethal radius, blast radius and the size
of the fireball are all read off it in `Sim/Warhead.cs`, as the **cube root** — Hopkinson–Cranz,
`R = Z · W^(1/3)`. Doubling a warhead multiplies its reach by 1.26, not by 2, which is the one
thing about explosives worth encoding rather than leaving to whoever types the next profile. Three
free radii could also describe a round whose lethal radius exceeds its blast radius;
`WarheadTests` pins that it cannot. The scaled distances are calibrated to the 57E6's flown
numbers: 20 kg → 20 m lethal, 60 m blast.

The *drawn* size has a floor and the radii do not. A 0.16 kg cannon shell scales to 0.2 by the
same law, which draws 5 cm particles — proportionate and invisible at any range anyone watches
from, which is the same as no effect at all. `Warhead.MinimumEffectScale` applies to decoration
only.

**The launcher ships its own art, and the asset XML lives at the mod root.** Instancing Core's
meshes by Id and shipping nothing works, and is the right answer for a part that can be assembled
from Core's kit; a Pantsir cannot be. The mod carries `Meshes/KSArmory_MeshAtlas.glb` and three
PNGs, declared with `<MeshAtlas>` and `<PbrMaterial>` exactly as Core does.

The XML sits at `src/KSArmory/*.xml` rather than in an `Assets/` subfolder **on purpose**.
`<MeshAtlas Path="Meshes/…">` is relative, and it is not documented whether it resolves against
the mod root or against the XML's own directory. With the XML at the root those are the same
directory, so the question never has to be answered. Moving it into a subfolder reopens a
silent-failure mode.

`Textures` are **PNG, not `.ktx2`** — KSA loads both, and `CharacterAssets.xml` mixes them in
one material. No `toktx` needed.

Run `./tools/validate-parts.py` after touching any of it: a bad Id or path is a *silent*
in-game failure. It also checks mesh Ids against the atlas and texture paths against disk.

**The part is inert; the behaviour is in C#.** KSA sees structure with mass and a collider.
`LauncherPart.Find` looks for it on the vehicle and the system mounts there. This sidesteps
registering a custom module type into the engine's internal update lists, which is not
reachable without patching.

**Launch and slew geometry live in the Blender script, not in the C#.**
`tools/model/pantsir.py` places the containers and writes `muzzles.json`;
the `LauncherProfile` in `Sim/Arsenal.cs` is pasted from what it prints.
`validate-parts.py` **fails if any of them disagree**, because geometry duplicated across a
boundary drifts. Change the pods, rerun `tools/model/build.sh`, paste the block. The tube count
is `LauncherProfile.Tubes.Length`, so it follows the block you paste.

**A system will not fire while its launcher is slewing.** `WeaponSystem.IsLaid` requires
both axes on target for `TurretSettleSeconds` first. Without that gate it launches the instant it
has a lock, out of tubes still pointing somewhere else; guidance recovers and the intercepts
still land, so nothing but watching it shows the difference.

**A launcher with nothing to aim and one that cannot aim are different, and `FireGate` keeps them
apart.** A profile declaring no training gear is always laid, so fire control cannot deadlock on
a launcher that will never move. A profile that declares gear whose transform the engine then
refuses is frozen wherever it stopped, and holds fire — treating that as laid ejects rounds along
a stale tube transform, which guidance recovers from well enough that nothing but the drawn
facing line shows it happened.

**Drive failures latch per assembly, not for the whole launcher.** `DriveStatus` carries one bit
per `DriveChannel`, so a refused search-array spin — cosmetic — does not freeze the traverse,
the pods and the cannon with it. `Reset()` clears the latches, because they record what one
vehicle's part tree refused and a new platform deserves a fresh assessment.

**Being laid is asked per weapon, for the same reason.** The cannon and the pods share only the
traverse, so `GunsAreLaid` reads `GunAimingAccepted` and the guns' own subpart while `IsLaid`
reads the pods'. Pointing both at one flag silences a working cannon whenever a pod elevation is
refused — or whenever the pods marker resolves to nothing, which needs no engine refusal at all.

**Mouse aim points the launcher, it does not fire it.** `Config.MouseAim` sends the turret and
the optical head at whatever the cursor is over, ahead of the radar *and* ahead of the tracking
switch — with it on the operator is the sensor, so needing to enable radar tracking first would be
surprising. Auto-engage still decides when to shoot, and `Aiming` counts mouse aim so `IsLaid`
still makes the drives settle: without that, rounds leave along a tube that is still swinging.

The conversion is the part worth being careful with, and it is **three** corrections rather than
one. `Camera.ScreenToEgoRay` divides by *its own* `FramebufferSize` while ImGui reports the cursor
across every window, so the cursor has to be offset into the viewport **and scaled from viewport
pixels into framebuffer pixels** — a render or display scale makes those different sizes, and
getting only the offset right leaves an error that is zero at the top-left corner and grows across
the screen. `Sim/CursorAim.TryToFramebuffer` does both and is tested against an offset viewport
rendered at twice its size.

The third is that **a cursor gives a ray, and a drive needs a bearing from the mount.** They are
not the same thing: the camera stands well away from the launcher, so its direction and the
launcher's coincide only for something infinitely far away. Against sky they agree; against
ground a few tens of metres off they disagree by tens of degrees, which reads as a turret that
follows the cursor perfectly above the horizon and points somewhere else entirely below it. So
`KsaWorld.TryCursorAimEcl` takes a mount, resolves the ray to a *point* against the terrain, and
`Sim/CursorAim.TryAimFromMount` does the subtraction. **There is deliberately no direction-only
form of it** — the two are indistinguishable wherever anyone tests them first, which is at the
sky.

The ray comes back in Ego, and a *direction* is identical in Ecl because the two differ by a
translation, so nothing converts it. An **origin** is not: `KsaWorld.TryCursorRayEcl` takes both
off one camera, because pairing a direction from the viewport under the pointer with a position
from `GetMainCamera()` puts the ray a viewport away from the cursor.

The overlay itself — search volume, tracks, tracers, drive facing — is diagnostic and off by
default (`Config.DrawOverlays`, under **Settings → Display → World overlay**). Round *bodies* are real
subparts and are unaffected.

**The optical head drives the main view, because it is the only one that draws a planet.** A
secondary viewport renders a starfield over a featureless grey ball — the planet, lighting, ocean
and atmosphere passes all run only for the frame viewport, which is KSA's and is recorded in
`docs/BLOCKED-ON-KSA.md`. So `Ksa/SightCamera.cs` borrows the player's view instead, and the
secondary path stays as the option for watching a site while flying something else.

**The sight magnifies by rewriting the field of view every frame, and it has to.** The player's own
zoom keys route through `Camera.ChangeFieldOfView`, which clamps to 15°–120°, so one keypress
throws away anything narrower and says nothing about having done so; only rewriting puts it back.
`Camera.SetFieldOfView` is the unclamped one and takes **degrees**, while `GetFieldOfView` answers
in **radians** — a factor of 57.3 between the two directions, which is why both are wrapped in
`KsaWorld` rather than called anywhere else. Unclamped also means unguarded:
`CreatePerspectiveFieldOfViewReverseZ` *throws* for a field of zero or more than half a turn, out
of the frame hook, so `SightZoom.MinFovDeg` is a crash guard rather than a preference.

**And a magnification, never an angle.** `OpticConfig.Magnification` is a factor on whatever
field the player already had, so the same setting is the same instrument to two people with
different preferences. The relation is optical — `tan(fov/2) = tan(base/2) / m` — so halving the
angle is 2.06×, not 2×, and the gap grows without bound as the field narrows: what a linear rule
would call 16× is 20.7×. `MainView` carries the field it borrowed for the same reason it carries
the follow: handing back everything except the zoom leaves the player at 3° with no control that
reaches it, because their own keys clamp at 15° and cannot widen past it.

**Two reticules, because one ring can only be laid on one solution.** The turret points at the
cannon's ballistic lead whenever `FireGate.GunsHaveTheEngagement`, and at the target otherwise, so
the pipper and the target bracket separate exactly when the lead matters and the line between them
is the lead being taken. `Ksa/Sight.cs` reads where the ring was actually sent back off fire
control rather than solving again: a second solve takes the target's position from a later instant
and paints a pipper the turret was never sent to. The pipper's *size* is what the shell covers at
that range, off `Warhead.LethalRadius`, so the ring closing on the bracket is the shot coming
together.

**A borrower that outranks another inherits its picture, not just its claim.** The chase outranks
the sight and the sight *yields* rather than releasing, so a transition begun at 16× would be flown
down a three-degree straw. The general rule: **what one borrower changed about the view is part of
what the next one inherits**, and the field of view is the first thing that is neither the pose nor
the follow.

So the field is **part of driving the view rather than something set beside it**.
`KsaWorld.TryLookFromMainViewport` requires it and `IViewPose.TryPose` answers it, both without a
default — a borrower with no opinion has to state the field it wants anyway, which is what makes
inheriting one impossible rather than merely unlikely. The only writes outside that path are
restores. `ChaseCamera.Field` is where "the player's own" is resolved: the sight's base while it
holds underneath, otherwise what the view was showing when the chase took it.

**The camera's aim is resolved inside the engine's frame pass, not written a frame early.** The
mod's whole update and draw is a postfix on `OnDrawUiViewports`, which runs *after* the viewport
pass that builds the frame's matrices — so a pose written there is consumed on the **next** frame,
and the scene is drawn along a direction solved against an older world. That gap is one frame of
the target's angular motion **times the simulation speed**: invisible paused, a couple of pixels at
1×, and a third of the picture at 16× magnification under warp. `Ksa/LevelHorizonController.cs` is
the only mod code that runs inside that pass, so `IViewPose` asks for the pose again there. A
refusal leaves the written fields alone, and nothing on that path may throw — it is inside the
engine's loop.

**And a sight is aimed from the sight, not from the hull.** `OpticalHead.AimPartFrame` measures
the head's bearing from the head's own pivot, because a command measured from the part's origin
lays the head *parallel* to the right bearing and displaced off it — a fixed distance, so a
shrinking angle: a tenth of the picture at a few hundred metres and nothing at 9 km.
`WeaponSystem.AimOriginEcl` is the same correction for the tube drives and carries the full
reasoning.

**Everything the mod draws is a step behind the world it is drawn against, and leading the aim to
correct it made things worse.** The mod integrates from the GUI hook, a postfix on the engine's UI
pass; the scene is built in the viewport pass of the *next* frame. So the ball's transform, the
camera's aim and the pipper are placed against a target that has since moved on by one step —
**0.007° per unit of simulation speed**, measured through the sight on a 156 m/s crosser at 7.7 km.
Steady, and invisible below high magnification.

Carrying the aim one step forward at the drive's own last turn rate removes that term and costs far
more than it saves. That rate is a per-frame report, so the lead varies frame to frame and the
picture shakes: measured at **0.35° at 10× against a target crossing 0.0037°** — ninety times
further than the target actually moved, and noisy. A small steady offset reads as a slightly
off-centre picture; a large varying one reads as jitter, which is worse at every speed above 1×.

So the aim is **not** extrapolated. A lead taken from the *target's* angular rate rather than the
drive's own turn would be the principled version, and is not built. The probe in
`Ksa/SightCamera.cs` measured both numbers and stays: the next thing to arrive a frame late will
look exactly like this, and the fix that looks obvious is the one already tried.

**`OpticalHead.AimWhenDrawn` is the only way to read the aim, and that part stands.** The ball, the
camera and the pipper are three views of one direction; taking two of them from different instants
separates them on screen, which is worse than the lag either would have had alone.

**A pointing head needs its roll chosen, not inferred.** `OpticGeometry.Rotation` carries the
rest direction onto the aim and then rolls about the aim by a *signed* angle, so the ball's own up
stays as near the mount's normal as the aim allows. A shortest-arc rotation instead has no axis
looking dead astern — the aim is exactly opposite the rest direction, any perpendicular is equally
correct, and the one picked flips as the aim creeps past, snapping the whole picture through half
a turn. The roll is built about a *named* axis for the same reason at 180°: a shortest arc there
picks a perpendicular that tips the aim off target, which is a wrecked bearing rather than a roll.

**The head is its own part, and a weapon is not what gives a craft one.** `Ksa/OpticalHead.cs` is
crewed per director fitted rather than per weapons system, finds its own targets through its own
`SensorProfile`, and needs no weapon on the craft at all — so a hull with one director on it is an
observation post. That it cost nothing in the sight, the chase camera or the claim ladder is
`IOpticalHead` earning its place: the interface was written when the head *was* launcher gear, and
every consumer of it was already reading a role rather than a system.

**A launcher may still carry one, and the Pantsir does.** Its turret roof holds a director that is
a second `OpticProfile` on the launcher's own part Id, instancing the same two bodies the standalone
part uses. Nothing downstream can tell the difference: `OpticParts` finds it, `OpticalHead` crews
it, and the sight neither knows nor asks that this one sits on a weapon. What the *launcher* knows
is one subpart — `OpticBaseMarker` — which its traverse carries round with everything else that
rides it.

**A head reads where its base is; it never reconstructs it.** `Sim/OpticGeometry.MountFrame` is the
base's pose as the engine has it, and every question a head asks — where its pivot is, where the
eye sits, which way is up, how far down it may look — is asked in those terms. That is the whole
reason a director can ride a traverse without either side learning about the other, and it is what
a hinge, an arm or anything else not built yet gets for free. The alternative, handing the head its
host's angle, works for exactly the one kind of host somebody taught it about.

`MountFrame.Fixed` is a director bolted to a hull, and reduces all of it to the constants — so the
common case pays nothing and the fallback when a base cannot be resolved is the ordinary answer
rather than a guess. The ordering that makes it safe: the drive owning a mount writes it in
`WeaponSystem.Update`, which `KSArmoryMod` runs before any head's `Update` and before anything
draws.

**A head's *mechanism* is its own field, and there are two.** `OpticProfile.Gimbal` decides three
things nothing can infer from the geometry: which axis the travel is measured from, which roll the
mesh is given, and where the head parks. A mast head elevates over its mounting face and keeps its
own up near that face's normal. A **roll-nod** head — the LITENING pod — turns its whole nose about
the pod's centreline and nods the sight within that turning frame, which is what every targeting
pod of the class actually is: the roll axis carries its mass close to the axis of symmetry, so it
slews and settles far faster than an az-el head of the same mass, and a body of revolution keeps
one drag profile whichever way it looks.

The two are **one expression apart**, and that is the thing worth not rediscovering.
`OpticGeometry.Rotation` swings the rest direction onto the aim and then rolls about the aim to
bring the head's own up onto a reference; make that reference the far side of the mount's
*centreline* instead of its *normal*, and the result is exactly a roll about the centreline
followed by a tilt square to it. Two bodies, one rotation, no second solve —
`RollNodGimbalTests.TheWindowOnlyEverNodsWithinTheShell` is the whole contract, and it fails
against the mast head's reference.

**Its travel is one number, and the aperture is measured beside it rather than assumed.** 360° of
roll makes every bearing the same bearing, so what bounds a roll-nod head is the nod alone.
`MaxOffBoresightDeg` is the *gimbal's* stop; `import-litening.py` casts rays from the ball's pivot
to find where the shell actually blocks, so the binding one is known. For the LITENING they are
150° and 158°, so the mechanism stops first with 8° in hand — and the far side of the shroud clears
only 107°, which never binds because **the nod is never negative** and the roll is what puts the
open side on the target. That is why the importer clocks the shell: get it half a turn out and the
sight looks through the closed side.

**And the far end of the travel is a singularity rather than a stop.** Dead along the roll axis
there is no roll angle at all, and near it a target crossing the nose asks for unbounded roll rate
— the same singularity an alt-az telescope has at zenith. `KeyholeDeg` holds the command out of
that cone, which is also why a pod stows looking *out of its mounting face* rather than along the
host: `OpticGeometry.RestAim`, because the mast head's rest direction is the pod's keyhole.

**A rolling nose turns the scene in the focal plane, and the picture is derotated.** Half a turn of
roll and the image is upside down; every pod of the class counters it, optically with a K-mirror or
Pechan prism at half the roll rate, or — as Litening does, being digital end to end — in the video
processor. `OpticalHead.RollReferenceEcl` takes the *forward* side of the nod plane for a roll-nod
head instead of the head's own up, which is that counter-rotation: what the pod hangs from stays at
the top of the picture however far the nose has rolled. Its two singular directions are the keyhole
and dead astern, and the travel excludes both.

**The map is sampled, not borrowed, and that is what makes it a square of relief.** KSA exposes no
map view a mod can take, so `Ksa/TerrainMapScan.cs` asks the height field for a grid of its own —
which is also why there is no ground texture, no coastline and no colour on it:
`GetTerrainHeightFromDirCce` answers with heights and nothing else.

**Its cost is a grid, and the grid is cached.** One refresh is `Cells²` lookups — 4096 at the
default — and `SensorProfile.TerrainSamples` defaults to zero precisely because that per-frame cost
has never been measured. So the scan is paid on *movement*, on a slow frame count, or on the
Rescan button, and never per frame; the map is a picture of the ground, and the ground does not
move. `TerrainMapScan.LastScanMs` is logged so the number stops being a guess.

**Its frame is the body's, not the ecliptic's.** North is `Celestial.GetRotationAxisCce`, so a map
is not wrong by the body's obliquity — 23° on Earth. And every coordinate on it is a *difference*
against the anchor, which is what cancels the ecliptic motion both terms carry;
`TerrainMapTests.TheFrameCarriesNoEclipticMotion` is that rule, and it is the same one the draw
anchor and the round bodies obey. There is no map at the poles, where up and the axis are one line
and no bearing exists — `MapFrame.TryAt` returns null rather than picking a perpendicular.

**A cell the height field will not answer for is drawn as unknown, never as flat.** Reading zero
from an unreadable field would put a whole square at the mean sphere with nothing to say it had.

`ISightPicture` is what a weapon beside the head contributes to the picture — the arm state, the
ammo, the gun's pipper. `Sight.Draw` takes it as **optional**, so an unarmed craft still gets the
bracket, the reference and the zoom, which are about looking rather than shooting.

**Two things borrow that view, and the loser waits rather than tidying up.** `Sim/ViewClaim.cs` is
the ladder: the player reclaiming the view outranks everything, then the chase camera, then the
sight. The rung that is not obvious is **Yield** — a sight that is no longer wanted must *not*
restore while the chase is driving. Both keep their own recording of what the view was doing, and
they were made in order, so restoring the older one undoes a takeover that happened this frame and
leaves the chase holding a recording of the *sight* to hand back at the end. The player is then
returned to a borrowed pose that nothing is driving. `ViewClaimTests` fails against that shape.

**A view is taken back in two halves, and watching one of them is how a borrower keeps driving a
view that is no longer its own.** The camera-mode menu changes the **mode** and leaves the follow;
`[` and `]` do the opposite — `Universe.SeekNextVehicle` calls `SetFollow` and never touches
`CameraMode` — and so do the panel's **Go to** button and KSA's `FollowWreckage`. So
`ViewClaim.StillOurs` asks both, and a borrower that asked only about the mode goes on writing an
offset measured from *its* craft against whatever craft the player switched to, which places the
camera wherever the two happen to be apart.

The exception that makes the rest safe is **outranked**: a stronger claim inside the mod sets its
own mode and follows its own object, so both halves read as taken while the borrower is the mod
itself. Standing down there clears the recording the chase is holding on the sight's behalf, which
is the Yield failure above by another route.

**And a hand-back gives up only the half the player did not take.** Whichever of the mode and the
follow they moved is their decision and stays; the other is the mod's leavings and goes back.
Restoring both drags them off the vessel they just switched to; restoring neither leaves them in
Fixed, which is a mode no input can leave. The **field of view** is outside that rule and is always
handed back — see `docs/KSA-CAMERAS.md`.

**And the simulation itself is gated on the scene, not on the craft.** Gating it on
`KsaWorld.InFlight` freezes every round in the air the instant a launcher dies: they stop being
stepped, so they neither land nor expire, and a salvo hangs suspended mid-flight — which reads from
outside as rounds that despawned. Confirmed in play with six warheads three minutes past their
impact time. `GoLoose` exists precisely so a fired round outlives its launcher, and it is worth
nothing if nothing steps it.

**Losing the craft being flown is not leaving flight.** `Universe.DestroyVehicle` clears
`Program.ControlledVehicle` and the scene carries straight on, so `KsaWorld.InFlight` is the wrong
question for anything handing the view back — it reads a destroyed craft as a scene change and
skips the hand-back in the one case that most needs it, stranding the player at the sight's
magnification with the only recording of their view thrown away. `KsaWorld.InFlightScene` is the
one to ask.

**The camera's roll is the engine's, and getting it needs the one extension point KSA leaves
open.** `FixedController` derives up by crossing the view with the camera reference frame's +Z,
and `GetFrame2Ecl` dispatches on the followed object's *type* — a followable that is not a
`Vehicle` or a `Celestial` gets the Identity frame and its declared `CameraReferenceFrame` is
never read. `RoundFollowable` is one, so the axis is ecliptic +Z and the horizon arrives rolled by
the site's angle from that pole, snapping to it the instant the view is taken.
`Ksa/LevelHorizonController.cs` subclasses the controller and supplies the up vector instead —
`Viewport.FixedController` is a public writable field and `OnFrame` is virtual, so this is
subclassing rather than patching. **Following the launching craft instead does not work**: its
frame would give local vertical, but `PrepareFrame` advances vehicle positions before the viewport
pass while a round's is integrated after it, so the engine would add a frame-newer platform
position to an offset built against the older one — ~500 m per frame, which is what
`RoundFollowable` exists to prevent.

**And what `RoundFollowable` reports has to be resolved the way a round *body* is**, not from the
round's own integrated position. The engine calls `GetPositionEcl` in its own frame pass, before
the mod has stepped anything, so `round.PositionEcl` belongs to a different instant from every
celestial and vehicle just placed — and a camera on it sits one simulated step out of register with
the scene. That is 715 m on a 24 ms frame against 238 m on a 9 ms one, and the display's frame
pacing alternates between exactly those, so the camera's height over the ground swings **±145 m
every frame**. Resolving through `platform + OffsetFromPlatform` is the pairing round bodies
already use — measured at 79.5 km with 0.0 m drift — and it is what holds the reticule steady
through a transition on the Moon.

It only shows on a small body. The same error exists on Earth and is dwarfed there: what makes it
visible is that a camera translation displaces an object by roughly `1/range`, and the terrain a
chase transition flies over is three and a half times closer on the Moon.

That controller is the one place the mod stands on something nobody promised. It is bound through
`docs/KSA-API-SURFACE.md`, so a signature change is caught; if it ever cannot be installed the
engine's own stays and the roll comes back, which is a worse picture and not a crash.

**A view a mod is driving cannot be taken back by mouse or keyboard**, so anything that borrows it
owes the player a way out and has to say what it is. `FixedController` reads no input at all, and
`Shift+C` routes through `Viewport.NextCameraMode`, whose switch has no `Fixed` case and returns
false — so both reflexes are dead and only KSA's **View** menu sets a mode outright. The panel
says so beside the control, and **off** is the mod's own route back. `docs/KSA-CAMERAS.md` has the
evidence. This is why the chase camera releasing itself matters: it holds for seconds, where the
sight holds until told otherwise.

**A camera move that tracks a round runs on simulated time too.** The rule in *Fire control runs
on simulated time* is not only about fire control: the chase transition advances on the step, so
it holds still through a pause and slows with the panel's slow-motion buttons, which is the whole
point of having them. On player time it slides the view across a world that is not moving.
Lingering on a burst is the exception and stays on wall clock — that is a viewing duration, not
something tracking an object.

**Attaching the view to a round moves the camera before the mod gets a say.** `Camera.SetFollow`
sets `PositionEcl` to the followed object plus 2.5 mean radii *before* switching what is followed,
and a round's mean radius is one metre — so the camera lands next to the missile, and the stored
offset is re-read against a different body frame on the way out, which is 9.8 m to 164 m of
displacement depending on the craft's attitude. Read the pose you mean to ease away from **before**
the follow swap, and put the camera back afterwards: this frame's view matrix is already built and
the controller does not pick the offset up until the next one.

**The chase travels onto the round rather than cutting to it, and only its position really moves.**
The player is looking at the target and the chase looks along a round flying at it, so the two aim
points are close and the view barely turns — which is the whole reason it reads as calm.
`ChaseView.TryBlend` takes those aims as two **points**, not two directions: directions turn at a
wildly uneven rate and collapse to zero length when opposed, which is a round fired back over the
launcher. Both ends are rebuilt from this frame's samples, held as separations from the craft and
the round; a point stored in the ecliptic at the take falls half a kilometre behind per frame.

**The transition's two ends are offsets from the round, never a pair of ecliptic positions.**
`PlatformEcl` is sampled in `SampleWorld` before the round is stepped and `round.PositionEcl` read
after it, so differencing them across the blend carries one whole step of the planet's motion —
715 m on a 24 ms frame against 286 m on a 9 ms one. That difference alternates with the display's
frame pacing, and the camera then reverses its vertical direction **every frame**, ±270 m against
a path that should climb steadily. `Interceptor.OffsetFromPlatform` is the round measured against
the *same* frame's platform sample, which is the pairing that cancels it; `TryBlend` is a lerp of
points, so running it in that translated frame is the same answer with none of the carrier.
`ChaseBlendFrameTests` runs both forms on identical inputs and fails if the differenced form
stops reproducing the fault.

A fault of that shape is found by measuring, not by guessing at a cause: `ChaseCamera.ProbeBlend`
logs what the camera *meant* to do beside what the engine *had*, per frame, through a transition,
under a verbose log.

**The transition runs on an evened-out step, and it is the only thing that may.** KSA's step is a
report rather than a clock — `dtPlayer × achievedFraction × simSpeed` — and `dtPlayer` carries the
display's frame pacing. On a 120 Hz screen at a nominal 60 fps it beats 1-3-1-3, measured in flight
as an alternation between **8.33 ms and 25.0 ms**, exactly one and three vsync intervals. Fed
straight into the blend that is a camera advancing three times as far along its path on alternate
frames. `Sim/SmoothedStep.cs` averages it for the ease and for nothing else — anything integrating
the world must take the step as it comes. Both properties that made simulated time the right input
survive, because it is still the step being averaged: a paused world contributes nothing and a
warped one scales the whole average.

**`ChaseView.TryPose` decides where the chase stands and is not the transition's business.** The
transition lerps towards whatever it says. Changing the pose to improve the transition changes the
shot everyone actually watches, and that is a separate decision.

**Reclaiming it has to switch the setting off, not just release the view once.** The setting is
what asks for it, so a borrower that stands down and leaves the request standing takes the view
straight back on the next frame — one frame of the player's camera, then the mod's again, which
reads as it refusing to let go. `StandDown` therefore restores what the view was *following*,
because the mod changed that, and leaves the *mode* alone, because the player chose it.

The camera follows the launcher's own craft while the sight holds it, whatever the player was
following. `FixedController` places the camera at `following.GetPositionEcl() + CameraOffset`
during its own pass, so the offset handed over must be a pure separation: `eye − PlatformEcl`,
both from one sample. Measured from any other craft it carries a frame of that craft's motion
every frame and the sight shivers. Same reason `ChaseCamera` follows the round it rides.

**Only one weapon can own the bearing, and the cannon win the overlap.** The turret lays on the
gun's *ballistic lead* whenever `FireGate.GunsHaveTheEngagement`, and rounds leave along the tube
— so a missile released in that state departs ~18° off a 300 m/s crosser. `FireGate.MissilesMayFire`
holds them. The gate asks where the ring actually points rather than whether the guns are in
range, because a lead solve that fails leaves the ring on the target, which the missiles can use.
Proportional navigation recovers from an off-axis launch well enough that nothing but arithmetic
shows it happened, which is why the condition is a tested function rather than an assumption.

**The class is `WeaponSystem`, not `Battery` and not `DefenceBattery`.** Two reasons, and
only the first is about names colliding: `KSA.Battery` is the game's electrical battery and these
files have `using KSA;`. The second is that a battery is an air-defence *fire unit*, several
launchers under one fire control, which a rail bolted to a booster and a gun on a stack node are
not. `docs/BATTERY-SPLIT.md` has the argument, and the word for a launcher that engages on its own
if a craft ever carries two.

**Consumers take a role, not the system.** `Ksa/WeaponSystemRoles.cs` names what each one actually
needs: rounds in flight, an effect source, an optical head, a manual trigger, a read-only view.
Ten of the seventeen consumers take one and nothing else, and `Visuals` is one method short of an
eleventh — `DrawShellStream` takes the class for three members `IRoundsInFlight` already carries.

Of the six that take the class, four have to: the frame hook, the panel, the scenario runner and
`TargetLock` command it rather than read it, and designating is a command, which is why
`IOpticalHead.Designation` is read-only. The other two only read — `BombSightOverlay` stays within
`IWeaponSystemView`, and `LockCueOverlay` reaches one member past it for `Hold`.

## Testing

**The system works end to end in game** — search, lock, slew, salvo, intercept, kill, and the
overlay. `CHECKLIST.md` records what has been confirmed and what has not; update its tick-boxes
and the risk table as items are proved or disproved. It is the list of what is *still* unverified
that matters, not the headline.

That does **not** weaken the rule in [Committing](#committing): a behaviour change is unverified
until it has been flown, whatever the suite says. The two facts sit together — most of the mod is
confirmed working, and each new change still has to earn that separately.

`tests/KSArmory.Tests` flies whole engagements headlessly. Three suites are load-bearing and
should not be weakened without understanding what they buy:

- `GuidanceDiscriminationTests` asserts the crossing-target scenario **misses** with the nav
  constant off. Without it a hit test can pass on a geometry that never needed a lead.
- `ProjectileContractTests` runs the frame and epoch rules against **every** `IProjectile`. Those
  rules belong to the engine, not to a weapon, so a new projectile type inherits the whole trap
  list — a sub-step phase error is 142 m at 29.8 km/s, and nothing else in the suite looks for it.
- `OffsetPhaseTests` varies the step the way a simulation-speed change does. A constant `dt`
  cannot distinguish the right phase from the wrong one, so a suite built on one passes against
  both.

## Not done

- **A frame in which this mod's hook never runs is integrated on the next one.**
  `ScreenshotCapture` sets `Program.DrawUI = false` and `Program` guards
  `OnDrawUiViewports` with it, so the method this mod postfixes is not *called* during a capture
  and a postfix on an uncalled method never runs. `Universe.GetLastSimStep` then reports only the
  most recent step, leaving the skipped one unintegrated while the world advanced across it — the
  whole deficit landing in the drawn offset at 29.8 km/s. Measured in flight as a bomb thrown
  **656 m sideways in one frame** and lost off screen. `KsaWorld.ConsumeSimStep` now hands
  `StepGate` the span between step boundaries rather than the last step alone, so the gap closes
  on the next frame. `SkippedFrameTests` fails against the step-only form.

- Round bodies survive at long range: measured in flight to **79.5 km with 0.0 m drift**, never
  dropping the subpart link and never culled or clamped. The gizmo tracers stay on as a fallback
  anyway, and `WeaponSystem.RoundBodiesWork` still turns the whole thing off if a write is
  refused — the engine is under no obligation to keep behaving this way.
- The guns elevate on the same solution as the pods — one turret, one aim. What they do not have
  is a firing solution of their own, so the cannon cannot engage a different target from the
  missiles.
- The Pantsir's search volume does not follow its turret, because its profile boresights on local
  "up". `BoresightMode.TurretAxis` exists and nothing registered uses it yet.
- The model has no normal or occlusion detail — flat palette swatches only. Faceted lighting is
  the whole look, which suits KSA's art style, but it is a floor not a ceiling.
- A round that is **not** the one being chased still shows a very slight stutter, millimetres of
  shift, most visible at 0.01x where it recurs about every 500 ms. Not measured to a cause. Ruled
  out: the engine coalescing sim steps at low speed — the dump shows a step arriving every frame,
  0.16 ms at 0.01x — and the drives, the array and the chase blend, all of which are frame pacing
  and are handled. The chased round cannot show it, because the camera moves with it.

- A round body's drawn position has a floor of a few millimetres at range, and it is the engine's.
  Part transforms are packed to `float` at the part's own **Ego** magnitude — its distance from the
  camera — so the quantum is ~3 µm at a 26 m chase stand-off, 0.6 mm at 10 km and 9.5 mm at 80 km.
  A chased round is immune because the camera is metres from it; another round in the same salvo is
  not. Sub-pixel at any range anyone watches from, and nothing the mod can do about it.

- Rounds collide with terrain only when their profile asks. `MunitionProfile.HitsTerrain` is set
  for the bomb and nothing else, because it costs a terrain sample per round per frame and a CIWS
  burst is 150 shells in the air — so a shell still passes through a hill and a missile that
  misses still carries on into space. Structures are not collided with at all: where a launch
  pad's surface is has no answer in this engine, so a bomb dropped on one bursts at ground level
  beside it.
- The radar can mask against the real skyline, and ships not doing it.
  `SensorProfile.TerrainSamples` is the number of height-map lookups one contact may cost, and it
  defaults to **zero** — the mean sphere alone, with `TerrainMarginMetres` inflating it. The cost
  has never been measured in a frame, and a number that says exactly what it spends is a more
  honest thing to ship off than a switch.

  The order the rejects run in is what makes it affordable at all, and it is the opposite of the
  order they read in: range, cone and the planet's own bulk all reject first, and only what
  survives all three is sampled. `TerrainMask.TryBandBelow` then narrows it again in closed form
  to the part of the line that passes under the body's highest terrain, so a contact well above
  the ground costs nothing whatever the count says. A sphere containing the terrain cannot produce
  a false negative, which is why the cheap test can stand in front of the exact one.

  An unreadable height field makes **no claim** rather than reading as flat ground: flat would put
  every sensor's horizon back at the mean sphere, planet-wide, with nothing to announce it.
- Battery settings live **inside the save**, at `saves/<save>/KSArmory/systems.json`. KSA's save
  format cannot be extended (`UniverseData` is a fixed XML-mapped class) and StarMap has no save or
  load hook — but a save is a *directory*, so the file sits beside the `universe.xml` it belongs
  to, under a mod-named folder so several mods can do this without agreeing on filenames.

  That placement is chosen because it makes the awkward cases vanish rather than need code:
  deleting a save deletes its settings, copying copies them, renaming takes them along. A session
  with no save open falls back to `<user dir>/KSArmory/`, and the first save opened adopts it.

  **Settings are written when the game writes its save**, detected by watching `universe.xml`'s
  timestamp — not continuously. A continuous write is what stops a reload restoring anything: the
  file is always already up to date with the session, so there is nothing older to go back to.
  What is *not* persisted is system *state* — ammo, tracks, rounds in flight all start fresh.
