# What a gun lay beyond reach costs, and how to make it cheaper

**A plan, not a record.** Four headless investigations on 2026-09-15, each prototyped on its own branch. Nothing here
is merged or flown.

## Where it stands

A gun-only mount is laid by flying simulated shells (`BallisticLead.TrySolveFlown`); a place beyond reach is found by
halving along the way to it (`TrySolveFlownOrReach`), a dozen solves of several flights each. From Mars, with a
horizon of minutes, the first search took **215 ms of one frame**, 76 ms on Earth.

- **The cost is simulation steps.** About 100 ns a step with test closures; the game's gravity and air callbacks are
  29–31 ns a call through a mock of the same shape, so about 240 ns a step in game (estimated).
- **The first search in a session also pays JIT**: 186 ms cold against 41 ms warm, on test closures. That and the
  callbacks together are the 215 ms.
- **Most steps prove something is out of reach.** In a re-confirmed Mars search, 38k steps re-prove the target
  itself is out of reach and 32k that 0.1% further is, against 6k flying the answer.
- **How often it runs.** A held place: one search, then a confirm every 0.5 s of simulated time. A mouse sweeping
  ground beyond reach: nearly every frame, and a move towards or away from the mount restarts the halving, because
  the hint is a fraction of the old range.

## The levers, measured

| lever | branch, commit | Mars 312 km, cold | Mars, confirmed | Earth 35.5 km, cold | landing |
| --- | --- | --- | --- | --- | --- |
| **find the longest reach by elevation** (`TrySolveFarthest`) | `worktree-agent-a77436f4811e3880f`, `6549bbb` | 68 → **9** flights, 239 → 38 ms | 11 → 3 flights, 42 → 11 ms | 69 → 14 flights, 61 → 14 ms | reach the same or longer; 0.1 m / 2.9 m from the place named |
| **RK4, a step sized to each flight** | `worktree-agent-a09865b0328d686c2`, `c485fdd` | 416k → **10.5k** steps, 43.8 → 15.1 ms | — | 105k → 5.4k steps, 11.3 → 8.0 ms | unchanged: 0.12 m Mars, 2.91 → 2.82 m Earth |
| **remember the reach as a distance** | `worktree-agent-a64de885b46a1e7af`, `d01761f` | — | a radial move 12 → **3** solves | — | unchanged |
| **read the body once per solve**, closed-form gravity and air | not built | ~2.4x on every lay, trackers included (estimated) | | | the same maths |

The first two multiply: one cuts the flights, the other the steps in each. The same branch as the distance hint
holds `ReachSearch`, a resumable search that can run under a per-frame step budget or on a worker.

Every prototype passes the suite on its branch. Times are headless and warm unless marked cold.

## Found on the way, and open

- ~~**A 20 mm solve against a fast missile stalls today.**~~ **Fixed on `dev`, flown.** The closest approach was
  interpolated across a whole 50 ms step, which at a head-on closing speed of 1,300 m/s leaves 0.1 to 0.15 m of miss
  along the closing line that no turn of the barrel takes out. `Flight.TryClosestApproach` now takes one straight-line
  closest-approach step from the interpolated meeting. Headless, 684 of 684 close geometries solve against 237, and
  none fails seeded from its own answer against 73. Flown on the CIWS save, `gunnery:2,head-on`: 9 hits and both
  drones destroyed, against 4 shells and no hit, because the unfixed lead alternated between solving and not and the
  gun never settled. The RK4 branch is no longer needed for this.
- **Ocean density below the mean radius.** `KsaWorld.MediumDensityRatioAt` reads ocean below sea level, so a place at
  the mean radius samples water in the last steps of a flight. With it modelled, Earth's 35.5 km search stopped at
  0.419 of the way against 0.667. Headless only; places near sea level may be affected in game.
- **A spinning Mars at 312 km** lands 19–28 m from the place named on both the old and the new search, outside the
  11 m lethal radius. Unexplained; the benchmark's air handling or the flight model.
- **A search carried across frames needs a snapshot** of the body's centre, mu, spin and air and a copy of the round,
  because the callbacks read the body live; and a gate that holds the trigger while the lay is provisional, since
  `Turret.OnTarget` accepts 0.05 rad. The 0.5 s reuse should carry its direction in the launcher's frame: the spin
  turns an ecliptic one by about 6 m at 174 km on Mars.

## Recommended order

1. **The distance hint and reading the body once per solve.** Hours, low risk.
2. **The elevation search with RK4 stepping.** The bulk of the win. Strip the benchmark instrumentation first, and fly
   the 20 mm change against a missile.
3. **A budgeted or background search** only if a frame still hitches after both.

Verify in flight with `gunnery:1,ground,,,35000` on Earth, the same from Mars
(`KSARMORY_SCENARIO_SYSTEM=Sol KSARMORY_SCENARIO_SITE=Mars,15,-160`), and a CIWS against an incoming missile, with the
lay's time per frame logged.
