# The nuclear effect: what the engine offers, and what a mushroom cloud needs

Research for making a nuclear burst look like one. Four questions were asked in parallel: how KSA
draws its SRB plumes, how it draws clouds, what its particle system can express, and what a real
mushroom cloud actually looks like. This is the result, kept because re-deriving it is a day's work.

**Nothing here is built.** It is the map, not the road.

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
fireball radius, airburst   = 55 · W^0.4  m          (surface burst ×1.32)
cloud top, stabilised       = 3.0 km · W^(1/3)       (below ~10 kt)
cap radius, stabilised      = 0.6 km · W^0.37
cap base                    = 0.5 · cloud top
stem radius                 = 0.5 · cap radius       (below 20 kt)
second thermal maximum      = 0.0417 · W^0.44 s      (the blinding flash)
thermal pulse over          = 0.417  · W^0.44 s
fireball goes dark          = 3.0    · W^0.4  s
initial rise rate           = 25     · W^0.2  m/s
```

For the yields this mod can dial:

| | 0.3 kt | 1.5 kt | 10 kt | 50 kt |
| --- | --- | --- | --- | --- |
| Fireball radius (air) | 34 m | 65 m | 138 m | 263 m |
| Flash over | 0.25 s | 0.50 s | 1.15 s | 2.33 s |
| Goes dark | 1.9 s | 3.5 s | 7.5 s | 14.5 s |
| **Cloud top** | **2.0 km** | 3.4 km | 6.5 km | 9.8 km |
| **Cap radius** | **0.39 km** | 0.70 km | 1.41 km | 2.35 km |
| Stem radius | 0.19 km | 0.35 km | 0.70 km | 1.18 km |
| Stabilises | 3.4 min | 4.2 min | 5.4 min | 6.0 min |

**Three things worth knowing before building anything:**

**The shipped fireball is about five times too large.** `Warhead.FireballRadius` is Hopkinson-Cranz,
`2.6 · W^(1/3)` in kg, which is right for chemical explosives. A nuclear fireball goes as `W^0.4`
with a different constant: 0.3 kt gives 34 m against the mod's 174 m. Whether to correct that is a
gameplay decision, not a physics one — the damage radii are calibrated separately and are roughly
right.

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
2. **A thin stem that lags.** The stem is not fireball, it is dirt lifted by afterwinds. It starts
   1–3 s later, climbs at a third to a half of the cap's rate, and never catches up. Half the cap's
   radius. This is the cheapest cue that separates a mushroom from a plume.
3. **The colour ramp**: white-hot → orange → dark red → **reddish-brown** (nitrogen oxides, the
   phase everyone forgets) → white as water condenses, with grey-brown in the stem for a surface
   burst.
4. **Base surge**: a dense dust torus rolling outward along the ground from ~10 s. Surface bursts
   only, and it sells scale because it interacts with the terrain.

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
