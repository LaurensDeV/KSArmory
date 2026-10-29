# Nuclear radiation

**A record of how the bursts' radiation is modelled, and not yet flown.** KSA 2026.10.19.5638 added
radiation: a dose rate at a craft from belts, the star and cosmic rays, read on its **Radiation**
gauge in mSv/h (bottom right, above Telemetry), and a **Magnetospheres** overlay in the map view's
Map View window. Since 2026.10.25.5707 crew also accumulate a dose through the same rates: sickness at
1 Sv, which stops warp and sets the world to 1x, death at 4 Sv, retirement past a lifetime 0.6 Sv. This mod adds its bursts to
that same reading, so what it changes is what the gauge and the overlay show.

## Where it reaches the game

- **`Ksa/DoseRateHook.cs`** postfixes `Radiation.ComputeDoseRates`, which the gauge and the crew dose
  both read, and adds `Radioactivity.DoseRateAt` to its cosmic share, the most penetrating channel KSA
  shields: a hull passes about 70% of it. Crew credited from an orbit average at warp do not see a ground
  burst, since the average is not a dose rate at a point. The gauge's six digits run to 999.999 mSv/h, so anything
  hotter than about 1 Sv/h pegs it.
- **`Ksa/Radioactivity.cs`** writes a burst above the air into the body's own
  `MagnetosphereReference.Belts`. KSA's gauge, its orbit average and its overlay then read it as
  they read a natural belt. It only does this for a field KSA already gives belts, because KSA gives
  none to a field too weak to trap, and it merges every burst's share into one belt per field
  because the overlay draws at most four. KSA re-averages an orbit only when the orbit changes, so
  each re-write clears the gauge's and every craft's orbit-average cache by reflection. If that fails,
  the averages catch up on the next burn. The belt is all electrons (`ProtonFraction` 0), as Core declares
  Earth's outer belt, so a hull stops most of it. If it lands outside every natural belt, KSA scales it
  by a storm's outer-belt factor in place of the real outer belt.

Each burst is held in the body's fixed frame, as the ground it lies on, so no epoch rule from
`FRAMES-AND-EPOCHS.md` arises. The prompt flash is the exception and is held in `Cce`, because it
lasts seconds and a burst in orbit does not turn with the ground. A world reload clears it all,
including the injected belts, since the body templates outlive a save.

## The four sources

All four are in `Sim/BurstRadiation.cs`. They are unshielded, like KSA's own reading.

**Prompt flash.** This is the initial gamma and neutron dose, delivered over the first minute:
`D = K·W/r² · exp(-column/Λ)`. It is fitted to DS02 free-in-air kerma at Hiroshima (15 kt): 4.2 Gy
at 1.17 km slant, 0.8 at 1.62 and 0.15 at 2.09. That gives K = 5.8 Gy·km²/kt and Λ = 527 kg/m²,
which is 430 m of sea-level air. Λ grows as W^0.075 for hydrodynamic enhancement, which puts 1 Mt's
5 Sv at 2.9 km (Glasstone & Dolan fig. 8.64). The attenuation is the column of air along the path,
so in vacuum the flash is a pure inverse square: 1 Mt still gives 5 Sv at 34 km. It is delivered as
`exp(-t/τ)` with τ = W^0.2 s, clamped to 0.3–10 s. Sv and Gy are taken as equal; the neutron share
is small at these ranges.

**Fallout.** This only comes from a burst whose fireball reaches the ground, scaled by
`MushroomCloud.GroundCoupling`, which already decides the dirt in the column.
- **Calibration:** Glasstone & Dolan's 1 kt of fission products spread over a square mile reads
  2,900 R/h at H+1, converted at 0.0087 Sv/R.
- **Fission share:** whole up to 100 kt, half from 1 Mt, log-interpolated between the two.
- **Where it lands:** 60% comes down locally. Of that, 30% falls round ground zero in the stem's
  disc (σ = three fireball radii) and the rest falls in a plume, with fall times spread
  exponentially about `CloudTop / 3 m/s` and carried downwind at 15 mph. KSA has no wind, so that
  is G&D's reference wind, along `NuclearClouds`' own downwind bearing.
- **The fit:** the settling speed, the plume's width (0.3 cap radii plus 4% of the distance) and the
  exponential were fitted so a 1 Mt surface burst's hotline reads about 1,500, 700, 270 and 70 R/h at
  H+1 at 35, 65, 110 and 180 km. That is within a factor of two of G&D's idealised pattern, as
  remembered rather than re-read.
- **Over time:** fallout reaches a place when the wind brings it there and then decays as
  `t^-1.2` (Way–Wigner).
- **Above the ground:** the rate is attenuated through the air column under the receiver at
  171 kg/m², which is the 0.007/m altitude correction aerial surveys use.
- **On an airless body:** the stem's disc is the whole of it.

**The debris cloud.** Whatever has not landed is still aloft. This is the remaining 40% of a surface
burst's fission products, and all of an air burst's. It is a ball rising with the cap over
`MushroomCloud.RiseSeconds`, then drifting downwind and spreading. Its rate is `Q/(4π(r² + R²/3))`,
which is the inverse square outside the ball and three times the rim's value at the centre. Q is
derived from the fallout calibration: a plane read 1 m over it is `Q/2·E1(1 m/140 m)`, so the cloud
and the ground cannot disagree about how active a kiloton is. The cloud starts after the first
minute, which is the prompt dose's share. A burst in air too thin for a mushroom has no cloud,
because its debris leaves at hundreds of km/s.

**The belt.** A burst in thin air or above it fills a shell at its own L, from KSA's
`Radiation.ComputeL` on KSA's tilted, offset dipole. KSA's belt law then decides where that dose is
felt, including its 200–600 km loss altitude, so a burst at Teak's 77 km fills a shell that hardly
reads. The peak is calibrated so that Starfish (1.4 Mt, about half fission) raises its shell to
4 Sv/h, about 100 times KSA's placeholder inner belt, and falls as L^-4. The shell's width is 0.15 L.
It holds for a year at L ≤ 1.25, and for less further out as `exp(-(L-1.25)/0.15)` to a five-day
floor. The real Starfish electrons persisted for years at L 1.2 and for weeks at L 2.

## What it is not

- **No shielding.** The reading is unshielded, as KSA's is. Inside a craft the dose would be lower.
- **No harm.** Neither KSA nor this mod accumulates dose or harms a kitten or a part with it.
- **No real wind, and no sea.** Fallout on the sea is laid as on land, where most of it would
  actually sink.
- **No neutron activation of the ground** and no rainout.

## What to fly

See `CHECKLIST.md`. The gauge only reads the craft being flown.
