# The guidance section

**A plan, not a record.** Nothing here is built. It is the design for turning the ballistic
computer from something a craft gets for free into something a craft is *fitted with*, and it is
written to be handed to `.claude/skills/ksa-blender/` and to `Sim/Arsenal.cs` in that order.

One part: a 3 m interstage ring, **AIRS guidance section**, `KSArmory_Prefab_Guidance`. It stacks
between the last decoupler and the MIRV bus, it is the stack's command authority, and it is the
only thing that confers an `IcbmComputer`.

## Why it is a part

`IcbmComputers.Sync` crews a computer on every craft where `WeaponInventory.IsWeaponSystem` is
true. That is `Launcher || Gun || FireControl`, so a Pantsir standing on a hillside is issued a
ballistic autopilot it will never use, and a rocket gets one because the *bus bolted to its nose*
is a launcher. The capability is real and nothing in the world says where it came from.

The rule the mod applies everywhere else is that a part gives a craft a capability. This is the
one place it is asserted in a doc comment and not enforced by anything.

## The role is `Guidance`, not `FireControl`

`WeaponRole.FireControl` is taken, and it is taken by everything: the LAU-7, LAU-128, LAU-118,
Mk 15, B61 rack and MIRV bus all declare a built-in `FireControl` in `Arsenal.Components`. So
tightening `IsWeaponSystem` to require one changes nothing, and the `IsWeaponSystem` remark asking
for "an explicit fire-control part" is asking for something that already exists six times over.

The distinction that has teeth is a different axis:

| Role | Question it answers |
| --- | --- |
| `FireControl` | what does this craft shoot at |
| **`Guidance`** | **what flies this airframe** |

They are orthogonal — the Pantsir has the first and wants nothing to do with the second; a booster
carrying a guidance section and no weapon has the second alone and is a perfectly good sounding
rocket. So:

- add `WeaponRole.Guidance`,
- `IcbmComputers.Sync` crews on `CountOf(WeaponRole.Guidance) > 0` rather than on
  `IsWeaponSystem`,
- `IsWeaponSystem` is left as it is, because the launchers are honest about what they carry.

The Pantsir, the rails and the CIWS lose a computer none of them ever asked for, and no other
roster moves.

## Where it goes in the stack, and why that is not a detail

The bus declares one connector (`_mirvConnectorBase`) and mounts on a 3 m node, so today's stack is

```
[ MIRV bus         ]
[ decoupler        ]
[ third stage      ]
```

and the ring goes **above the decoupler**:

```
[ MIRV bus         ]   6 x Mk 21, 16 nozzles, its own tank and battery
[ guidance section ]   <- 3.00 m dia, 0.55 m tall, 450 kg
[ decoupler        ]
[ third stage      ]
```

Below the decoupler it is staged away at booster separation — which is precisely when `BusTrim`
needs it, the trim being the only actuator left once the burn is over. Above it, the ring and the
bus separate together and the guidance section is part of the post-boost vehicle, which is what
Peacekeeper's fourth stage actually is.

The computer **lives with the part**: `PlatformHandover` carries it wherever the ring goes, and a
stack that drops its guidance section reports `guidance section staged away` and holds. That is
the failure mode the layout above exists to teach, and it is only worth having because it is
signposted.

## One master, the rest redundant

Two rings on one stack is physically harmless and must not deadlock. The first in part-tree order
is the master and owns the `IcbmComputer`; the others appear as `redundant` rows under
**Components** and do nothing but add mass. Tree order is the same stable ordinal
`WeaponSystems` already keys on.

No failover. A master that is staged away does not re-crew onto a survivor — that would soften the
staging rule into a suggestion, and the whole point of putting the part in the stack is that where
it sits is a decision.

## What the part declares

```xml
<PartGameData Id="KSArmory_Prefab_Guidance" DisplayName="AIRS Guidance Section">
  <EditorTag Value="Weapons" />
  <Diameter M="3" />

  <!-- A guidance section IS the command authority: a booster carrying one needs no pod, and the
       post-boost vehicle keeps it across separation. Same declaration the bus carries, for the
       same reason. -->
  <Control />
  <Battery><MaximumCapacity J="?" /></Battery>   <!-- see Gates -->

  <!-- Two node connectors, no Flags and no ToSurface: it stacks both ways, and a part with no
       surface connector may root a craft. -->
  <Connector Id="_guidanceBase" />
  <Connector Id="_guidanceTop" />

  <SolidSphereMass>
    <Mass Kg="450" />
    <Radius M="1.20" />
    <LocationAsmb X="0.275" />
  </SolidSphereMass>

  <Collider Id="KSArmory_GuidanceCollider">…the atlas's _ColPrim_ cylinder…</Collider>
</PartGameData>
```

`EditorTag` is `Weapons` so it is found beside the bus it is always fitted with, rather than filed
under control gear where nobody looking for a ballistic shot will go.

## What it weighs, and where that mass comes from

**450 kg, and it is taken out of the bus rather than added to it.**

The buildup, anchored on the one number that is measured rather than estimated:

| | kg | |
| --- | ---: | --- |
| AIRS | 204 | the beryllium ball itself — gyros and accelerometers in fluorocarbon. 450 lb against a 430 lb spec |
| electronics, computer, harness, sequencer | ~90 | |
| battery | ~30 | see Gates — the capacity is not settled |
| ring structure | ~130 | 3.00 m load-bearing shell, two mating flanges, equipment shelf |
| | **~450** | |

**The bus already carries this, and that is the part worth catching.** Peacekeeper's fourth stage
is ~1,363 kg and is *"a maneuvering rocket **and a guidance and control system**"* — the guidance is
inside it. `KSArmoryGameData.xml` anchors the bus to exactly that stage:

```
2,750 kg  =  6 x Mk 21 @ 250 kg  +  1,260 kg dry Stage IV
             1,450 kg loaded Stage IV - 190 kg propellant
```

So a 450 kg ring bolted underneath counts the guidance twice, and the post-boost assembly comes out
28% heavier than the vehicle its mass model was calibrated against. The ring's mass is a **split of
the 1,260 kg**, not an addition to it:

| | now | with the ring |
| --- | ---: | ---: |
| `KSArmory_Prefab_MirvBus` | 2,750 kg | **2,310 kg** — 1,500 kg of RVs plus 810 kg of manoeuvring rocket, tank, plumbing, RV mounts and shell |
| `KSArmory_Prefab_Guidance` | — | **450 kg** |

1,260 kg of post-boost vehicle across two parts, which is what Stage IV is. Both XML comments have
to carry the split, or the next reader re-derives the bus from Peacekeeper's throw weight and puts
the 440 kg back.

## The geometry brief

Closed shroud. 3.00 m diameter, **0.55 m** tall, origin on the **bottom** mating face, extending
**+X** — the same convention the bus body uses (0.90 × 3.00 × 3.00, centred at +0.450).

- **Shell.** 32-sided cylinder, skin only. Match the bus's radial resolution so the two read as one
  vehicle rather than two parts that happen to be stacked.
- **Mating flanges.** A proud ring at each end, 3.02 m across and ~30 mm tall, with bolt detail
  around it. This is what makes the joint look like a joint from three metres away.
- **Cable raceway.** A half-round conduit ~180 mm wide running the full height on one side, held
  by two clamp bands. The one asymmetry on the part, and the thing that tells a player which way
  round it is.
- **Access hatches.** Two recessed panels, ~500 × 400 mm, fastener ring around each. One carries
  the `AIRS` stencil.
- **Umbilical plate.** A recessed ~300 mm square with a pin connector and a hinged cover, 90° round
  from the raceway.
- **Thermal band.** A shallow ridge at mid-height suggesting a wrapped section — one loop, cheap,
  and it breaks up the silhouette.

Its own atlas and material: `Meshes/KSArmory_Guidance.glb`, `Textures/KSArmory_Guidance_{Diffuse,
Normal,PBR}.png` at 2048², declared with `<MeshAtlas>` and `<PbrMaterial>` beside the bus's.

Export contract, per `.claude/skills/ksa-blender/`: `KSArmory_Subpart_Guidance`, its `_VM` twin,
and a `_ColPrim_Cylinder` carrying the collider; node names matching mesh names; part space with
the origin on the mounting face. An authored asset obeying all of that needs no import step —
copied in as exported and declared, the way the suspension rail is.

Run `tools/model/checkmesh.py` and `tools/validate-parts.py`. `checkswept.py` has nothing to say:
nothing on this part moves.

## The panel

A `Guidance` component row under **Components**, and the ICBM pane hangs off it — loft, arrival
angle, ascent schedule, staging, trim. Those describe what flies the rocket, and CLAUDE.md's
ownership rule puts a control that drives one part of one installation on that part's row.

The **designated site stays with the weapon**, which is where `IcbmConfig`'s own doc comment
already puts it: the target is a designation, and it belongs to the thing that will act on it.

`IcbmConfig` moves from being created per craft in `Sync` to being owned by the master ring, which
also gives `SettingsStore` an ordinal to key on — the same shape as two rails on one aircraft.

## Gates before it flies

- **The battery size is unknown and it can lose a shot.** `BurnWindow`'s horizon is a **day**: a
  target off the ground track has no affordable arc until the planet has turned under it, and the
  computer will sit in `Holding` for hours. The bus's 50 kJ is sized for a deployment measured in
  minutes. Measure what `<Control />` actually drains in KSA and whether a flat cell kills control,
  before picking a number — and if the answer is that the ring draws from the stack while attached
  and its own cell only covers the post-separation phase, say so in the XML comment.
- **Re-seating the bus's mass is a behaviour change, not a bookkeeping one.** Taking 440 kg off
  `KSArmory_Prefab_MirvBus` changes the post-boost mass, hence `BusTrim`'s authority per second of
  firing and the deadband the release is aimed within. It is a night of shots under
  `docs/SHOT-PROTOCOL.md`, flown as its own arm against an unchanged baseline — not something to
  commit alongside the art.
- **The ring shifts the assembly's centre of mass, and `checkring.py` cannot see it.** It reads one
  part's own declared mass seating: `KSArmory_Prefab_MirvBus: mass seated at X=0.300`, chosen so the
  thruster ring's axial lever arm is zero and only the axial pair pitches. 450 kg at X ≈ −0.275
  below the bus's origin moves the combined centroid to roughly +0.212, giving all twelve non-axial
  nozzles a lever arm — which enrols them in pitch and yaw and coarsens the attitude quantum that
  aims the release. Same blind spot as `checkswept.py`'s per-vehicle one: **when a part stops being
  the only thing in the stack, check what still assumes it is.** Either teach `checkring.py` a
  stack, or re-seat the bus's mass for the assembly it will actually fly in.
- **Existing test craft need refitting.** Pre-1.0, so this is a cost rather than a blocker, but
  `tools/scenario.sh mirv`'s save carries a bus with no guidance section and will not fly a shot
  until it does.

## Sources for the mass figures

- AIRS at 450 lb, 430 lb spec — [Advanced Inertial Reference Sphere](https://en.wikipedia.org/wiki/Advanced_Inertial_Reference_Sphere),
  [nuclearweaponarchive.org](https://nuclearweaponarchive.org/Usa/Weapons/Airs.html)
- Peacekeeper post-boost vehicle ~3,000 lb, "a maneuvering rocket and a guidance and control
  system" — [GlobalSecurity LGM-118A](https://www.globalsecurity.org/wmd/systems/lgm-118.htm),
  [FAS](https://nuke.fas.org/guide/usa/icbm/lgm-118.htm)
