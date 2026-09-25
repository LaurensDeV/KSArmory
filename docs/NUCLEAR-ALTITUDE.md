# A burst at every height -- the plan

**A plan, not a record.** What a nuclear burst does as it goes higher, from the sources, against what
the mod does today, and the changes that close the gap, ranked by what a player sees for what the
frame pays. Written 2026-09-25 from three research passes: altitude phenomenology (Glasstone and
Dolan 1977 ch. 2, 3, 7, 10; Gombosi et al. 2017, arXiv 1611.03390; the Hardtack and Fishbowl test
records), yield and density scaling (G&D, Sublette's FAQ, NUKEMAP's fits), and an audit of the code.
Numbers marked *fit* or *derived* are someone's reading, not a measurement.

## What the sources say, by height

The mod keys every regime on the air at the burst against sea level's (`airRatio`). The bands below
are the sources' altitudes and their US Standard Atmosphere density ratios (±30%).

| Band | Air ratio | Fireball | Cloud | Ground blast and sound | Sky |
|---|---|---|---|---|---|
| **Surface / low air** (< 55 W^0.4 m) | 1 | Sharp, white to orange-red to brown; ~1 min luminous at 1 Mt | Dirty mushroom, dust stem, fallout | Strongest; Trinity's shock took 40 s to 16 km, felt > 160 km | -- |
| **Troposphere** (to ~16 km) | > 0.13 | Sharp, double pulse, f ≈ 0.35 | Torus-capped mushroom, stem of air, white condensation in moist air; stable ~10 min | Sachs-scaled; Mach stem from ~1 × HOB | -- |
| **Lower stratosphere** (16-30 km) | 0.13-0.015 | 2-4× larger; **yellow-orange disc becoming a purple torus** that glows for a few minutes (Tightrope, 21 km) | No stem, no condensation | **Weak, late**: Yucca's (1.7 kt, 26 km) reached a ship ~70 km off (slant) 3 min 16 s later | Radar blackout 30 s-3 min |
| **Upper stratosphere** (30-60 km) | 0.015-2.5e-4 | Very large (Orange, 3.8 Mt at 43 km: IR ball ~24 km *radius*, glowing ~18 s though its thermal pulse was ~2 s); **the brightest regime**, f ≈ 0.6, retinal burns at Bluegill | Rising glowing sphere, no mushroom | Barely: infrasound, a far boom. 50 Mt at 37 km ≈ 8 kPa at ground zero (*derived*, ±×2-3) | Conjugate aurora (Apia, 17 min) |
| **Mesosphere / lower thermosphere** (60-105 km) | 2.5e-4-2e-7 | Huge, diffuse; Teak (3.8 Mt, 77 km) 18 km across at 0.3 s, 29 km at 3.5 s; pulse under 1-2.5 s; rises ~1 km/s to > 145 km in a minute | Luminous ball for 15-30 min | None felt | **A red spherical wave ~970 km across at 6 min** round Teak; aurora at the burst and at the conjugate point in < 1 s; purple streamers; a green patch for hours (Kingfish) |
| **Exo-atmospheric** (> 105 km) | < 2e-7 | **No local fireball**: a flash, and an X-ray "pancake" heated at 80-110 km | Debris bubble held by the field: Starfish's reached **1,840 km along B and 680 km up in 1.2 s**, collapsed by ~16 s | None | Red sky disc to 45° from zenith for ~400 s; conjugate aurora; white "fingers" and rings for 90 min; belts for years |

Thresholds where behaviour changes (G&D unless noted): 55 W^0.4 m fireball off the ground; ~12 km
blast efficiency starts to bite; ~16 km X-ray fireball growth; **~30 km no mushroom and ground blast
negligible**, f → 0.6; ~49 km X-rays start escaping, f → 0.25 by 61 km; **betas reach the conjugate point from ~40 km**
(Orange at 43 km lit Apia for 17 min; G&D's "above ~40 mi" is where it becomes strong); ~80 km debris kinetic energy forms the fireball; **~105 km no local fireball**;
~150 km the 630 nm red is no longer quenched (Gombosi); ~300 km the field, not the air, stops the debris.

## Where the mod stands

**Right already.** No mushroom above 1% air. X-ray glow, conjugate aurora, the debris shell stretched
along the field. The thin ball's climb of about four radii in a minute matches Teak's ~1 km/s. Not
claimed right any more: the fireball's size at altitude. `cbrt(1/air)` capped at 10× matches Teak only
because the cap bites, and at Orange's height it draws an 18 km ball against a measured ~48 km IR
diameter. Visible and IR size need fitting against both shots (item 3).

**Wrong** (the code audit, confirmed by review):
1. **Every blast law is sea level's**: `ShockRadius` (ρ₀ 1.225, 340 m/s, energy always doubled even in
   free air), `PositivePhaseSeconds`, `ReflectedPascals`, and every damage verdict -- the part sweep,
   the fuse's confirmed hit (`WeaponSystem.cs:3564`, which falls back to destroying the whole craft), the
   binary path in `BlastSweep.Effect` (`:68`) and the round intercept (`WeaponSystem.cs:3598`).
2. **`IsThin` (1% air) does four jobs**: the mushroom, the drawn front, the shake and the bang, so a
   burst at 40 km is silent and still. Its sites: `NuclearClouds.cs:530, 597, 769, 830`,
   `CloudPass.cs:386`, `UiDebug.cs:70`, `MushroomCloud.cs:331, 1294`.
3. **The bang cannot be late.** `BurstSound.FurthestSeconds` drops anything past 45 s, and the delay is
   fixed at the burst from where the camera stood then.
4. **Flash timing ignores altitude**, and the thermal pulse and the ball's long glow are one number.
5. **Above ~105 km the shell is the fireball's size**, not the field-held bubble's.
6. **An airless burst gets the sea-level flash**: `_burning` stores `AirRatio = 1.0` for airless
   bodies (`NuclearClouds.cs:798`), and `AirDensityRatioAt` returns 1.0 when a read throws.
7. **A bus stacks its sky**: glows, curtains and fireball lights are added before the same-burst merge.
8. **Every northern aurora was drawn off the screen**: the curtain's `FireSun.w` (its latitude) tripped
   the scorch-mark pixel offset. Fixed in the working tree, flown with the next deploy.
9. Smaller: the shake uses reflection 2 rather than `GainAt`; four sound speeds (343 in the bang, 340 in
   the front, `DustFrontSpeed` 340 in the shader, `MushroomCloud.SoundMetresPerSecond`); the ambient
   pressure taken from the density ratio (`BlastArrivals.cs:353`, `BlastShake.cs:104`); the thin cloud
   outliving its shell; stale doc lines.

**Measured** (item 1, landed): the aurora was the whole cost, 28 ms a frame from orbit; now 2.5. The glow is
0.13-0.26 ms and the debris 0.06-0.09. The table is in `docs/NUCLEAR-EFFECT.md`.

## Rules the plan keeps

These came out of review, and each one replaces an earlier idea that was wrong.

- **Blast and heat are two laws, not one energy budget.** G&D's blast efficiency (Table 3.68) is a
  multiplier on sea-level blast -- 1.0 up to 12 km, where heat is also 0.35 -- not a share of the
  energy. The only budget test is heat plus prompt radiation ≤ 1.
- **One pressure rule everywhere, G&D's** (§3.68): efficiency by the *burst's* altitude, Sachs scaling
  by the *target's* ambient. Damage, dents, shake and the bang's loudness all read it, so they cannot
  disagree at a point. Anchor restated in it: 50 Mt at 37 km ≈ 5-8 kPa at ground zero.
- **Damage moves only by η.** `BlastDamage` is a cube law, where Sachs cancels exactly, so a burst's
  damage at altitude is `EquivalentChargeKg = W · η(air at burst)`, routed through **all four** verdict
  paths. Conventional charges are untouched (η = 1 below the nuclear threshold).
- **In near-vacuum, damage keeps today's law** as the stand-in for X-ray fluence -- which is what kills
  a craft near an exo-atmospheric burst, over a wider reach than blast ever had. Letting η → 0 there
  would make a Starfish harmless to a satellite beside it, which is backwards. A real X-ray and heat
  fluence law is a later item, listed below.
- **Unchanged means at air exactly 1 in Sim, and nowhere else.** No burst in the game is at exactly 1
  (the pad reads ~0.99), so every flown number in CLAUDE.md -- the dents at 1,300 and 1,450 m, the
  300/1,000/3,000 m counts, "0.3 kt at 800 m dents 2.06 s" -- is re-flown and rewritten.
- **The laws that switch keep the sea-level form exactly below the switch.** `t_max` keeps
  0.0417 W^0.44 below 15,000 ft and blends over a decade of air into 0.038 W^0.44 (ρ/ρ₀)^0.36; the high
  law is held at its validity limit (100,000 ft) rather than extrapolated.
- **The thermal pulse and the glow are separate terms.** The pulse follows `t_max` (~2 s at Orange's
  height, under 1-2.5 s at Teak's); how long the ball glows while cooling is its own law, fitted to
  Orange's ~18 s and Teak's ~2.5 s.
- **What a ground observer gets, in order:** the flash and the heat on the skin at once (Tightrope's),
  then the shake and the bang *together* -- one pressure wave -- at about the height over the sound
  speed. The shake and the bang both follow the live front to the eye, never a delay fixed at the burst.
- **One sound speed per body**, `c = √(γP/ρ)`, used by the front, the bang, the dust ring and the shader.
- **Air is read where the burst is at the sample** (`BlastSweep.GroundAtSample`), never at the round's
  instant against the body's end-of-frame position: at an 8 km scale height a frame's slip is ~6% of
  density.
- **Air above KSA's atmosphere boundary is zero** (`CalculateBoundaryHeight`, ~167 km on Earth), so
  nothing is keyed on density above it, and every pressure ratio guards P = 0.
- **The arrival stays a bisection.** The sonic join is eased by an exponential with no closed inverse;
  a closed form would be a different law and would move the guard. The CPU it costs is 60 iterations
  once per load, which was never the frame's cost.

## The plan, ranked

### 1. Measure what the sky costs, and set the budget
Separate profiler regions for glow, aurora and debris (`CloudPassCost` already reports any
`"KSArmory Cloud: <x>"` region as a stage). Record per stage the **median and the peak**, per phase
(0-2 s, 2-20 s, minutes), with the resolution and GPU, at four poses: the ground looking at zenith, orbit
at 1,000 km on the limb (the aurora's worst), `CloudWatch`, and the player's own. **The budget, which
fails a step**, set from what was flown: the whole sky ≤ 2.5 ms median from orbit and ≤ 1.2 ms on the limb
for six bursts at 100 km, at 1440×900; any new sky effect ≤ one X-ray glow (~0.2 ms) at the same pose.
The first draft's 1 ms for the whole sky is not met from orbit -- the aurora alone is 2.1 ms there -- and a
tighter one would need a half-resolution aurora, which is not planned. **The sky cap** (landed): at most
four full-screen sky dispatches, each kind's brightest in turn.

### 2. Blast by altitude
Efficiency by burst air, Sachs by target air, damage by `W·η` through every verdict path, the front in
the burst's air with energy doubled only by ground coupling, the ring following the same front, the bang
and the shake split from `IsThin`: heard and felt where the ground pressure is ≥ ~50 Pa, following the
live front to the ear with no 45 s cap for a high burst. Tests: 50 Mt at 70 km breaks no weak part on the
ground **with a separate target craft** (the flown craft is spared by default, so without one the check
passes whatever the code does), shown failing against the parent; 50 Mt at 37 km ≈ 5-8 kPa; Yucca's
arrival within 20% -- a guard, and one that passes only because KSA's isothermal air runs at 340 m/s,
which is faster than the real stratosphere (the ship was ~65-70 km off in 196 s).

### 3. Flash, heat and the fireball by altitude
The pulse law above; the thermal fraction's knots (0.35 below 12 km, 0.6 across 30-49 km per G&D §7.90,
0.25 by 61-79 km, a few percent above ~82 km); the long glow as its own term; the fireball's visible
and IR radius fitted to Orange and Teak; the double pulse turning single above 100-130 kft (§7.89). Feeds
the glare, the whiteout and the fireball light.

### 4. Above the air: the field-held bubble
- **With a field:** the bubble's size is the **equal-volume** radius from `R_B = ∛(3μ₀·f_KE·E / 2πB²)`,
  with `f_KE` **calibrated on Starfish** (its 1,840 × 680 km full extents are an equal-volume ~474 km;
  0.25 overshoots about 9× in volume), stretched 2.7:1 along B, its bottom clipped at the X-ray layer
  where it runs into air, grown in ~1.2 s, collapsed by ~16 s. Low yields give a small bubble on their
  own (R_B ∝ E^⅓): Checkmate's green-blue centre and red ring, gone in under a minute.
- **With no field:** debris expanding ballistically and fading -- no shell that collapses, since with no
  field `R_B` has no size.
- Drawn as an **analytic rim** with the mottle from one noise lookup at each crossing, not taps, in the
  shell's own units so a 2,000 km shell keeps its precision.

### 5. Teak's red wave
A red shell expanding at ~1.35 km/s, 965 km across at 6 min, **trailing ~150 km** behind its front
(`v · τ`, oxygen's red living ~110 s), glowing only where the air is thin enough for red to survive
(a density, and anything above the atmosphere boundary), fading over minutes. One full-screen analytic
dispatch inside the sky cap, computed in shell units with a floor on its projected thickness.

### 6. The dry stratospheric ball
Where the air is too dry for condensation: no white cap, no Wilson cloud, yellow-orange then a faint
purple glow for minutes (Tightrope). **Dryness removes condensation, never ground dust**: a ground
burst's column follows ground coupling on any body.

### 7. Housekeeping
One glow, curtain and fireball light per bus (merge first, and invalidate anything cached on a merged
cloud); airless bursts get air 0 and an explicit "unknown" when a read throws; the thin cloud ends with
its shell; the shake by `GainAt`; the northern-aurora fix; stale doc lines.

### Deliberately not planned (yet)
**X-ray and heat fluence as damage** -- heat scorching the ground under a 37 km burst (~175 cal/cm² at
50 Mt) while the blast is a gust, and X-rays killing craft far out from an exo burst; today's law stands
in, as above. Radiation belts, EMP, the beta patch, lithium twilight glow, Kingfish's green patch, crater
formation, precursor dust waves: invisible from a KSA camera, a gameplay system of their own, or a march
the frame cannot pay for.

## Integration -- how it goes into KSArmory

### What KSA gives, and what it does not
- **KSA's atmosphere is isothermal**: `SeaLevel · exp(-h/H)` for both pressure and density, cut to zero
  at the boundary height. Temperature and sound speed are one number per body. Recorded in
  `docs/BLOCKED-ON-KSA.md` and the upgrade-ksa skill, with a test pinning the derived sound speed, since
  a KSA atmosphere model would change every altitude result here.
- **Its declared scale height need not be hydrostatic**: H = P/(ρg) is 8.4 km for its Earth against a
  declared 8, and 25.5 against 150 for its Jupiter. Density in game follows the *declared* H, so the
  column above a point is **Σ = ρ(h) · H**, not P/g (6× off on Jupiter).
- **KSA's Earth has more air aloft up to ~150 km and none above ~167 km**: 37 km reads 0.0098 of sea
  level (real 0.0055). Every flight logs the air ratio; the heights in the tables are the sources'.

### The pieces

| New | What |
|---|---|
| `Sim/BodyAir.cs` | what a body's air is: P₀, ρ₀, declared H, g, R, boundary height, and from `Bodies.xml` the field, glow, condensation, γ, whether it has a surface, and an X-ray opacity factor. Sim takes this, never a body |
| `Sim/BurstRegime.cs` | every threshold, each keyed on **its own physics**: pressure for blast; fireball radius against scale height for the mushroom (so a thin-aired body's small bursts still stand); the column times the opacity factor for X-rays; the field's pressure against the debris's for the bubble |
| `Sim/BlastAltitude.cs` | `AmbientAir` with an explicit known flag; `Efficiency`; `EquivalentChargeKg`; the front in the burst's air; the ground pressure by G&D's rule |
| `Sim/BurstHearing.cs` | the bang's gate (≥ 50 Pa at the ear) and loudness, following the live front |
| `Sim/ThermalAltitude.cs` | the pulse law and its blend; the thermal fraction; the long glow |
| `Sim/CloudFlags.cs` | the march's flag float packed in Sim with 3 bits for dryness; the shader's decode and `MostShockMetres` (to 262,143 m) change in the same commit, or the float stops being exact |
| `Sim/SkyDispatch.cs` | names for the sky kinds (glow 0, aurora 1, debris 2, red wave 3), **what each kind puts in `FireSun.w`**, and a shader-text test that the mark offset never runs on a sky dispatch and kind 3 is tested before kind 2 |
| `Sim/RedWave.cs` | Teak's shell as above |
| `KSArmory/Bodies.xml` | read by **its own reader**, dispatched on its root element before `PackReader` -- in the pack folder as a `<Bodies>` root it would be refused as a weapon pack and logged as a fault on every load. A `PackAudit`-style check after the world loads warns about an entry naming no body; a parse error is a warning, never a silent loss of the home body's aurora; a test that the shipped file gives the home body a field; a row in `tools/pack-api.py` |

### Build order

Each step builds, passes `check-all.sh`, adds the log line it will be judged on before it is judged,
carries its docs and a CHECKLIST §14 box, and is flown before it counts. A regression test is shown
failing against the parent; a new-API test that cannot compile there says so, or tests through an
existing entry point with a defaulted parameter. Each step lands as **its own conventional commit** --
the squash rule is for experimental arms, and squashing here would fold a dozen player-visible fixes
into one changelog line.

| # | Step |
|---|---|
| 0 | **Ask how `agent/airburst` lands** (24 commits off `dev`, and the uncommitted work), then land the northern-aurora fix, the night fill and `BallIsThin` after a night flight, and this plan |
| 1 | `test`: pin the **Sim** sea-level laws bit for bit at air exactly 1 -- shock radius and arrival, Kinney–Graham, reflection, `At`, `FlashAt`, glare -- and a regime table |
| 2 | `refactor`: `SkyDispatch`, its `FireSun.w` contract and the shader-text test |
| 3 | `chore`: time the sky stages apart; `status` counts the lists |
| 4 | `perf`: the sky cap -- at most four sky objects, brightest first |
| 5 | `chore(tools)`: `sky-matrix.sh` (every call under `timeout`, captures copied out of `last/` before the next call, the newest KSA log grepped for exceptions after each flight), `sky`/`orbit` presets, night by the antisolar longitude with `sun_elevation_deg` < -18 checked |
| 6 | `docs`: price the sky against the budget in item 1 |
| 7 | `refactor`: `BodyAir`, `KsaWorld.AirAt` (read at the carried burst; P = 0 guarded; boundary respected), `Bodies.xml` and its reader, audit and tests |
| 8 | `fix`: one glow, curtain and fireball light per bus; airless is air 0; the thin cloud ends with its shell |
| 9 | `fix`: one sound speed, `√(γP/ρ)`, everywhere including the shader; shake by `GainAt`; guard rows re-recorded with the reason in the message |
| 10 | `fix`: the X-ray layer and the aurora foot from the column (calibrated on the game's Earth: the layer at Σ ≈ 0.35 kg/m², the foot at ≈ 0.037), so they sit apart on every body |
| 11 | `refactor`: `BurstRegime`, `BlastAltitude`, `BurstHearing` -- nothing calls them yet |
| 12 | `fix(blast)`: damage by `W·η` through all four verdict paths |
| 13 | `fix(blast)`: the front, arrivals, the drawn front and the ring on the burst's air; every `ShockRadius` consumer listed and moved together, including `WeaponSystem.cs:3838`'s log |
| 14 | `fix(sound)`: hear and feel a high burst -- the `IsThin` split and the live-front bang |
| 15 | `feat(flash)`: pulse, fraction, glow and fireball size by air |
| 16 | `refactor` then `feat(clouds)`: `CloudFlags`, then the dry ball |
| 17 | `feat(clouds)`: the bubble above the air |
| 18 | `feat(clouds)`: Teak's red wave |
| 19 | `docs`: record the flights; this file from plan to record; the re-flown CLAUDE.md numbers |

**Between steps, players:** steps 12-14 change damage before the sound and front catch up. They ride
behind an off-by-default flag, as `IcbmConfig`'s do, or `dev` is not merged to `main` until 14 is flown.
Every new sky effect gets a toggle under **Settings -- Display** (checked by `check-tunables`), and the
pass may shed sky dispatches when `CloudPassCost` reads over budget, so a weak GPU loses an effect
rather than frames.

### How each step is shown to work
- **Early seconds at a crawl**: every cell before ~20 s is flown at `speed 0.02` with `every_s` spacing
  in sim time -- the bridge polls every 0.1 s and a capture waits 24 frames, so a 0.3 s moment cannot be
  caught at 1x, and a paused frame freezes the noise.
- **Flicker**: each new shader effect -- the rim, the red wave, the dry ball's colour, the bubble's
  growth -- gets the 0.02x `vis.temporal` check.
- **"Unchanged"** is a `vis` diff under a stated tolerance, never "pixel-identical": the noise is keyed
  on the frame counter.
- **Sound** cannot be captured; its evidence is the logged due time and Pa at the ear.
- **Only Earth, Luna and Sol exist to fly to.** Thin air and thick air are covered by the synthetic-body
  tests and by Earth at the matching air ratio; Luna is flown as the airless case. A thin-air body is
  promised in game only once a developer system file with one has been shown to load.
- **The matrix is long** (four yields × six bands plus Luna is ~1-1.5 h at speed 10), so each step
  flies a named subset and the full matrix runs at steps 6 and 19, at handoff or bedtime -- never by
  relaunching a game the user is playing.

## Planet-agnostic by construction

Nothing may know which planet it is on: **no body is named in code, and no table in code is keyed by a
body.** Every quantity comes from what KSA declares, or from `Bodies.xml` where it declares nothing.

| Quantity | Derived as |
|---|---|
| Sound speed | `√(γP/ρ)`, γ from `Bodies.xml` (default 1.4) |
| Column above a point | `ρ(h) · H_declared` |
| X-ray layer | where the column times the opacity factor reaches its calibrated value; ~2.5 H thick |
| Aurora foot | where the column reaches its calibrated value |
| Where red survives | below an absolute density, or above the atmosphere boundary |
| Tropopause and cloud height | from the **hydrostatic** H = P/(ρg), and cloud rise ∝ √H (plume theory), not linearly -- *modelled* off Earth; a gas giant's 150 km declared H would otherwise make clouds 19× taller |
| Mushroom or not | fireball radius against H, not air density alone |
| Ground | terrain and ocean as the body has them; whether it has a surface at all comes from `Bodies.xml`, since KSA clamps density below the mean radius rather than declaring none |

**`Bodies.xml` fields**: `MagneticField` (strength, tilt, azimuth; none by default: no aurora, no
stretch, ballistic debris), `Airglow` (`OxygenNitrogen`, `CarbonDioxide`, `Hydrogen`, or explicit
colours; a dim neutral glow by default, logged once), `Condensation` (default: has weather layers or an
ocean), `Gamma` (1.4), `Surface` (default: yes), `XRayOpacity` (1 for N₂/O₂; hydrogen air absorbs far
less per kilogram). Entries for KSA's current bodies ship as data; a custom system without entries still
works, with no aurora and neutral glows. If KSA ever declares a field or a composition, its value wins.
A body renamed by a KSA update is caught by the audit, and the upgrade-ksa skill says to check it.

**Held to it by**: Sim tests over synthetic bodies -- Earth-like, thin and cold (0.006 atm at
0.02 kg/m³), thick and hot (92 atm at 65 kg/m³), airless, no surface, no field, a strong tilted field, a
hydrogen giant -- each behaving by its numbers; and `tools/check-bodies.sh` in `check-all.sh`, matching
body names **inside string literals only** (the source has ~75 in comments) with `KSArmory/*.xml`
excluded, plus a denylist of Earth-valued constants in code (343, 340, 11000, 8000, the 80 km and 100 km
layer heights) outside their stated reference uses. The synthetic matrix is the real guard; the script
keeps the easy mistakes out.

**Known limits.** G&D's laws are Earth's, measured in N₂/O₂ air near 288 K. Blast, fireball growth and
X-ray absorption transfer by pressure, density and column; cloud rise and colour are modelled. A body
far denser than Earth's sea level (53× on KSA's Venus) is outside every calibration -- modelled, not
validated -- and `ThinAirGrowth`'s clamp at 1 becomes `cbrt(1/air)` both ways so its fireball shrinks.
