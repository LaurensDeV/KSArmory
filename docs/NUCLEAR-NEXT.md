# What the nuclear effect still needs

**The backlog, and what came of it.** Most of it is built now, each item marked where it stands
with what was flown; what is not built says why. `docs/NUCLEAR-EFFECT.md` is the research — which of
KSA's renderers a mod can reach, and what a mushroom cloud actually looks like — and `CHECKLIST.md`
§7.1f4 is what has been flown.

## The shape of the gap

**The rise is the part that is finished.** A column grows, rolls over on its own vortex, shears
into a corkscrew, sheds fallout, stands, dissolves, and burns the ground under it — all of it lit
by the engine's own atmosphere, and all of it flown at two yields and on two bodies.

What is thin is the **first second** and the **last minute**.

Nearly every signature that makes a burst read as *nuclear* rather than as a large explosion lives
in the first second: the double thermal pulse, the shock front going opaque and then transparent
again, the condensation ring, the fireball becoming the thing that rises. The mod draws one flash
that decays, and a ball that contracts into its own smoke.

And a real cloud does not fade where it stood. It drifts, it spreads under the tropopause, and its
fallout runs downwind for tens of kilometres. The mod's cloud leans in a wind that never moves it.

---

## Against the real thing

Where the drawn burst still differs from a low-yield surface burst on film — the Nevada tower and
ground shots — phase by phase, as it stood on 2026-09-22. The dirty first ten seconds is the
largest gap.

**The first second.**

| Real | Drawn |
|---|---|
| The fireball's surface is mottled, but only in sub-millisecond rapatronic frames; to a real-time eye it is a smooth, blinding ball, and a tower shot throws spikes where the guy wires flash off | **A smooth, opaque ball of its own light while white-hot** (item 1b), with the fire pockets and the soot arriving as it cools; a debris shell on the ball's own skin was built and taken out, because the same frame with it and without it could not be told apart. No guy-wire spikes |
| The air round the ball glows violet for an instant, strongest at night | **Built**, and mostly in the whiteout: from far off the whole blinded view is lavender, lilac round the burst and deeper toward the edges (a film of a Nevada shot, sent on 2026-09-23). The veil keeps the violet of the light that drove it through its recovery, so a night burst clears through lavender to purple over three seconds; the halo round the ball warms with the ball. A faint violet aura round the young cloud besides, summed analytically along each ray |
| Heat and the shock front visibly bend the view round the ball | Nothing refracts |
| A ring of dust races out along the ground behind the shock | **Built**, in the raymarch: a low wall lifted behind `MushroomCloud.ShockRadius`, thinning and climbing behind the front, out to about where it falls to a pound per square inch |
| The flash throws hard shadows across everything in sight | **The light is built; the shadows are not, by what the engine allows.** The ball and its light were not drawn at all unless one of the mod's overlays happened to be on screen: `Fireball.Draw` converted through the overlay's draw anchor, which is cleared every frame. Fixed on 2026-09-23, it is placed against the render camera as the cloud is, and reaches 60 ball radii. Flown at night with the camera turned away: a forest 350–700 m out lit green by the flash at 0.8 s, orange as the ball cools, dark by 5 s, and faintly lit from 1.5 km. **No shadows**: the terrain takes this light through KSA's forward list, which has none, so a shadow map would render the landscape into a cube every frame for nothing on the ground |
| Flash blindness lasts seconds, far longer at night | **Built**: recovers on 0.28 s by day and 2.2 s in full night (`FlashGlare.RecoverySeconds`); flown at night, 0.57 at 1.2 s and 0.14 at 4.3 s |

**The first ten seconds.**

| Real | Drawn |
|---|---|
| The ball swallows the ground under it and rises wrapped in churning black-brown dust and smoke, orange glow showing through gaps as it boils | **Built.** Once the ball has left its white-hot pulse, the young cloud is soot that clears over about ten seconds, and the heat left in its core (`MushroomCloud.Incandescence`) glows through where the noise thins the shell |
| Reddish-brown from the nitrogen oxides the heat makes, lasting after the glow | **Built.** The cap is tinted brown from the second second, fading over about twenty |
| The ball hollows into a glowing ring as its underside is drawn up, a doughnut of fire turning over | **Built.** The glow moves from the core onto the ring over four seconds, running together round it as it does, and rolls with it; seen from low and close it reads as a torus edge-on by 4.4 s, bright at both limbs with a darker middle. From underneath, judged on 2026-09-23 from just outside the stem's flared foot, looking up: the fire first covered the whole underside, because early the cap's ring is no wider than its own tube; it is a narrower band round the ring's core now, kept off the axis where cooler air is drawn up the middle, and reads as brighter sides round a dimmer centre. Not a crisp doughnut: by then the core is faint and the soot shell hides most of it |
| Violent churning at every scale, rising at hundreds of metres a second at first | **Built.** The roll and the boil start six times the steady rate and settle over about six seconds |
| A crater, and dark jets of thrown dirt | Neither; a crater is blocked on the engine (item 12) |
| Smoke from what the flash set burning | **Built and taken out on 2026-09-23, on request.** Nine plumes scattered round the burst read from the ground as a ring of evenly spaced smokestacks with nothing burning at their feet, and not as part of the burst. The burn mark stays |

**The first minute.**

| Real | Drawn |
|---|---|
| Condensation rings form round the stem at moist layers, and a smooth ice cap can sit over the top | **Rings built**: two ragged white collars low on the stem, formed as the cap climbs past and gone by 40 s. **The cap veil is built and not seen**: at 0.3 kt it lies inside the eroded crown |
| White condensation cap, dirty-brown underside, dark brown stem | **Built**: the crown whitens from 6 s as the soot clears, over the brown underside. Hard to see under an overcast, which lights the whole cloud flat |
| Often a thin, tall stem, with dust visibly drawn up into the cap's underside | **Built**: the stem is 0.19 of the cap's radius, and its top flares into a trumpet of streaked dust drawn up into the underside (`InflowDensity`), plain by 25 s |
| Past the tropopause the cap spreads into an anvil | Built (item 5) |
| Fallout curtains take minutes to show | **Built**: nothing until 18 s on the rise's clock, full by 32 |

**After that.** A real cloud lasts tens of minutes to hours, drifting, shearing and spreading into
a long plume, with a haze of dust over ground zero. **The drawn one stands four minutes now**, where
it burst — still by decision (item 3), and asked for longer on 2026-09-23: over the stand the cap
spreads by 60% and thins, the stem narrows away first, and the whole fades out over the last minute,
gone at 278 s. Flown at 10x: a full mushroom at 49 s, a wider cap on a thinner stem at 109, a broad
flat cap on a thread at 189. It does not drift, but it **shears**: over the stand its upper part is drawn out downwind to 2.2 times its reach on that side (`AgedShear`), the upwind edge and the foot unmoved — at 200 s the cap is half as long again downwind, the same paused frame with the shear at zero as the control. The march's bound grows with it, so the lean, which is reckoned against the bound, is about a quarter weaker by the end of the stand. **It costs the pass the
whole time it is on screen**, and a six-warhead bus stands six of them.

**Still not built, and why.**

- ~~**KSA's weather does not shade the cloud.**~~ **Built on 2026-09-23.** The burst and
  its dust ring are shaded by the weather's own shadow data now, read the way KSA's ground reads it, and
  what the deck takes from the sun comes back as its grey glow (`DeckGlow`). Under a heavy overcast
  the column went from the brightest thing in a grey scene to a soft grey-beige cloud, and a partly
  cloudy scene only greys the cap where the weather covers it; no measurable cost. **Getting there**:
  the global set's shadow volume reads 1 everywhere, and KSA's own shadow set is declared for
  fragment shaders alone and reads garbage from compute — `docs/KSA-MODDING-NOTES.md` has the route
  that works.
- ~~**KSA's cirrus punched holes in the cap.**~~ **Fixed on 2026-09-23.** The weather was one sheet
  at KSA's one distance per pixel, which is an average over all its layers, so from above a cap
  standing up through the 11 km cirrus read as behind the whole deck wherever there was cirrus, and
  dropped out in patches and small boxes that crawled as the cirrus drifted. Each layer is now its own
  sheet at its own crossing, with KSA's opacity split between them; flown at 58 km and 15° round a
  340 kt cap from four sides, the holes and boxes are gone. **Its cost and the view from under the
  deck were not compared**: the "old" arm of both was copied into the mods folder and the reload tool
  put the tree's shader back over it, so both arms ran the new one (`docs/VISUAL-TESTING.md` item 2).
  `docs/KSA-MODDING-NOTES.md` has what the distance actually holds.
- **Heat shimmer** needs the scene read at an offset while it is being written, which is a copy of the
  scene image this pass does not have.
- **Guy-wire spikes** are a tower shot's, and nothing in the arsenal is on a tower.

**By design.** The cloud is drawn at 65% of its law (`MushroomCloud.DrawnScale`); a true 0.3 kt
ball is 1:7 against its cap and reads as wrong.

**Ranked for what they would change on screen:** ~~the dirty rising fireball, with glow through the
dust and the brown of the oxides; the ball turning into a ring of fire; the dust ring racing out
along the ground; condensation rings on the stem; a longer life~~ (built on 2026-09-23).

**The collars, flown.** The first layer heights, 5.5 and 8.5 stem radii, put both at or inside the
cap's underside at 0.3 and 20 kt: one never formed and the other showed only as white scraps where
the erosion opened the cap, which under an overcast read as wisps floating in the sky. At 2.2 and
3.3 they stack on the stem, and they read as saucers until the rim was ragged and the edge soft.
The veil over the crown drew first as a glass bubble, round the analytic dome, which stands clear
of the eroded cloud. Cost: 3.74 ms for the pass against 3.73 with them at zero.

**The dust ring, flown.** The front is Sedov–Taylor and then sonic, in real time rather than on the
rise's clock, so at 0.3 kt it has left its reach two seconds in, mostly under the flash, and what
reads is the layer it leaves: a dust collar at 1.6 s and a grey-brown layer round the base by 6. At
20 kt it visibly races out between 4 and 11 s. It takes the ground's hue and not its brightness;
borrowing both drew it as a dark stripe across dark grass. Measured through the bridge's `cost` at
a close 20 kt pose where it covers a wide band of the screen: 3.55 ms for the pass against 3.08 with
the dust at zero. Both it and the column are shaded by KSA's weather (above).

**The dirty fireball, flown** on a 0.3 kt burst from the side view: at 2 s black smoke with
fire in its pockets, at 4 s the pockets dimming inside a brown-red shell, and by 8 s a brown cap
over a dark stem. It is also more opaque: soot absorbs, so extinction is three times higher at the
burst and back to normal as it clears. The tuning constants are `ChurnBoost`, `FireGlowNits`,
`SootDepth`, `SootSeconds`, `NoxStrength` and `NoxSeconds`.

## Tier 1 — done

All three flown on 2026.9.10.5438. Kept here rather than deleted, because two of them ended
somewhere other than where the plan expected.

### 1. The double flash — built

`MushroomCloud.FlashAt`'s glow is monotone: the maximum of two decaying exponentials, then an
ember floor. Real thermal output has **two** maxima — an intensely bright, very short first pulse
from shock-heated air, a minimum as the shock front becomes opaque to its own radiation, and then a
second maximum that is dimmer but lasts far longer and carries about 99% of the thermal energy.

It is *the* diagnostic: bhangmeters identify a nuclear test from orbit by that curve alone, and
nothing else in nature produces it.

Glasstone and Dolan give both times as powers of yield in kilotonnes:

```
t_min  = 0.0025 * W^0.44 s        10 ms at 20 kt,  53 ms at 1 Mt
t_max2 = 0.0417 * W^0.44 s       157 ms at 20 kt, 890 ms at 1 Mt
```

**It was over before anyone could see it**, as expected: 25 ms at the B61's yield. So the pulse is
slowed to put the second maximum at `LegibleSecondPeak`, 0.35 s — a **floor rather than a
multiplier**, which is what makes it self-limiting: it is identically 1.0 at a megatonne, where the
law puts the second maximum at 0.87 s on its own.

Two things the plan did not anticipate:

**The minimum is not the fireball going out.** The shock front is opaque to what is behind it and
is itself radiating, just cooler. Drawn without a floor the glow collapsed to about 5 against an
ember floor of 40 — under the bloom threshold, which reverts the ball to being drawn as geometry
for a fifth of a second. `ShockFrontShare` is a quarter, which is the shape Glasstone's curves
have.

**And `PulseMinimumSeconds` is not where the drawn curve is dimmest.** It is the law's shock-front
time; the composite goes on falling past it while the opening behind it is still small, so the
trough is where a decaying exponential crosses a smoothstep and has no closed form.
`PulseTroughSeconds` walks it. The first capture aimed at the minimum landed on the rising edge and
read 106 instead of 49.

Flown, with the glow reported beside each capture because **no screenshot can settle this** — at
2.4 km the whiteout saturates over the whole pulse, and an eye is what the whiteout models, so an
eye does not see the dip either. A fast camera would. The numbers are 276 at the first pulse, 49 at
the trough, 148 at the second maximum.

### 2. Blinded only if you are looking at it — built

`Ksa/BurstFlash.cs` takes the eye's **position**, for the solid angle, and throws the **direction**
away — `TryMainCameraPose(out double3 eyeEcl, out _)`. So a burst directly behind the camera whites
the screen out exactly as one dead ahead does.

`Sim/FlashGlare.cs`. Full while the burst is in frame, then a smoothstep to a floor, and the field
is the view's own so the sight's three-degree frame is judged as three degrees.

**Flown on 2026-09-23**, with the bridge camera turned from the burst (`turn_deg`) before it went
off — turned after, the first frame's rise is taken facing it and the side-on reading comes out as
high as facing. A 0.3 kt burst at 2.4 km by day, 0.15 s in:

| Turned from it | 0° | 30° | 60° | 90° | 180° |
|---|---|---|---|---|---|
| whiteout | 0.67 | 0.67 | 0.67 | 0.43 | 0.40 |
| glare | 0.72 | 0.72 | 0.56 | 0.25 | 0.24 |

The whiteout saturates out to 60°, because even a reduced share of a flash that many suns bright is
decades over daylight on the eye's log scale; past the fall-off it settles at the floor and never
reaches nothing. The curve's invariants are still pinned by the unit tests.

**And it is centred on the burst, white only where it clips.** A glare is brightest at its source
and falls off roughly as the inverse square of the angle off it; where it stops clipping it shows
the light's own colour, which for a fireball seen through kilometres of air is a warm yellow. So the
whiteout is its own dispatch, last, carrying the burst's direction: a core within about 6 degrees
over the clip, which KSA's bloom spreads into the halo, and a pale gold veil under it everywhere
else that warms further as the flash dies. It veils the clouds too, being in the eye rather than
in the world. The levels come from bands written through KSA's post chain — 1.0 is 240, and 1.25
and up is white — and `CHECKLIST.md` has the numbers.

**And it is measured the way an eye measures it.** Linear in the light, the whiteout fell as the
inverse square of the range and was set to saturate at the watch's 2.4 km, so a few kilometres
further out it was nearly nothing. It is now the flash in suns — the burst's thermal power at the
pulse's peak spread over a sphere, against sunlight — against what the eye is adapted to, one sun by
day and a hundredth at night, on a log scale: `FlashGlare.Level`. Measured through the bridge, a
0.3 kt burst whites the view to 0.97 at 2.4 km by day and to 0.44 at 10; at night it reaches much
further. Reckoned off the drawn ball's size instead, the same burst was eight suns at 2.4 km and
0.51, because at the instant of the flash the ball is still small.

**And the rises add up.** The whiteout followed the largest single rise, so a flash arriving over two
frames stopped at the first one's share; it is their sum now, fading as before.

**And the glare outlasts the whiteout.** The whiteout follows the rise, because an eye adapts; the
halo round the ball follows its level, in its colour, because scattering lasts as long as the
source. Without it the view cleared while the ball was still visibly orange.

### 3. The cloud never drifts — built, then taken out by decision

A drift was built — nothing until the rise ended, then one cap radius downwind over the stand — and
removed the same day on request. The cap grows with the yield, so the same fraction of it is a
kilometre at 0.3 kt and several at 300 kt: the cloud slid sideways at about a hundred metres a
second and read as dragged rather than blown. **The cloud stays over the ground it burned.** The lean
and veer tilt a rooted column and the fallout plume marks the ground downwind, so the wind is still
visible without the cloud travelling.

---

### 1b. The first fifteen seconds, against the physics — built

Community comments on the burst ("more spherical and white-yellow for 3 s", "the fireball should last
longer before the smoke", "the cloud should glow at first") were checked against Glasstone & Dolan
and the test-film record before anything changed. What held, and what was built:

- **The ball glowed 3.4 s at every yield over about 1 kt**, where the law is 9.9 s at 20 kt and
  30.9 s at 340 kt. The ceiling was the smoke pens' — no smoke laid while the ball glowed — and
  outlived them: the cloud is a raymarch and nothing read the check. It now runs to the law, up to
  14 s.
- **It went orange by 2 s.** It now stays white-yellow for its heat pulse (5.4 s at 340 kt, 1.6 s at
  20 kt) at a glow held near its peak, then cools through orange to deep red.
- **It was lumpy from the first frame**: fire pockets under a soot shell. While white-hot it is now a
  smooth, opaque sphere of its own light (`BallRadiance`), with the soot and the pockets arriving as
  it cools — which is the order a real one goes in.
- **It reached full size in 0.34 s** at every yield. It now grows as `t^0.4`, over 2.2 s at 340 kt.
- **The condensation shell timed off the glow**, so lengthening one would have stretched the other.
  It is on the blast's clock now: 3 s at 20 kt (Glasstone §2.49), by the cube root elsewhere, and it
  condenses a third of its life in rather than at the burst.

What did not hold: a five-second swelling ball is high-speed film played slowed (test cameras ran at
about 2,400 fps), and a ground burst is a dome, not a sphere. Flown at 340 kt from 30 km and 20 kt
from 14 km, both with the sun behind the camera: white dome, orange from about 5 s and 2.7 s, a
mushroom glowing from inside to 14 s and 10 s.

## Tier 1a — found while building the above

- **A control run reported a shader that would not compile.** `CloudPassCost` read "no pipeline" as
  "build failed", and a pass switched off is never asked to build one. Fixed.
- **`PulseMinimumSeconds` was documented as the dimmest point and is not.** Fixed by naming, and by
  adding the thing the name promised.

---

## Tier 2 — real work, and the payoff is large

### 4. The fireball should become the cap — built

`Ksa/Fireball.cs` draws a ball at the burst and `CloudPass` grows a column around it. They are
separate systems that happen to overlap. **Since 2026-09-23 it draws no ball at all, only its
light**: a mesh sphere inside the raymarched cloud read as a dark ball that drifted off centre when
the camera panned, and the cloud's fire and the glare round it, centred on the risen ball, carry it.

Physically the fireball **is** the cap: it cools, becomes buoyant, rises, and the toroidal
circulation that the raymarch already draws begins inside it.

**It turned out to be a deleted law rather than a designed handoff.** The ball had a rise of its
own — two of its own radii and stop — and the cap centre already was the answer, so the whole thing
went and the draw site reads `shape.CapCentre`. The shader's in-cloud glow moved with it and needed
no push constant: the cap centre is already `Shape.x`.

**The reason the old law existed had expired.** It was there because the ball is "an emissive sphere
drawn *over* the smoke rather than inside it", so a lit ball climbing read as a flare ascending —
written when the cloud was smoke pens. It is a raymarch now, clipping against scene depth and
multiplying what is behind it by its own transmittance, and the ball is a mesh. The ball is inside
the smoke, and the height limit was holding the two apart for a reason that had gone. The guard was
replaced rather than dropped: what has to hold is not a height but that the cloud is around the
ball by the time it is up there.

**Measuring it needed normalising.** Two runs ten minutes apart differ over 43% of the frame just
from the sun moving, so the raw diff says nothing. Against regions the ball cannot reach — sky 5.8
and far ground 30.0 of 255 — the stem's foot came back at 31.1, its own baseline, and the cap at
73.2.

### 5. The anvil — built

Past the tropopause the cap is braked and spreads; `CHECKLIST.md` has the law and the flown sizes.
What follows is the case for it, kept because it is why the constants are what they are.

`CloudTop = 3000 * W^(1/3)` carries no tropopause term, so a megatonne cloud is drawn as a taller
kilotonne one. Real clouds hit the tropopause and spread **sideways** — Castle Bravo's was about
100 km across against 40 km tall, which is wider than it was tall.

`DrawnCapWidening = 1.9` is a constant standing in for this at every yield, and says so. A real
tropopause with the cap spreading *under* it would make the yield readable from the silhouette
instead of only from the scale, which is the one thing a player cannot currently judge.

**Reachable, and it was wrongly demoted.** The shipped *defaults* are the B61's 0.3 kt and the Mk 21's
20 kt, which is what a first reading took for the arsenal — but the Tuning tab's charge slider runs to
**340,000,000 kg**, the real B61's top setting of 340 kt. The cloud is drawn at `DrawnScale` 0.65 of
its law, so a tropopause drawn to the same scale is reached at the law's own **~49 kt**, and at 340 kt
the drawn top is 13.6 km. That is the whole range where the yield should read from the silhouette.

`TWOCLOUDS=1000` puts a 300 kt burst beside the 0.3 kt one, which is the run to look at it with — and
the first run to look at *anything* in this file above 30 kt.

### 6. The projected decal — two of its three reasons are gone

`docs/DAMAGE-DECALS.md` has the mechanism. It was wanted here for three things, and looking at it
properly retired two:

- **unbounded**, where `MaxScorches` was four — **got most of this without it.** The four was a
  budget on full-screen dispatches; a mark is dispatched over its own projected footprint now,
  measured at 55.1% of the screen from the watching pose and far less from anywhere else, and the
  bound is twelve.
- **no full-screen dispatch per mark** — same change.
- it reaches **hulls and ground clutter** — **it already does.** The compute mark reconstructs
  world position from the resolved depth, so it marks whatever is in the depth buffer, clutter
  included. The height gate is what keeps it off things in the air, not an inability to reach them.

What a box rasterisation still buys is the last of the cost — a cube touches only its own
footprint where even a tiled dispatch touches a rectangle around it — and **image-based** decals,
which a procedural stain does not need and a burn mark on a hull does. That is the damage-decal
feature rather than this one.

**And `DAMAGE-DECALS.md` §1 was wrong in three ways on this build**, which is recorded there: the
seam's signature has grown an `inResolveDepth`, there are four call sites rather than three, and
`RenderGame` resolves twice — the first without depth. A postfix fires on both, and the identity
checks in that table no longer separate them.

### 7. Fallout is a plume, not a disc — built

`PlumeReach` is three patch radii, and the plume is a cigar: leaving the patch at about its own
width, widest halfway and closing to a rounded tip, soft across and with a low-frequency field
wandering its edges and its tip. **It was a ruled strip until 2026-09-23** — a square-root widening
with a hard start across the burst, a flat core with a sharp edge and a square end — and seen from
above it read as a dark box laid on the ground, which is how it was reported. The dispatch's
footprint grew with it (`ScorchFootprintTests`), including upwind, where the patch's ragged rim ran
past the box by 0.15 of a radius.

**The wind is one rule now.** The cloud and the mark were each deriving the bearing for themselves,
and a plume at right angles to the column it fell out of is the plainest possible tell.

**And the camera was the reason none of this could be judged.** `CloudWatch` stood on an arbitrary
perpendicular, and everything about a burst that is not symmetric about its own axis lies on the
downwind line — the lean, the veer, the drift and the ground. It stands across the wind now.

Every shape here was settled against a **same-camera control** — the identical run with
`PlumeDepth` at zero, differenced pixel by pixel — because at a glancing angle on textured terrain
a plume and a cloud shadow look alike, and a first attempt measured 28.6% darker on the plume side
of a frame whose whole left half was in shadow anyway. The honest number is the diff: 12.9 of 255
mean over the ground it covers at the first shape, 19.8 at the shipped one, peaking at 58 which is
the same darkening the crater itself reaches.

### 8. A rumble, not a bang — built, and not yet heard

**The rumble was already there.** Core's `ExplosionBig` is a crack, a distance-filtered far report
and a **10.6–10.8 s** echo layer whose spatial data turns non-directional between one and ten
kilometres — which is the enveloping part of a real roll. What was missing was that it was the same
eleven seconds for a rocket, the B61 and the Mk 21.

So it is pitched by yield instead of rebuilt: every time in a blast wave goes as `W^(1/3)`, so
playback at `W^(-1/3)` lengthens every layer together — `MushroomCloud.BangPitch`, anchored at the
B61's 0.3 kt and floored at 0.35, where the echo runs about thirty seconds. One multiplier reaches
all three layers through the engine's multi-channel wrapper, and the bang starts paused so none of it
is heard at a rocket's pitch first.

**And the rumble was never mixed in, which is why nobody heard one.** Core's layers fade on a
`Distance` parameter the engine sets when it plays an explosion itself (`ExplosionSoundSystem`), and
played by hand it was never set: read as zero, the far report sat at volume nothing at every range,
the close crack at full at every range, and the echo at its floor. The bang now sets `Distance`,
`Pressure` and `Intensity` as the engine does, off its audio camera, and re-places itself every frame
while it plays. **And a burst that joined a standing cloud queued no bang at all** — a bomb dropped
on the last one's fireball went off in silence; every detonation queues one now, and two due within
half a second of each other from within a kilometre are one bang.

**Still half-verified.** Flown, bursts 2.0 and 2.1 km out were heard 5.8 and 6.0 s after they went
off, merged into a standing cloud, with nothing thrown. Whether it sounds right — and whether 0.35
of Core's sample is a rumble or mud — is a question for somebody's ears, and it is the one thing on
this list that no screenshot, diff or log line can settle.

Nothing echoes off the terrain that is actually there.

---

## Tier 3 — bigger, or not yet known to be possible

### 9. Temporal accumulation — built

`CHECKLIST.md` has the flight. What is left is watching it with a camera that moves fast.

The march is 48 steps on a per-pixel hash, and grain is what limits it. Interleaved gradient noise
was tried in its place and reverted, because it is built to be resolved by temporal accumulation
and there is none here. Accumulating across frames buys either half the cost or twice the quality,
and is the only thing that does.

### 10. Water bursts — the surface and the deep built; the shallow one is not

`Sim/BurstSetting.cs` sorts every burst into land, the sea's surface, or under it. On the sea the
column is spray from the foot up, the surge collar is white, and nothing is burned; deep enough that
the fireball never breaks the surface, there is no column at all.

**It was found as a defect rather than built as a feature.** Pointed at the South Atlantic, the drop
harness set its rocket down on the **seabed** 4,162 m down and the store burst there — and a full
brown mushroom stood in the dark, because the cloud asked whether the *body* had air rather than
whether the burst was in it.

**And a land burst beside the sea was staining it.** The mark is depth-projected, so a plume running
downwind from a beach went straight out across the water. KSA's ocean is everywhere under sea level,
so there is no dry ground there and a pixel at or under the waterline is water — gated against the
mark's own height over the sea, in small numbers, never a planet radius. Measured against the same
run with the gate off: sky and beach 0.4 of 255 apart, the water 14.4 by the shore.

**Not built: the shallow underwater burst** — Baker's white dome, its column and the base surge that
races out kilometres past the cap. A store stops at the waterline, so nothing in the arsenal can make
one; it is worth building when something can.

**Finding a coast is a harness problem of its own.** KSA's shoreline sits east of the real one at
Palm Beach, the pad has to be on land and the sea within the second burst's kilometre, and a rocket
on the dune at 26.70 N, −80.01 topples more often than it stands. −80.02 holds, and its second burst
lands on the dune. The drop's wait phase had no budget, so a toppled rocket hung the run for as long
as anybody let it; it fails in thirty seconds now.

### 11. Something other than damage follows from it — last, by decision

**Built only once the burst itself is finished.** How a burst looks is what every one of these
would be judged against, so the order is the look first and the consequences after it.

**Radar blackout is built**: the fireball's ionised air hides what is behind it from a set that
transmits. `CHECKLIST.md` has the flight. The other two below are not.

Today a burst is visuals plus `BlastDamage`. A nuclear one could blind radar for a scaled
duration, burn at ranges the blast never reaches, and bloom out an `OpticalHead`'s sight — which
would reuse the claim ladder and the zoom that already exist.

### 12. A crater

Probably blocked. The height field is GPU-side; `docs/DAMAGE-DECALS.md` records that ground clutter
placement is entirely on the GPU and that the readback path is never constructed in a shipping
build. Worth re-checking after a KSA update rather than planning around.

---

## One thing already shipped that is reasoned rather than flown

**`MaxScorches` was reached on 2026-09-23.** Sixteen 0.3 kt bursts through the bridge on a 1.5 km
grid, four rows of four, far enough apart not to merge: the count ran 1 to 12 and held at 12 through
the last four, and the difference between the view with the marks and without them showed twelve
marks in three rows, the first row gone. Twelve cost the pass 0.23 ms a frame from a pose where they
cover 8.8% of the screen.

The airless mark's radius used to be the other: it borrowed the blast law. It is how far the
radiation reaches now, and `CHECKLIST.md` has the flight.
