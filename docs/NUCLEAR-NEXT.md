# What the nuclear effect still needs

**A plan, not a record.** Nothing here is built. `docs/NUCLEAR-EFFECT.md` is the research —
which of KSA's renderers a mod can reach, and what a mushroom cloud actually looks like — and
`CHECKLIST.md` §7.1f4 is what has been flown. This is the ranked backlog between them.

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

**Unit-tested and not flown.** `CloudWatch` pins the camera on the burst from the frame it happens,
so no shipped scenario can face away from one. The curve's invariants are pinned instead —
monotone, bounded, full in frame, floored behind — and the flight confirms only that the flash
still fires when you are looking at it.

### 3. The cloud never drifts — built

`Cloud.BurstCcf` is written once and never updated. The column leans downwind — that is
`Downwind`, and `VeerRadians` turns it with height — and its foot stays nailed to the burst point
for the whole 78 s.

**Nothing drifts until the rise is over**, which turned out to be the point rather than a
simplification: what wind does to a column still being fed from the ground is tilt it, and that is
the lean and the veer already drawn. A cloud sails once it stops being fed.

One cap radius over the stand, named like `DrawnScale`. An honest drift is four cloud widths — wind
aloft is twenty to forty metres a second over a five-minute rise — and carries the column out of
any frame that also holds the ground it burned. In cap radii rather than metres, so it reads the
same at any yield. Flown: at 72 s the residual column stands clear of a centred mark.

---

## Tier 1a — found while building the above

- **A control run reported a shader that would not compile.** `CloudPassCost` read "no pipeline" as
  "build failed", and a pass switched off is never asked to build one. Fixed.
- **`PulseMinimumSeconds` was documented as the dimmest point and is not.** Fixed by naming, and by
  adding the thing the name promised.

---

## Tier 2 — real work, and the payoff is large

### 4. The fireball should become the cap — built

`Ksa/Fireball.cs` draws a ball at the burst and `CloudPass` grows a column around it. They are
separate systems that happen to overlap.

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

### 5. The anvil

`CloudTop = 3000 * W^(1/3)` carries no tropopause term, so a megatonne cloud is drawn as a taller
kilotonne one. Real clouds hit the tropopause and spread **sideways** — Castle Bravo's was about
100 km across against 40 km tall, which is wider than it was tall.

`DrawnCapWidening = 1.9` is a constant standing in for this at every yield, and says so. A real
tropopause with the cap spreading *under* it would make the yield readable from the silhouette
instead of only from the scale, which is the one thing a player cannot currently judge.

**Demoted, because nothing in the arsenal can reach it.** `CloudTop` passes a mid-latitude
tropopause at about **49 kt**, and the two nuclear charges that ship are the B61's **0.3 kt** and
the Mk 21's **20 kt** — 2.0 km and 8.1 km of cloud top against an 11 km ceiling. Even
`TWOCLOUDS=100` only reaches 30 kt. So this is capability nothing in `Arsenal.cs` or the panel can
observe, which CLAUDE.md says is a `refactor` rather than a feature, and it would ship untestable
in the shipped game.

It becomes worth building the moment a larger warhead is registered, and not before.

### 6. The projected decal

`docs/DAMAGE-DECALS.md` has the whole mechanism, read out of gatOS's working implementation and
re-verified against this build. It replaces the screen-space mark with something strictly better:

- **unbounded**, where `NuclearClouds.MaxScorches` is four and drops the oldest
- **no full-screen dispatch per mark for the rest of the session**
- it reaches **hulls and ground clutter**, so the rocket that dropped the bomb is scorched too

### 7. Fallout is a plume, not a disc — built

`PlumeReach` is three patch radii and `PlumeWidth` widens as a square root, which is a puff
spreading by diffusion while it travels.

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

### 8. A rumble, not a bang

`Ksa/BurstSound.cs` is one event arriving at 343 m/s. A real burst is a crack followed by tens of
seconds of rumble, whose length scales with yield and with what the terrain echoes it off.

---

## Tier 3 — bigger, or not yet known to be possible

### 9. Temporal accumulation

The march is 48 steps on a per-pixel hash, and grain is what limits it. Interleaved gradient noise
was tried in its place and reverted, because it is built to be resolved by temporal accumulation
and there is none here. Accumulating across frames buys either half the cost or twice the quality,
and is the only thing that does.

### 10. Water bursts

CLAUDE.md already records that the wide base surge racing outward belongs to an **underwater**
burst, and that the land one is deliberately a collar around the stem. The mod has ocean handling
and knows when a round is in it. A water burst is a completely different and far more dramatic
shape — column, plume, and a genuine base surge — for one profile flag.

### 11. Something other than damage follows from it

Today a burst is visuals plus `BlastDamage`. A nuclear one could blind radar for a scaled
duration, burn at ranges the blast never reaches, and bloom out an `OpticalHead`'s sight — which
would reuse the claim ladder and the zoom that already exist.

### 12. A crater

Probably blocked. The height field is GPU-side; `docs/DAMAGE-DECALS.md` records that ground clutter
placement is entirely on the GPU and that the readback path is never constructed in a shipping
build. Worth re-checking after a KSA update rather than planning around.

---

## Two things already shipped that are reasoned rather than flown

**The airless mark's radius comes from a blast law.** `Warhead.LethalRadius` is Hopkinson--Cranz,
which is overpressure from a shock wave — the thing a vacuum does not have, and the reason
`Sim/AirlessBurst.cs` exists at all. Thermal scorch in vacuum is real and reaches *further* for
having nothing to absorb it, but the number it reaches to is borrowed from physics that cannot
happen there. The visible consequence on Luna is a 220 m burst under a 493 m mark, with
`CloudWatch`'s own pose standing the camera inside it.

**`MaxScorches` has never been reached.** It is four and the oldest is dropped; the harness
produces at most two, and nothing has ever watched a mark vanish.
