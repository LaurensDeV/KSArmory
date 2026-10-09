# Code health — modularity and comment hygiene

**A living list, unlike `docs/AUDIT-2026-08.md`.** Items are ticked as they land and deleted once
the fix and its reasoning are in the code. What is left unticked is the backlog.

Taken 2026-08-17 against `de61f85`, from three independent reviews of `Sim/`, `Ksa/` and the
comments. Every item below was checked against the source before being written down; where a claim
was reported and did not survive that check it is recorded under
[Did not survive](#did-not-survive) rather than dropped, because a finding that looks right and is
not will be found again by the next reader.

---

## Remove the automatic save repair after the release that ships it

- [x] **Delete `Ksa/SaveRepairs.cs`, `Sim/SaveRepair.cs` and `SaveRepairTests.cs`, and the
  `SaveRepairs.RunAll()` call in `KSArmoryMod.OnFullyLoaded`, in the release after the first one to
  ship them** (the first release after v0.9.4). The owner's decision: the repair exists only to carry
  saves across the Mk 42 losing its twenty shell subparts, for one release. Remove the two layout
  rows in `CLAUDE.md` and the note under "A shipped part's subpart list is append-only" with it.
  `tools/repair-saves.py` stays: it is the manual route and predates this.

## Modularity

- [~] **`Interceptor` and `Slug` duplicate the frame and epoch bookkeeping.** The physics is done:
  buoyancy and drag were character-for-character identical and are now `Sim/Medium.cs`, so a third
  round asks for both terms rather than being copied from one of these two.

  **What is left is deliberate.** The trail, `ShootDown`, `VelocityLocal` and the
  `OffsetFromPlatform` phase rule are two or three lines each, and sharing them means a base class
  under `IProjectile` — which is a change to how every round is constructed and stepped, in the one
  place a mistake is a round that leaves the world. `ProjectileContractTests` already runs the frame
  and epoch rules against *every* `IProjectile`, so a third type is checked whether it inherits the
  lines or copies them. Worth doing behind a flight, not alongside a comment sweep.

- [x] **Emitter pooling and held sounds are shared.** `Ksa/EmitterPool.cs` holds the take, point
  and give-back that `MotorPlume`, `MuzzleFlash` and `DecoyEffects` each copied, with the
  Kill-before-`RemoveEmitter` rule in one place, and `Ksa/SoundChannels.cs` holds the listener,
  pressure, move and stop that `MotorSound` and `GunSound` copied. Leaf functions, not a base class:
  the keys, cardinality and lifetimes genuinely differ. The chaff puff is left as it was, because it
  sets a burst's origin before attaching it and the pool helper attaches first.

  Flown on 2026-09-29: an AIM-9J's flame and trail at the nozzle, a Mk 42 flash after twenty-odd
  separate shots, and flare cores lit again after sixteen had burnt at once against the twelve-core
  cap -- no `no free emitters` line, no sound warning, no exception in KSA's log.


## The ballistic computer

- [~] **`Ksa/Icbm/IcbmComputer.cs` was 4,337 lines and about 120 methods**, and everything after cutoff -- the split,
  clearance, trim, post-boost passes, aim correction and release -- is orchestrated there, out of the tests' reach.
  It is split into ten partials by concern, 1,531 lines left in the main file. `docs/ICBM-OUTSTANDING.md` 5.3. A rig that flies past cutoff (2.5) is the
  other half: whatever of that orchestration is arithmetic belongs in `Sim/`, where it can be flown headlessly.
- [x] **`Sim/Icbm/IcbmProgram.cs` was 1,966 lines**, the phase machine and every phase's rules in one class. Split by
  phase into eight partials, 994 lines left in the main file. `docs/ICBM-OUTSTANDING.md` 5.3.
- [~] **`Sim/IcbmConfig.cs` carries 65 settings.** Thirteen with flown or judged verdicts were removed on
  2026-10-06 (5.1), and every doc comment is now its verdict and a pointer (5.2, 452 lines from 1,065). Several
  that remain are off and unflown, and each is either flown and decided or removed.
- [ ] **The body fall is summed over one link** (`KsaWorld.BodyFallEcl`): a round over the Moon keeps most of
  Earth's fall around the Sun as an error. Unflown; `docs/MIRV-NEXT.md` 2g.

## Comment hygiene

The ratios are fine — `Sim/` is a data-and-contracts layer and its comments carry engine contracts
and flown numbers. What is left is prose duplicated between a file and the doc it cites, which goes
stale in two places at once.

- [x] **`Sim/MushroomCloud.cs` duplicates `docs/NUCLEAR-EFFECT.md`** — the near-verbatim passages
  (drawn scale, flash against the rise, base surge, ember, stem lag, cap proportions, born width, the
  front holding the life) are cut to a sentence and a pointer, and the Glasstone and test-shot numbers
  kept: 731 comment lines to 599 of code, now 689 to 599.


## Known gaps, recorded rather than fixed

- **Hand-copied counts in prose drift, and nothing checks most of them.** `check-docs.sh` reads
  back the API totals, the KSA build and the layout table's coverage; every other figure in
  `CLAUDE.md` and `README.md` is copied from output that moves. The consumer count has now been
  found wrong twice — "ten of the thirteen" when it was 11 of 17, then "ten of the seventeen" when
  it was 12 of 20 — and the same pass found the mirror's size, the subset's assembly count, the
  corpus line count and the check count all stale. The fix is not a bigger `check-docs.sh` for
  every number: it is preferring a figure a tool prints on demand over one written into a sentence.

- **A coasting round that outlives its launcher is not fully carried over.** It keeps its tracer
  (a shell, for its whole flight) and its plume (a missile, while the motor burns), because both
  hang on the celestial body rather than on the craft, and its body mesh, which
  `Ksa/LooseBodyDrawHook.cs` draws as an instance of the part model taken off the subpart at
  `GoLoose` (a shell's is not carried). What it loses:

  - **Its motor sound.** `MotorSound` needs a camera-relative position and gets it from
    `camera.GetPositionEgo(vehicle)`. Whether that call has a celestial overload, and what it means
    for a body 6,000 km across, is unverified — worth an hour with the game rather than a guess.
  - **The diagnostic gizmo overlay.** `KsaWorld.BeginDraw` takes a `Vehicle`, and its own comment
    explains why `EclToEgo` is not a substitution: `GetPositionEgo(vehicle)` is the engine
    answering per case, and `EclToEgo` only agrees with the rendered scene while the followed
    craft's analytic and physics positions coincide. Off by default, so the lower priority of the
    two.

- **`KsaWorld.CentreOfMassEcl` may count the centre-of-mass offset twice.** It is
  `GetPositionEcl() + Asmb2Ego * CenterOfMassAsmb`, and on a floating hull whose `MassToGeometryAsmb`
  was zero, KSA's buoyancy sphere and `GetPositionEcl()` agreed to a centimetre while
  `CentreOfMassEcl` read the whole 12.48 m offset higher — so the position already *is* the centre of
  mass, at least there. It feeds lever arms: blast shoves and a store's release. Wants a flight on a
  rocket or a rack before anything is changed, because one craft is not the rule.

- **A gun's lay flip-flops when the mount is on a moving craft.** A Mk 42 on a ship doing 13 m/s and
  heaving ±3 m in waves, laying on a piece of wreckage still rolling on a beach 10 km off, alternated
  every half second between "laid on it" and "laid to its longest reach", with that reach wandering
  from 8.9 to 10.2 km of a 23.7 km gun — so the turret never settled. Every mount the lay was flown
  from stood on the ground, and the target was moving too; which of the two breaks it was not
  separated. Lay on a stationary target from the moving mount, and on the rolling one from a still
  mount.


## Did not survive

Reported and checked; leave these alone.

- **`PlumeSmoke`'s reflection.** One site in the whole mod, not a family, and the file says why it
  must stay visible.

- **`WatchCamera` outside the claim ladder.** It borrows nothing and restores nothing; folding it
  in would need the takeover its design note rules out.

- **`tools/model/pantsir.py` at 1,194 lines.** CLAUDE.md already declares the headless generator
  frozen.
