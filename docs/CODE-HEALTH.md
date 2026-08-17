# Code health — modularity and comment hygiene

**A living list, unlike `docs/AUDIT-2026-08.md`.** Items are ticked as they land and deleted once
the fix and its reasoning are in the code. What is left unticked is the backlog.

Taken 2026-08-17 against `de61f85`, from three independent reviews of `Sim/`, `Ksa/` and the
comments. Every item below was checked against the source before being written down; where a claim
was reported and did not survive that check it is recorded under
[Did not survive](#did-not-survive) rather than dropped, because a finding that looks right and is
not will be found again by the next reader.

---

## Defects

Things that are wrong now, not things that could be tidier.

- [x] **A load-bearing test asserts `f(x) == f(x)`.** `tests/ChaseBlendFrameTests.cs`,
  `SharedMotionDoesNotReachTheBlend` calls `BlendedOffset` twice with byte-identical arguments and
  asserts the results agree. Its own doc says it adds a common velocity to the launcher, the round
  and the scene; it adds none, and an inline comment rationalises the identical inputs. CLAUDE.md
  names this suite as load-bearing. Give it the shared velocity it claims, and check it fails
  against the differenced form.

- [x] **The weapons switcher is unreachable with the panel closed.** `Ksa/Ui/Ui.cs` returns inside
  the `if (!Visible)` branch, and `DrawWeaponsWindow()` sits after it — under a comment reading
  "Not gated on the panel being open: the switcher is for use while flying." FIRE and the group
  master arm go with it.

- [x] **`ChaseCamera.Release` drops the view recording on a refused hand-back.** It discards
  `BeginRestoreMainView`'s bool and clears `_saved` regardless. `SightCamera` retries for 180
  frames, and its comment states what dropping it costs: the player is left in Fixed mode at the
  borrowed pose with the only description of their view thrown away. Two copies of one hand-back
  that have drifted apart.

- [x] **`Armament.Steers` answers from the armament slot, not the round.** `Sim/WeaponFit.cs` reads
  `Kind == ArmamentKind.Tubes`, while the flight model is chosen off
  `Munition.Guidance == GuidanceMode.None`. Three registered munitions are unguided and two racks
  declare tubes, so a bomb rack is offered seven guidance sliders its `Slug` never reads. The
  sibling `Drop` already resolves correctly through `Arsenal.MunitionNamed`.

## Modularity

- [x] **Testable maths stranded in `Ksa/`.** Highest value first:
  - [x] `Radar.TeamOf` — now `Sim/Teams.TeamFor`, with the substring trap pinned.
  - [x] `WeaponSystem.Holding()` — now `Sim/FireLadder.cs`, with the rung order tested.
  - [x] The blast sweep in `WeaponSystem.Detonate` — the geometry is now `Sim/BlastSweep.cs`,
    with the back-dating frame rule tested for invariance.

- [~] **`Interceptor` and `Slug` duplicate the frame and epoch bookkeeping.** The physics is done:
  buoyancy and drag were character-for-character identical and are now `Sim/Medium.cs`, so a third
  round asks for both terms rather than being copied from one of these two.

  **What is left is deliberate.** The trail, `ShootDown`, `VelocityLocal` and the
  `OffsetFromPlatform` phase rule are two or three lines each, and sharing them means a base class
  under `IProjectile` — which is a change to how every round is constructed and stepped, in the one
  place a mistake is a round that leaves the world. `ProjectileContractTests` already runs the frame
  and epoch rules against *every* `IProjectile`, so a third type is checked whether it inherits the
  lines or copies them. Worth doing behind a flight, not alongside a comment sweep.

- [x] **Shortest-arc rotation implemented twice**, with different epsilons and different degenerate
  axes — `TubeGeometry.RotationFromTo` and `FireGeometry.RotationFromNose`, where the second is
  the first applied to `NoseAxis`. `OpticGeometry` reaches into `TubeGeometry` for the generic
  helper.

- [ ] **Emitter pooling is byte-identical across three files** — `MotorPlume`, `TracerTrail`,
  `MuzzleFlash` — including the Kill-before-`RemoveEmitter` safety comment, whose failure mode is
  that nothing in the world can spawn particles again. `MotorSound` and `GunSound` likewise, and
  five files share an identical roster scan. Leaf functions, not a base class: the keys, cardinality
  and lifetimes genuinely differ.

  Partly done: the identical roster scan in all five is now `WeaponSystems.Knows`, which had to
  happen anyway so that a system flying rounds for a destroyed craft does not have its plume and
  motor sound cut on the frame its launcher dies. The `Take`/`Give`/`Point` triple is what is left.

  **Wants a flight, not a tidy-up.** All of it is `Ksa/`, so nothing here is reachable from the test
  project, and the failure it guards against is silent and global — a pool leaked dry stops every
  particle in the world, not just this mod's. A faithful extraction is checkable by reading, but
  "checkable by reading" is exactly what CLAUDE.md says is not evidence. Do it as its own change,
  with the game open.

- [x] **`UiSystem.cs` holds a different owner for a third of itself** — the optical-head block
  never reads the battery or the policy, contradicting the file's own doc comment.

- [x] **Nothing checks that `SystemSettings` covers every persistable `SystemConfig` field.** A new
  field silently fails to persist across three hand-written lists, and `check-tunables.py` asks
  for a control rather than for persistence.

## Comment hygiene

The ratios are fine — `Sim/` is a data-and-contracts layer and its comments carry engine contracts
and flown numbers. What is wrong is comments that have gone stale or slid off their subject, which
is the failure CLAUDE.md rates worse than a missing comment.

- [x] **Doc blocks have slid off their members.** `GenerateDocumentationFile` is off, so nothing
  warns; blocks now hold more than one `<summary>`, each earlier one describing a member left
  undocumented. Confirmed: `KsaWorld.ParentBody` (carrying `GravityAt`'s and `GroundVelocityAt`'s),
  `KSArmoryMod`'s mushroom-cloud comment on `_motors`, an orphan "last kitten reported" comment for
  a field that no longer exists.

- [x] **`MuzzleFlash`'s class summary describes behaviour `b71fa90` removed** — "one endless
  emitter per battery", anchored to the barrel cluster's centre "averaging them". That commit's own
  message quotes this comment as the thing that defended the old behaviour.

- [x] **Other stale claims.** `LauncherPart` ("several launchers on one craft still give one
  battery" — false since the per-launcher roster), `ChaseCamera` ("a second and a half" against
  `LingerSeconds = 3.0`), and dead `cref`s to `WeaponSystem.OpticOriginEcl` and `Sim/Designation`,
  neither of which exists.

- [x] **`CLAUDE.md`'s consumer count is wrong** — "ten of the thirteen consumers take a role" is
  11 of 17. Three of the named exceptions need no widening at all: existing roles already cover
  every member they touch.

- [x] **`docs/MODULARITY.md` is stale on its own headline bug** — the `Interceptor.Munition`
  default it describes is fixed; the property is `required` and set on both branches.

- [x] **History narration that `check-comments.sh` does not catch.** Its regex misses "used to
  go", "it began at", "and it did", "first attempt", "two earlier versions", "once claimed", and a
  block quoting a commit subject verbatim. Widen the regex rather than fixing the instances alone.

- [ ] **`Sim/MushroomCloud.cs` duplicates `docs/NUCLEAR-EFFECT.md`** — 503 comment lines to 212 of
  code, with three sentences near-verbatim from the doc it cites twice.

  Lowest value on this list and the easiest to do damage with: the ratio is high because the file
  encodes Glasstone's numbers and why each was departed from, which is the kind of comment CLAUDE.md
  wants kept. What is actually duplicated is a handful of sentences, so the fix is to cut those and
  leave the pointer — not to thin the file toward a ratio.

## Known gaps, recorded rather than fixed

- **A coasting round that outlives its launcher cannot be seen.** It keeps its tracer (a shell,
  for its whole flight) and its plume (a missile, while the motor burns), because both hang on the
  celestial body rather than on the craft. What it loses:

  - **Its body mesh — permanently, and this one is KSA's.** The body is a subpart of the launching
    craft. There is nothing to write a transform to.
  - **Its motor sound.** `MotorSound` needs a camera-relative position and gets it from
    `camera.GetPositionEgo(vehicle)`. Whether that call has a celestial overload, and what it means
    for a body 6,000 km across, is unverified — worth an hour with the game rather than a guess.
  - **The diagnostic gizmo overlay.** `KsaWorld.BeginDraw` takes a `Vehicle`, and its own comment
    explains why `EclToEgo` is not a substitution: `GetPositionEgo(vehicle)` is the engine
    answering per case, and `EclToEgo` only agrees with the rendered scene while the followed
    craft's analytic and physics positions coincide. Off by default, so lowest priority of the
    three.

## Did not survive

Reported and checked; leave these alone.

- **`PlumeSmoke`'s reflection.** One site in the whole mod, not a family, and the file says why it
  must stay visible.
- **`WatchCamera` outside the claim ladder.** It borrows nothing and restores nothing; folding it
  in would need the takeover its design note rules out.
- **`tools/model/pantsir.py` at 1,194 lines.** CLAUDE.md already declares the headless generator
  frozen.
