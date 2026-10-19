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

## Clouds

**Hidden behind them.** KSA's cloud renderer hands over, for the main view, one distance (km) and
one transmittance per pixel for all its layers together. The beam pipeline binds those two images
— plain sampled images, nothing indexed — and splits each pixel's tube integral at that distance:
whole in front, times the transmittance behind. The transmittance is raised to the eighth power first,
because it is drawn to look right against a sky, and a cumulus is tens of optical depths to a beam:
at KSA's own value the beam stayed saturated through the thick of a deck. Haze at 0.95 still lets
two-thirds through. Flown up through broken cumulus, the beam went out where it entered the deck and
showed again in the gap above. Other views and a world with no weather bind the pass's own distance
layer as a stand-in, and the push's `A.w` says not to read it.

**Stopped by overcast, and glowing where it enters.** A cloud deck stops a real beam within metres,
and the light it scatters lights the cloud base from below. That is decided on the CPU, because it has
to cut the damage as well as the drawing: `Ksa/CloudLayers.cs` reads each layer's coverage map off disk
once, through the game's own libktx (Earth's are zstd-packed BC4 cubemaps, read at 2048 a face, about
5 km a texel), and turns it by the layer's drift exactly as `CloudLayerRenderData.UpdateCommonFrameData`
does. `Sim/CloudCover.cs` marches the beam through each layer's shell and stops it at the first point
over the threshold, below a top that rises with the coverage. A coverage grid round a craft against the
view from 1,500 km up matched band for band.

The map places cloud *fields*, not the puffs in them: those are KSA's detail tiles and GPU noise, which
would need its private tiling frame read by reflection and still miss by hundreds of metres. Under
broken cumulus the map read 0.7 everywhere, gaps and all, so a threshold there stopped the beam in
clear sky. It is **0.8** -- overcast, which read 0.8-0.9 -- and broken cloud is left to the hiding
above. Cirrus is a few optical depths, not tens, so a body's thin layers are named in `Bodies.xml`
(`ThinCloud`) and stop nothing.

The glow is a 40 m core and a halo of light in the cloud just above its base: falling away over 400 m
across, exponentially rather than as a gaussian so it has a tail and no rim, over 150 m up into the cloud,
and none below, so from underneath its bottom is flat and from afar it is a lit patch on the ceiling
rather than a ball under it. It is integrated along each pixel's ray by midpoints and masked by the
drawn cloud's opacity there, so it lights the cloud KSA drew and not the gaps. The tile it is drawn
over is sized at the nearest the halo comes to the eye, since a disc's near side is the larger.

**Never through a bindless index.** The first try at a cloud stop read KSA's weather layers in the beam
shader, as `KSArmoryCloud.comp` does — the layer buffers bound in the pass's own set and each layer's
coverage map read through the bindless texture array by the id the buffer carries. **Flying it was
followed by a 0x116 VIDEO_TDR_FAILURE on the developer's machine**, the GPU hung and the driver unable to
reset it. A wrong binding or a placeholder buffer makes that id garbage, and an out-of-range bindless
index is exactly such a hang. So nothing the beam shader reads is indexed by a number from somewhere
else, and the stop is the CPU's.

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
