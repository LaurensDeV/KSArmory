# A post-boost bus with a main engine

**A plan, not a record.** What a second MIRV bus with an axial engine and a bigger tank would buy, how big to make it,
and what the guidance needs before it can fly one. The art is on `dev` (`03dcca4a`, `KSArmory_PostBoostBus.glb`);
nothing declares it yet. The declaration drafted with the art is `tools/model/PostBoostBus.draft.xml`.

## Why a second part

The Heavy MIRV Bus (`KSArmory_Prefab_MirvBus`) diverts on twenty small nozzles from one 134 kg MMH/NTO sphere, and the
guidance spends at most 60 m/s of it (`PostBoostAim.MaxTrimMetresPerSecond`; ~70 on a walk with the split reserve).
That caps six targets at about 4.5 km apart on the shipped schedule and about 6 km with the walk from cutoff
(`ICBM-OUTSTANDING.md` 4.12) -- inside a Mk 21's 6 km blast radius, so barely separate targets at all.

A real post-boost vehicle diverts on an axial engine and keeps its small thrusters for attitude and the last
correction. A second part rather than a reworked one, because a shipped part's subpart list is append-only and its Id
is fixed: old saves keep the old bus, and "replacing" it later is changing what new designs carry.

## What spacing costs

A metre a second spent on the coast moves the landing by about the time left to arrival in metres -- pinned, it falls
off as `sin(wt)/w` (`ICBM-GUIDANCE.md`, "What bounds a set is its spacing"). Averaged over a walk from cutoff, priced on
the narrower axis as the planner prices a hop (6.1 km against 8.5 straight-line for six at 60 m/s):

| range | reach per m/s, planner-priced |
| --- | --- |
| 6,179 km | ~0.70 km |
| 12,902 km | ~1.06 km |

Seven RVs (the art seats seven) are six gaps, so a chain `S` apart is `6S` end to end and costs `6S / reach` in hops.
Every figure below adds the 1.3x a flown hop costs over its price (`ICBM-OUTSTANDING.md` 4.12, 1.13-1.29x) and 20 m/s
for the split's null and the corrections.

| spacing | 6,179 km | 12,902 km |
| --- | --- | --- |
| 10 km | 131 m/s | 94 m/s |
| 25 km | 299 m/s | 204 m/s |
| 50 km | 577 m/s | 388 m/s |
| 100 km | 1,134 m/s | 756 m/s |

## How big a tank

The rocket equation with a warhead's 250 kg leaving at each stop, an assumed exhaust velocity of 2,900 m/s (typical
MMH/NTO; **not measured** -- KSA derives it from the combustor, see below), seven RVs and 1.5 t of structure:

| propellant | hops it flies | spacing at 6,179 km | at 12,902 km |
| --- | --- | --- | --- |
| 500 kg (the draft's four 0.332 m spheres) | 509 m/s | ~44 km | ~66 km |
| 1,000 kg | 948 m/s | ~83 km | ~126 km |
| 1,500 kg | 1,334 m/s | ~117 km | ~177 km |

So **the draft's 500 kg is the 50 km bus at long range**, and 100 km at 6,179 km wants about 1.2 t. The same arithmetic
on the old bus (1.27 t structure, six RVs, 134 kg) gives **~167 m/s** of physical divert against the 60-70 the
guidance allows itself: the current bus is already capped by the code, not the tank.

## How big an engine

A hop has to fit its slot on the coast (65 s today, `ReleaseItinerary`), and at 50 km spacing a hop is ~90 m/s with
the margin. At ~3.8 t wet that is 3 m/s2 to burn it in 30 s, so **~10-15 kN**: 4-5 kg/s at 2,900 m/s, about two
minutes of burning for the whole 500 kg. Today's twenty nozzles push ~0.56 m/s2, which is why a large hop runs into
`BusTrim.MaxSeconds`' 120 s. The bell is modelled at a 0.44 m exit; its `ExitDiameter` sets the thrust and is chosen
to match.

## What the guidance needs, in order

1. **The bus's numbers on its profile** (refactor, headless). The divert budget, the per-pass ceiling
   (`BusTrim.MaxMetresPerSecond`, 10), the per-hop clock (`BusTrim.MaxSeconds`, 120) and the hop slot are constants
   today. Each becomes a field the bus part provides, the old bus keeping today's values exactly, so nothing it flies
   moves. Also worth flying on its own: the old bus's budget raised toward its ~167 m/s, with the budget-edge reserve.
2. **The step-3 reach** (`ICBM-OUTSTANDING.md` 4.12): a walk from cutoff priced at the epoch it opens, with the
   `sin(wt)/w` falloff. At these spacings the first hops are long before 760 s.
3. **Hops as burns.** Point the bus, fire the main engine for the hop's velocity, null the remainder on the small
   nozzles. The trim's passes were built to null a few m/s; a 90 m/s hop chained through 10 m/s passes is the wrong
   tool. Rig-testable on `PastCutoffRig` before the part exists.
4. **The part**: the draft declaration, the tank and `ExitDiameter` from this note, both registries in
   `Sim/Arsenal.cs`, `checkring.py` on the ring at X 0.52, and `validate-parts.py`.
5. **Fly it** paired against the old bus at 6,179 and 12,902 km: stops walked, worst warhead, spacing reached.

## Unknowns to settle before sizing is final

- **The nozzles' and the engine's exhaust velocity in KSA.** Read it off the combustor model in the decompiled source,
  or off one flight's jet mass against the trim's m/s. Every propellant figure above scales with it.
- **The reach at hundreds of m/s.** The per-m/s figures are small-divert slopes; a 90 m/s hop is still under 2% of the
  velocity at cutoff, but the planner should be checked against a flown chain rather than trusted at 100 km.
- **The coast.** Seven stops need a coast of the gate plus six slots, about 3,100 km and up today; faster hops shorten
  the slot and lower that floor.
