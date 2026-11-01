# Armour

**Part built, part plan.** Everything that needs no new art is built and flown (*What is built*, at the
end); the plate family's art, armoured tank variants and the panel's armour rows are not. How a player protects a craft at a cost, what KSA lets a mod
do towards it, the law a hit is judged by, and the order to build it in. Researched from this codebase, the
decompiled game and another game's combat mod (*the reference mod*), then reviewed twice — once for the
ballistics and the balance, once against the code — and revised; what each review changed is folded in.

## What the player gets

**Armour plates: parts the player bolts onto a craft**, in a few materials and four thicknesses, each the mass
its area, thickness and density make it. A plate is met first by a round coming from its side and stops it,
slows it, glances it off or wears; behind it, everything already built — tank walls, craters, the
hydrodynamic ram — judges what is left. The cost is mass, paid in acceleration, delta-v and a shifted centre of
mass, and drag where a plate stands proud of the craft's box (KSA's drag is the craft's bounding box,
`VehicleProperties.RecomputeAerodynamicProperties`). Nothing else: more trade-offs would be invented ones.

**Later, armoured versions of Core's tanks**, the same tank with a thicker or steel wall: KSA works out the
mass, the reduced storage and the higher rating from the wall itself (`AsmbTankTemplate.GetTankPropertiesAsmb`).

**Thickness tiers** from what each stops, by the law below: **6 mm** light fragments, **12 mm** an HEI shell's
fragments, **25 mm** the 20 mm round, **50 mm** the 30 mm and the Phalanx's dart. Steel at 10 mm is 78.5 kg/m²,
so a 3 m tank's side under 20 mm is about 4.4 t — a real price.

## Why parts, and not a slider

| Approach | Saved with the craft | Seen in the editor | Verdict |
|---|---|---|---|
| A thickness slider on every part, as the reference mod has | only by patching three engine methods (`docs/BLOCKED-ON-KSA.md`, *Configuring a part in the vehicle editor*) | no inspector hook | blocked |
| A craft-wide armour level in the mod's own settings | no: keyed by craft name, which is not unique, with no stable per-part key | no: mass would appear only in flight and the editor's delta-v would lie | rejected |
| **Plate parts, and armoured part variants** | **yes, as any part is** | **yes, mass and all** | **chosen** |

A thickness is therefore a part, not a number, and every plate's Id is permanent once shipped
(`CLAUDE.md`, *Nor is a shipped `<Part>` Id renameable*). `docs/BLOCKED-ON-KSA.md` carries the line: a per-part
setting saved with the craft would make the family one plate with a thickness.

## The law

**Not plug shear alone.** `TankWall`'s plug law — energy against `½π·d·t²·τ` — holds for thin walls, which is
where it is used today, and fails as an armour law. Against plates it over-predicts penetration 1.6–2.3×
(72 mm of steel for the Phalanx's dart against a real 35–50); its √(E/yield) makes aluminium and titanium about
1.8× steel per kilogram, where they are roughly 1.0–1.4×, so every player would build aluminium; and its `t²`
makes two half plates worth half of one, where real spaced plates are about equal to one.

**The Lambert–Zukas ballistic limit** covers both regimes with one expression:
`v_bl² ∝ (L/D)^0.3 · D³ · f(z) / m`, `f(z) = z + e^(−z) − 1`, `z = (t/D)·sec^0.75 θ`, scaled by the plate
material's resistance. Thin plates take `f ≈ z²/2`, which is the plug form, so the tank walls keep their
behaviour; thick plates take `f ≈ z`, energy linear in thickness, Tate-like. Calibrated on a handful of
reference shots and pinned by tests: the table the review computed (12.75 mm tungsten dart about 44 mm of
steel, 20 mm AP about 30, 30 mm about 42–50, 1.6 g fragments 6 mm at 1 km/s and 14 at 2), fragment-simulating
projectile V50s, and the aluminium and titanium mass ratios. The coefficients are recalled and are checked
against their sources before they are written in.

- **Slope** is `sec^0.75 θ`, not `1/cos θ`, which over-states it (2.0 against 1.68 at 60°); an overmatching
  round, calibre past thickness, takes a capped angle.
- **A glance** is deterministic: a round that does not get through, beyond a critical angle that rises with
  speed and falls with thickness over calibre (Tate 1979, to be read before coding), glances off. Never a dice
  roll, which is the reference mod's.
- **What gets through** keeps `v_r² = a²(v² − v_bl²)`, `a = m/(m + m_plug)` — Lambert–Jonas with the momentum
  the plug carries off — and **the plug goes on as a fragment** at the common speed, so a tank behind a
  perforated plate is hit by the plate too.
- **Exploding shells** carry a class: a superquick fuze bursts on the plate's face — blast and fragments
  there, nothing punching on as a solid round — and a delay fuze goes one layer deep first. Today any HE shell
  is through by fiat (`BulletHoles.Strike`), which would put a 5"/54 shell through some 300 mm of steel.
- **What stops wears the plate** through `WallCraters`, re-derived from the new law.
- **The damage scale** divides the plate's resistance as it divides a wall's, checked to scale the same way.
- **Nuclear bursts never consult armour.**
- **Ceramics** shatter rather than shear and wait for their own law.

**Fragments are shadowed**: a part behind a plate is reached only by fragments that get through it, judged at
their drag-reduced speed. **Blast is not attenuated** in this version: a blast diffracts round a metre-scale
plate well inside its positive phase, and cutting it in `BlastDamage.Costs` alone would leave the dents, the
shove and the combined fronts (`BlastArrivals`) untouched, so a part behind a plate would still be broken. The
plate is a part: it takes its own blast share and can be broken or torn off. **A plate is not invulnerable**:
it has health, spends it on hits it stops, and its joint can break — and since toughness grows as mass to the
two thirds, a heavy plate is checked against a contact burst so it does not shrug one off.

## Where it goes in the code

**Keep a plate apart from a tank.** `TankWall.Known` and `BulletHoles.Penetration.Tank` both mean *this is a
tank*: a part given either is rammed, and `Ram` returns nothing for a part with no tank, so a plate would take
no damage and pass the round on whole (`WeaponSystem.GoesThrough`'s `part.Wall.Known` branch). So:

- **`Sim/ArmourLayer`**: thickness, density and resistance, the Lambert–Zukas limit, slope, residual, plug
  fragment and glance. The plug shear moves into a static both it and `TankWall` use; `TankWall` stays a
  tank's, with its rating, bursting and ram.
- **`DamageablePart.Layer`**, its own field, set in `TryCollectDamageableParts` from a cached lookup by part
  template, as the walls are cached.
- **`Penetration`** gains the part struck and a layer verdict beside `Tank`, which stays the tank to ram.
- **The walk moves into `Sim/`**: `GoesThrough`'s loop — layers in sequence, residual energy and momentum, the
  plug fragment, the order plate → wall → liquid — becomes a `Sim/` function over a span of crossings
  returning per-part shares and the residual, so it is tested headlessly. `KsaWorld.Crossing` carries the hit
  normal `TryPartsAlong` already returns and throws away, so every layer gets its slope and its craters, not
  only the first part struck; cos θ comes from `Local − CameFrom` and the normal, both in the sub-part's frame,
  under a rotation-invariance test.
- **The part charged is the part the ray hit**: `StrikesSkin` and `StrikeInert` charge the hull cast's part,
  not the nearest bounding sphere — falling back to the nearest when the cast part is internal (the cast does
  not skip `IsAttachedInternal`, the handles do) or the cast missed, and keeping the gap check. That also fixes
  today's mismatch where a dud's ram and its share land on different parts. `BulletHoles.Strike`'s burst
  instant goes through `InFrame.AtBurst` like everything else.
- **Fragment shadowing, cheaply**: in `Ksa/`, before `BlastDamage.Costs`, and only on a craft whose cached
  part templates include a plate. `Costs` is split so the fragments' expected hits are known first; then each
  part with hits is cast against **the plates alone**, not every part — `TryPartsAlong` casts every part, so
  one burst on a 50-part craft would otherwise be about 2,500 part casts — and what lies in front goes into
  `Sim/` as data on `DamageablePart`, rebuilt per burst as the array already is. The same crossings feed
  `RamFragments`. A first version casts to each part's middle, which covers a part all or nothing; four or five
  rays to its box's corners give a covered share once it matters.
- **The pack reader**, after the decoy and dispenser shape: `<ArmourMaterial>` in the first pass, `<Armour
  PartId Material ThicknessMm>` in the second, materials qualified, a `knownMaterials` list through `Read`,
  `PackFiles` and `Armoury`, a registry and its merge in `Catalogue` with the refused-reference cascade, and
  `PackAudit` for a missing part. Unknown attributes are already faults (`Reader.Sound`), which the reference
  mod's silent defaults taught. **An `<Armour>` adds no component**, so a plate never makes a craft a weapons
  system — pinned in `ArsenalTests`. An `<Armour>` on a part that is also a tank lies outside its wall; on a
  launcher it is refused.
- **The munition class** — penetrator, HE superquick, HE delay — is a new `MunitionProfile` field, with its
  control or its `check-tunables` exemption.
- **The parts**: a first plate instances a Core panel mesh to fly the code before art exists; the family's art is
  authored in Blender and signed off before it is unwrapped. Mass, thickness and material are one number in
  three places — the part data, the definition, the display name — so `validate-parts.py` checks
  mass ≈ area × thickness × density and the name. A broken-off plate is a part that rides
  (`KsaWorld.PartRides`): not an installation, no head — correct as it stands.
- **The panel**: what each armoured part carries and how much of it is left, under the component rows, since
  the editor has no inspector hook. Craters and health are not saved, so a reload starts every plate whole.

## Performance

Unmeasured; to be measured, not assumed — the "≤ 20 µs a cast" of the first draft was a budget
(`TravelSweeps.RaysPerFrame`), not a measurement.

| Step | Cost | When |
|---|---|---|
| A plate's layer, cached by template | once per part | first collect |
| The kinetic walk through a plate | arithmetic; the ray casts are already paid | per hit |
| Fragment shadowing | parts with hits × plates part-casts | per burst, **only on an armoured craft** |

**How to measure**: `FrameBudget` buckets `armour` and `shadow`, and a bridge burst against a 50-part craft
carrying 0, 1 and 10 plates at 60 fps, reporting part casts per burst. A craft with no plate must cost exactly
what it costs today, and `Costs` with no layers must match today's output bit for bit on the same seed.

## The order of work

1. **The law in `Sim/`**: `ArmourLayer`, Lambert–Zukas with slope, residual, plug fragment and glance, with
   tests against the reference shots and the material ratios. The shared plug static, `TankWall` unchanged in
   behaviour.
2. **The walk into `Sim/`**, with `Crossing.Normal`, and a test that fails against today's `Wall.Known` path.
3. **The part charged is the part the ray hit**, with its fallbacks — worth having without armour.
4. **The munition class** and the burst on a plate's face.
5. **The pack reader** and its faults, the registry, the audit, `pack-api.py`, `docs/WEAPON-PACKS.md`,
   `validate-parts.py` and `export-example-copy.py`.
6. **One plate part** on a Core mesh, flown with the Phalanx and the M197 against a tank behind a plate and one
   without.
7. **Fragment shadowing**, flown with a warhead against the same pair, and the `shadow` bucket measured.
8. **The plate family and its art**; then **armoured tank variants**; then **the panel's armour rows**.
9. **Docs**: `CLAUDE.md`'s layout table, `docs/DAMAGE-PLAN.md`, `docs/BLOCKED-ON-KSA.md`, README's *What ships
   with it*, the wiki's family overview and models, `CHECKLIST.md`.

## What the reference mod taught

- **Copied**: materials as data in real units; one penetration law with slope; residual energy carried on;
  armour that wears; armour that costs real mass.
- **Not copied**: a tower of fudge factors on top of the law, which needed clamps and logarithmic hitpoints to
  balance; armour areas guessed from bounding boxes that differ between the editor and flight; random
  ricochets; a slider to pick a material from a list; armour that adds no mass, which it shipped with for a
  time.

## What is built

Flown on 2026-10-31 against a 2 m LOX tank with a test plate on each side, the Phalanx at 800 m.

- **The law** (`Sim/ArmourLayer.cs`): Lambert's limit as BRL-MR-3960 p. 70 gives it, reproducing the report's
  worked example to every printed digit; its residual with Lambert's own `A` and `p`; Rosenberg's glance with
  a hydrodynamic penetration speed; the dent of a round that stops. Materials (`KSArmory/Armour.xml`): rolled
  steel and aluminium on the report's constants, titanium's `a` derived from a 1.2 mass efficiency. A round
  that states no drawn body is taken four calibres long, and a fragment as long as it is wide — guesses.
- **The walk** (`Sim/Penetrator.cs`), with `GoesThrough` gathering the crossings: every part on the path gets
  its slope and its craters.
- **The part charged is the part the ray hit**, falling back to the nearest where the cast met nothing it
  can charge.
- **An exploding shell** bursts on a plate and holes only one thinner than 0.635 calibres of steel
  equivalent; there is no delay fuze, since no shipped round has one.
- **The pack reader**: `<ArmourMaterial>` and `<Armour>`, refused when misspelt, a plate's material resolved
  across a pack's files, no component row.
- **Fragment shadowing**: on a craft carrying plates only, each other part is cast to from the burst against
  the plates alone, and a fragment reaches it with Lambert–Jonas's residual or not at all.
- **The test plate**: Core's 2 m structural plate scaled to 25 mm, 617 kg.

What flying it showed: every dart met a plate first and got through it, as the law says at 928 m/s against
25 mm's 744; behind it a full tank was entered at 631 m/s and raised 4.12 bar, where the same tank bare was
entered at 892 m/s and raised 5.83. Both burst, since a flight tank's 3 bar load and either rise pass the
5.99 bar a dart's hole holds; a plate near 45 mm thick would stop the dart outright.

## Questions answered since

Answered from the decompiled game (2026.10.25.5707) and the code, 2026-11-01.

- **The editor scales parts, and saves it.** `VehicleEditor.UpdateSelectedScale` sets `Part.Scale` from 0.5x to
  2x, uniformly and in steps of a quarter metre of diameter (`ScaleBoundsFor`, `QuantizeScale`). The scale is
  written with the craft (`Part.GetReferenceWithChildren`) and mass grows as its cube (`InertMass.Rescale`,
  `Tank.SetScale`), so a scaled plate is thicker by the same factor — which the mod does not read yet: it judges
  the definition's thickness, and a scaled tank's wall and liquid in its mesh's units. It cannot stand in for a thickness, since
  size and thickness move together: the family still needs a part per thickness, but no part per size.
- **KSA has no lift, and nothing points to it arriving.** No wing, lift, aerofoil or control-surface type
  exists; drag is still the craft's bounding box (`VehicleProperties.RecomputeAerodynamicProperties`). A plate
  inside the box costs no drag, and one standing proud of it costs it on the face it widens.
- **No part variants.** There is no switch or option mechanism on a part, and the save's module data is
  registered from KSA's own assembly alone (`XmlHelper`), so a mod's per-part data would need the serializer
  patched (`docs/BLOCKED-ON-KSA.md`, *Configuring a part in the vehicle editor*).
- **A plate is not invulnerable to a burst on its face.** At the strongest tolerance KSA derives, a 5"/54
  shell's 3.5 kg bursting on the 2 m plate can cost it more than its whole health at 25 mm and at 50 mm, and a
  20 mm HEI's 11 g about 2%; a plate would have to weigh some 11 t to shrug off the 5"/54
  (`ArmourLayerTests`).
- **Armour on somebody else's part** weighs nothing, since the definition carries no mass.
  `validate-parts.py` checks a plate's mass box against its definition, in this mod and in a pack, and that a
  pack's plate is its own part.
- **How much the guessed lengths matter.** The limit goes as `(L/D)^0.15`, so the four calibres taken for a
  round with no drawn body moves it 11% if the truth is half or twice that.
