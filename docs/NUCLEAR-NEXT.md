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

## Tier 1 — cheap, and each one is unmistakably nuclear

### 1. The double flash

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

**The difficulty is that it is over before anyone can see it at the yields this mod fires.** At the
B61's third of a kilotonne the second maximum is at 25 ms — under two frames. The rise is already
compressed sevenfold and `DrawnScale` and `DrawnCapWidening` are both named lies for exactly this
kind of problem, so a `DrawnFlashStretch` would be in keeping. **Measure before naming one**: put
captures at `t_min` and `t_max2` and see what the real law actually looks like first.

### 2. Blinded only if you are looking at it

`Ksa/BurstFlash.cs` takes the eye's **position**, for the solid angle, and throws the **direction**
away — `TryMainCameraPose(out double3 eyeEcl, out _)`. So a burst directly behind the camera whites
the screen out exactly as one dead ahead does.

One dot product against the view axis, softened across the field of view rather than cut at its
edge, and with a residual floor: light that bright scatters in the atmosphere and in the optics, so
facing away should dim it and never abolish it.

### 3. The cloud never drifts

`Cloud.BurstCcf` is written once and never updated. The column leans downwind — that is
`Downwind`, and `VeerRadians` turns it with height — and its foot stays nailed to the burst point
for the whole 78 s.

The wind is already there. Advecting the cloud along it is one addition per update, and over a
cloud's life it is about a kilometre: enough to separate the column from its own scorch, which is
what every photograph of an hour-old cloud shows and what the mod currently cannot produce.

---

## Tier 2 — real work, and the payoff is large

### 4. The fireball should become the cap

`Ksa/Fireball.cs` draws a ball at the burst and `CloudPass` grows a column around it. They are
separate systems that happen to overlap.

Physically the fireball **is** the cap: it cools, becomes buoyant, rises, and the toroidal
circulation that the raymarch already draws begins inside it. The handoff between the two has never
been designed, and it is the reason the first few seconds read as two effects rather than one.

### 5. The anvil

`CloudTop = 3000 * W^(1/3)` carries no tropopause term, so a megatonne cloud is drawn as a taller
kilotonne one. Real clouds hit the tropopause and spread **sideways** — Castle Bravo's was about
100 km across against 40 km tall, which is wider than it was tall.

`DrawnCapWidening = 1.9` is a constant standing in for this at every yield, and says so. A real
tropopause with the cap spreading *under* it would make the yield readable from the silhouette
instead of only from the scale, which is the one thing a player cannot currently judge.

### 6. The projected decal

`docs/DAMAGE-DECALS.md` has the whole mechanism, read out of gatOS's working implementation and
re-verified against this build. It replaces the screen-space mark with something strictly better:

- **unbounded**, where `NuclearClouds.MaxScorches` is four and drops the oldest
- **no full-screen dispatch per mark for the rest of the session**
- it reaches **hulls and ground clutter**, so the rocket that dropped the bomb is scorched too

### 7. Fallout is a plume, not a disc

The mark is a circle on the lethal radius. Real deposition runs far downwind in a long narrow
footprint. `Downwind` already exists, and using it here is what ties the mark to the cloud that
made it rather than to the point the bomb went off at.

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
