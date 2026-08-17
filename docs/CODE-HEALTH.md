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

- [ ] **Testable maths stranded in `Ksa/`.** Highest value first:
  - `Radar.TeamOf` — a pure string function, the only untested half of IFF, carrying the
    documented longest-substring-wins trap ("Redstone" lands on team "Red"). Move to `Sim/Iff.cs`.
  - `WeaponSystem.Holding()` — 87 lines, every input already a `Sim/` type, no test, and the
    mod's most-read line.
  - The blast sweep in `WeaponSystem.Detonate` — now written twice inside one method, rounds and
    craft, differing only in where position, velocity and radius come from.

- [ ] **`Interceptor` and `Slug` duplicate the frame and epoch bookkeeping.** Trail constants and
  append, the buoyancy-plus-drag block, `ShootDown`, `VelocityLocal`, and the
  `OffsetFromPlatform` phase rule. `ProjectileContractTests` exists so a third `IProjectile`
  inherits the trap list; what it would inherit today is copy-paste.

- [ ] **Shortest-arc rotation implemented twice**, with different epsilons and different degenerate
  axes — `TubeGeometry.RotationFromTo` and `FireGeometry.RotationFromNose`, where the second is
  the first applied to `NoseAxis`. `OpticGeometry` reaches into `TubeGeometry` for the generic
  helper.

- [ ] **Emitter pooling is byte-identical across three files** — `MotorPlume`, `TracerTrail`,
  `MuzzleFlash` — including the Kill-before-`RemoveEmitter` safety comment, whose failure mode is
  that nothing in the world can spawn particles again. `MotorSound` and `GunSound` likewise.
  Leaf functions, not a base class: the keys, cardinality and lifetimes genuinely differ.

- [ ] **`UiSystem.cs` holds a different owner for a third of itself** — the optical-head block
  never reads the battery or the policy, contradicting the file's own doc comment.

- [ ] **Nothing checks that `SystemSettings` covers every persistable `SystemConfig` field.** A new
  field silently fails to persist across three hand-written lists, and `check-tunables.py` asks
  for a control rather than for persistence.

## Comment hygiene

The ratios are fine — `Sim/` is a data-and-contracts layer and its comments carry engine contracts
and flown numbers. What is wrong is comments that have gone stale or slid off their subject, which
is the failure CLAUDE.md rates worse than a missing comment.

- [ ] **Doc blocks have slid off their members.** `GenerateDocumentationFile` is off, so nothing
  warns; blocks now hold more than one `<summary>`, each earlier one describing a member left
  undocumented. Confirmed: `KsaWorld.ParentBody` (carrying `GravityAt`'s and `GroundVelocityAt`'s),
  `KSArmoryMod`'s mushroom-cloud comment on `_motors`, an orphan "last kitten reported" comment for
  a field that no longer exists.

- [ ] **`MuzzleFlash`'s class summary describes behaviour `b71fa90` removed** — "one endless
  emitter per battery", anchored to the barrel cluster's centre "averaging them". That commit's own
  message quotes this comment as the thing that defended the old behaviour.

- [ ] **Other stale claims.** `LauncherPart` ("several launchers on one craft still give one
  battery" — false since the per-launcher roster), `ChaseCamera` ("a second and a half" against
  `LingerSeconds = 3.0`), and dead `cref`s to `WeaponSystem.OpticOriginEcl` and `Sim/Designation`,
  neither of which exists.

- [ ] **`CLAUDE.md`'s consumer count is wrong** — "ten of the thirteen consumers take a role" is
  11 of 17. Three of the named exceptions need no widening at all: existing roles already cover
  every member they touch.

- [ ] **`docs/MODULARITY.md` is stale on its own headline bug** — the `Interceptor.Munition`
  default it describes is fixed; the property is `required` and set on both branches.

- [ ] **History narration that `check-comments.sh` does not catch.** Its regex misses "used to
  go", "it began at", "and it did", "first attempt", "two earlier versions", "once claimed", and a
  block quoting a commit subject verbatim. Widen the regex rather than fixing the instances alone.

- [ ] **`Sim/MushroomCloud.cs` duplicates `docs/NUCLEAR-EFFECT.md`** — 503 comment lines to 212 of
  code, with three sentences near-verbatim from the doc it cites twice.

## Did not survive

Reported and checked; leave these alone.

- **`PlumeSmoke`'s reflection.** One site in the whole mod, not a family, and the file says why it
  must stay visible.
- **`WatchCamera` outside the claim ladder.** It borrows nothing and restores nothing; folding it
  in would need the takeover its design note rules out.
- **`tools/model/pantsir.py` at 1,194 lines.** CLAUDE.md already declares the headless generator
  frozen.
