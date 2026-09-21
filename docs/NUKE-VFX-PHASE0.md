# A raymarched nuclear effect: what a post-processing pass would buy

Phase 0 of a plan to draw the nuclear burst as a full-screen raymarch, with the volume injected
into KSA's frame through a ported copy of ShaderExtensions' Vulkan plumbing.

Findings are against KSA **2026.9.10.5438**, the build in `ksa-assemblies.lock`. Paths are relative
to `../ksa-game-assemblies/current/src`.

**The conclusion is that the plan's four phases are aimed at something already built, and that the
gap it would genuinely close is one the cheapest reachable renderer already covers.** The detail is
below; `docs/NUCLEAR-EFFECT.md` is the effect as it stands and is the thing to read first.

## The effect exists

`Sim/MushroomCloud.cs` is the choreography, `Ksa/NuclearClouds.cs` walks it with volumetric-trail
pens, and `Ksa/Fireball.cs` draws the flash as an emissive sphere with a real point light. The
dimensions are Glasstone's, the rise is calibrated against Teapot Wasp theodolite data, and the
handover from fireball to smoke, the ground skirt, the lobing and the downwind lean have each been
through rounds of play feedback. That is the plan's Phase 2 and most of Phase 3.

So the question this document has to answer is not "can a raymarched cloud be built" but "what does
one draw that the shipped one does not".

## What the shipped route cannot draw, and why

One thing, and it is structural rather than a matter of effort.

`CloudRenderer.RenderVolumetricTrailsWithUpscaling` takes an `AtmosphericBody`
(`KSA/KSA.Atmosphere.Rendering/CloudRenderer.cs:1079`) and is the only place the trail volume is
raymarched. The renderer is constructed holding the atmosphere and cloud renderers
(`KSA/KSA/Program.cs:1176`) and `FinalizeTrailSegments` is handed `GetCamera().NearbyCelestial`
(`Program.cs:2326`). **So a trail draws only over the camera's nearby atmospheric body, with clouds
and atmosphere both enabled, and nothing at all on an airless one.**

`PlumeSmoke`'s own summary already says this. What is new here is that `NuclearClouds.Begin` does
not test for it: it gates on `MushroomCloud.ThresholdKg`, a resolvable body and
`PlumeSmoke.Available`, then logs `nuclear cloud: <n> kt at <h> m altitude, rising to ...`. On the
Moon that line is written for a cloud the engine will never draw.

**That is a reporting bug worth fixing on its own**, and it is independent of everything below: the
log should say a burst made no cloud, and `ChaseView.LingerSeconds` should not hold the camera for
`MushroomCloud.RiseSeconds` on a burst with nothing to watch.

## The vacuum case mostly wants less, not more

The plan's Phase 4 asks for a mushroom cloud's absence in vacuum, which is what already happens, and
`Ksa/Fireball.cs` is not atmosphere-gated — it draws through `GenericMeshRenderer` and pushes a
light, so the flash and the fireball already work anywhere. What is missing on an airless surface is
the ballistic dust hemisphere, and on a vacuum burst the expanding plasma shell.

**Neither needs a post-processing pass.** `ParticleSystem.WriteCommandsColorOpaque` and
`WriteCommandsColorTranslucent` are called from the main render path (`Program.cs:4690, 4736`),
outside the atmosphere and cloud passes, so particles draw on airless bodies. The system is fully
public, the mod already spawns volumetric emitters through it, and its spawn volumes include a
sphere and a torus. A dust hemisphere that rises and falls back is emitter animation in C#, which is
where all of this mod's particle animation already lives.

## Plan A is feasible, and costs a patch in the renderer

Taking the questions the plan called blocking:

| | |
| --- | --- |
| **A post hook** | Still absent. `KSA.Rendering.PostProcessing` holds `Cmaa2Renderer` and `HableFilmicToneCurve` and nothing else; the corpus has no `GlobalPostShader` or equivalent. The `BLOCKED-ON-KSA.md` entry stands on this build |
| **Depth** | Reachable. `_offscreenTarget.DepthImage` is already transitioned to sampled reads for other passes (`Program.cs:4694, 4712, 4746`), and `PrePassRenderer.CopyDepthImageToSrc` exists (`:4623`) |
| **Depth convention** | **Reverse-Z**, via `RenderingPresets.ReverseZDepthStencil` at every pipeline; format is `Renderer.DepthFormat` |
| **HDR before tonemap** | Available. `_offscreenTarget` carries HDR, bloom runs at `Program.cs:4749` and the tonemap composite after it (`:4486`, `Profiler.GpuTag.TonemapComposite`) |
| **Per-frame hook and time** | Already solved for this mod. `KSArmoryMod.StepOnce` runs off `KsaWorld.ConsumeSimStep`, and `NuclearClouds` is already advanced on simulated time |

So the rendering questions all answer favourably. The cost is the one the plan does not price:
**there is no hook, so the pass has to be recorded by patching the render path.** That would be this
mod's fifth Harmony patch and the first outside the simulation frame order.

The four existing patches each degrade to something survivable — `PreRenderHook` falls back to the
frame postfix, `WorldReloadHook` costs one reload's stale rounds, `RoundBodyDrawHook` leaves the
engine's cull. A patch that records Vulkan commands has no such story: a mismatch against a moving
renderer is a corrupted frame or a device loss, not a log line, and `tools/api-surface.sh` reads
compile-time binds so a reflected render path is not covered by it.

## What this leaves

Three things are worth doing and none of them is the plan as written:

1. **Say nothing was drawn.** `NuclearClouds.Begin` should refuse on a body whose atmosphere the
   trail renderer will not draw over, and say so in the log, so the airless case stops claiming a
   cloud. `ChaseView.LingerSeconds` follows it.
2. **Draw the airless burst with particles.** The dust hemisphere and the plasma shell, through the
   system that already works there. This is the plan's Phase 4 at a fraction of its cost.
3. **Leave the atmospheric cloud alone.** It is calibrated and it is the part a raymarch would
   replace.

A ported post-processing pass is the right answer to a different question — the gunner's sight
through real optics, which is what the `BLOCKED-ON-KSA.md` entry was written about. Vendoring rather
than depending on ShaderExtensions is a genuinely new answer to that entry's recorded trade, and it
is worth reopening there rather than here.
