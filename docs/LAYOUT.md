# Layout

Every file under `src/KSArmory/`, `tools/`, `tests/` and the asset trees, and what each is
for. `tools/check-docs.sh` holds this to the source: a `.cs` under `Sim/` or `Ksa/` with no
row here fails, and so does a row naming a path that no longer exists.

Moved out of `CLAUDE.md`, which loads in full on every session: this is reference to consult
when working on an area, not a rule to hold in mind.

**The source is split by whether it touches KSA.** `Sim/` cannot; `Ksa/` does. That is not a
convention to remember — the test project links `Sim/**` wholesale and references no KSA
assembly, so a `using KSA;` under `Sim/` fails the test build. It also means a new file under
`Sim/` is tested the moment it exists, with no build plumbing to add.

| Path | What |
| --- | --- |
| **`src/KSArmory/Sim/`** | **no KSA types, linked into the tests wholesale** |
| `Sim/Catalogue.cs` | **what the mod reads** — the built-ins from `KSArmory/*.xml` plus anything else registered; the only registry |
| `Sim/Armoury.cs` | **the code surface a weapon pack may bind to**, for the pack that ships an assembly — four members, taking text rather than profiles so a pack needs no game assemblies. A pack with no code never calls it: `InstalledPacks` reads its folder and registers through the same `Register` |
| `Sim/PackReader.cs` | somebody else's weapon definitions, read into profiles — **text in, no file access**, so every refusal is testable headlessly |
| `Sim/PackScan.cs` | where KSArmory looks for somebody else's weapons — **a convention, not a list** |
| `Sim/PackAudit.cs` | whether what registered can actually be found — the half of validation that has to wait until the world has loaded |
| `Sim/IPartCatalogue.cs` | **the seam the audit asks what parts exist through** |
| `Sim/PackResult.cs` | what one pack got out of registering: how much stuck, and everything that did not |
| `Sim/CoreDefinitions.cs` | this mod's own definition files, registered **as one unit and before anyone else's** — from its own `[StarMapBeforeMain]`, which StarMap runs ahead of every pack that ships code, and its files read as one unit through `PackFiles` |
| `Sim/PackFiles.cs` | one mod's definition files read **as one unit** -- every name each declares known to the others, so a launcher may name a round in another file whatever order they sort in; the built-ins and every pack go through it |
| `Sim/BuiltInSwitches.cs` | which of KSArmory's own weapon families the player switched off, as the file it is kept in -- **anything unreadable switches nothing off** |
| `Sim/PackContents.cs` | what reading one pack produced: what may register, and what may not |
| `Sim/PackFault.cs` | one definition that was refused, and the reason an author can act on |
| `Sim/WeaponSurvey.cs` | reads a weapons system off a craft the mod did not design |
| `Sim/LauncherProfile.cs` | one launch platform: part Id, tube geometry, drives |
| `Sim/MunitionProfile.cs` | one round: boost, guidance, drag, fuse, warhead — **its drag from its mass, calibre and coefficient**, not a constant typed for it |
| `Sim/Warhead.cs` | explosive charge to lethal, blast and fireball radius |
| `Sim/FlashGlare.cs` | how much of a burst's glare reaches somebody facing elsewhere — **never nothing**, because light that bright scatters in the air, veils across the optics and lights the landscape they *are* looking at |
| `Sim/WarheadExplosion.cs` | which of KSA's explosions a warhead goes off as — **the preset carries the size**, because KSA's floor swallows every conventional charge |
| `Sim/SensorProfile.cs` | one sensor: range, cone, threat model |
| `Sim/OpticProfile.cs` | one optical head — its own part, or one a launcher carries |
| `Sim/OpticGeometry.cs` | where a director's head sits and how far it may look, **measured from the base it rides** |
| `Sim/OpticConfig.cs` | one director's own settings — where it looks, how far it zooms |
| `Sim/Config.cs` | session-wide settings — team names, drawing, logging |
| `Sim/SystemConfig.cs` | one installation's own settings — arm, engage, turret mode, IFF |
| `Sim/SystemSettings.cs` | those settings flattened, so they can be written down and read back |
| `Sim/IProjectile.cs` | **what everything in the air must be** — a weapon kind is an implementation, not a profile field |
| `Sim/RoundDriver.cs` | **the one place a round is advanced by one frame** — the game and the rig both come through it, so neither can differ about which fields a round re-reads within a frame |
| `Sim/Interceptor.cs` | guided round: proportional navigation, boost, fuse, and the ground it stops on |
| `Sim/Slug.cs` | unguided round — shell, bomb or rocket: ballistics, a contact fuse, and a motor pushing along the velocity it gained when the munition declares a boost |
| `Sim/TailKit.cs` | steering a falling store onto a place on the ground — **the landing it predicts, moved onto the designation**, because a store thrown from a climb flies away from where it lands and a line-of-sight law turns the wrong way |
| `Sim/TailKitReach.cs` | how far a store already falling can still move its landing — **flown, because `½·a·t²` is not a bound**: a lateral push does not accumulate against drag, it settles where fin authority and lateral drag balance, so the share of `a·t²` delivered runs 0.46 at a 21 s fall and 0.18 at 95 s and a constant fitted at two kilometres promises three times the truth from twenty. **It decides what a falling store may be sent to**, so it has to stay a floor |
| `Sim/BlastSweep.cs` | how near a burst a body was, and what that does to it — shared by the sweep over craft and the one over rounds |
| `Sim/InFrame.cs` | a point carried between the instant a round burst inside the step and the sample at its end — **one function for each direction**, because a sign slipped at any site is a step of ~30 km/s the wrong way |
| `Sim/BlastWave.cs` | the blast wave in real pascals — **Kinney–Graham**, the overpressure, how long it pushes and the wind behind it — because `BlastDamage`'s law is calibrated to KSA's part strengths and says nothing about what the air is doing |
| `Sim/BlastAltitude.cs` | a burst's blast in the air it went off in, by **G&D's rule**: efficiency by the air at the burst, Sachs by the air at the target — and a damage charge whose floor comes back to the whole charge where X-rays outrun the lethal radius, **because what breaks a craft beside a burst above the air is its X-rays**. Sea level to the bit |
| `Sim/BlastFront.cs` | **one burst's front, built once** from its charge, the air at it, its body's speed of sound and how far the ground doubles it — so the drawn front, the ring, the cloud held inside it and every part's load cannot disagree about where it is |
| `Sim/BurstRegime.cs` | which of a burst's behaviours its air switches on, **each keyed on its own physics** — pressure for the blast, the fireball against the scale height for the mushroom, the column above for the X-rays, the density at the burst for whether its air is **too dry to condense** — and each reproducing Earth's old threshold |
| `Sim/BurstHearing.cs` | whether the front is heard and felt where it reaches — **one gate at 50 Pa for the bang and the shake**, because they are one wave |
| `Sim/ThermalAltitude.cs` | a burst's heat and light by its air — **G&D's pulse law, the thermal fraction, the glow fitted to Orange and Teak, and the double pulse merging** above 100,000 ft — each exactly one at sea level; no air at all is the vacuum's half-second flash, `MushroomCloud.VacuumFlashSeconds` |
| `Sim/GroundReflection.cs` | what the ground under a burst adds to its blast at a point — **the hemisphere's doubling on the ground, and an air burst's reflected front where it stands on the ground as the Mach stem**, 2x a weak front to 8x a strong one, so at the right height an air burst loads the ground harder than a surface burst; nothing above the stem. A multiple of the free-air pressure, so of the charge the damage law sees |
| `Sim/BlastShove.cs` | what that wind does to a craft, **part by part**, summed into a kick and a turn about the centre of mass — a tall rocket hit side-on tips because it is pushed hardest above its middle |
| `Sim/ViewShake.cs` | the view thrown about as a front passes the eye — a jolt then a rattle, as hard as the real overpressure there |
| `Sim/BlastDamage.cs` | which parts of a craft a burst breaks — **nothing here picks a part**: each is judged on its own distance and the strength the engine derived for it |
| `Sim/HoleLook.cs` | how big a shell's mark on a hull is — an exploding shell's hole at a radius of one and a half calibres, an inert dart's little wider than itself, the soot by the cube root of the charge |
| `Sim/TankLeak.cs` | where the liquid in a holed tank stands and how fast it runs out — **the surface square to the acceleration the craft feels**, followed with a 1.5 s lag because liquid crossing a tank takes seconds (read raw, a coasting craft's attitude pulses settled and floated it several times a second), found by counting points spread through the tank's own shape rather than solving a tilted one: `TankShape` is KSA's one family of tank, a truncated cone with a dome each end, its kind and proportions read off the template by `KsaWorld.TryTankShape` and **fitted into the part's box**, because a template is sized to KSA's volume model and not its mesh (the stock 3 m tank's is 3 m in *radius*) while the holes are on the mesh; the plain cylinder in the box where it cannot be read, and Bernoulli's rate through each hole under it, `√(2P/ρ + 2ah)` — **only a hole inside the cylinder round that shape, give or take the skin** -- the domes are the volume model's and meet in the middle of Core's 3 m tank, whose mesh is a cylinder -- so a raceway holed on the same part leaks nothing. KSA gives a liquid no pressure, so the tank's is the mod's, **0.2 bar and held constant**: low enough that depth and pull still double or halve a leak, where a flight tank's couple of bar would make the hole's place irrelevant, and enough that a **coasting craft** — its liquid floating, wetting the walls — leaks from any hole as often as the tank is full; **a gas tank vents by its own pressure**, which KSA now keeps (`Mole.GetPressure`) at a held temperature: choked from any hole in the tank while it is at twice the air outside, so it decays exponentially, at a γ of 1.4 since KSA declares none |
| `Sim/HoleMerge.cs` | holes in one skin close enough to tear into each other taken as one opening — **their whole area at its area-weighted middle**, nearer than twice their radii together or 15 cm, chaining along a row of hits — so a burst leaks as one jet rather than a bundle of threads, kept under its first hole so a later hit joining it does not restart the stream |
| `Sim/LeakStream.cs` | a hole's stream, **flown parcel by parcel** (`LeakJet`) in a frame riding with the craft but not turning with it, under the pull the craft feels reversed — so a throttle change bends it from the hole outward and a turn swings it, where an arc solved for the acceleration of the moment re-bends whole in a frame; thinning as it speeds up (`r ∝ 1/√v`) and whole for about thirty diameters before it is drops |
| `Sim/PartHealth.cs` | what is left of each part, from one down to nothing — a hit costs its share times `Config.DamageScale`, never more than a whole share at once, and a load short of breaking is not forgotten |
| `Sim/Fragmentation.cs` | **what kills past a few metres** — a warhead's fragments thrown in a band about its nose, tilted forward by its own speed against the target, thinning as the square of the range and counted on a part as a Poisson draw; a part outside the band takes nothing however close, which is why a missile's kill probability is not one. And where a fuze matched to its warhead fires, since bursting at the nearest point throws the band ahead of a fast pass |
| `Sim/EngineDamage.cs` | what a hurt engine can still do from what is left of its part — **its thrust capped as a lower throttle would cap it**, whole above 0.8 and nothing at 0.2, the gimbal gone below 0.5, and at 0.2 shut down |
| `Sim/PartFire.cs` | parts on fire — lit by a hit on a part holding propellant — the munition's `IgnitionChance` for its own strike and `FragmentIgnitionChance` for each fragment — burning it out in 20–100 s, **a fifth going up at once** rather than burning |
| `Sim/Paintball.cs` | **paintball**: a hit marks what it struck with the shooter's paint and harms nothing (`Config.Paintball`, the damage slider's far left) -- one hue a team, stable across sessions, and a splat a hand across for a shell and metres for a warhead |
| `Sim/PartToughness.cs` | how much energy breaks a part — **its mass to the two thirds**, so size buys toughness where KSA's tolerance reads only density — and what a burst's intercepted share, a fragment or a shell's own impact pays against it; a round in the air is a fraction as tough, since one fragment through its warhead or motor ends it |
| `Sim/TankWall.cs` | a tank's wall as KSA declares it -- thickness, yield, rating -- and **what gets through it**: a dart or a fragment holes it only with more energy than shearing a plug of its own size out of what is left there -- one that stops dents it (`Sim/WallCraters.cs`), and a fragment that stops costs the share of the wall it got through -- and a holed gas tank **bursts** where its pressure beats the hoop stress a through-crack of the hole's radius stands, one aluminium-like toughness for every material since KSA declares none. The wall is divided by `Config.DamageScale`, so a tenth of the damage is a wall ten times as strong. A tank KSA declares no wall for stops nothing and never bursts |
| `Sim/HydroRam.cs` | what a round entering a tank's liquid does to the tank — **the speed past which the liquid's push cracks the wall at the hole**, fitted to NASA TM X-52063's table I (2014-T6, loaded to yield, 5.56 mm spheres) and scaled by wall, size, load, liquid and mass per area; the energy the liquid takes off the round; and **the cavity's pressure rise as an energy balance** against the ullage gas and the wall's give, finite for a full tank. The wall's load is a flight tank's 3 bar, since KSA gives a liquid none; `docs/HYDRODYNAMIC-RAM.md` |
| `Sim/ArmourMaterial.cs` | what an armour plate is made of, as the law reads it — density, **Lambert's coefficient** (4,000 rolled steel, 1,750 aluminium, BRL-MR-3960) and Tate's target resistance — read from a definition file, so a pack can add one |
| `Sim/ArmourProfile.cs` | a part that is an armour plate: its material and thickness. **No component row**, so a craft of plates is not a weapons system |
| `Sim/ArmourLayer.cs` | a plate's layer and what a round does against it — **Lambert's ballistic limit** with slope as `sec^0.75 θ`, the Lambert–Jonas residual with Lambert's own constants, Rosenberg's glance, the dent of a round that stops, and whether an exploding shell's casing holes it (0.635 calibres of steel, the M56's). Not a tank: nothing here bursts or rams; `docs/ARMOUR-PLAN.md` |
| `Sim/Penetrator.cs` | **a solid round walked through the parts on its path** — a plate by Lambert's law, a tank by its wall then its liquid through a ram callback, anything else keeping its share — returning each part's share, the dents and the speed left, so spaced plates and a plate in front of a tank follow from the order alone |
| `Sim/WallCraters.cs` | where darts that did not get through a tank's wall have thinned it -- **as deep as the hit's energy would punch**, since the plug's work grows as the thickness squared -- kept per skin in the skin's frame as the holes are, so a spot hit again needs less: a dart a tenth short of going through gets there on about the fourth hit; the shallowest goes past 32 a skin |
| `Sim/TargetAllocation.cs` | what one craft's weapons have in the air **between them** — the unit that over-commits is the craft, not the weapon |
| `Sim/RoundReach.cs` | whether a round the ground stops can still get to it — **the reaper for a store that will never arrive**, because a long fall is long rather than stuck |
| `Sim/PointGravity.cs` | **the pull every round, store and decoy is flown with** — the inverse square aimed where the body was at the round's own instant, plus the body's own fall toward its primary; a contact's acceleration leaves the fall out, as the ground frame does |
| `Sim/Medium.cs` | what the air or water a round flies through does to it — buoyancy and drag, shared by every round, against **one reference air for every body**, so a round is dragged by the air actually there |
| `Sim/ContactSweep.cs` | the contact rule: whether a round runs into a body over one step |
| `Sim/IHullTest.cs` | **the seam a kinetic round asks whether it truly touched something** |
| `Sim/IGroundTest.cs` | where the ground is under a round, for the one round the terrain stops |
| `Sim/CoarseGroundTest.cs` | the sight's ground test, which skips the lookups a falling round cannot need |
| `Sim/MushroomCloud.cs` | the shape of a nuclear cloud over time, as offsets from **the ground under the burst** — an air burst's cap starts up at its height and everything else stays on the ground; Bravo and Tsar Bomba are the two shots its top end is calibrated on |
| `Sim/XRayGlow.cs` | the layer of air a burst over the atmosphere lights — **placed by the column of air above it**, 82 km on Earth and wherever the same column lies on any other body — with its X-rays — **a night sky's red for minutes**, green for the first seconds, and a faint pink by day, because an aurora is thousands of times fainter than the day sky |
| `Sim/DebrisShell.cs` | what a burst in air too thin for a mushroom leaves -- **its debris as light, not as a cloud**: white-hot and filling the ball, cooling into a red, rim-bright shell gone in three minutes, stretched along the field line where the air is thin enough for the field to hold it |
| `Sim/DebrisBubble.cs` | what a burst above the air leaves instead: **its debris held by the field**, at the equal-volume radius where the field's pressure stops it, stretched 2.7:1 along the field and collapsed within 16 s — the kinetic share fitted on Starfish, which it then draws at 1,833 by 679 km against 1,840 by 680; ballistic on a body with no field |
| `Sim/RedWave.cs` | the red sphere a burst 60 km up or higher sends through the thermosphere — **Teak's, 965 km across at six minutes** — at 1.35 km/s with oxygen's red trailing 150 km behind the front, glowing only where the air is thin enough that the red is not quenched: a density, about 150 km on Earth. Its own sky dispatch, drawn in closed form, and a Display switch |
| `Sim/Aurora.cs` | the aurora a burst above the atmosphere lights **at both ends of its own field line** — near the burst and at the magnetic conjugate point in the other hemisphere — drawn as KSA draws its own aurora, cut to a patch: two folded arcs, field-aligned rays, green low and red high, nothing by day; Teak's comes down over Samoa |
| `Sim/MagneticDipole.cs` | **a body's field as KSA declares it — tilted and off the centre** — and a field line's feet **walked down to the border's sphere about the planet**, because off the centre that sphere is not one about the dipole; `KsaWorld.DipoleOf` reads it, `Bodies.xml` only for a body KSA gives none |
| `Sim/SkyDispatch.cs` | the kinds of full-screen dispatch a burst's sky is drawn with -- **and what each puts in `FireSun.w`**, which the scorch-mark branch would otherwise read as its own and offset off the screen, as it did every northern aurora |
| `Sim/CloudFlags.cs` | the cloud dispatch's packed flag float — spray, weather, first, **dryness in sevenths** and the front's radius — **exact to 262,143 m**, because every term is an integer under 2^24 and the shader divides by powers of two with nothing added |
| `Sim/ScorchFootprint.cs` | how far a scorch mark reaches downwind, across and upwind, in patch radii — **the shader's own constants restated**, so the box the cloud pass dispatches a mark over never crops it; `ScorchFootprintTests` compares the two |
| `Sim/AmbientAir.cs` | the air at a point as density and pressure **against fixed physical references**, not a body's own sea level -- so a threshold calibrated in Earth's air holds on any body -- with a flag for air that could not be read, never a silent sea level |
| `Sim/BodyAir.cs` | a body's air as numbers, **mirroring KSA's layered model exactly** -- the column hydrostatic, `P(h)/g`, one sound speed per body, the surface's -- so nothing downstream knows which planet it is on; without a profile it is isothermal, which is what the rigs fly |
| `Sim/AtmosphereProfile.cs` | **KSA's `PhysicalAtmosphere` restated**: temperature linear in geopotential between the declared points and held above the last, pressure hydrostatic through each layer; `KsaWorld.BodyAirOf` logs how far it lands from the engine's own density |
| `Sim/BodyTraits.cs` | what a burst or a beam needs about a body, read from `Bodies.xml` rather than the game -- field, airglow, condensation, γ, surface, X-ray opacity, which cloud layers are too thin to stop a beam -- **the neutral answer by default**, never Earth's |
| `Sim/BodyReader.cs` | a mod's `KSArmory/Bodies.xml` read -- text in, and **told from a weapon pack by its root**, since the pack reader would refuse it on every load |
| `Sim/BodyCatalogue.cs` | every mod's body entries, a later mod restating a body, and the audit that finds an entry naming no body in the system |
| `Sim/BurstSetting.cs` | what a burst went off on or in — **land, the sea's surface, or under it** — which decides whether it lifts dirt or spray, burns the ground, or raises no column at all; anything unreadable is land, which is what every burst was before. And how high over it the burst stood, over the sea where there is sea |
| `Sim/FireballBlackout.cs` | the air a nuclear fireball ionised, which a **transmitting** radar cannot see through or out of until it cools — a sphere riding up with the ball, about a minute for a megatonne |
| `Sim/BurstRadiation.cs` | what a burst irradiates, in Sv/s as KSA's dose gauge reads it — **the prompt flash, the fallout plume, the debris still aloft and the belt a burst above the air fills**, the ground and the cloud on one calibration (Glasstone & Dolan's 2,900 R/h per kt over a square mile), so neither can disagree with the other; `docs/NUCLEAR-RADIATION.md` has the fits |
| `Sim/AirlessBurst.cs` | what that burst leaves where there is no air — **the ballistics are the engine's**, because KSA counts no atmosphere below 100 Pa and falls every particle at full local gravity there, so thrown ground arcs and lands with nothing here integrating it |
| `Sim/StoreLoad.cs` | how much of a launcher part's mass has left with its rounds — **worked out from what is still aboard, never subtracted**, so a reload, a refused shot and a save reload all put it back with nothing to undo |
| `Sim/Magazine.cs` | which tubes hold a round, which fires next, what each body does |
| `Sim/RoundLabel.cs` | what to call a round in a line somebody reads — **the one place the tube field's sentinel is decoded**, because a shell has no tube |
| `Sim/TubeGeometry.cs` | tube positions and directions, pod and radar pose, body placement |
| `Sim/TurretMode.cs` | which rung the ring was laid on this frame, and **the claims each rung makes on it** — decided from this frame's rung alone, because the fire ladder holds the missiles on them |
| `Sim/Turret.cs` | rate-limited traverse and elevation drives — **a limited traverse turns the long way round**, never through the host behind it |
| `Sim/TravelMap.cs` | where a gun can point without its barrels or its line of fire meeting its own craft — **a traverse arc and a band of elevation per bearing**, which replace the profile's fixed limit once swept |
| `Sim/TravelSweep.cs` | which poses of that map to test — **coarse first, then only where the answer changes**, so an obstacle smaller than 10 deg both ways can be missed |
| `Sim/ActuatorLinkage.cs` | a cylinder on the traverse and a rod on the cannon, posed from the elevation — drawn only |
| `Sim/GunRotor.cs` | a rotary cannon's barrel cluster, spun up to a barrel per round while it fires and run down after — drawn only |
| `Sim/PointingDrive.cs` | a head that points rather than trains — two degrees of freedom, no axes of its own |
| `Sim/StowTuck.cs` | a parked roll-nod ball nodding on until its windows face into the nose — **and back before the head may slew** |
| `Sim/Ejector.cs` | an ejector's push as a stroke — **the store accelerated evenly over the arms' travel**, leaving at the ejection speed as they reach the end, and the arms drawn on the same motion, so they stay on it the whole way |
| `Sim/FireGeometry.cs` | launch direction and round-body orientation |
| `Sim/BodyAttitude.cs` | which way a round points, and how a released store noses over |
| `Sim/RoundSpin.cs` | the roll a round's own spin motors give it as it leaves -- **drawn only**, a closed form of its age, on the launcher that seats the body carrying the motors |
| `Sim/BombSight.cs` | where a store released now would land, flown rather than solved |
| `Sim/Lambert.cs` | the transfer between two points in a stated time — **the one thing solved rather than flown** |
| `Sim/BallisticBody.cs` | the planet an arc is flown around, and how it carries a point on its surface |
| `Sim/AimSite.cs` | a place on a world, as the thing a ballistic missile is aimed at |
| `Sim/BallisticArc.cs` | what a vehicle must be doing at burnout for the fall afterwards to arrive |
| `Sim/Kepler.cs` | where a coasting body will be later, in closed form — **so a search can ask thousands of times** |
| `Sim/AimFrame.cs` | which way is up for a vehicle told to point somewhere — **the roll a pointing command leaves undecided** |
| `Sim/OrbitPlane.cs` | how far off the plane a target sits, and what that costs — **the explanation for an inexplicable burn** |
| `Sim/BurnWindow.cs` | **when** to start burning, which is not the same question as how to fly it |
| `Sim/ArrivalBudget.cs` | the steepest arrival the tanks can pay for — **what bounds the angle control**, so a ceiling is seen rather than discovered when the shot falls short |
| `Sim/ImpactPredictor.cs` | where it would come down if the engines stopped now — flown, not solved |
| `Sim/ArrivalFrame.cs` | the axes an arrival is measured in — **a drift up costs `cot γ` times the same drift across**, which is eight at the angle this mod flies |
| `Sim/GroundSlope.cs` | how steep the ground under a target is, and what a round arriving on it pays — **past a loop gain of one re-aiming has no fixed point**, and it is the gradient, never a ring's median |
| `Sim/AimCorrection.cs` | where to aim so it lands on the target — **the solver arrives at a point, a round stops at the ground** |
| `Sim/AimAuthority.cs` | what a metre of aim movement costs the trim — **a property of the trajectory**, and the rate the trim floor in `Ksa/Icbm/IcbmComputer.Trim.cs` is a stop band over |
| `Sim/BoosterPerformance.cs` | what the stack can still do, as the four numbers guidance needs |
| `Sim/BurnoutGuidance.cs` | where to point and when to stop — velocity still to be gained |
| `Sim/AscentProfile.cs` | the schedule flown while there is air, and the limit that keeps the stack in one piece |
| `Sim/PostBoostAim.cs` | correcting the aim after the engines stop — **the trim is the actuator**, and nothing is read off a bus whose nose is turning |
| `Sim/Icbm/IcbmProgram.cs` | **the flight** — pad to cutoff to release, as one phase machine |
| `Sim/Icbm/IcbmProgram.Ascent.cs` | the vertical rise and the pitch programme, and when the ascent hands over to the closed loop |
| `Sim/Icbm/IcbmProgram.Holding.cs` | coasting on purpose until the burn window opens |
| `Sim/Icbm/IcbmProgram.Solve.cs` | the trajectory re-solved a few times a second, and the countdown it sets |
| `Sim/Icbm/IcbmProgram.Drag.cs` | a short shot's arc flown with the warhead's drag, and the aim moved by what it misses |
| `Sim/Icbm/IcbmProgram.ClosedLoop.cs` | guiding to cutoff — the throttle-down, the held line, the pause out of the air, and when to stop |
| `Sim/Icbm/IcbmProgram.Coast.cs` | after cutoff — the arc re-solved for the aim correction, and the release gate |
| `Sim/Icbm/IcbmProgram.Reach.cs` | whether the tanks can pay for the shot, why there is no arc, and the arrival floor the search is bounded by |
| `Sim/Icbm/IcbmProgram.Limits.cs` | what may be commanded — the airflow and horizon limit on the line, the acceleration cap, the ascent reserve, spent solids |
| `Sim/HoldPointing.cs` | when a stack holding for its window turns onto the burn — **as long before ignition as the turn takes**, because pointing on the bus's jets empties them |
| `Sim/ReleaseCoast.cs` | where the predicted arc will be when the warheads leave it — the kick added there rather than at cutoff (`IcbmConfig.KickAtTheRelease`, on) |
| `Sim/HoldingCost.cs` | what a second of holding the warheads costs, **measured off the arc rather than typed**, because it runs from 0.82 m/s at 500 km to 21.79 at 12,900 |
| `Sim/BusTrim.cs` | putting the bus back on its solution after the split — **the only thing that can**, because the burn is over |
| `Sim/BusTurn.cs` | one commanded turn of the bus after its split — **judged on the nose the engine reports**, and held until it settles or the cap keeps whatever it reached |
| `Sim/HopBurn.cs` | one hop on a post-boost bus's own engine -- **held until the warhead just released is 10 m clear, turned onto it, burnt down to the nozzles' metre a second, turned back** -- with the arc re-solved every two seconds of burn, because a burn is not an impulse (`IcbmConfig.HopsBurnTheEngine`, off) |
| `Sim/EntryDirection.cs` | the airflow a coasting RV meets where the air first turns it — **closed form on the conic**, so any coast state gives the same entry |
| `Sim/PostCutoffSequence.cs` | what the loop after cutoff does next — **the decision that dominates where the warheads land** |
| `Sim/ShedStage.cs` | which of the vehicles that just appeared is the half this stack let go of — **bounded and refusable**, because several rockets stage together and a foreign stage passes the clearance at once |
| `Sim/SeparationClearance.cs` | whether what let go has got far enough away to manoeuvre — **the shove is the separation**, so nulling it ends it |
| `Sim/ProximityWatch.cs` | how near the bus ever came to the stage it dropped — **the minimum over the whole coast** |
| `Sim/ReleasePointing.cs` | which way a launcher must hold for one tube to throw along the line the others did |
| `Sim/ReleaseSequence.cs` | letting a magazine go one round at a time, each along that same line |
| `Sim/SalvoProbe.cs` | the release probe a warhead's separation kick is solved against — **its own, or its salvo's last that landed** |
| `Sim/ReleaseFocus.cs` | the velocity each round leaves its tube with so it lands where the tubes' mean does, and the probe's own miss cancelled — **under a cap, so a separation cannot become a burn** |
| `Sim/WarheadFootprint.cs` | where each warhead of one salvo is aimed — **all six on the designation is what ships**; a commanded ring is for measurement, bounded by the separation cap |
| `Sim/TargetSet.cs` | the places one bus is aimed at and how many warheads each gets — **data only**, so a set of one is exactly the single-target salvo; **the flight is aimed at `LeadIndex`, not the first chosen** |
| `Sim/TargetEdit.cs` | when that list may be edited — **an add needs a reach that can refuse it**, the lead moves only while the aim is free, and is never removable |
| `Sim/DivertFootprint.cs` | how far one bus can move its own landing and what the trim pays for it — **the arrival clock has no default**, because the flight pins it and the pinned reach is a disc |
| `Sim/ReleaseItinerary.cs` | when a bus with several targets lets each one's warheads go — **ending on today's release gate**, so a set of one is the shot everything is measured against |
| `Sim/ReachDisplay.cs` | that reach as something drawn, refused against and read off the panel — **one answer for all three**, or a refused click inside the outline reads as the tool being broken |
| `Sim/ReleaseLoop.cs` | which stop a bus is on and when it hops — **the decision half**, refusing a walk rather than flying one at half its price |
| `Sim/ReleaseWalker.cs` | the cursor that walk is flown through — **every statement is behind `Walking`, false for a set of one**, so a single-target flight executes none of it |
| `Sim/ShaderTunables.cs` | the cloud's look as numbers that change while the game runs — **GLSL specialization constants**, so a change is a pipeline rebuild rather than a recompile, and `DebugView` swaps the picture for one term of it |
| `Sim/BridgeCommand.cs` | one command dropped into the bridge's folder, read — **text in**, so every refusal is testable here, and a `Config` field set by name as the panel would |
| `Sim/ShotRequest.cs` | where a scripted shot is aimed and the bar it is judged against — **text in**, so the harness's one line is testable headlessly |
| `Sim/ShotGroup.cs` | where a salvo landed and whether that is a pass — **scored on the worst warhead**, one that never arrived counting; `ShotBoard` beside it is one group per target |
| `Sim/ShotArms.cs` | which variant each rocket in a world flies — **the comparison moved inside the run**, because the same baseline read 14.49 km and 5.43 km on identical code three hours apart |
| `Sim/AimSpread.cs` | where each rocket of a group aims, so the first warhead down does not kill the rest — **a warhead's blast reaches other warheads**, and eight groups on one point measured three of eight |
| `Sim/PlatformHandover.cs` | which craft a part went to, when a decoupler took it off the one carrying it — **one decision, every roster that follows a part** |
| `Sim/StageDisposal.cs` | whether a spent stage may be taken out of the world — **frame time is the only thing that buys simulation rate**, and a rocket sheds four vehicles to keep two |
| `Sim/IcbmConfig.cs` | one installation's ballistic settings — armed, loft, arrival angle, ascent, staging, trim |
| `Sim/BallisticSettings.cs` | a ballistic computer's setup flattened for the save — every `IcbmConfig` field off its default **but `Armed`**, and the target set; **text in, text out** |
| `Sim/FinMixer.cs` | one steering command resolved into four blade deflections — **drawn only** |
| `Sim/FinTest.cs` | the built-in-test sweep a tail kit runs on the rack — **drawn only** |
| `Sim/StoreRetarget.cs` | which falling store a new designation reaches — **the one released last, until the weapon releases again, and only inside its fins' reach**: the reach is what tells a correction from aiming the next store |
| `Sim/FireGate.cs` | whether the launcher is pointing where it is about to shoot |
| `Sim/FireLadder.cs` | **why a system is not shooting** — the gates in order, and the first one that says no |
| `Sim/OpticHold.cs` | **why a director is not looking at what it has bracketed** — the same question for the sight, in the precedence the head's own aim applies |
| `Sim/DriveStatus.cs` | which drives the engine is still accepting writes for, latched per channel |
| `Sim/GunChannel.cs` | the cannon's belt, burst position and next-round timing |
| `Sim/GunRecoil.cs` | how far a barrel has run back since the gun fired — **drawn only**, because the round has left before the barrel moves |
| `Sim/BallisticLead.cs` | where an unguided round must be aimed to arrive where the target will be — **its acceleration included**, because a lead on velocity alone misses anything slowing or falling by ½·a·t² — and **flown through the air and the pull of whichever body it is on**, because distance over muzzle speed times a five-inch fuse 24 s early at 15.7 km, 5.5 km short |
| `Sim/AccelerationEstimate.cs` | the engine's accelerometer, **checked against how the contact's velocity actually changed** — the reading includes every impulse over one step and misled a lead by 232 m, while the history lags and, used as the reading, put every first shell 16 m behind |
| `Sim/ContactHold.cs` | whether a designated craft rests on something, for the gun's lead -- **held a second past its last contact**, because KSA's contact flips 2-6 times a second on a craft sitting on a slope, and the lead swapped the acceleration it flies on every flip |
| `Sim/DragShape.cs` | a craft's drag and mass **as KSA's drag box computes them**, which since 2026.11.16.5901 is the engine's fallback rather than what flies a craft in air — a box fixed to the body over its mass, turned with the body's rates, and a mass its engines burn away — so a craft holding its attitude while its path bends changes its drag, one tumbling changes it faster, and one under power still has all of it and pushes harder as it lightens; a lead holding the coefficient put every first shell 25 m behind and above |
| `Sim/LeadError.cs` | where a shell burst relative to its target, along the target's track, up and right — beside what a steady-target lead would have missed by, which is what tells an unled acceleration from a wrong aim |
| `Sim/Aimpoint.cs` | what a round is shooting at — craft, component or coordinate |
| `Sim/ThreatModel.cs` | CPA threat classification, priority, engagement envelope |
| `Sim/RadarSignature.cs` | how large a contact looks, and how far that lets the set see it |
| `Sim/BeamProfile.cs` | a high-energy laser an optical head fires down its own line of sight — power, aperture, burn-through fluence and the seconds of lasing its load of chemicals gives |
| `Sim/Beam.cs` | what that beam does to the part it is held on — **diffraction and jitter set the spot, the air on the path thins it**, and the burn is a share of the part's health per second, calibrated to the ABL's 3–5 s at 100 km |
| `Sim/BeamTrigger.cs` | when a beam weapon lases — **a press holds it on for one dwell, auto-engage for as long as it asks**, and the dwell runs down only while the director is on target, so a press made while it slews waits rather than lasing at nothing |
| `Sim/CloudCover.cs` | where a beam first enters cloud thick enough to stop it — **overcast only**, coverage 0.8 and over, because KSA's coverage map places cloud fields and not the puffs inside them, so broken cloud would stop a beam in a visible gap; up to a top that rises with the coverage |
| `Sim/CoverageCube.cs` | a cloud layer's coverage map as KSA's renderer samples it — a BC4 cubemap in Vulkan's face order, kept compressed and decoded per texel |
| `Sim/InfraredSearch.cs` | whether a passive infrared set sees a contact — **its heat over range squared, thinned by the air as the density ratio to the fourth**, after water vapour's scale height, against a threshold raised for a contact seen against the ground |
| `Sim/Signature.cs` | how bright a thing looks to a seeker — heat in kW/sr off the thrust being made, and the lock strength a seeker would take on it, **over range squared for heat and to the fourth for radar** |
| `Sim/Decoy.cs` | a flare or a chaff cloud in the air — **not a round**: nothing aims it, fuses on it or tracks it, so it is a point with a signature falling through the air, its stiff drag stepped semi-implicitly |
| `Sim/SeekerLock.cs` | one seeker's choice between its target and the decoys around it — **each decoy judged once**, the first frame it outshines the target, so how often a flare works does not follow the frame rate; a radar seeker sees only chaff in its target's resolution cell, and a Doppler gate keeps even that out unless the target beams |
| `Sim/ChaffNotch.cs` | a tracking set losing a beaming target to chaff beside it — **notching**: both in the Doppler notch and a bigger return in the target's cell, decided by geometry rather than rolled; the track takes seconds to find again, and an optical channel then holds it until the target leaves the notch |
| `Sim/Dispenser.cs` | a dispenser's magazine and the salvo it is working through, on simulated time — **one kind per part**, so the loadout is chosen by what is fitted; every dispenser of a load throws on each press, so a symmetric pair throws either side of the craft, and **auto-dispense** throws flares for a heat seeker coming for the craft, chaff for a radar one, both for anything else |
| `Sim/SearchSetProfile.cs` | a part that is a search set and nothing else — an IRST bolted to a hull |
| `Sim/SearchHead.cs` | where a search set's head looks — **at what its set holds, inside its travel, or sweeping a figure of eight**, each set at its own phase; drawn only, because the set sees its whole cone whichever way the head points |
| `Sim/PictureChoice.cs` | one craft's picture from every set searching aboard it — **each contact once, a threat before a passer-by, and never a friendly handed out** |
| `Sim/TrackState.cs` | one contact, as the threat model sees it |
| `Sim/WatchChoice.cs` | which contact a director keeps looking at — **the one it holds until another is clearly more urgent**, because two near-equal contacts swap places on noise and the head of the list swings between them |
| `Sim/Wreckage.cs` | what this mod's warheads broke off a craft — **wreckage, not targets**, recognised by the name the engine gives a piece, because the engine keeps no other record of where it came from |
| `Sim/Iff.cs` | which side a contact is on, and whether it may be engaged |
| `Sim/TeamRules.cs` | one team's rules of engagement — allies, neutrals, what it fires on — **a team's, not a weapon's**: every system and director on the team takes them each frame, and carries its team's copy into a save |
| `Sim/TeamRoster.cs` | which craft the panel's flag put on which side — **the half a display name cannot carry**, because a name is what a craft is called and not whose it is |
| `Sim/GuardState.cs` | whether a craft's weapons are standing guard — **the switcher's one switch for auto-engage on every weapon aboard** |
| `Sim/LineOfSight.cs` | whether a body is between the viewer and something |
| `Sim/ITerrainHeights.cs` | **the seam a sensor looks over the real skyline through** |
| `Sim/TerrainMask.cs` | whether a ridge hides a contact, and how few samples that can cost |
| `Sim/TerrainRay.cs` | where the pointer meets the ground — **the first place the ray goes under it**, walked from the eye, because the mean sphere's hit refined by the height there lands behind a hill seen side-on |
| `Sim/TerrainMap.cs` | a local east/north frame on a body, and the square of ground drawn around it |
| `Sim/BoosterReach.cs` | how far a stack could still throw a warhead — **a ceiling, not a promise**: it prices the velocity still to be gained and knows nothing about what the climb costs, and a floor built on an unmeasured loss allowance would be worse than a bound that says what it is. Bisected with `BallisticArc.TryCheapest`, the solver the flight uses |
| `Sim/GroundCircle.cs` | a ring of ground a fixed distance from a place — **walked on the sphere, because a blast radius is not a circle on an equirectangular map**: it widens with the cosine of the latitude and wraps whole at a pole |
| `Sim/MapViewport.cs` | a place on an equirectangular planet map, and the visible crop of one — **the crop is KSA's own arithmetic restated, and only the fallback**: the live one is captured as the engine computes it, and these two are checked against each other once a session |
| `Sim/Picking.cs` | what the cursor's ray meets, and what is nearest it on screen |
| `Sim/Reticle.cs` | the gunner's sight as strokes on a screen — geometry only |
| `Sim/ScopeGeometry.cs` | the radar scope's face — where a blip belongs on it, in bearing and range |
| `Sim/LockCue.cs` | how far a lock has matured, as the number a closing bracket is drawn from |
| `Sim/SightPicture.cs` | where the sight's horizontal lies, and which way a contact off the glass went |
| `Sim/EdgeCue.cs` | where an indicator for something off the screen sits on its edge — **from the bearing against the camera's right and up, not a projection**, because a projection throws a point behind the camera to the opposite side |
| `Sim/SpotSearch.cs` | which laser spot a director's tracker locks onto — **its own code, somebody else's spot**, in range and where the head can look, the nearest; last step's spots carried over the step by the velocity of what each lies on |
| `Sim/LaserCode.cs` | a laser's pulse code and the spot it lands — **a position and the velocity of what it lies on**, so a weapon replaying it keeps it on the ground or the craft it is on |
| `Sim/SensorMode.cs` | which channel a director's camera window shows — TV, white-hot, black-hot or colour, in the order a pod's mode button steps |
| `Sim/SightZoom.cs` | the head's magnification, as the field of view it asks a camera for |
| `Sim/CursorAim.cs` | cursor to viewport coordinates, and the bearing from a mount to what it points at |
| `Sim/WeaponFit.cs` | **what a weapons system is fitted with** — the panel asks this rather than testing profile fields |
| `Sim/WeaponSelection.cs` | stepping round a craft's weapons, wrapping — the half of the selector a test can reach |
| `Sim/SettingsOrdinal.cs` | the number a weapon's settings are filed under -- **moved past a switched-off family's launchers**, so the weapons after one keep their own |
| `Sim/StepGate.cs` | hands a simulation step out once and only once |
| `Sim/FrameLatch.cs` | hands a **frame's** work out once, to whichever hook reaches it first — **the UI pass is skipped while the UI is hidden and the frame postfix is not** |
| `Sim/SmoothedStep.cs` | the step evened out, for the one consumer that wants a smooth clock |
| `Sim/SimClock.cs` | classifies a step: usable, paused, or too long to integrate |
| `Sim/WarpPolicy.cs` | holds timewarp down while rounds fly, and gives it back after |
| `Sim/WorldSpeed.cs` | what one world clock runs at when several flights each want something — **the slowest wins**, because a speed is a ceiling on what can be simulated faithfully |
| `Sim/OverrunLog.cs` | how much simulated time the clamp threw away, and whether that cost anything — **an empty sky loses nothing**, and a scene load is always one |
| `Sim/FrameBudget.cs` | **what this mod costs the frame, split by what costs it** — the other side of `SolverLoad`'s subtraction, because three suspects eliminated one at a time is not the same as measuring the whole |
| `Sim/SolverLoad.cs` | whether the engine is keeping up, and what more work in the world costs — **the instrument both throughput levers are priced with**, because neither several rockets nor several game instances has ever been measured |
| `Sim/ChaseView.cs` | where to put a camera riding behind a round |
| `Sim/ChaseOrbit.cs` | the player looking around a chased round with KSA's own orbit drag and wheel — **eased back behind the round when let go** |
| `Sim/ChaseInterest.cs` | whether a chased round is still going anywhere — **a round with nothing to arrive at can only run out of flight**, and riding it there is watching a despawn |
| `Sim/ViewClaim.cs` | who may hold the player's main view, and what that means for the loser |
| `Sim/OrbitAim.cs` | the orbit-camera angles that would point the view at something |
| `Sim/ReportDraft.cs` | a bug report or idea being written, and whether it is worth sending |
| `Sim/TracerLook.cs` | how a tracer is drawn — **the streak is exposure, not the round**: speed over 40 ms of the player's time, so it shortens in slow motion and is the burning base alone when paused; lit out of the muzzle, burnt out on `MunitionProfile.TracerBurnSeconds`, and thinned and dimmed rather than widened with distance. Only one round in `TracerEvery` glows, as a belt is loaded; **the rest a faint grey streak of their own calibre**, a pixel wide at the least and as bright as `Config.BallRoundBrightness` **in daylight, fading with the sun to nothing at night** — sunlight off the round, never light of its own — so the stream reads and the tracers stay the only bright thing |
| `Sim/Distance.cs` | a distance in the unit that shows it — **a fixed unit goes blind as the shot improves**, and `bias 0.0 km` was the whole of a ten-metre miss printed as nothing |
| `Sim/Charge.cs` | an explosive charge in the unit that shows it — kg, t, kt or Mt — since the charge slider runs from a shell to 50 Mt |
| `Sim/GoldenSection.cs`, `Sim/Ramp.cs` | the minimum of a cost over an interval, and the clamped smoothstep — one copy each, where several files carried their own |
| `Sim/Vec.cs`, `Sim/DrawAnchor.cs` | vector helpers, the two-instant draw anchor |
| **`src/KSArmory/Ksa/`** | **everything that binds to the game** |
| `Ksa/KSArmoryMod.cs` | StarMap entry point and frame hooks |
| `Ksa/KsaWorld.cs` | most KSA contact is funnelled through this class — keep it that way. **One class in partial files by concern**, so every call site reads `KsaWorld.X` whichever file holds it; this one is the scene, the clock, warp and the save |
| `Ksa/KsaWorld.Vehicles.cs` | a craft's state, the physics bubble as diagnostics, the census every scan shares, and whether a craft is a platform |
| `Ksa/KsaWorld.Bodies.cs` | gravity, the air and the ocean, the ground's own motion, sea level, the sun, and what a burst went off in |
| `Ksa/KsaWorld.Terrain.cs` | occlusion and the skyline, anchoring a point to the ground, rays onto it, and snapping to it |
| `Ksa/KsaWorld.Parts.cs` | parts: boxes, matrices, tanks, the hull ray cast, blast faces and dents, and the part-failure queue |
| `Ksa/KsaWorld.Controls.cs` | engines, jets and the flight computer's frame — the throttle and jets put back past the keyboard clear, and KSA's popups |
| `Ksa/KsaWorld.Draw.cs` | the draw anchor and gizmos — **`TryAnchoredEgo` answers only after `BeginDraw` this frame** |
| `Ksa/KsaWorld.Projection.cs` | a point onto the screen, or clamped to its edge |
| `Ksa/KsaWorld.Cursor.cs` | the cursor's ray, what it meets, and the aim solved once a frame from it |
| `Ksa/KsaWorld.Viewports.cs` | camera windows and the ground-track window — leased, posed, pictured and handed back |
| `Ksa/KsaWorld.MainView.cs` | the player's own view — its pose, field and follow, the level-horizon controller, and handing it back |
| `Ksa/KsaWorld.Renderer.cs` | the renderer's internals reached by reflection — screenshots, weather clouds and their shadow buffers |
| `Ksa/WeaponSystems.cs` | one system per weapon fitted, crewed with the craft and followed across a split |
| `Ksa/WeaponSystem.cs` | fire control, salvo logic, warhead effects, drives |
| `Ksa/WeaponSystemRoles.cs` | **the slices consumers take** — effects, sights and cameras get a role, not the whole system |
| `Ksa/SearchSets.cs` | one set per search part fitted — **keyed on the part itself**, because it carries no settings and no magazine, so a split needs no handover; its side is its craft's; and its head turned behind the glass |
| `Ksa/CraftPicture.cs` | the merged picture per craft — **derived every frame, as `Armaments` is**, and it hands out a handle, never a position: a director under **Track** and a beam on auto-engage take it below anything more specific |
| `Ksa/Armaments.cs` | one coordinator per craft — **derived every frame, never pinned**, because it carries nothing a split could lose |
| `Ksa/Radar.cs` | cone search, CPA threat model, lock |
| `Ksa/LauncherPart.cs` | finds a registered launcher, resolves tubes and subparts |
| `Ksa/StoreCarriage.cs` | the ejector or launch rail a store hangs by, shown when it means something: a store's own (`LauncherProfile.CarriageMarker`, which also picks which of the two a missile brings) only while it is attached, so the part list shows the store alone -- **declared full size and parked on the template at load**, so a tool reading the part files draws it -- and the empty Weapon Mount only while nothing is on its station -- **the mod's one pass in the vehicle editor** as well as in flight |
| `Ksa/LauncherSeparation.cs` | the decoupler on the joint holding a launcher on — **a property of the part, not the craft** |
| `Ksa/OpticParts.cs` | finds a director on a craft, and turns its head |
| `Ksa/OpticalHead.cs` | **one director** — its own sensor, its own aim, no weapon involved |
| `Ksa/OpticalHeads.cs` | one head per director fitted, crewed with the craft and followed across a split, and handed its craft's picture |
| `Ksa/TravelSweeps.cs` | sweeps each gun's travel against its own craft with the engine's part ray casts — **50 rays and 1 ms a frame across every mount**, the craft on the panel first, and only once a craft has kept its shape for 60 frames, so a break-up is swept once. Other mounts are left out, since their barrels move |
| `Ksa/DamagedEngines.cs` | `EngineDamage` written through KSA's own levers in `AttitudeHook`'s window — a combustor's or thermal core's `ChamberPressureMax`, the gimbal's `MaxAngle`, `SetIsActive(false)` again whenever the player relights it — and the engine's cached performance re-derived after, since nothing structural changed. Not saved, as the health it follows is not |
| `Ksa/PartFires.cs` | each fire's burn charged to its part on the simulated clock and the part broken through the engine's failure queue when it runs out, put out when the air or the tank runs out — and drawn as a flare is: a pooled flame placed on the part every frame, **at most twelve**, and a strand of dark booster smoke |
| `Ksa/StoreMass.cs` | a launcher or dispenser part made lighter by what it has fired, through KSA's own per-part mass slot and in `AttitudeHook`'s window — **scaled where the part declares it, never moved to the tube**, because the bus's mass sits on its thruster ring on purpose. Not saved, which matches a magazine |
| `Ksa/Countermeasures.cs` | every dispenser fitted and every decoy in the air — **stepped after the airborne sample and before any round**, so a seeker reads a decoy as it reads a craft, at the end of the step it integrates |
| `Ksa/InstalledPacks.cs` | reads those folders and registers what is in them — **what lets a pack be assets only**; this mod's own folder it reads earlier, through `CoreDefinitions` |
| `Ksa/BuiltInFamilies.cs` | the player's switch for each built-in family, in `builtins.json` beside the loose settings, **read before any save is open**, so a change waits for a restart; and a switched-off family's parts hidden from the editor |
| `Ksa/DeclaredParts.cs` | the part library as that seam — off `PartTemplate`, because the question is what was *declared*, not what is on a craft |
| `Ksa/HullTest.cs` | whether a round's step meets a craft's actual geometry, per triangle |
| `Ksa/GroundTest.cs` | the surface under a round, off the engine's own height field |
| `Ksa/TerrainHeights.cs` | one body's height field, sampled coarsely and many times per scan |
| `Ksa/TerrainMapScan.cs` | that height field as a cached grid — **the cost lives here**, so it is paid on movement rather than per frame |
| `Ksa/BombSightOverlay.cs` | the pipper: the impact ring and the arc down to it |
| `Ksa/StoreReach.cs` | that region flown, drawn, logged and read off the panel — **one answer for all of them**, because a ring that disagrees with the line beside the trigger or the log reads as the tool being broken. Three `BombSight` flights a solve, so solved only while a store that steers its own fall is actually in the air, at half the pipper's rate |
| `Ksa/Icbm/IcbmComputer.cs` | **one craft's ballistic computer** — reads the world, runs the program, flies the rocket |
| `Ksa/Icbm/IcbmComputer.Warp.cs` | that computer's warp-ahead — **handed to KSA's own warp-to-a-time in hops**, and carried only while it is the computer's |
| `Ksa/Icbm/IcbmComputer.Separation.cs` | letting the launcher go, the stages a split or a staging shed, and the clearance the trim waits on |
| `Ksa/Icbm/IcbmComputer.Trim.cs` | driving `BusTrim` on the bus's own jets, the post-boost passes it feeds, and what the trim is said and measured to owe |
| `Ksa/Icbm/IcbmComputer.Turn.cs` | turning the bus onto its RVs' entry airflow once the split is nulled (`IcbmConfig.PointRvsAtEntry`, on), holding the release and the passes while it turns — **however late, because the passes were aimed for the turned kick** |
| `Ksa/Icbm/IcbmComputer.Hop.cs` | driving `HopBurn`: the line the bus holds, the engine Active and lit while it burns, the trim and the release held, one line per hop |
| `Ksa/Icbm/IcbmComputer.Coast.cs` | the coast written down every ten simulated seconds — rails, the bubble, what is near — **measurement, not control** |
| `Ksa/Icbm/IcbmComputer.Release.cs` | letting a warhead go, the line the launcher holds for it, and the once-a-flight release summary |
| `Ksa/Icbm/IcbmComputer.Walk.cs` | the walk between targets as actuated — a stop's hand-over, and a hop the trim cannot fly refused |
| `Ksa/Icbm/IcbmComputer.Trace.cs` | starting and stepping `WarheadTrace`s, the craft alive or not |
| `Ksa/Icbm/IcbmComputer.Kick.cs` | the separation kick each warhead is thrown with, and the release probe it is solved against |
| `Ksa/Icbm/IcbmComputer.Prediction.cs` | the flown impact prediction the aim correction reads |
| `Ksa/Icbm/IcbmComputer.Reach.cs` | the divert footprint flown, pinned at the epoch, and read back as offsets and axes for the panel and the overlay |
| `Ksa/Icbm/IcbmComputers.cs` | one per craft carrying a part that provides `Guidance` — **the Heavy MIRV Bus alone today**, so a Pantsir or a rail gets none — crewed and forgotten with it |
| `Ksa/AttitudeHook.cs` | **one of the thirteen methods this mod patches in the game** — the only window in which an attitude command survives |
| `Ksa/PreRenderHook.cs` | the second — **a step before the render on a frame that draws no UI**, because StarMap has no hook that is both |
| `Ksa/WorldReloadHook.cs` | the third — **that a save was loaded**, which nothing else can tell: the mod never leaves the flight scene across one |
| `Ksa/GroundTrackHook.cs` | **what lets this mod draw on KSA's ground-track map** — a prefix measuring the image's rect, which `DrawContent` keeps in locals, a postfix that draws, and a third patch **taking the crop as the engine works it out**, so a mark and the picture under it cannot disagree. KSA already renders the planet, so owning an exporter would mean driving a Vulkan pass for a picture the game is drawing anyway |
| `Ksa/PlanetMap.cs` | **this mod's own planet picture** — an equirectangular map from KSA's own exporter, built on demand and given back the moment nothing is looking at it, because it is a second planet render per frame |
| `Ksa/PlanetMapHook.cs` | the instant one may be rendered — a postfix on `PlanetRenderer.ProcessExporter`, which the engine calls inside its `UpdatePlanetMaps` region with the command buffer, the body, the viewport and the frame index. **The only place a render pass may begin**; and **the paintball splats are painted there**, after the opaque scene and before KSA's ocean, air, clouds, exhaust and particles, so those cover a splat — main view, and with MSAA into every sample too (`Shaders/KSArmoryRingMsaa.comp`), since the resolve after the translucent passes would overwrite a splat in the resolved image alone; a device that cannot write multisampled images, and every camera window, keeps them in the late pass |
| `Ksa/GroundTrackDesignator.cs` | clicking that map to put the warheads somewhere — **the same decision as `Ksa/SiteDesignator.cs`**, through `TargetEdit.ClickDoes` and `ReachDisplay.Verdict`, so a click on the map and one on the ground behave alike down to the refusals. A release near its own press, because the map is the button KSA pans with |
| `Ksa/GroundTrackMarks.cs` | what goes on that map — the bus's reach, each target's blast rings, the targets and the craft, and on request a calibration cross at nine round coordinates — **placed with KSA's own `ProjectOnImage`** so a mark cannot disagree with the locations beside it. The crosses sit on the ground they name, **not on KSA's grid lines**, which are spread across whatever is visible rather than drawn at round degrees |
| `Ksa/SaveHook.cs` | **which save is open, and that the game just wrote one** — postfixes on `UncompressedSave.Load` and `Make`, because KSA keeps no record of the loaded save and `GameSaves.Selected` is only the row highlighted in its list, which a console load, a scenario and a save-as never set |
| `Ksa/RoundBodyDrawHook.cs` | the fourth — **a launcher's rounds drawn after the engine has culled the launcher**, because a body is one of its parts and KSA draws none of a craft under a pixel across |
| `Ksa/LooseBodyDrawHook.cs` | **a destroyed launcher's rounds drawn anyway** — each round's mesh, taken off its subpart at `GoLoose`, added as an instance of the engine's own part model every viewport, because a model outlives the craft its parts were on — **and every gun shell with a model, launcher alive or not**, so however many are in the air: one instanced draw, no subpart each — **and any round past 10 km of its launcher**, where a subpart's float placement shakes |
| `Ksa/VehicleCommand.cs` | **the only place this mod flies somebody else's rocket** — attitude, throttle, ignition, staging |
| `Ksa/IcbmOverlay.cs` | the arc it is on, the rings it is aimed at and the ground the bus can still divert to — **painted by the ring pass, with no draped fallback** |
| `Ksa/WarheadTrace.cs` | **one warhead against the prediction of it**, re-flown from where it has got to — measurement only, off by default, and the discriminator is whether the two part *smoothly* or in a *step* |
| `Ksa/SiteDesignator.cs` | click the world to name where the warheads go — **a mode, not a button**; with a place named, a click adds a target, refused where the bus cannot divert |
| `Ksa/Ui/Ui.cs` | the panel's shell: the switcher — one row per craft, grouped by team, a name to fly it and weapons, guard, chase and team as drawn icons, and **a row per dead craft whose rounds are still falling**, with a chase button, since no craft row can reach them — the panes, and which system they read |
| `Ksa/Ui/UiSession.cs` | the world clock, the teams, and what the session draws and hears — and, on a developer's install, **Capture for Claude**, which saves what the player is looking at with the state and the log for whoever is diagnosing it |
| `Ksa/Ui/UiSystem.cs` | one row per component: what each part is, sees and is doing |
| `Ksa/Ui/UiOptic.cs` | one director's rows — what it looks at, looks through, and will watch. **Reads no weapons system**, because a craft with a director and no armament has all of them |
| `Ksa/Ui/UiTeams.cs` | the Teams window — every team, who is on it, what it makes of each other team and what it fires on, in one place |
| `Ksa/Ui/UiTuning.cs` | the sensor, guidance and warhead numbers |
| `Ksa/Ui/UiDebug.cs` | test targets, moving craft, hand-fired bursts, the log |
| `Ksa/Ui/UiMap.cs` | the ground under a director as shaded relief, with what it can see marked on it |
| `Ksa/Ui/UiIcbm.cs` | the ballistic computer's pane — what it is aimed at, whether it will get there, and **everything a player need not touch under one closed Engineering fold**, shown only on a developer's install |
| `Ksa/Ui/UiPlanetMap.cs` | the installation's own map window — the planet, the target list beside it, and KSA's own pan-and-zoom viewport **called directly**, so nothing has to work out what the picture is showing |
| `Ksa/Ui/UiScope.cs` | the radar scope: what the *set* holds, craft-centred and polar, on the Radar tab (Emitters for the HARM's seeker) — and what the craft's IRSTs hold, marked `IR` |
| `Ksa/Ui/UiWeapons.cs` | the weapon switcher — which of a craft's weapons the trigger is pointed at, with each one's ammo and arm state |
| `Ksa/Ui/UiReport.cs` | the one window behind **Report bug** and **Feedback** |
| `Ksa/FeedbackClient.cs` | posts a report to the endpoint, off the frame thread |
| `Ksa/Visuals.cs` | gizmo rendering |
| `Ksa/Detonation.cs` | KSA's own explosion where a warhead goes off, anchored to the body rather than the target |
| `Ksa/Fireball.cs` | the light a nuclear fireball casts -- **the light alone**, because the ball is the cloud pass's fire, and a mesh sphere in the raymarched cloud read as a dark ball that drifted off centre when the camera panned |
| `Ksa/PlumeSmoke.cs` | smoke through the renderer KSA draws booster plumes with, one reflected field away — **the motor trail's now; the nuclear cloud left it for a raymarch** |
| `Ksa/MotorSmoke.cs` | the trail a burning round leaves, through that same renderer — one cursor per round |
| `Ksa/BlastArrivals.cs` | what a burst loads, **held until the front gets there** — the dent, the dust and the push land as the shock passes the part, on the simulated clock like the bang — **followed live**, the burst anchored to the ground and each part struck when the burst's own front (`Sim/BlastFront.cs`) reaches where it is now, so a craft in flight is hit where and when it actually meets the front, and one climbing faster than it is never caught. **Fronts reaching one part inside each other's positive phase load it once, together** — what is left of each, the most head-on pair meeting as at a wall (`BlastDamage.Combine`) — and charged to `Sim/PartHealth.cs` for the reflection their meeting adds, each front having paid its own share at its flash |
| `Ksa/BlastShake.cs` | the front passing the **camera** — the rattle, struck by `BurstSound` as the front arrives so the shake and the bang are one arrival; the model, and `CloudPass` moves the picture |
| `Ksa/BlastPuff.cs` | the dust a front throws off the face it strikes — **a sprite the colour of dirt**, because KSA's billboard is unlit and ignores the particle colour |
| `Ksa/NuclearClouds.cs` | the mushroom clouds standing in the world — **state and the fireball only**: the shape is drawn by `Ksa/CloudPass.cs`, which reads the newest cloud off this and raymarches it |
| `Ksa/BurstEjecta.cs` | that airless burst drawn, through the particle system — **the renderer that draws on a body with no air**, because a mushroom is buoyant and there is no cloud to draw there. One-shot emitters, so nothing has to hold or return them |
| `Ksa/CloudPass.cs` | this mod's own compute shader, dispatched inside KSA's frame — **no renderer was ported to get there**: KSA compiles a `<Shader>` asset out of any mod's folder and `ComputePipelineWrapper` builds the descriptor sets. It also bends the view round **every blast front** strong enough to see (`Shaders/KSArmoryShock.comp`): the scene copied, then written back at an offset where a ray grazes the shock's shell, fifteen times what air does because the real one is a pixel — one copy and bend per front, so where fronts cross their bends add, for about 0.06 ms a front and nothing once the camera is inside it, where no ray can graze the shell. The pass is **the march, and the march is the cost**: 2.9 ms of a 3.3 ms pass for one 20 kt cloud from 12 km, 16 ms with the camera 500 m off and the cloud filling the screen, of which the dust ring is about 2.5, the sun taps 0.9 and the weather 0.4 -- the density's noise is the rest, and already rejected exactly before it runs. A **half-resolution grid** (`MarchScale` 2) is a third of the cost -- 16.6 ms to 4.8 up close, 3.0 to 1.1 from 12 km -- and is **off**: only the marched pixel of each square is written, the rest marked as unsampled for the resolve to keep, which took the flickering pixels at 0.02x from 30% more than full resolution to 0–12%, on one 20 kt cloud and not yet judged enough. A paused capture cannot show that, since the turn stops with the frame counter; measure flicker at a crawl, never paused. Fewer steps close up was tried and is not in -- the cap came out lighter and streaked. **The count follows the chord instead, at the same spacing**: a ray grazing the bound or stopped by the ground inside it took all 48 steps over a sliver, and half a ground burst's bound is under the ground -- so a full-size 0.3 kt cloud from 1.5 km went from 4.2 ms to 1.5, and from 500 m from up to 15.2 to 7.7, with the same picture to a quarter of a level and no more flicker. `cost` splits it by stage |
| `Shaders/KSArmoryCloud.comp` | the march: every nuclear cloud into a layer this frame, jittered by the frame number, lit through KSA's own atmosphere header (`Ksa/CoreShaderInclude.cs`) |
| `Shaders/KSArmoryCloudResolve.comp` | that layer blended with last frame's history where it now lies on screen, and composited into the scene |
| `Shaders/KSArmoryShock.comp` | the blast front's bend in the light — two dispatches, a copy of the scene and then each pixel written back from it at an offset where the ray grazes the front's shell |
| `Shaders/KSArmoryFireLight.comp` | a fireball's light on the craft and structures **KSA's light pre-pass dropped it from** -- that pass cuts a light beyond about 3 km before its intensity is counted, so a burst lit the ground round the pad and left the pad black. Read off what the pre-pass wrote, never re-voted; `docs/BLOCKED-ON-KSA.md` |
| `Ksa/GroundRings.cs` | the targeting rings and ellipses to paint on the ground this frame, **anchored to the body they lie on** and put back into the ecliptic only when the pass records, as the scorch marks are. The only way a ring reaches the screen: the pass paints them with the cloud off too |
| `Shaders/KSArmoryRing.comp`, `Shaders/Content/KSArmoryRing.glsl` | one targeting ring painted on whatever the depth buffer holds — **an exact circle or ellipse at any size, lying on the terrain under it**, with a line a fixed number of pixels wide — an ellipse's long axis and the vertical packed two floats each, because the push constant is already at its 128 bytes; one dispatch per ring over its own patch of screen ; or, with a negative alpha, **a paintball splat** -- a ragged blob, splash rays and droplets, lit by the ground under it; a splat paints only the ground -- terrain and clutter by KSA's pre-pass normal, anything else facing up within 20 m of its plane -- so a craft overhead is not painted |
| `Shaders/KSArmoryRingMsaa.comp` | that shader with `RING_MSAA` defined: **the splats alone, into every sample of KSA's multisampled scene**, before the resolve after the translucent passes would overwrite them -- the body is the shared include, under `Content/` because KSA's hot reload keys an include on the text after it |
| `Ksa/Leaks.cs` | holed tanks draining ten times a second, **written to the tank from `AttitudeHook`'s window** the way KSA's own `DepleteConsumables` does it, mass recomputed after, since the engine does not; a stream from every leaking hole, the sixteen biggest drawn, started behind the skin as KSA's dents have left it — **not particles**, because KSA draws none a motion can stretch; a gas tank vents with nothing drawn, since a gas is not seen |
| `Shaders/KSArmoryLeak.comp` | every leak's stream in one dispatch, as the tracers are — a rope of liquid darkening and tinting what is behind it, a highlight down its middle, and drops past its breakup |
| `Ksa/BulletHoles.cs` | the holes shells have left in hulls — **held in the struck sub-part's own frame**, as the engine's ray cast answers it, and carried to the camera through the matrix the engine draws that sub-part with, so a hole cannot drift off the mesh; the newest 400 kept |
| `Shaders/KSArmoryHole.comp` | one hole painted on whatever the depth buffer holds along the shell's path — black hole, torn petals, ragged soot — **measured across the path, not round the point**, because the same burst dents the skin 0.57 m in and a sphere about the undented point never reaches it; one dispatch over every hole in view, each workgroup gathering the holes that reach its tile, and every hole kept is painted (`CloudPass.MostHolesPainted`); a paint splat only on surfaces facing back up its spray, by the normal rebuilt from the depth beside the pixel, since its 30 m reach along the path runs through the craft, and in the main view never on the ground behind the craft, which KSA's pre-pass normal marks |
| `Ksa/CloudPassHook.cs` | a place the mod patches the game, and the first in the renderer — **an ordinary prefix on a public method**, because `SunbloomRenderer.Render` hands over the command buffer at the one instant the scene colour is storage-writable, the depth is sampled, and bloom and the tonemap are both still to come |
| `Ksa/CloudPassCost.cs` | what that pass costs the GPU, read back out of KSA's own profiler — **the whole frame is sampled beside it**, because 2 ms on a 30 ms frame and 2 ms on an 8 ms one are different answers |
| `Ksa/Radioactivity.cs` | every burst's radiation still in the world, body-fixed as the ground it lies on — and **a burst above the air written into KSA's own belts**, one merged belt per field, so the gauge's orbit average and the map's Magnetospheres overlay show it too; only into a field KSA already gives belts |
| `Ksa/DoseRateHook.cs` | a place the mod patches the game: **a postfix on `Radiation.ComputeDoseRates`**, which KSA's Radiation gauge and its crew dose both read, adding the bursts to its cosmic share, the most penetrating it shields — **so fallout can sicken and kill kittens**, and sickness drops the world to 1x; and **one on `Radiation.ComputeBeltDoseRate`** handing the engine only the natural belts and adding a burst's after, at a storm scale of one, because KSA storm-scales whichever belt has the largest L; a patch that does not apply leaves both on KSA's sources alone |
| `Ksa/BurstSound.cs` | a nuclear burst's two sounds, both recorded at ANNIE: the **equipment picking up the flash** at the instant of the burst, and the bang, arriving when it actually would — **each burst's front followed live to the camera**, so seven seconds behind the flash where a cloud is watched from and a minute for 50 Mt 37 km overhead, **as loud as the pressure there** and not at all under 50 Pa or where there is no air, and **slower and deeper by the cube root of the yield** above ANNIE's 16 kt and never faster, with **Core's own close crack** under it near the burst, since the recording's was lost to 10.7 km of air |
| `Ksa/BurstFlash.cs` | the view going white for a moment when a burst goes off in front of you, and the glare round the ball for **as long as it burns** — **the model, not the drawing**: `CloudPass` writes it into the scene image, because KSA wraps its whole UI pass in `if (DrawUI)` and both F2 and a screenshot clear that |
| `Ksa/CloudWatch.cs` | a camera pinned on a standing cloud — **the measurement needed it**, because what the pass costs is set by how much of the screen it marches and a camera the operator can move makes every number one about where somebody stood. The pose is also the one worth watching from, which is not a coincidence: a mushroom is a side-on silhouette |
| `Ksa/CoreShaderInclude.cs` | the one line that lets this mod's shader reach KSA's own shader library — **an absolute include resolves where no relative one can**, since Core's shader tree and a mod's do not meet; written at load against the player's install, never committed |
| `Ksa/MotorSound.cs` | the rocket motor you can hear, one spatialised channel per burning round |
| `Ksa/EmitterPool.cs` | taking an endless emitter set from the engine's pool, pointing it and **giving it back, `Kill` first** — the one step whose omission leaves nothing in the world able to spawn particles; leaf functions shared by the plume, the muzzle flash and the flares — and **firing a one-shot set**, pointed and tuned before it is registered as KSA's own `FireStage` does, for the dust, the ejecta, the flak and the chaff |
| `Ksa/ScreenOverlay.cs` | the transparent full-screen window every world mark is drawn in — **NoInputs**, or it swallows every click in the game |
| `Ksa/MotorPlume.cs` | the flame at the nozzle, one pooled emitter per burning round |
| `Ksa/SpinPlume.cs` | a released RV's spin-motor jets, one pooled emitter per nozzle while they burn, placed through the body's drawn attitude |
| `Ksa/DecoyEffects.cs` | a flare's white-hot core and smoke trail, and chaff's faint grey puff — **at most twelve flare cores lit at once**, because each holds a pooled emitter |
| `Ksa/MuzzleFlash.cs` | the flash at the cannon's muzzles, one pooled emitter per firing system |
| `Ksa/SoundChannels.cs` | the listener, its pressure and a held channel moved or stopped — each guarded, shared by the motor and the cannon |
| `Ksa/GunSound.cs` | the cannon you can hear: one looping channel per firing system, and a gunshot per round from a gun slow enough to be heard shot by shot |
| `Ksa/ShellTracers.cs` | every gun round each frame, the tracers and the rest, **resolved and projected when the pass records** through the call that places a round's body, so a tracer sits where the round is; collected after the step, so they stay up with the UI hidden |
| `Shaders/KSArmoryTracer.comp` | every gun round on screen in one dispatch — a tracer **added into the scene before bloom** with a hot core and a halo, the rest as dim grey light with none, all depth-tested; each workgroup gathers the segments reaching its tile first. Without the pass, `Visuals.DrawShellStream`'s lines are the fallback |
| `Ksa/LaserBeams.cs` | the high-energy beams firing this frame, held as offsets from the craft firing them and anchored on where it is drawn when the pass records, as the gizmo line is; that line is only the fallback |
| `Ksa/CloudLayers.cs` | a body's cloud layers for `CloudCover` — each coverage map read off disk once, on a worker, **through the game's own libktx**, and turned by the layer's drift exactly as `CloudLayerRenderData` turns it. Thin layers `Bodies.xml` names are left out, and a map not yet in stops nothing |
| `Shaders/KSArmoryBeam.comp` | a beam as the light it scatters toward the eye — **a gaussian tube integrated per pixel along the view ray in closed form**, clipped to its ends and the depth buffer, never narrower than a pixel with its energy kept, brighter looking down it; one dispatch per beam. `docs/BEAM-RENDERING.md` has the derivation |
| `Ksa/GroundSplats.cs` | paintball's marks on the ground -- a splat of the shooter's paint where a round went off on or over it, **hundreds of metres across for a nuclear charge** -- kept, anchored to the body, and handed to `GroundRings` each UI pass for the ring pass to paint; the newest 64 |
| `Ksa/PaintBurst.cs` | a paintball round going off: a burst of the shooter's paint in place of the explosion and its cloud, sized by the charge's cube root |
| `Ksa/FlakPuff.cs` | the black smoke a gun shell leaves where it burst in the air, **sized off the charge by its cube root** from the 5"/54's, and none where there is no air |
| `Ksa/Ui/DirectorWindow.cs` | a director's camera window drawn by the mod — KSA's picture of the leased viewport with zoom, sensor mode and tracking laid over it. **KSA's own window round it is taken off by reflection** (`KsaWorld.TryHideViewportWindow`), and where that fails KSA's stays and this draws nothing |
| `Ksa/LaserDesignators.cs` | every laser spot in the world this frame — **any craft's**, read by code, because a weapon homes on a code and not on its own launcher's pod. A pod's beam stops at the first craft mesh or ground it meets, and MASK holds it off rather than letting it fire into its own craft |
| `Ksa/SensorPass.cs` | that window's sensor look, recorded beside the cloud pass before bloom, for a window alone. **Rebuilt on a size change, not only an image change**: a resize rebuilds the image inside the same object, and a set still naming the old one lost the device |
| `Shaders/KSArmorySensor.comp` | TV and thermal off the scene's brightness, the sky read cold — **a look, not a temperature**, since KSA has none — mapped back round the frame's mean brightness, measured first into a 1x1 image so no pixel reads one already grey |
| `Ksa/Sight.cs` | paints the gunner's sight over the camera the optical head drives |
| `Ksa/SightSurface.cs` | **which window that sight paints on** — and the draw list that reaches it, because a camera window's picture is a window and the main view's is not |
| `Ksa/SightCamera.cs` | borrows the main view to look through the optical head, and gives it back |
| `Ksa/Markers.cs` | on-screen brackets over every weapons system, labelled on hover or when pinned |
| `Ksa/PartHealthBars.cs` | a health bar over each part near the camera, and over any damaged one — on `Config.DrawPartHealth`, the switch under the damage slider in Settings |
| `Ksa/LockCueOverlay.cs` | brackets on **what the selected weapon is engaging**, closing as the lock matures |
| `Ksa/RoundFollowable.cs` | a round, presented to the engine as something a camera can follow |
| `Ksa/ChaseHud.cs` | brackets around what a chased round is flying at |
| `Ksa/ChaseCamera.cs` | rides the main view behind a round, and gives it back |
| `Ksa/LevelHorizonController.cs` | KSA's fixed camera controller, with an up vector it does not otherwise offer |
| `Ksa/WatchCamera.cs` | nudges the main view round onto one system, then lets go |
| `Ksa/Contact.cs` | **what a sensor can hold** — a craft, or anything else that can be seen |
| `Ksa/RoundContact.cs` | somebody else's round in the air, as a thing a radar can see and a gun can shoot at |
| `Ksa/Track.cs` | one contact, with the kinematics the threat model reasons about |
| `Ksa/TestTarget.cs` | spawns drones to shoot at, from the panel |
| `Ksa/Bridge.cs` | **commands from outside the game**, read from a folder beside the log and answered in another — pause, step, burst, camera, capture with a manifest, reload the shaders, and for a scripted engagement a craft's weapons settings, its countermeasures, flying it by pitch and heading, a craft set down anywhere, a stock or library craft parked, a craft flown or taken out of the world, every landed craft a terrain change buried set back down on it, the game saved, a timed burn on a craft's engine or jets read back as an exhaust velocity, KSA's own attitude hold pressed and its tracker read back, a craft's infrared search sets read back, and the view held on one round, crewed or loose — and **KSA's own ground-track map opened on a craft and pointed somewhere**, so what this mod draws on it can be flown without anybody pressing F3 — so an agent can drive a game that stays running. **Files, not a socket**, because the mod reaches the network only when a player clicks Send |
| `Ksa/ScenarioRunner.cs` | flies a scripted scenario with nobody watching, and says what happened |
| `Ksa/BallisticScenario.cs` | the ballistic one of those — designate, arm, stage, and report what the warheads did |
| `Ksa/DropScenario.cs` | the store one — fly a craft up, let a store go, and say where it landed against the sight and against a flight off the state it actually left with |
| `Ksa/GunneryScenario.cs` | the gun one — drones crossing past a mount one at a time, and **every shell scored where it burst**, because the first detonation says a gun can hit and nothing about how well. `ground` scores shells against a designated place instead, short or long |
| `Ksa/CraftMover.cs` | picks a craft up and sets it down elsewhere, from the panel -- **a copy of it drawn where it would land**, posed by the teleport's own function so the two cannot disagree |
| `Ksa/BurstTool.cs` | click the world to set off a warhead there, from the panel |
| `Ksa/Designator.cs` | click the world to shoot at that spot, with no target and no lock |
| `Ksa/TargetLock.cs` | shift-click anything to lock an installation onto it |
| `Ksa/Diagnostics.cs` | the periodic world dump — what the system can see and why |
| `Ksa/JitWarmup.cs` | the kill path compiled on a thread of its own at load, so the first kill of a session is not also the first time its code runs -- **the mod's half, and a fifth of the engine's**: what is left of that frame is KSA building its first debris vehicles |
| `Ksa/Build.cs` | what build this is, read off the assembly rather than written down — and **whether it is a developer's install**, which is what shows the developer tools |
| `Ksa/SettingsStore.cs` | per-craft settings across sessions, in JSON inside the save — `systems.json` and `ballistic.json` |
| `Ksa/Log.cs` | the mod's own log file, which is the only debugging channel it has |
| `src/KSArmory/KSArmory*.xml` | the parts, the warhead effects and the sounds — at the mod root, mirroring Core. **No character**: a mod's character ends up on kittens in nearly every save, which then cannot load without it — `tools/repair-saves.py` re-dresses them |
| `src/KSArmory/KSArmory/Bodies.xml` | **what KSA's current bodies are, as data** -- fields, airglow, condensation, thin cloud -- so a custom solar system gains them by adding lines, not code |
| `src/KSArmory/KSArmory/Pantsir.xml` | **this mod's own weapons, as data** — the Pantsir and its roof director; read by the pack reader through `CoreDefinitions`, not by KSA |
| `src/KSArmory/KSArmory/Weapons.xml` | **empty on purpose** — where the Pantsir used to be, so an install unzipped over an older one does not keep the old file and register it twice |
| `src/KSArmory/KSArmory/Rails.xml` | the AIM-9J, AIM-120C and AGM-88 as players fit them, each bringing an ejector and a slide-off launch rail and showing the one its `CarriageMarker` names, and the shipped LAU-7, LAU-128 and LAU-118 rails with their pylons built in (hidden, kept for saves); their rounds and their seekers, the same way |
| `src/KSArmory/KSArmory/Rockets.xml` | the LAU-10 and the six-round pod, their Zuni and their sight |
| `src/KSArmory/KSArmory/Guns.xml` | the Phalanx, the 5"/54 Mk 42 and the M197, their three rounds — the Phalanx's tungsten dart, the M197's incendiary shell, the 5"/54's — and their three sets |
| `src/KSArmory/KSArmory/Sensors.xml` | the EO director, the LITENING pod and the IRST, with their three sensors |
| `src/KSArmory/KSArmory/Armour.xml` | the armour materials — rolled steel, 7039 aluminium, titanium 6Al-4V, each number's source beside it — and the test plate, Core's 2 m structural plate scaled to 25 mm |
| `src/KSArmory/KSArmory/Countermeasures.xml` | the two AN/ALE-47 dispensers and the flares and chaff they throw |
| `src/KSArmory/KSArmory/DirectedEnergy.xml` | the YAL-1 beam director, its laser and its sensor |
| `src/KSArmory/KSArmory/Strategic.xml` | the B61 on the ejector launcher, the shipped B61 rack (hidden, kept for saves), the Heavy MIRV Bus, their B61 and Mk 21, the bombsight and the bus's designation set |
| `src/KSArmory/Meshes/`, `Textures/` | art. `KSArmory_MeshAtlas.glb` is generated — rebuild with `tools/model/build.sh`; every other atlas is **authored**, and its `.blend` is not in this repository |
| `src/KSArmory/Sounds/` | the cannon, cut from a recording by `tools/cut-cannon.py`; the Mk 42's gunshot, cut from its recording by `tools/mk42-sounds.py`; the M197's loop and tail, by `tools/m197-sounds.py`; a nuclear burst's pickup and bang, by `tools/annie-sounds.py`. A conventional warhead's burst is KSA's own explosion, sound and all |
| `src/KSArmory/mod.toml` | serves as both the content-mod and StarMap manifest |
| `tests/KSArmory.Tests/` | links the KSA-free sources and flies engagements headlessly |
| `KSArmory.sln` | both projects, for editors only — every script builds a csproj directly |
| `infra/dns/` | ksarmory.com's Cloudflare records, as OpenTofu; see `infra/README.md` |
| `infra/services/` | Caddy and the feedback service on the VPS, same |
| `services/feedback/` | the endpoint that takes in-game bug reports and files them as issues |
| `tests/Feedback.Tests/` | its text rules and the log gate, needing neither the game nor the model |
| `tools/apidump/` | reflection dumper for the game assemblies |
| `tools/apisurface/` | reads the KSA API this mod binds to out of its own metadata |
| `docs/KSA-CAMERAS.md` | what the engine does with cameras and viewports, from the decompiled source |
| `docs/KSA-FRAME-ORDER.md` | **the engine's own frame order and what instant each sample belongs to**, from that same source — the evidence under `FRAMES-AND-EPOCHS.md`'s rules |
| `docs/KSA-TERRAIN.md` | **where the engine thinks the ground is** — the height field's resolution, what `accurate` buys, and the one place three surfaces disagree |
| `docs/KSA-API-SURFACE.md` | **generated** — the members an upgrade has to preserve; it states its own totals |
| `docs/PACK-API-SURFACE.md` | **generated** — the elements, attributes and members a weapon pack binds to |
| `docs/AUDIT-2026-08.md` | a review of where the code and tools mislead; the ranked list at the end is the backlog, and items come off it as they land |
| `docs/CODE-HEALTH.md` | **living** — the modularity and comment-hygiene backlog, ticked off as it lands |
| `docs/BLOCKED-ON-KSA.md` | **what the mod cannot build**, with the engine reason and what would unblock it |
| `docs/IRST-PLAN.md` | **a plan, not a record** — an infrared search part that finds targets for the directors, which find nothing by themselves, and one picture per craft that hands them on; the detection law, its calibration targets and the order of work |
| `docs/BUILTINS-AS-DATA.md` | **a plan, not a record** — every built-in weapon moved into one XML file per family, a startup switch to disable built-in families, and a generated reference copy for the example mod; the mechanism, the hazards the reviews found, and the order of work |
| `docs/SHARED-PICTURE.md` | **a plan, not a record** — a team's scopes showing what another installation's set holds: the display-only step first, and why firing on somebody else's track is a much bigger one |
| `docs/MULTIPLAYER.md` | **a plan, not a record** — what KSA's networking skeleton does, why this mod's rounds have nothing to sync against until KSA replicates vehicles and time, and the two pieces worth doing first |
| `docs/CIWS-REALISM.md` | **a plan, not a record** — what the real Phalanx does that the mod does not: detection only as the search beam passes, and closed-loop spotting of its own rounds, with why each waits and **what would make spotting worth building** |
| `docs/LAY-COST.md` | **a plan, not a record** — what a gun lay beyond reach costs a frame, and four measured ways to make it cheaper, none merged |
| `docs/ICBM-MAP.md` | **part record, part plan** — targets set by clicking KSA's own ground-track map: what is built and flown, the three faults the checks caught, and what is left |
| `docs/ICBM-GUIDANCE.md` | **the reference for the ballistic computer and the bus as they work now** — the solve, the ascent, the cutoff, the hold, the coast, the trim, the release, several targets, short shots and the constants that carry the frame; the records are elsewhere and it says where |
| `docs/ACCURACY-PLAN.md` | **a record** — the dated notebook of every accuracy night and finding since 2026-08-30, numbered entries (3aq, 3fh, ...) that code comments cite; the live ranking is `docs/ICBM-OUTSTANDING.md` |
| `docs/MIRV-NEXT.md` | **a record** — the bus's numbered backlog of 2026-08, cited by item; item **7g** is the flown account of the aim freeze — and of the crest the night turned out to be measuring |
| `docs/WIDE-SPREAD.md` | **a plan, not a record** — the sequential plan for walking a bus's warheads onto targets tens of km apart, with a progress list to tick and how to resume it in a fresh session |
| `docs/SHOT-PROTOCOL.md` | **how to spend a night of shots** — how many a difference costs, why the baseline is re-flown all night, and the rule that says when to stop |
| `docs/ARRIVAL-ANGLE.md` | **what a steeper arrival is worth** — precision, impact speed and propellant against the angle a round comes in at, why twelve degrees is the air's answer rather than the guidance's, and the control that asks for another |
| `docs/KINETIC-FLOOR.md` | **how accurate a round could possibly be** — the terms no amount of guidance work removes, and why the arrival angle is the whole lever |
| `docs/SHORT-RANGE.md` | **a record** — 25–1,000 km shots: why every short target once flew one identical cutoff, the plan that answered it, and every flight since; how they fly now is `docs/ICBM-GUIDANCE.md` "Short shots" |
| `docs/MIRV-TARGETS.md` | **a record** — how several targets for one bus were priced, and the walks flown; how they work now is `docs/ICBM-GUIDANCE.md` "Several targets" |
| `docs/ICBM-OUTSTANDING.md` | **a plan, not a record** — everything still open for the ballistic computer and the bus, ranked into tiers with the evidence for each, a **release gate** for merging `dev` into `main`, and the order of work; read it before picking up ICBM/MIRV work |
| `docs/AFC-COMPARISON.md` | **a plan, not a record** — what the AdvancedFlightComputer mod does that the ballistic computer could take, ranked, each with what was read and what to measure first; nothing in it is built |
| `docs/RV-POINTING.md` | **a record** — one turn of the bus after the split so every RV leaves nose-along its entry airflow: step 0, the two timing faults, both paired nights and the one declared cell that missed; how it works now is `docs/ICBM-GUIDANCE.md` |
| `docs/POSTBOOST-BUS.md` | **a plan, not a record** — the Heavy MIRV Bus, which has an axial engine: what spacing costs, the tank and engine that buy it, and the guidance work before the engine flies |
| `docs/METRE-LEVEL.md` | **a record, largely superseded** — the 2026-09 route from a kilometre to a metre as a ladder of arrival angles; its own header lists what is now wrong in it, and §5b's model of sim rate against frame time is the part still current |
| `docs/GUIDANCE-SECTION.md` | **a plan, half built** — the ballistic computer as a part you bolt on. The `Guidance` role, distinct from `FireControl`, is built and the bus provides it; the 3 m interstage ring that would take it over is not, and its mass is a split of the bus's rather than an addition to it |
| `docs/BEAM-RENDERING.md` | how a high-energy beam is drawn — what makes one visible in air, the closed-form tube integral the shader uses, and why not a ribbon or screen-space segments |
| `docs/NUCLEAR-EFFECT.md` | which of KSA's four volumetric renderers a mod can reach, and what a mushroom cloud actually looks like |
| `docs/NUCLEAR-ALTITUDE.md` | **a record** — what a burst does at every height from the surface to space, sourced, against what the mod did, and the twenty steps that closed the gap, each with what flying it showed; what is still open is at the end of its build order |
| `docs/NUCLEAR-RADIATION.md` | **a record, not yet flown** — what a burst irradiates and how KSA's own dose gauge and belts are made to show it: the four sources, each fit and what it was fitted to, and what is left out |
| `docs/NUCLEAR-NEXT.md` | **a backlog, mostly built** — what the burst still needed, ranked: the first second, where every signature that reads as *nuclear* lives, and the last minute, where a real cloud drifts and spreads rather than fading where it stood |
| `docs/VISUAL-TESTING.md` | **half built** — how an agent sees what the mod draws from a terminal: what the screenshot loop cost the night the burst was built, and a ranked route to a live session with an MCP bridge, shader hot reload and same-instant controls |
| `docs/DAMAGE-PLAN.md` | **part record, part plan** — how a hit becomes damage: fragments, the blast held to what a part intercepts, toughness by size, a shell's impact, and the calibration; then what is left to build — damage to engines and tanks, fire and its effects, and the gun rounds split |
| `docs/ARMOUR-PLAN.md` | **a plan, not a record** — armour plate parts a player bolts on at a cost in mass, why parts rather than a per-part setting KSA cannot save, the Lambert–Zukas law a hit is judged by and why not plug shear alone, and the order to build it in, after two reviews |
| `docs/ARMOUR-SETTING.md` | **a plan, not a record** — a plate's material and thickness set in the editor's part window and saved with the craft by KSA's own serializers, after a spike that proves the save; reviewed three times, with the part-per-shape fallback if the spike fails |
| `docs/HYDRODYNAMIC-RAM.md` | **a plan, not a record** — what a fast round does to a tank full of liquid, why the energy-share damage gets a full tank and an empty one both wrong, and the order to build a ram model in |
| `docs/DAMAGE-DECALS.md` | **a plan, not a record** — how a decal is projected onto a hull, a hillside or a rock out of the depth buffer, read off gatOS's implementation and re-verified here, and what a burn mark on a craft would cost |
| `docs/FROM-KSP-MODDING.md` | the concept map for anyone arriving from KSP part modding |
| `docs/MODULARITY.md` | how far the profile/registry split actually generalises, and the test gaps to close before widening it |
| `docs/WEAPON-TAXONOMY.md` | the same question from outside: which real weapon families share this data model, and which need a different one |
| `docs/GUIDANCE-BACKLOG.md` | **a backlog, not a record** — seeker and guidance types to add eventually (SARH, laser, TV, MMW, APN, the seeker notch, multipath), what each reuses, and what is out of scope |
| `docs/BATTERY-SPLIT.md` | what `WeaponSystem` should be split into, what to call it instead, and in what order |
| `docs/WEAPON-PACKS.md` | **the pack author's reference** — the folder, the optional code entry point, and every attribute a definition file may carry |
| `docs/EXTENSIBILITY.md` | **mostly built** — how a weapon pack registers itself without this mod knowing it exists, and what such a pack could never express |
| `docs/PACK-PORTING.md` | **a plan, not a record** — what a weapon from a KSP mod loses on the way into a pack, measured on one ported part, and the steps that close each gap on this side |
| `.claude/skills/upgrade-ksa/` | the whole KSA-update procedure, as a skill |
| `.claude/skills/verify-in-game/` | **flying a change before calling it a fix** — whose game it is, which scenario exercises what, the bridge, catching an effect that lasts a moment, and what counts as evidence |
| `tools/meshinfo.py` | prints mesh bounds from a KSA `.glb` atlas |
| `tools/validate-parts.py` | checks asset Ids, texture paths, and launch geometry vs the mesh |
| `tools/listing.py` | `listing/` as Borea's TOML and SpaceDock's markdown — printed to paste; it publishes nothing |
| `tools/spacedock-changelog.py` | a release's notes cut to what SpaceDock accepts — **10,000 characters**, which one after a long run on `dev` overruns six times over |
| `tools/export-example-copy.py` | **a working copy of every weapon as a pack**, written into the example mod: the definitions, the parts and the art, every Id renamed `KSArmory_` to `ExampleCopy_` -- inside the meshes too, since KSA matches a mesh by name -- so it loads beside KSArmory, with an index of what each family uses. **Read from HEAD** so each header names its commit; run by hand, `--check` to compare |
| `tools/copy-save.py` | a save copied with its craft carrying the example copy's parts instead of KSArmory's -- so with every family switched off, a scenario flown on it flies the pack |
| `tools/wiki/build.py` | **ksarmory.com, assembled from what the repository already maintains** -- a page per weapon and an overview per family from the family files and README's "What ships with it", the homepage's listing card and download count from the KSA content index, rebuilt daily, each carrying the title, sentence and picture a link preview reads, the pack pages from `docs/`, the player pages from README's own sections, every outside link pointed at GitHub; `--pages-only` is the check-all step, `--serve` the local preview |
| `tools/wiki/models.py` | one viewable `.glb` per part for the wiki, **assembled as KSA assembles it** -- mesh by name with the first atlas winning, the XML's transforms through `checkmesh.euler_quaternion`, the `PbrMaterial` as a glTF material, stood on its mounting face -- or, for a store, what it hangs from, and a pod or turret carried under a hull, hung under it; standard library only, `--check` in check-all |
| `tools/wiki/compress.mjs` | those models to WebP textures at 1024 and meshopt geometry, normal maps lossless: 27 MB to 4.5 MB; its npm packages are pinned in `tools/wiki/package-lock.json` |
| `tools/wiki/stills.mjs` | a still of every model, drawn by the same viewer at the same camera in headless Chromium -- the gallery's pictures and each viewer's poster, so a still and its model cannot disagree -- and from them a 1200x630 card per weapon and one for the site, **the picture Discord shows for a link** |
| `tools/wiki/listing-sheets.mjs` | **the store listing's pictures, from the wiki's stills** -- a sheet per family with each weapon named, and the Pantsir's, the YAL-1's and the site's link cards copied, into `listing/images/`; run after a wiki build |
| `wiki/` | the site's config, `mkdocs.yml` as Zensical reads it, `overrides/main.html` (the link-preview tags every page carries), the home page and the brand's stylesheet (`pages/assets/ksarmory.css`: `branding/`'s palette in both schemes, its fonts served from the site); everything else is generated into `wiki/build/` |
| `tools/pack-api.py` | records the API a weapon pack binds to, and fails when it moves -- and that `docs/WEAPON-PACKS.md`'s tables name only attributes the reader takes, with the Values it parses (`--fill` writes that column) — **the mirror of `api-surface.sh`**, because a pack lives in somebody else's repository and never builds here |
| `tools/check-studies.py` | which tests are tagged out of the push loop, as a committed record — **it cannot tell a mis-tag from a real one**, and does not try: it puts the change in the author's own diff, where a human can still ask |
| `tools/repair-saves.py` | realigns saves written before a part lost a subpart, and **renames the Core parts KSA renamed** (`CORE_RENAMES`, 2026.11.10.5801), connectors renumbered where they moved, in saves and designs an earlier build wrote |
| `tools/make-rocket.py` | a rocket design carrying the MIRV bus, from a few stages on a command line -- **part positions from the parts' own connectors**, checked against GeoSat FAT to 1.5 mm, and the game's staging and fuel grouping; written into the player's design library and turned into a scenario save with the bridge's `spawn`, `control`, `remove` and `save` |
| `tools/make-scaling-save.py` | a save carrying N copies of one rocket, for pricing how the vehicle solver scales |
| `tools/model/` | the headless Blender generators, and the checkers over what they export — which need neither Blender nor the game |
| `tools/model/pantsir.py` | the Pantsir, and the entry point that builds the whole atlas |
| `tools/model/sidewinder.py` | the LAU-7 rail and its AIM-9J, into that same atlas |
| `tools/model/optic.py` | the EO director: the sight, as a part anything can carry |
| `tools/model/mk42-textures.py` | composes the Mk 42's KSA maps, mount and shell, from the set its author paints — **rerun it on every version he sends**, because the unwraps are his and nothing else has to move |
| `tools/model/m197-textures.py` | the same for the M197, from his `M197_*` set |
| `tools/model/zuni-textures.py` | the same for the Zuni and both pods, one set on one unwrap, from his `5InchRocketCamo_*` |
| `tools/model/preview-glb.py` | renders any `.glb` from a few angles, so an authored asset can be judged before it is declared |
| `tools/model/preview.sh` | runs that from WSL, which is the only comfortable way: Blender is a Windows binary and wants Windows paths for the script *and* for everywhere it writes |
| `tools/model/checkmesh.py` | finds unpaired node/mesh names, zero-UV-area triangles and coplanar faces in a `.glb`; takes several at once, and `--compare` diffs two atlases by geometry *and* node transform |
| `tools/model/dilate-atlas.py` | fills the empty space around a baked atlas's islands from their nearest neighbour — **what a bake margin cannot do**, because a margin wide enough to survive mipmapping is wide enough to write one body's dilation over another's |
| `tools/model/checkswept.py` | sweeps the drives and reports any assembly passing through another |
| `tools/model/checkring.py` | what KSA's flight computer will make of a thruster ring — **which nozzles end up steering**, how coarse that makes the attitude quantum, and `--translation` for which of the six directions the set can actually push |
| `tools/model/smokepuff.py` | the soft sprite the billboard smoke is drawn with |
| `tools/screenshot.sh` | captures the Windows screen; readable from here |
| `tools/ksa-mcp/server.py` | **an MCP server over the bridge**, registered in `.mcp.json`, returning captures inline; `cli <tool>` runs one from a shell. It launches a game only if none is running and closes only one it launched |
| `tools/check-shaders.sh` | the mod's compute shaders compiled with Khronos' `glslangValidator` against the install's shader library — **a GLSL error in a second rather than a flight**; skips where there is no install, which is CI |
| `tools/vis/vis.py` | what the bridge's pictures are judged with: crops from the manifest, same-instant diffs, the temporal-noise map, grain, contact sheets and animations |
| `tools/check-bodies.sh` | the burst code names no planet in a string, and every Earth-valued constant is on its list with the reason — **a custom solar system is a line in `Bodies.xml`, never code** |
| `tools/sky-matrix.sh` | flies a list of nuclear bursts (`KT@KM`) at night through the bridge and keeps a capture and the GPU split per age -- **every call under a timeout** and each capture copied out before the next call wipes it; `orbit` and `limb` are its camera presets |
| `tools/scenario.sh` | drives one engagement or one ballistic shot end to end and exits pass/fail; screenshots on cue |
| `tools/shot-batch.sh` | a night of ballistic shots, **arms interleaved and every arm built before the first one flies** — so nothing done to the tree overnight can reach a shot in flight |
| `tools/shot-report.py` | what that night settled — the rank test, the effect with its interval, the arms to stop flying, and **whether the ground under the target was shaping the misses** |
| `tools/sounds.py` | the synthesised fallback cannon, behind `--synth-cannon` |
| `tools/cut-cannon.py` | cuts a gunfire recording into spin-up, loop and tail, on measured envelope boundaries |
| `tools/mk42-sounds.py` | cuts the Mk 42's recording into its mono gunshot |
| `tools/m197-sounds.py` | cuts the M197's recordings into a seamless mono loop and its tail |
| `tools/annie-sounds.py` | cuts the ANNIE film's soundtrack into a nuclear burst's pickup and bang — **without the countdown, the scream or the film's hiss** |
| `tools/audio/` | the recordings the shipped sounds are cut from — the CC0 Phalanx, the Mk 42's, the M197's and the ANNIE film's — and the provenance of each |
| `branding/` | **the brand's sources**: the mark, its small and favicon forms, the light mark and the patch, as SVG; and the PNGs rendered from them -- the README's lockup for each of GitHub's themes, the mod icon and the patch |
| `tools/wiki/branding.mjs` | those PNGs, rendered from `branding/`'s SVGs in the brand fonts by headless Chromium, and refusing to write one set in a fallback font |
| `listing/` | **the store page**, Borea's and SpaceDock's, apart from the developers' `README.md` — abstract, description, icon and screenshots, rendered for each site by `tools/listing.py`. Every image URL is pinned to a commit and Borea's record pins its hash, so replacing one means sending new records |
