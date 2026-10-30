# Hydrodynamic ram

**A plan, not a record.** Nothing here is built. What a fast round does to a tank full of liquid, why the
mod's damage gets it wrong today, and the order to build it in.

## What is wrong now

A tank part pays for a dart or a shell the way any part does: a share of its toughness out of the round's
energy (`Sim/PartToughness.cs`, `KineticCoupling` 0.5). That share does not know whether the tank is full or
empty. So an empty Core 3 m tank breaks off after about four Phalanx darts, which it should not: four holes
of 15 mm in a 4 mm skin leave the structure nearly whole. And a full one breaks for the same reason, an
energy share, when what actually fails a full tank is the liquid.

The wall model itself is sound (`Sim/TankWall.cs`): every gun round in the mod perforates a Core tank's
4 mm of 2014 aluminium by a margin of hundreds, and a holed gas tank bursts or vents by its pressure. What
is missing is everything that happens after a round is inside a tank of liquid.

## The physics

A round entering liquid is slowed hard, because liquid is about a thousand times denser than air. Its
energy goes into the liquid in phases (as the aircraft-survivability literature describes them; to be
checked against Ball, *The Fundamentals of Aircraft Combat Survivability*, ch. 5):

1. **Shock.** At entry, a pressure pulse of roughly `ρ_liquid · c_liquid · v` — the water-hammer pressure;
   for liquid oxygen at 1,100 m/s, on the order of a gigapascal at the entry point — falling off quickly
   with distance. It loads the entry wall locally and is over in microseconds.
2. **Drag.** The round decelerates as `v(x) = v0 · exp(−x / λ)`, with `λ = 2m / (ρ · Cd · A)`: for a 100 g,
   12.75 mm dart in liquid oxygen at a cavitating drag coefficient near 1, about 1.4 m, so it leaves a
   3 m tank with about 1% of its energy. Nearly all of it goes into the liquid.
3. **Cavity.** That energy opens a cavity behind the round, which pushes the liquid out against every wall.
   This is the phase that splits tanks: the walls are loaded as a pressure vessel, all at once.
4. **Exit.** If the round reaches the far wall it exits through a wall already loaded from inside, which
   tears far more easily than the entry did.

The cavity's push is what scales with the tank. A first model: a pressure rise of
`Δp ≈ η · E_deposited / V_liquid` over the liquid, with `η` a coupling of order 0.1–0.3 to be fitted to
tests. That `Δp` against `TankWall.HoldsPascals` of the largest opening — the entry hole, with what the
exit has torn — says whether the tank splits, with the code that already exists.

## What the model needs

- **Where the hit is against the liquid.** `Ksa/Leaks.cs` already knows the level and the felt
  acceleration; a ram needs the path's length below the surface, which is a ray through the tank's shape
  (`Sim/TankLeak.cs` `TankShape`) against the surface plane. Above the surface, no ram, which is also what
  makes an empty tank safe.
- **The energy into the liquid** over that path, from `λ` and the round's mass, speed and calibre — all on
  `MunitionProfile`.
- **The wall's capacity**, `TankWall` as it stands, scaled by `Config.DamageScale` as it already is.
- **An exploding shell** that bursts inside the liquid deposits its charge's energy there too, as a
  bubble rather than a cavity — larger, and worth a separate coupling.

## The order of work

1. `Sim/HydroRam.cs`: the deposit along a path (`λ`, exit speed) and the pressure rise, in closed form and
   tested headlessly. Invariance test as every `Sim/` entry point has: the frame cannot enter, since
   everything is relative to the tank.
2. Calibrate `η` against published tests (to find and check: Varas et al. 2009, *Int. J. Impact Eng.*,
   spheres into water-filled aluminium tubes at 600–900 m/s; Disimile et al. on partially filled tanks).
   Write the fit and its sources into this file.
3. `Ksa/`: when `BulletHoles.Strike` reports a dart through a tank, ask `Leaks` for the depth below the
   surface, run the ram, and split the tank through the part-failure queue if it bursts. The tank's
   energy share stops applying to a tank skin; the part behind it still pays what the round has left.
4. Fly it with the `TANK BURST` saves' approach: one tank full of liquid, one empty, the same Phalanx burst.
   The full one should split after few hits and the empty one hold.

## What it leaves out

- A tank's internal structure — baffles, a common bulkhead — which in a real tank stops much of the
  cavity's push reaching the far wall.
- The ullage gas as a cushion: a nearly full tank rams harder than a three-quarters one, since there is
  less gas to compress. A first model can take the fill as a factor and leave the rest.
- Cryogenic boil-off from the holed tank, which would make a liquid-oxygen leak a spray rather than a stream.
