# Where KSA thinks the ground is

Everything below comes from the decompiled corpus at `../ksa-game-assemblies` against build
**2026.8.19.5261**, from the shipped `Content/Core/Astronomicals.xml`, and from measurements taken
directly off the shipped `Earth_Height.ktx2`. Nothing here has been flown.

**Why it matters now.** A MIRV shot lands at about **0.65 km** from a **six-to-seven-degree**
arrival. At that angle one metre of disagreement about where the surface is costs **9.3 m of
ground** — measured, `SurfaceAgreementTests.AMetreOfHeightIsCotangentMetresOfGround`, 11.1 m at
five degrees and 2.7 m at twenty. So a surface question that would be a rounding on a steep arrival
is a first-order term on this one.

Three things in this mod sample the surface and they have to agree:

| | call | surface |
| --- | --- | --- |
| `Ksa/GroundTest.cs` | `GetTerrainHeightFromDirCce(dir, accurate: true)` | where the round stops |
| `IcbmComputer.TerrainRadiusAt` | `GetTerrainHeightFromDirCcf(dir, accurate: true)` | what the prediction flies against |
| `IcbmComputer.SurfacePointEcl` | `GetTerrainHeightFromDirCcf(dir, accurate: true)` | where the aim point is placed |

Two use `Ccf` and one uses `Cce`. **That is correct** — see [Cce against Ccf](#cce-against-ccf).
All three clamp to the waterline; see [The sea](#the-sea-all-three-clamp-to-it) for why that matters
more than it sounds.

## The field itself

`Celestial.GetTerrainHeightFromDirCcf` (`Celestial.cs:792`) is the only height query in the engine.
Every other entry point funnels into it: `…FromDirCce` (`:780`) and `…FromDirCci` (`:786`) rotate the
direction into `Ccf` and call it; `GetSurfacePositionEclFromDirCce` (`:770`) calls it and multiplies
out to a position.

It is a **cubemap lookup plus a procedural stack**:

```csharp
if (!HasTerrainHeightmap) return 0.0;
double num2 = MathEx.Lerp(a: HeightReference.Minimum, b: HeightReference.Maximum,
                          t: accurate ? SampleHeightBicubic(...) : SampleHeightMapBilinear(...));
if (NormalReference is null || !NormalReference.TextureAsset?.Texture.IsCubemap) return num2;
...
if (BiomeMaterials?.NumBiomeMaterials is not > 0) return num2;
... for each terrain modifier ...
    if (accurate) modifierReference.Evaluate(..., ref heightKm, ref gradient);
return heightKm;
```

For Earth (`Astronomicals.xml:522`):

| | |
| --- | --- |
| height map | `Earth_Height.ktx2`, **4096 × 4096 per face**, 6 faces, `R16_UNORM`, 13 mips, uncompressed (268 MB) |
| range | `Minimum Km="-10.930"`, `Maximum Km="8.631"` — `DistanceReference` multiplies `Km` by 1000, so **metres** |
| modifiers | six: **Erosion** (amplitude **1000 m**, 7 octaves), four **TilingDetail** (**1900**, **1500**, **1400**, **225 m**) and **Dunes** (**1500 m**) |
| biomes | seven, with a `<BiomeMaterials>` block — so the modifier loop **does** run on Earth |
| ocean | `<Level Km="0"/>` |

### Resolution

A cube face is a gnomonic projection of a 90° quadrant, so texel spacing on the ground is not
uniform. On Earth (`MeanRadius` 6,371 km, 4096 texels across a face):

| | ground spacing |
| --- | --- |
| face centre | **3,111 m** |
| face edge midpoint | 1,556 m and 2,200 m on the two axes |
| face corner | **1,466 m** |

So the *base* field is a 1.5–3.1 km grid. Everything finer than that on Earth comes from the
procedural modifiers — which are analytic, but **not** unlimited in resolution: they are evaluated
on a direction packed to `float3`, which is a floor of about a third of a metre of ground. See
[The modifiers run in single precision](#the-modifiers-run-in-single-precision) for that and
[Where the detail actually comes from](#where-the-detail-actually-comes-from) for what fills the
scales in between.

### Quantisation

`R16_UNORM` over a 19,561 m declared range is **0.2985 m per level**. That is the floor under any
answer this field can give, and at a six-degree arrival it is **3.40 m of ground** —
`SurfaceAgreementTests.TheHeightFieldsOwnQuantumIsMetresOfGroundAndNoMore`. The procedural
modifiers are continuous and add on top, so the *returned* height is not quantised; the base term is.

### The modifiers run in single precision

**The base bicubic is `double` end to end; the procedural stack on top of it is not.** On a body with
a normal cubemap *and* biome materials — Earth has both — `GetTerrainHeightFromDirCcf` packs the
direction down before the modifier loop:

```csharp
float3 float6 = float3.Pack(in vector);        // Celestial.cs:1637
float heightKm = (float)num2;                  // Celestial.cs:1650
modData = new ... { Position = float6, TextureNormal = float6, ... };
```

`float3.Pack(in double3)` defaults to a plain cast, so the direction every modifier is evaluated at
is a single-precision unit vector. Its neighbours are `2^-24` to `2^-23` apart, which on Earth is
**0.38 m to 0.76 m of ground**; measured by walking a great circle and counting distinct packed
directions, the tread is **0.31 m** and the worst displacement over 20,000 random directions is
**0.307 m** (`KineticFloorTests.TheProceduralTerrainIsEvaluatedOnAFloatDirection`).

So below about a third of a metre the modifier stack answers with one value: **the surface is a
staircase.** It is deterministic and identical for every caller, so it biases nothing — the round,
the prediction and the aim point all read the same treads. What it does is put a floor under how
finely the surface can be asked about at all. The base term is unaffected: `SampleHeightBicubic` is
`double` throughout and its texel samples are exact 16-bit integers.

A body with no normal map returns before any of this, in `double`.

### Where the detail actually comes from

Between the base grid and that staircase there are two mechanisms and one gap, all on Earth:

| scale | what shapes it |
| --- | --- |
| coarser than **3,111 m** | the base cubemap, Catmull-Rom |
| 3,111 m down to **166 m** | `EarthErosion` — 7 octaves, lacunarity 2, gain 0.5, sampled at `direction x 600 x 2^i`, so 10.6 km down to 166 m of wavelength at 500 m down to 7.8 m of amplitude |
| 166 m down to **7-19 m** | the four `TilingDetail` modifiers — each a 4096-square `R16` texture whose UV is `direction x Frequency`, giving 7.4 m/texel (Alpine, f=209) to 20.5 m/texel (Desert Mountains, f=76) |
| **10 m down to 0.38 m** | nothing: a bilinear ramp between two detail texels, which is locally a tilted plane |
| below **0.38 m** | the float staircase above |

Each erosion octave carries an undamped slope of up to **0.30** — amplitude halves as frequency
doubles, so every octave contributes the same. What survives is that times the biome weight, times a
gradient-falloff power of the angle between the texture and surface normals, times `1 - |dot|` of
those two again, all near zero over flat ground. **The product is unmeasured here**; only the
geometry is.

`docs/KINETIC-FLOOR.md` has what all of this is worth to a round trying to land on a point.

### Interpolation

- `accurate: false` → `SampleHeightMapBilinear(TextureReference, double3)` (`Celestial.cs:1077`).
  Four texels, `-0.5` texel offset, and a proper cross-face wrap through `GetAdjacentFaceUv` for taps
  that fall off the edge.
- `accurate: true` → `SampleHeightBicubic(TextureAsset, direction, lod: 0)` (`Celestial.cs:1585`).
  Catmull-Rom over a 4×4 texel neighbourhood, each tap a point fetch through `SampleCubeFacePointR`,
  seams handled by re-deriving the direction and re-projecting. `lod` is a parameter and is always
  passed 0 — there is no exposed way to ask for a coarser one, and `SampleCubemapFaceSingleChannel`
  reads mip 0 regardless, so a non-zero `lod` would be a latent bug rather than a feature.

The two face-selection routines are written twice — `DirectionToCubemap` (`:1454`) for the bicubic
path and an inlined copy inside the bilinear one — and they **agree**, face for face and axis for
axis. Checked term by term.

**Measured disagreement between them** on the shipped Earth cubemap, 199,154 directions drawn
uniformly on the sphere, sampled well inside a face so the seam paths never differ:

| | height | ground at six degrees |
| --- | --- | --- |
| mean | 4.21 m | 39 m |
| median | 1.21 m | 11 m |
| 99th percentile | 44.0 m | 410 m |
| max | **186 m** | **1.73 km** |

Above sea level only, the numbers are much the same (mean 4.37 m, max 167 m). **And that is the
interpolation alone.** `accurate: false` *also* skips every `Evaluate` call in the modifier loop, so
on Earth it drops erosion, tiling detail and dunes entirely. Their declared amplitudes total several
kilometres; what any one direction actually loses is that scaled by biome weight, by the noise value
and by the slope factors inside `Evaluate`, so the real figure is unmeasured here and somewhere
between hundreds of metres and kilometres of height where those biomes apply. Either way it dwarfs
the interpolation term, and on a shallow arrival it is tens of kilometres of ground.

### `accurate: false` is not as cheap as it looks

The early returns are before `SampleNormalMap`, not before the loop. On a body that has a normal
cubemap **and** biome materials — Earth does — `accurate: false` still pays for the normal-map
bilinear fetch, the tangent frame, the biome ID and control samples, and one LUT evaluation per
modifier. All it saves is the bicubic (16 taps against 4) and the modifier `Evaluate` bodies, which
for Earth's erosion is seven octaves of gradient noise. That is the expensive part, so the saving is
real — but a body with no normal map returns after the interpolation and the two paths then differ
only by 12 texel fetches.

`Ksa/TerrainHeights.cs` is the mod's only `accurate: false` caller — the radar's horizon mask, asked
tens of times per contact per scan. KSA's own `TerrainImpactFinder` makes the same choice for the
same reason (`TerrainImpactFinder.cs:64`).

## Is `accurate: true` the surface the game itself uses?

**For physics, yes — the identical call.**

- `PhysicsEnvironment.RecomputePositionalValues` (`PhysicsEnvironment.cs:112`) sets
  `TerrainRadius = MeanRadius + GetTerrainHeightFromDirCcf(dir)`, default `accurate: true`.
- `TerrainPatch.GetTerrainRadiusCcf` (`TerrainPatch.cs:518`) does the same, and
  `GetTerrainVertexCcf` places every vertex of the collision patch on it. The patch grid step is
  **2 m** (`TerrainPatch.UpdateState` initialises with `2.0`), so the surface a craft rests on is a
  2 m triangulation of exactly the field the mod samples continuously. Against the finest erosion
  octave above, the chord error of that triangulation is of order **a centimetre**.

So a landed craft and a round that stops on `GroundTest`'s answer are standing on the same surface.
There is no separate collision height field.

**For rendering, the same field through a second implementation.** The terrain mesh is displaced in
`Content/Core/Shaders/Planet/TerrainMesh/PrepareModifiers.comp`, which reads the same cubemap with
`SampleHeightmapBicubicTrilinear(planetHeightMap, …, mipLevel)` — bicubic like the CPU, but at a
**mip level derived from the vertex spacing**, and then runs the same modifier set in
`ProceduralModifiersLibrary/ProceduralModifiers.comp`. Two consequences:

- Where the mesh is coarse — far from the camera — the drawn surface is a filtered, smoother version
  of what the CPU reports. Close up the mip goes to zero and the two converge.
- The modifiers are implemented twice, in C# and in GLSL. They are the same functions with the same
  parameters, but they are not the same code and nothing enforces that they stay in step.

The practical rule: **the CPU `accurate: true` answer is authoritative for where things happen, and
the GPU is a separate rendering of it.** A warhead that bursts where `GroundTest` says will not be
visibly floating, because the physics agrees; whether it is a pixel above or below the drawn mesh at
a given camera distance is a rendering question.

## Cce against Ccf

**Not a bug. The mixed use is correct, and the two entry points are not interchangeable.**

| frame | what it is |
| --- | --- |
| `Ccf` | body-**f**ixed. Rotates with the planet. `+Z` is the spin axis. |
| `Cci` | body-centred **i**nertial. Same origin, does not rotate. |
| `Cce` | body-centred, **e**cliptic axes. Same origin, ecliptic orientation, so a *direction* in `Cce` is the same direction in `Ecl`. |

`GetTerrainHeightFromDirCce` (`Celestial.cs:780`) is one line plus a delegation:

```csharp
double3 positionDirCcf = positionDirCce.Transform(GetCcf2Cce().Inverse());
return GetTerrainHeightFromDirCcf(positionDirCcf, accurate);
```

So the `Cce` entry point applies the body's rotation *for you*. That is exactly what `GroundTest`
needs, because it starts from `unit(positionEcl − centreEcl)` — an ecliptic direction with no
rotation taken out. `TerrainRadiusAt` and `SurfacePointEcl` already hold body-fixed directions (one
from `GetCci2Ccf()`, one from `GetDirCcfFromLatLon`), so they use the `Ccf` entry point and must.
Passing either direction to the other entry point would rotate it by the planet's current phase —
the full 465 m/s × (time since epoch) error the question was worried about — but nothing here does.

The rotations all come from one per-frame snapshot and cannot drift against each other:
`UpdatePerFrameData` (`:594`) sets `_ccf2Cci = GetCcf2Cci(Orbit.StateVectors.StateTime)` and
`_ccf2Cce = _ccf2Cci * _cci2Cce`, and `Universe` calls it once per frame after applying the step
(`Universe.cs:1714`). `GetCci2Ccf()` and `GetCcf2Cce()` are both derived from that same `_ccf2Cci`,
so the `Cce` path and the `Ccf` path are in phase by construction.

## The round trip is exact

`SurfacePointEcl` places the aim at

```
dirCcf(lat, lon) · (MeanRadius + h(dirCcf)) · Ccf2Cce + bodyEcl
```

and `GroundTest` recovers `unit(pos − bodyEcl)` = the same `Ccf` direction after
`Cce2Ccf`, so it reads the same `h`. `Celestial.GetDirCcfFromLatLon` (`:670`) and
`GetLatitudeFromCcf`/`GetLongitudeFromCcf` (`:708`, `:743`) are exact inverses —
`(cos φ cos λ, cos φ sin λ, sin φ)` against `asin(z)` and `atan2(y, x)` — and both sides use the same
cached `_ccf2Cce`, so the trip closes to floating-point.

Nothing rounds the stored value. `Sim/AimSite.cs` holds `double` latitude and longitude;
`Describe()` formats to three places for display only. Worth keeping in mind that it is *display*
only, because 0.001° of latitude is **111 m** of ground — a designation typed to three places rather
than picked would carry that.

## The sea: all three clamp to it

**All three route the height field through `GroundSurface.Height`**, so the surface the round stops
on, the surface the prediction flies to and the surface the aim point sits on are one surface over
water as well as over land.

```csharp
// Ksa/GroundTest.cs
if (nearest.GetOceanReference() is { } sea && sea.Density > 0.0) { hasSea = true; seaLevel = sea.Level; }
height = GroundSurface.Height(height, seaLevel, hasSea);

// Ksa/IcbmComputer.cs -- SurfaceHeight, called by TerrainRadiusAt and by SurfacePointEcl
return body.GetOceanReference() is { } sea && sea.Density > 0.0
           ? GroundSurface.Height(terrainHeight, sea.Level, hasSea: true)
           : terrainHeight;
```

The height field answers with **terrain**, which under an ocean is the seabed, so without the clamp a
contact-fused round falls through the waterline and bursts on the bottom while the prediction of it
agrees and reports zero miss. `SurfaceAgreementTests` is what holds the three together, and the
numbers below are what the disagreement was worth when there was one.

**What it would be worth.** Measured over all 100,663,296 texels of `Earth_Height.ktx2`:

| | |
| --- | --- |
| below the waterline | **71.2%** of the surface |
| mean depth | 3,776 m |
| median depth | 4,180 m |
| deepest | 10,930 m |

At the flown arrival angle,
`SurfaceAgreementTests.ThePredictionFliesToTheSeabedAndTheRoundStopsOnTheSeaAboveIt` measures the
mean depth as **35.0 km of ground**. The median is worse. The deepest trench is about 100 km.

Two shapes it takes, and both are what the clamp exists to stop:

- **An aim point over water** placed on the seabed is a point no round can reach. A prediction that
  agrees with it makes `AimCorrection` converge and report zero while the warheads splash tens of
  kilometres short — the same failure mode `docs/ICBM-GUIDANCE.md` records for drag, *a correction
  loop can only remove what its observer can see*, with a different blind spot.
- **A coastal target approached from the sea.** The last few kilometres of a six-degree arrival are
  only hundreds of metres up, so where the arc crosses the waterline short of the shore the round
  bursts on the sea and an unclamped prediction carries on to the seabed tens of kilometres inland.

Over dry land the terrain is above the waterline and the clamp is a no-op either way, which is why a
purely inland shot never showed it.

**One residual, and it is small.** KSA's own physics uses `OceanRenderer.GetOceanHeightAtPositionCcf`,
the *displaced wave* surface, not the flat level (`PhysicsEnvironment.cs:120`). The mod uses
`OceanReference.Level` on all three paths, so they agree with each other and not with the engine.
Waves are metres, which is tens of metres of ground here — below the noise, and the reason the mod's
surface and KSA's will never agree exactly.

## Is there anything more exact?

**No.** `accurate: true` is the most exact surface query a mod can reach. Everything else in the
engine either calls it or is coarser:

| | |
| --- | --- |
| `GetTerrainHeightFromDir{Ccf,Cce,Cci}(dir, accurate: true)` | the field itself, bicubic at mip 0 plus every modifier. **This is the ceiling.** |
| `GetSurfacePositionEclFromDirCce(dir, accurate)` | the same call, returned as a position |
| `Celestial.SampleHeightMapBilinear(TextureReference, double3)` | `public static`, raw base texture, no modifiers — strictly coarser |
| `Celestial.SampleCubemapFaceSingleChannel(asset, texel, face)` | `public static`, one raw texel |
| `TerrainImpactFinder.TryFind(body, trajectory, …)` | `public static`, does the crossing bisection for you — but samples `accurate: false` internally, so it is coarser than `Sim/ImpactPredictor.cs` already is. 24 bisection iterations plus a coarse march. |
| `TerrainPatch` / the physics collision mesh | its public entry points want a `ReadOnlyPhysicsStates` ref struct a mod cannot construct, and its vertices come from `accurate: true` anyway |
| `KSA.Rendering.BoundingVolumeHierarchy` | a builder for the raytracing acceleration structure, not a query API; its terrain triangles come from the same call (`:651`) |

There is **no raycast**, no collider query, and no higher LOD. The cost of the exact call is roughly
16 texel fetches for the bicubic plus the normal, biome and LUT work plus the modifier evaluation —
for Earth, seven octaves of gradient noise and four texture-driven detail lookups. `ImpactPredictor`
already only asks near the surface (`SurfaceUnder` returns the mean sphere above
`SurfaceRadius + 12 km`), which is what makes it affordable.

## Two smaller things worth knowing

**The mod's terrain-mask ceiling is a sample, not a bound.** `KsaWorld` passes
`body.MaxTerrainHeightApprox` to `TerrainMask.Blocked` as the sphere that contains all terrain.
`Celestial.UpdateApproxTerrainAltitudes` (`:931`) computes it once at construction from **16,384**
Fibonacci-spiral directions at `accurate: true` — about one sample per 5.6 km on Earth. So it can
undershoot the true maximum, and CLAUDE.md's *a sphere containing the terrain cannot produce a false
negative* holds only up to that sampling. The alternative, `Astronomical.MaxTerrainRadius`
(`Astronomical.cs:116`), is `MeanRadius + HeightReference.Maximum` — a hard bound on the base texture
that does not bound the modifiers standing on top of it. Neither is a guarantee.

**The round samples the ground once a frame and holds it across every sub-step.** `Sim/Slug.cs` calls
`Ground.TryGround` before the sub-step loop by design — the sample is the expensive call, and
`IGroundTest` answers as a centre and a radius precisely so the sub-steps cost a subtraction. The
consequence on a shallow arrival is that the round stops on the surface as it was up to one frame of
ground track behind: with slope `s` and arrival angle `γ`, the error is about `s·Δ / (tan γ + s)`
where `Δ` is the ground track covered in a frame. At an impact speed near 1.8 km/s and 60 fps that is
30 m of track — about 9 m of stopping error on a 5% slope — and it grows in proportion to the step,
so it is one more thing that gets worse under timewarp. `Sim/ImpactPredictor.cs` re-samples every
integration step, so the prediction sees the terrain more finely than the round does.
