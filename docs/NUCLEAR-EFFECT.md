# The nuclear effect: what the engine offers, and what a mushroom cloud needs

Research for making a nuclear burst look like one. Four questions were asked in parallel: how KSA
draws its SRB plumes, how it draws clouds, what its particle system can express, and what a real
mushroom cloud actually looks like. This is the result, kept because re-deriving it is a day's work.

Route A is built, and *What was built* at the end records the two engine rules that ended up
deciding the shape. Everything before that section is the survey, kept as it was written: it is
still the map for anything else drawn this way.

---

## The four volumetric systems, ranked for this job

KSA has exactly four raymarched volumetric renderers. Two are reachable.

| | Reach | Shape | Frame | Life | Gated on |
| --- | --- | --- | --- | --- | --- |
| **Volumetric trail** (the SRB smoke) | one reflected field | chain of swept capsules | **CCF, body-fixed** | 1200 s | clouds + atmosphere |
| **Volumetric particles** | fully public, already used | sphere / torus / cone / capsule spawn volumes | world | per particle | **screen-space particles, default off** |
| Volumetric exhaust | public call, but its instance list is cleared before any StarMap hook | analytic nozzle plume | | | needs Harmony |
| Clouds | reflected field, and pointless | planet-wide shell | | | — |

**The cloud renderer cannot host a local volume, and that is a fact about the model rather than
about access.** `GetCloudDensity` evaluates procedurally per raymarch step from a global lat/long
coverage texture and a vertical spline between two concentric spheres. There is no positional term
to inject into: with full write access you still could not place a cloud at a place. What *is* free,
and unrelated, is cloud tuning — `CloudRenderer.ShowEditor` is a public static behind a live editor
with an XML round trip.

**Why the reachable two look as good as the game's clouds:** both include the cloud shader itself.
`Particles/Render/Screenspace/Volumetric.comp` opens with `#include CloudFunctions.glsl` and
`#include AtmosphereLuts.glsl`; the trail renderer's raymarcher carries its own 128³ Worley volume
and states in its header that "noise erosion is applied similarly to the clouds system". Same
density functions, same atmosphere ambient. Matching the game's cloud quality is therefore not a
matter of trying harder, it is a matter of using either of these two.

### The trail renderer, in detail

`VolumetricTrailRenderer` is a public class whose every look knob is a public mutable field, and
`SubmitEmitter` is public. One thing is private: `Program._volumetricTrailRenderer`, which its
sibling `VolumetricExhaustRenderer` did get an accessor for. So the whole system is one
`GetField(NonPublic | Instance)` away.

**`DutyCycle > 0f` is not a gate.** `docs/BLOCKED-ON-KSA.md` describes it as the reason the XML route
fails, which is right, but it is worth being exact: it is the `isActive` *argument*, computed at the
one call site in `Vehicle.UpdatePlumeTrailEmitters`. `SubmitEmitter` forwards a bool. A caller
passing `true` never meets it — no nozzle, no propellant, no thrust.

What makes it the best fit for a cloud that stands over a target:

- Segments are stored in **CCF**, the body-fixed rotating frame, so a planted cloud stays over the
  ground for its whole life rather than being left behind by the planet.
- Segment lifetime is **1200 s** and the radius expands over a settable time.
- After expansion the vertices advect through a **simplex wind field, sheared by altitude**. The cap
  drifts against the stem for free, which is otherwise choreography.
- Erosion noise scale is derived from segment radius, so large capsules billow at a large scale
  automatically.
- Self-shadowing, Beer absorption, cloud shadows and full atmospheric in-scatter.

Its costs, which are real:

- **Colour is one global uniform.** `DebugTrailColor` tints every trail in the world; the shader
  carries a `TODO` about making it per-vertex. A grey mushroom would grey every solid booster.
  White is defensible for a condensation cloud, and is what it already is.
- Gated on clouds *and* atmosphere being enabled, and only drawn for the camera's nearby
  atmospheric body. Nothing on an airless world.
- A **stationary** emitter lays one dragged segment rather than a chain. The shape has to be drawn
  by *moving* emitters: one climbing for the stem, several tracing a circle for the cap.
- Reflection is not covered by `tools/api-surface.sh`, which reads compile-time binds. A KSA update
  can break it silently, so it needs its own loud failure.

Free look development either way: `Program.SetPlumeTrailsDebugUi()` is a public static, and the
`plumes` console command opens a live tuning window for lifetime, expansion, wind and erosion.

### The particle system, in detail

Fully public, no reflection, and the mod already spawns volumetric emitters through it. The limits
that decide the design:

- **512 particles per emitter**, divided by particle quality (so 128 on Low), 1024 emitters for the
  whole game, 8192 volumetric particles gathered per frame, 63 per screen tile, 16 spheres per ray.
- Spawn volumes are `Point`, `Sphere`, `Box`, `Cone`, `Torus`, `Capsule`, `VehicleSurface`,
  `PrecomputedTransform`. **A torus and a capsule are a cap and a stem.** The axial ones are built
  about local +X and aimed by the *rotation* of `LocalOffset`.
- There are **no curves, gradients or keyframes**. Over a particle's life you get: grow (quadratic,
  toward `EmitterExtra.W`), shrink (linear to zero), ease-in on spawn, and one optional hue rotation
  applied once. Colour and velocity are fixed at spawn. No drag, no turbulence, no vortex field.
- The volumetric path gets a `(1-t)²` density fade and a `1 - r² - t²` shape erosion for free, which
  is a proper break-up-and-dissolve. The Billboard fallback gets none of that, ignores
  `ParticleColor.rgb` entirely, cannot roll, and is unsorted.
- Every emitter field is public and mutable and is read at each spawn, so **all animation is C#**.
  The XML is a template.

**`GrowOverLifetime` needs `EmitterExtra.W` and fails backwards without it**: the shader is
`mix(initialScale, initialScale * extra.w, lifeRatio²)`, so the default of zero shrinks the particle
to nothing. Every stage in `KSArmoryParticles.xml` had this. It is recorded there now.

---

## What a mushroom cloud actually is

From Glasstone & Dolan, *The Effects of Nuclear Weapons* (1977), the standard reference. `W` is
yield in kilotons.

```
fireball radius, maximum    = 67 · W^0.4  m          (surface burst ×1.32)
     ...breakaway           = 33.5 · W^0.4 m         (max is about twice it, §2.127)
cloud top, stabilised       = 3.0 km · W^(1/3)       (below ~10 kt)
cap radius, stabilised      = 0.6 km · W^0.37
cap base                    = 0.5 · cloud top
stem radius                 = 0.5 · cap radius       (below 20 kt)
second thermal maximum      = 0.0417 · W^0.44 s      (the blinding flash)
thermal pulse over          = 0.417  · W^0.44 s
fireball goes dark          = 3.0    · W^0.4  s
initial rise rate           = 42     · W^(1/6) m/s   (see below -- NOT 25 · W^0.2)
```

For the yields this mod can dial — the slider spans the B61's own range, 0.3 kt to 340 kt:

| | 0.3 kt | 1.5 kt | 10 kt | 50 kt | 170 kt | 340 kt |
| --- | --- | --- | --- | --- | --- | --- |
| Fireball radius (air) | 41 m | 79 m | 168 m | 320 m | 523 m | 690 m |
| ...on the ground (×1.32) | 55 m | 104 m | 222 m | 423 m | 690 m | 910 m |
| Flash over | 0.25 s | 0.50 s | 1.15 s | 2.33 s | 4.00 s | 5.42 s |
| Goes dark | 1.9 s | 3.5 s | 7.5 s | 14.3 s | 23.4 s | 30.9 s |
| **...as drawn** | **1.9 s** | **3.4 s** | **3.4 s** | **3.4 s** | **3.4 s** | **3.4 s** |
| **Cloud top** | **2.0 km** | **3.4 km** | **6.5 km** | **11.1 km** | **16.6 km** | **20.9 km** |
| **Cap radius** | **0.38 km** | **0.70 km** | **1.41 km** | **2.55 km** | **4.01 km** | **5.19 km** |
| Stem radius | 0.19 km | 0.35 km | 0.70 km | 1.28 km | 2.01 km | 2.59 km |
| **Ground skirt, widest** (drawn) | **0.13 km** | **0.23 km** | **0.47 km** | **0.86 km** | **1.35 km** | **1.75 km** |
| ...settled, after the draw-in | 0.09 km | 0.16 km | 0.32 km | 0.58 km | 0.91 km | 1.18 km |

The drawn flash parts company with the law above about 1.4 kt, and has to: the cloud's clock is
compressed and the flash's is not, so 340 kt would glow for 30.9 s against a 38 s rise — still
flaring after its own mushroom had finished forming. Compressing it by the same factor is not the
alternative, since that works out at a blink nobody sees. The ceiling it is held at is `ClimbUntil`
rather than a number of its own: no smoke is laid while the ball is luminous, so a flash outlasting
the climb up the axis leaves the pens already out on the cap when they lay their first segment, and
the column from the ground up is never drawn at all.

**Two of the numbers everybody quotes do not survive checking, and one of them was in this list.**

- **`25 · W^0.2` for the initial rise rate is folklore.** It appears in none of Glasstone & Dolan,
  DNA EM-1, WSEG-10, DELFIC, KDFOC, Norment, or the modern LLNL and ORNL literature: a search of
  eight primary texts returns zero occurrences. It is probably a corruption of a *height* exponent,
  since WSEG-10's cloud-centre height has a local slope of 0.202 over 10 kt to 1 Mt. Checked against
  data it also has the wrong trend: it gives 99.5 m/s at 1 Mt against Glasstone's measured peak of
  147.5 m/s. **Three independent routes give `W^(1/6)` instead** — DELFIC's own initial condition
  `u_i = 1.2·√(g·R_c)`, Moresco's vortex-ring velocity scale, and Chang's `v_max ∝ (θ·Y/ρ)^(1/6)`.
  DELFIC's form evaluates to about `42 · W^(1/6)` m/s.
- **The fireball constant is confirmed by measurement.** Five shots in DASA 1251 carry a measured
  radius at the second thermal maximum (Wasp 60 m, Moth 70 m, Tesla 112 m, Bee 125 m, Encore 190 m),
  fitting `57.5 · W^0.359` m. Those are radii at 44–150 ms, before the ball is at full size; the
  maximum is about twice breakaway, which for a 1 kt contact surface burst is 88 m against the
  88.4 m drawn here.
- **The cloud top is not low, and the fits that say it is are fitted to the wrong burst type.**
  Norment's least squares over 53 shots gives `3914 · W^0.270` m and the BRL fit in DNAF-1 gives
  `3597 · W^0.2553` m, both well above the `3.0 km · W^(1/3)` used here. Both are dominated by air
  and tower shots, and Glasstone §2.16 warns that a land surface burst stabilises *lower* for the
  mass of dirt it carries. Against the one measured low-yield **surface** burst there is no contest:

  | Jangle Sugar, 1.2 kt, 3.5 ft above ground | cloud top | error |
  | --- | --- | --- |
  | **measured** (15,000 ft MSL at 4.3 min, ground 4,215 ft) | **3,287 m** | — |
  | `3.0 km · W^(1/3)`, as drawn here | 3,188 m | **−3.0%** |
  | DNAF-1 `3597 · W^0.2553` | 3,768 m | +14.6% |
  | Norment `3914 · W^0.270` | 4,111 m | +25.1% |

  Sugar's measured cap radius at three minutes is 747 m against the 642 m this draws, so the cap is
  about 14% narrow — which is the opposite correction from the one the newer fits imply, and small.

  **The exponent is right too, and the low coefficient is right for a physical reason.** A refit of
  the 54-shot US test set gives `W^0.344` over 0.5 to 20 kt, against the `W^(1/3)` used here; the
  dry-thermal theory value is `W^(1/4)`, which is what Church's high-explosive law uses. The
  coefficient sits below every published fit because those fits are dominated by air bursts, and a
  surface burst carries its own soil: DELFIC's 50 kt initial cloud has 2.85e7 kg of soil against
  2.06e7 kg of gas, leaving **45% of the buoyancy a pure-gas cloud of the same yield would have**.
  That is the whole reason a surface burst stabilises lower, and it grows *weaker* with yield, since
  entrained soil goes as `W^0.882` against energy as `W`.

  **What is wrong is the top of the dial, and the cause is the tropopause.** Above roughly 20 kt a
  real cloud stops rising freely and spreads against the stratosphere, so the measured exponent
  collapses to `W^0.153`. A cube root does not know that:

  | | 50 kt | 170 kt | 340 kt |
  | --- | --- | --- | --- |
  | as drawn, `3.0 km · W^(1/3)` | 11.1 km | 16.6 km | 20.9 km |
  | measured fit above 100 kt | 14.0 km | 16.9 km | 18.8 km |
  | error | −21% | −2% | **+11%** |

  Left alone: the crossover lands near the middle of the B61's dial, the two ends err in opposite
  directions by about the same amount, and a piecewise law would buy a tenth of a cloud height at
  the extremes for a discontinuity in the middle.

**And the small end of the dial is the well-behaved one**, which is the opposite of what you would
expect. Tested against the Hardtack II sub-kilotonne shots (7.8 t to 77 t), the physical models come
within 11 to 13 per cent, and the spread *between* independent laws is 2 per cent at 0.01 kt against
66 per cent at 15 Mt. Norment's reason: a megaton cloud's ceiling is set by the height of the
tropopause and the structure of the stratosphere rather than by its own buoyancy, while a low-yield
cloud is a clean buoyant thermal in near-uniform stratification, which is exactly the case the
theory covers. So 0.3 kt is not an awkward corner of the laws. It is the middle of their range.

**One classical result is worth carrying because it validates the proportions for free.** A buoyant
thermal entrains at a fixed cone angle: `R = 0.25 · z`, from Morton, Taylor and Turner (1956) fitted
to Scorer's laboratory data. This cloud draws a 384 m cap radius under a 2010 m top, which is 0.19 —
inside the 0.25 ± 0.1 the literature gives for miscible thermals. The rise and radius laws are
`z = 2.41 · B^(1/4) · √t` and `w = 1.20 · B^(1/4) / √t`, so **the rise rate decays as `1/√t`, or
equivalently as `1/z`.**

**Three things worth knowing before building anything:**

**The shipped fireball is about four times too large for a nuclear burst.** `Warhead.FireballRadius`
is Hopkinson-Cranz, `2.6 · W^(1/3)` in kg, which is right for chemical explosives. A nuclear
fireball goes as `W^0.4` with a different constant: 0.3 kt gives 41 m against that law's 174 m. So
the nuclear path reads `MushroomCloud.FireballRadius` and leaves `Warhead`'s to the chemical charges
it is right for; the damage radii are calibrated separately and are roughly right for both.

**Brightness does not scale with yield.** Glasstone §2.05: fireball surface temperature, and so
luminance, is much the same at any yield; only size and duration change. One emissive ramp works
everywhere, and it is a 6,000–7,000 K blackbody, so blue-white rather than yellow.

**The real thing is far too slow to watch.** 0.3 kt takes three and a half minutes to stabilise.
Every film and game production compresses it. What must be preserved are the *ratios*, not the
clock.

### The four cues that do the work

In order of effect per unit of effort, from the VFX literature:

1. **Toroidal rollover.** The cap is a vortex ring: outward and down at the rim, tucking under and
   back up through the middle, and the rotation **decays to a stop** as the cloud reaches its
   ceiling. Visible in the lower, brighter part of the cap.
2. **A thin stem, attached to the cap at every instant.** The stem is not fireball, it is dirt
   lifted by afterwinds, at half the cap's radius. **It does not lag, and drawing it as though it
   does is the named tell of an amateur mushroom.** A dust column that climbs separately and joins
   the cloud later is the *air*-burst picture: LLNL's fallout-cloud regimes separate a "bomb debris
   stem" hanging under the cap from a "dust stem" rising off the ground, and they stay separate only
   above a scaled height of burst of about 500 ft/kt^(1/3). Below 20 — every burst this mod draws —
   "large amounts of dust are **immediately** drawn into the cloud cap", so the column is continuous
   from the first instant. What develops over 2 to 4 buoyancy times is only how clearly it reads as
   narrower than the cap. Causally top-down, materially bottom-up: the rising fireball makes the
   suction, the ground supplies what you see.
3. **The colour ramp**: white-hot → orange → dark red → **reddish-brown** (nitrogen oxides, the
   phase everyone forgets) → white as water condenses, with grey-brown in the stem for a surface
   burst.
4. **A skirt of dust round the base**, thrown out by the blast and then drawn back in. It sells
   scale because it interacts with the terrain, and it is the one cue that is easy to get
   *categorically* wrong. See below.

**The base surge is a water burst, and drawing one on land is the single most legible mistake
available.** Glasstone treats it under underwater bursts: the column of water thrown up by the shot
falls back, and the spray runs outward over the surface as a dense annular wall, far past the
cloud's own width. That is Crossroads Baker, and it is what everybody's mental image of "the ring
round the bottom" actually comes from. A land surface burst does the opposite: its afterwinds blow
**inward** along the ground, because that inflow is what lifts the dirt that becomes the stem. Dust
the blast threw out is pulled back to the axis and up, so the skirt ends as a collar round the
column rather than a ring beyond the cap. Shipping the outward version drew three independent "that
looks like it happened at sea" reports inside a day.

A fifth, cheap and very recognisable: the **Wilson cloud**, an expanding translucent shell *ahead
of* the fireball at 1–2 s, dome first then ring, gone by 3 s. Needs humid air to be honest.

**Surface versus air burst is one comparison:** the burst is a surface burst when its altitude is
below `55 · W^0.4` m, which is the same number as the maximum fireball radius, because that is the
height at which the fireball just fails to touch the ground.

---

## What to build

The shape has to be drawn rather than simulated. Neither reachable system has drag, turbulence or a
vortex field, so the toroidal roll comes from moving emitters on a schedule, not from physics.

**Route A, the trail renderer.** One `PlumeTrailEmitterState` climbing for the stem, six to eight
tracing a circle that widens and lifts for the cap, all on simulated time. Body-fixed and 1200 s, so
the cloud stands over the target and shears in the wind by itself. Best result, one reflected field,
white only.

**Route B, particles.** One asset with child emitters: fireball core (`Sphere`, burst, HDR colour),
cap (`Torus` aimed at local up by `LocalOffset`, slightly negative gravity for buoyancy, long life),
cap crown (`Sphere` above the torus so the dome is not hollow), stem (`Capsule`, `OverTime`), ground
surge (flat `Box` with `PlaneCollision`). C# raises each stage's origin and grows its radius per
frame. No reflection, full colour control, but most players have the good renderer switched off.

Both want the same C# choreography, and the choreography is most of the work — so it is worth
writing that against an interface and picking the renderer behind it.

**Do the flash separately either way.** It is sub-second, it is a different colour regime, and its
duration is `0.417 · W^0.44` s.

---

## What was built, and the two rules the engine imposed

Route A. `Sim/MushroomCloud.cs` is the choreography and `Ksa/NuclearClouds.cs` walks it with
`PlumeTrailEmitterState` pens; `Ksa/Fireball.cs` does the flash separately, as an emissive sphere
through the generic mesh renderer plus a real point light.

The skirt is the fourth cue above, and it is the one that answers "the explosion looks
underwhelming for such a cloud" — because at 0.3 kt that complaint is *correct arithmetic*. The
fireball is 110 m across and the cap 770 m, a ratio of 1:7 that the test photographs agree with.
Nothing about the fireball can fix it, and enlarging it past the law would be a lie about the
weapon. The dust is what a surface burst actually shows:

```
skirt radius = cap radius · (0.70 · (1 − e^(−t/τ_out)) − 0.35 · (1 − e^(−t/τ_in)))
                                   τ_out = 0.10 · rise,  τ_in = 0.55 · rise
skirt height = 0.10 · cloud top · (1 − e^(−t/τ)),   τ = 0.45 · rise
```

Two exponentials against each other: the blast drives the outrush and nothing sustains it, while
the inflow feeding the stem lasts as long as the column is rising. So the skirt is widest at
**0.52 cap radii, 11 s in**, and settles at 0.35. It is drawn back in by a third, and the height
keeps climbing through that, because the same inflow lifts what it pulls inward. At 0.3 kt: a 129 m
radius at its widest, 116 m high by the end of the rise, well inside a 250 m drawn cap.

**The first version ran outward to 1.4 cap radii and stayed**, which is the water-burst shape above
and was read as one immediately. Correcting it took the skirt's drawn volume from ~150 Mm³ to
~24 Mm³, so it also answers the other half of that feedback: less smoke, and what is left is
attached to the column instead of standing beyond it.

### How much smoke is the right amount

A separate question from how big the cloud is, and the one that makes a small device look wrong.
The dimensions are Glasstone's and are not negotiable; the *opacity* is ours. A 0.3 kt surface burst
lofts roughly 90 t of soil — the standard figure is about 300 t per kilotonne — and at any
concentration that reads as visible dust that fills a few hundred million cubic metres:

| dust concentration | volume it fills |
| --- | --- |
| 1.0 g/m³ | 90 Mm³ |
| 0.5 g/m³ | 180 Mm³ |
| 0.2 g/m³ | 450 Mm³ |

Against that, the drawn volume was **660 Mm³ and opaque** — the same air the real dust occupies, but
rendered as though it held several times the mass. Two levers, since the renderer has **no density
or absorption field** and `trailColor`'s alpha is unused:

- **Erosion.** `ErosionMaxDepth` is how deep the Worley volume carves into each capsule, and it is
  the only thing that makes the smoke translucent rather than solid. 0.8 is the shipped value and
  shreds an exhaust; 0.45 gave billows and a wall. 0.68 keeps the billows and lets sky through.
- **Pen count, whose sign depends on which term is setting the tube.** Where coverage sets it,
  on a wide ring where tube = pitch, more pens means a *thinner* tube for the same closed surface, and
  volume goes as its square, so adding pens *removes* smoke. Where the floor sets it,
  on a ring small enough that its pens already overlap, a pen is just more smoke. The skirt is now in the
  second regime at every radius it reaches, so its count is the smallest that still closes: 18,
  giving a 45 m pitch against a 56 m limit at its widest.

### The grouping rule is real, and never runs on this mod's pens

**This is the most expensive mistake in this document, and it was believed for several rounds of
work.** `PlumeEmitterGrouping` genuinely merges emitters within `0.4` of an expanded radius of their
running centroid into one ball of `cbrt(n)` radii, and genuinely deletes each member's own stroke
after 1.83 s. It is real code. It cannot reach anything a mod submits. From `Program.OnFrame`:

| | |
| --- | --- |
| `Program.cs:2096` | `OnDrawUiViewports` → the `[StarMapAfterGui]` postfix → `PlumeSmoke.Lay` → `TrackEmitter` adds a grouping point |
| `Program.cs:2209` | `PrepareTrailSegments()` → `ResetForFrame()` → **clears every point just added** |
| `Program.cs:2214` | the engine's own vehicle emitters submit |
| `Program.cs:2217` | `FinalizeTrailSegments()` → `BuildGroups()` — sees only those |

No StarMap hook lands between the reset and the build, so this is unreachable without Harmony. Mod
pens are never grouped, never get `cbrt(n)`, and their strokes live the full 1200 s rather than 1.83.

**What overlapping capsules actually do is the raymarcher's own rule: the deeper of the two, never
the sum.** So *n* coincident pens of radius `r` render as one tube of radius `r`. That is the rule
to reason from, and it is much simpler.

What survives, what does not:

- **Coverage stands**, and is now the only rule setting emitter counts. A capsule is solid only
  within `0.55 r`, so pens spaced more than `1.1 r` apart leave clear air between them and the cloud
  reads as ropes.
- **The arrowhead and the 371 m ball were real**, but not for the stated reason. Three concentric
  shells are still right, because the axis is otherwise hollow — the justification is filling a
  volume, not defeating a merge.
- **The stem's nine pens are one column** because their spread is inside their own tube, not
  because anything merges them.
- **The handover tube was sized three times too thin.** Dividing the fireball radius by `cbrt(36)`
  is correct *if* the engine is merging the pens into one fat ball, and wrong otherwise: it lays a
  14 m thread where a 45 m ball belongs. A thread that climbs is a pillar rising out of the ground,
  which is precisely how the effect was described from play. `TubeAtHandover` is now one radius.

The lesson worth keeping is not about plumes. **Reading the engine's source told us what the code
does; it did not tell us whether our call path reaches it.** Every conclusion here should have been
checked against the frame ordering before anything was built on it.

### The handover, and why the cloud read as too big for the burst

The sizes were right and the ratio still read wrong, which is worth understanding because the cause
was neither of the two numbers anybody was arguing about.

Every pen starts on the burst point, so at the instant the smoke takes over the whole cap bundle
lays one coincident stroke — overlapping capsules render as the deeper of the two, so 36 pens at the
cap's own tube are **one ball of that tube, arriving in a single frame where a fireball a fraction
of its width was the frame before**. No transition. So what anybody watches is a small flash, and
then a large cloud, and there is no moment at which the first becomes the second. The cloud is then
judged against the flash and found far too big for it, which is exactly the report that came back
from play.

The fix is to lay the pens at the fireball's own width and swell them as the stroke climbs:

```
tube at handover = fireball radius
tube at progress = that, grown to the full tube by p = 0.40
```

Sized that way there is nothing to see at the handover at all, and it holds at every yield without a
constant to tune, because each end is read off the law that owns it — the fireball's at the start,
the cap's at the end. At 0.3 kt the pens are laid at the fireball's 55 m and swell to the cap's
73 m over about fifteen seconds, so the cloud *inflates* out of the burst instead of replacing it,
and the column comes out thin at its base and fat at its head, which is the right silhouette anyway.

Three things had to move with it:

- **The skirt's floor grows in from the same width**, for the same reason the cap's does: a collar
  laid at its full floor on the first frame is a ball of dust arriving where the fireball just was,
  which is the same discontinuity in miniature.
- **The stem's spread grows with its tube.** The bundle reads as one column only while the spread
  stays inside the pens' own tube, so nine pens at full spread over a handover-width tube are nine
  poles.
- **The ball goes out rather than being deleted.** It is removed on the one frame the smoke is
  taking over from it, which is the single instant the eye is watching for continuity. It carries on
  for another 0.09 of the **rise** — against the rise rather than against the flash, because the
  rise is the clock the smoke is on — shrinking to a seventh of its radius as the cloud closes over
  it. This is an extinction and not a phase: the glow there is more than an order of magnitude under
  the flash's own, so what it draws is a dull red ember. It is cut at a floor just above the bloom
  threshold rather than dimmed to nothing, because under that threshold the same sphere is drawn as
  ordinary shaded geometry — which is to say, as a ball. Anything brighter would be a lie, since
  `3.0 · W^0.4` is *defined* as when the fireball stops glowing.

**Full width before a pen reaches the rim, not after.** The coverage rule keeping the rim closed is
written against the full tube, so a pen still thin when it reaches the rim is a rope. `CapPoint`
holds a pen on the axis until `p = 0.15` and carries it out over the rest of the stroke, crossing
the equator around `0.68`; the growth finishes at `0.40`.

### The cap is a spheroid, and it follows from two numbers already in the table

The first cap drawn from this choreography was reported from play as *a lamp*, which was exact. It
was a flat annulus at one height, with a lip hanging off its outer edge and a 57 m ball perched on
the axis above it: a shade, a rim and a finial. 739 m tall against 861 m wide.

The shape is not a matter of taste, and the reference had already answered it. A cap whose **base is
half the cloud top** and whose **crown is the cloud top**, at the **cap radius**, is within a few
per cent of a *sphere of the cap radius centred at three quarters of the top*:

| 0.3 kt | base | crown | width |
| --- | --- | --- | --- |
| Glasstone | 1004 m | 2008 m | 769 m |
| as drawn, before `DrawnScale` | 1077 m | 1984 m | 861 m |

So it is **taller than it is wide**, which is the opposite of the anvil everyone pictures — the anvil
is megaton-scale and mostly later-time spreading. Anything flatter reads as a lampshade, because a
lampshade is exactly a cap that is wider than it is tall with its widest point along its lower edge.

Getting there is a change to what one pen *does*, not to any dimension. It walks a meridian of that
spheroid: up the axis to the pole, over the top, down the outside to the equator, and on past it
until the lip is back in underneath. That is the vortex ring's own circulation, which the previous
stroke gestured at and did not draw — and it is what fills a **volume** with a single stroke. A pen
that climbs and then flares can only ever sweep a surface at one height, which is a plate, and no
amount of drooping its edge makes a plate into a cap.

Two things fall out of it for free. The rings no longer need a crown lift to dome the top, since a
smaller spheroid inside a larger one is already lower; and the arrowhead cannot recur, since every
shell is concentric rather than stacked. `TheCapIsTallerThanItIsWide` is the guard, and it fails
against the lamp.

### The cloud is drawn smaller than the laws say, on purpose

`MushroomCloud.DrawnScale` is 0.65, and it is the only deliberate lie in that file. Every dimension
in it is Glasstone's and checks out against the one measured low-yield surface burst to within three
per cent, and the cloud still read as far too large for the burst that made it — twice, from two
different people, across four rounds of fixing everything *else* that was wrong.

At these yields it genuinely is that large: a 0.3 kt fireball is 110 m across under a 770 m cap, a
ratio of 1:7 that the test photographs agree with and that nobody watching a game believes. The
alternative was enlarging the fireball, which is worse — it is the one number a player can check
against a photograph, and it has already been taken to the top of its own ±25% provenance spread.

`CloudTop` and `CapRadius` still say what the laws say, so the reference tests still mean something;
`DrawnCloudTop` and `DrawnCapRadius` are what the drawing uses.

| 0.3 kt | law | drawn |
| --- | --- | --- |
| cloud top | 2.01 km | **1.31 km** |
| cap across | 0.77 km | **0.56 km** |
| fireball across | 0.11 km | 0.11 km, unscaled |
| fireball : cap | 1:7.0 | **1:5.1** |

**And the rise was too fast, which is a separate complaint with a separate cause.** `RiseSeconds`
went 22 → 38, so the compression against a real five-minute stabilisation is about eight times
rather than fourteen, and the mean drawn climb rate falls from 69 m/s to 26 m/s.

That change alone would have broken the handover, and the way it does is worth keeping: the flash
runs on **real** time while the cloud's clock is compressed, so stretching the cloud's clock leaves
it less developed at the instant the smoke takes over, and the ball barely leaves the ground.
`AxisHeight` therefore climbs as **√progress** rather than linearly in it — which is the classical
thermal result `z ∝ √t` anyway, and holds the ball's lift at 1.14 of its own radii across the
change. `TheFireballLiftsOffAndTheSmokeTakesOverWhereItIs` is what caught it.

### The rise curve is the wrong shape, measurably

`Rise` is an underdamped second-order step response: it accelerates from rest, overshoots and
settles. The reasoning was that a buoyant parcel starts at zero velocity, which is true and is not
what the measurements show at this scale. Teapot Wasp's cloud top was tracked by theodolite
(WT-1152, Project 9.4) and normalised against its own five-minute rise it goes as **√t**, which is
also the classical thermal result `z = 2.41 · B^(1/4) · √t` and the vortex-ring result:

| t / rise | 0.10 | 0.30 | 0.50 | 0.60 | 0.80 | 1.00 |
| --- | --- | --- | --- | --- | --- | --- |
| **measured** (Wasp, 1.2 kt) | **0.308** | 0.509 | 0.771 | 0.840 | 0.991 | 1.000 |
| `√(t/rise)` | 0.316 | 0.548 | 0.707 | 0.775 | 0.894 | 1.000 |
| as drawn | **0.088** | 0.514 | 0.888 | 1.001 | 1.092 | 1.078 |

So the cloud creeps for the first tenth of its rise where the real one has already done a third of
it, and then arrives early — it is at its ceiling by 0.6 where the real cloud is at 0.84. The
overshoot itself is real: Ruth and Post were both tracked peaking and then subsiding a few per cent.

**Not changed, because the one thing it is currently getting right is the handover.** The ball rides
this curve, so the slow start is what keeps its lift to 1.24 of its own radii while it is still
glowing — against 1.9 from the buoyancy laws and the "one to two radii, it is not a rocket" the VFX
literature gives. A `√t` rise puts it at 2.4 radii, which is still inside that range and closer to
the physics, so this is worth doing; it needs a form that rises as `√t`, overshoots by about a tenth
and settles, and that is a curve to design against a screen rather than in a document.

### What is still wrong with it

One thing came out of the same round of feedback as the skirt and is still not built.

**It was too symmetrical, and is now only barely so.** Every pen still sits at exactly `2πi/n`, but
neither the cap nor the skirt is a surface of revolution any more: `Lobe` pulls each ring out of
round on two harmonics, `PhaseOffset` runs each pen slightly behind its neighbours so the rim does
not reach every stage at one instant, and the skirt's *height* varies with bearing as well as its
radius, which is what stopped it reading as a machined disc under the cloud. A shape that is exactly
the same from every bearing is the tell that separates something generated from something
photographed, and the renderer's Worley erosion breaks up the *surface* without ever touching the
*silhouette*.

**The coverage cost of that is far smaller than it looks, and the reason is worth keeping.** Pulling
a pen inward also shortens the arc to its neighbour, so the radial separation a lobe adds is very
nearly cancelled by the pitch it removes. Measured across the whole stroke at 0.3 kt, against an
80 m limit:

| lobe depth | 0 | 0.10 | 0.20 | 0.25 | 0.35 |
| --- | --- | --- | --- | --- | --- |
| widest neighbour gap | 73 m | 71 m | 71 m | 72 m | 75 m |

So the constraint that looked binding is not, and the depth is chosen for how it reads rather than
for what closes. `ALobedRingStillCloses` holds the real limit and walks the whole stroke rather than
checking the equator, because the lobe moves the widest point around.

The axis is no longer vertical either: `LeanAt` shears every point downwind by height, super-linear
so the foot stays over the crater while the cap rides out. How far it may lean is bounded by the cap
having to stay over the column holding it up — `TheCapStaysOverTheColumn` holds that, and the margin
is worst at small yields, where the cap is narrowest against its own height.

**Baked paths instead of an analytic stroke.** The suggestion from play was VDBs, which is not
reachable: nothing in KSA loads a volume a mod supplies, and the four renderers above are the whole
list. But the *other* half of it is reachable and is the better idea: a pen takes an arbitrary
position per frame, so `CapPoint` could be a table sampled from an offline vortex-ring simulation
rather than a closed form. That would buy the one thing the choreography cannot fake, which is
turbulent detail in the *path*, and it costs a build step plus a data file. The two engine rules
still apply to whatever comes out of it, so the baked paths would have to be resampled to a spacing
that closes.
