# Drawing a high-energy beam

How `Shaders/KSArmoryBeam.comp` draws the YAL-1's beam, and why it is drawn that way rather than as
a ribbon or as screen-space segments.

## What is seen

A beam in air is visible only by what it scatters toward the eye. Per metre of beam and per
steradian that is `P · Σ βᵢ pᵢ(θ)`: power, scattering coefficient, phase function. Seen across, the
radiance profile of a gaussian beam integrates over its width to `P β p(θ) / sin θ` — **independent of
the beam's diameter** (side-scatter lidar geometry, arxiv 1704.04417). Three consequences drive the
shader:

- **A far beam is a line source.** Once it is under a pixel, what it adds to that pixel falls as `1/r`,
  not `1/r²`, and its drawn width carries no information.
- **Viewing angle matters more than anything else.** Haze scatters forward (Henyey–Greenstein,
  `g ≈ 0.7`); seen looking back along the beam toward its source it is a few hundred times brighter
  than side-on, and the `1/sin θ` grows with it.
- **No air, no beam.** In vacuum nothing scatters and only the impact shows.

The COIL's 1.315 µm and the YAL-1's illuminators were all invisible to the eye. The green is a
stylisation; the physics above sets how it behaves, not its colour.

## The integral

Model the core as a gaussian tube of radius `w`, normalised to unit brightness across its width:
`E(r) = exp(−r²/w²) / (w√π)`. A view ray `v t` passes the beam's axis (start `A`, unit `a`, length `L`)
nearest at `t₀`, a distance `h` from it, at `s = sin θ`. Along the ray `r² = h² + s²(t − t₀)²`, so the
light along the stretch `[ta, tb]` is

`L = exp(−h²/w²) / (w√π) · ½[erf(s(tb − t₀)/w) − erf(s(ta − t₀)/w)] / s`

with `[ta, tb]` the part of the ray whose axial coordinate lies in `[0, L]`, in front of the eye and in
front of the depth buffer — so the ends and the occluding scene are exact, and the beam fades into a
surface rather than ending on it. `erf` is Abramowitz–Stegun 7.1.26.

**Thin and far:** `w` is drawn no narrower than 0.8 of a pixel at the nearest point (Persson's wire
anti-aliasing, GPU Pro 5). Because the profile is normalised across its width, widening it keeps the
energy and a far beam thins to a faint thread instead of shimmering or vanishing.

**Look:** the core, a tinted glow six times wider at a fraction of its energy, a ball of glow at the
window, and an incandescent ball where it burns — a target glows by its own temperature, not the
beam's colour. The balls are gaussians integrated the same way, so each has an edge the dispatch's
patch can contain; a `1/d²` point's in-scatter falls only as `1/h` and would show the patch.

## Along the beam

What it scatters follows the air: the density ratio is read at both ends on the CPU and blended
log-linearly between them in the shader, which is exact for a straight climb through an exponential
atmosphere, times what the air has already taken out of the beam (`exp(−k·ρ̄·u)`). A beam fired at the
sky therefore fades as it climbs rather than stopping at its 300 km cast, and one that meets nothing
also fades over the last 15% of its length. Seen down its length the `1/sin θ` term is held to three
times the side-on brightness and the phase to four, because uncapped they white out the picture.

## Clouds: tried, and taken out

A cloud stops a real beam within metres and glows where it enters. That was built by reading KSA's
weather layers in the beam shader, as `KSArmoryCloud.comp` does — the layer buffers bound in the pass's
own set and each layer's coverage map read through the bindless texture array by the id the buffer
carries. **Flying it was followed by a 0x116 VIDEO_TDR_FAILURE on the developer's machine**, the GPU
hung and the driver unable to reset it. A wrong binding or a placeholder buffer makes that id garbage,
and an out-of-range bindless index is exactly such a hang. The shader now reads its push constants,
the scene and the depth and nothing else. Clouds come back only with the coverage read somewhere a bad
id cannot reach the GPU — the CPU, or the cloud pass's own already-proven bindings.

## Why not the others

- **Camera-facing ribbon:** collapses looking along the beam — exactly the view from beside the
  emitter — and needs a width that interpolates linearly in screen space while perspective is
  hyperbolic.
- **Screen-space segments** (what the tracer pass does for shells): the width is right only at the
  segments' ends, and a beam crossing the near plane mismeasures it. The first GPU beam was drawn
  this way and came out either a wall or a pencil line.
- **Cylinder mesh:** a hard silhouette side-on, a ring end-on, and aliasing far away.

## Sources

Quílez, ray–primitive intersectors (iquilezles.org/articles/intersectors); Persson, wire anti-aliasing
(GPU Pro 5; geeks3d.com/?p=8334); Sun et al. 2005, analytic single scattering; Henyey–Greenstein and
haze `g` (arxiv 1311.5338); Rayleigh and Koschmieder visibility (`β = 3.912/V`); aerosol scale height
1–2 km (arxiv 2205.04296); side-scatter radiance of a laser beam (arxiv 1704.04417); why beams look
brightest near their own axis (rp-photonics.com/spotlight_2010_01_11.html).
