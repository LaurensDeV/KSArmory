# Code health — modularity and comment hygiene

**A living list, unlike `docs/AUDIT-2026-08.md`.** Items are ticked as they land and deleted once
the fix and its reasoning are in the code. What is left unticked is the backlog.

Taken 2026-08-17 against `de61f85`, from three independent reviews of `Sim/`, `Ksa/` and the
comments. Every item below was checked against the source before being written down; where a claim
was reported and did not survive that check it is recorded under
[Did not survive](#did-not-survive) rather than dropped, because a finding that looks right and is
not will be found again by the next reader.

---

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


## Comment hygiene

The ratios are fine — `Sim/` is a data-and-contracts layer and its comments carry engine contracts
and flown numbers. What is left is prose duplicated between a file and the doc it cites, which goes
stale in two places at once.

- [ ] **`Sim/MushroomCloud.cs` duplicates `docs/NUCLEAR-EFFECT.md`** — 498 comment lines to 217 of
  code, with three sentences near-verbatim from the doc it cites twice.

  Lowest value on this list and the easiest to do damage with: the ratio is high because the file
  encodes Glasstone's numbers and why each was departed from, which is the kind of comment CLAUDE.md
  wants kept. What is actually duplicated is a handful of sentences, so the fix is to cut those and
  leave the pointer — not to thin the file toward a ratio.


## Known gaps, recorded rather than fixed

- **Hand-copied counts in prose drift, and nothing checks most of them.** `check-docs.sh` reads
  back the API totals, the KSA build and the layout table's coverage; every other figure in
  `CLAUDE.md` and `README.md` is copied from output that moves. The consumer count has now been
  found wrong twice — "ten of the thirteen" when it was 11 of 17, then "ten of the seventeen" when
  it was 12 of 20 — and the same pass found the mirror's size, the subset's assembly count, the
  corpus line count and the check count all stale. The fix is not a bigger `check-docs.sh` for
  every number: it is preferring a figure a tool prints on demand over one written into a sentence.

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
