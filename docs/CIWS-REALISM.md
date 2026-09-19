# A more realistic Phalanx

**A plan, not a record.** Two things the real Mk 15 does that the mod does not, written down so they
are weighed rather than rediscovered. Neither is built.

**What is built** is presentation only. The CIWS scope sweeps at the real search antenna's 90 rpm,
and every scope draws a line to the contact that has been locked, which shows the search set handing
it to the tracker. Neither changes when anything is seen or where a shell goes.

## What the real set does

The dome holds two Ku-band radars ([NavWeaps](https://www.navweaps.com/Weapons/WNUS_Phalanx.php),
[FAS](https://man.fas.org/dod-101/sys/ship/weaps/mk-15.htm)):

- **Search**, at the top: an antenna turning at **90 rpm**, a moving-target-indicator set. It finds a
  contact, the computer judges whether it is a threat, and hands it over.
- **Track**, below it: a monopulse pulse-Doppler antenna boresighted on the gun. The dome and the gun
  elevate together, which the mod already models.

The track radar follows the target **and the rounds leaving the gun**, and the gun drives are
adjusted to move the stream onto the target. That is **closed-loop spotting**, the Phalanx's
signature, and the opposite of a gun laid on a computed lead and left there.

## 1. Detection only as the beam passes

**Today** `Radar.Scan` is a cone search: every contact inside the range and the cone is seen every
frame, and the dwell toward `LockSeconds` starts at once. The sweep on the scope is decoration.

**Realistic** is a contact becoming visible only when the search beam crosses its bearing, once a
revolution. At 90 rpm that is up to **0.67 s** before a new contact is seen at all, on top of the
0.6 s the VPS-2 already takes to lock. Only acquisition would wait: once a contact is held, the track
antenna follows it continuously.

**What it would take:**

- The test is whether the beam swept **across** the contact's bearing during the frame, not whether
  it points there now. At 90 rpm and 60 fps the beam turns 9° a frame, so a point test misses most
  contacts. It is arithmetic on two angles and belongs in `Sim/`, where it can be tested.
- The beam angle is `WeaponSystem.RadarSpinRad`, which is decoration today. Detection reading it
  makes it simulation, so it has to advance on the step as the engine reports it rather than the
  smoothed one the drives use: anything that integrates the world takes the step as it comes.
- It is a field on `SensorProfile`, **off by default**, by the same rule as the other discrimination
  fields: a profile that says nothing about it behaves as before. `Radar.Scan` serves every sensor,
  the Pantsir's included.

**Why it waits:** it makes the weapon worse at its job for realism alone. A Mach 2 sea-skimmer
crosses the gun's 1,486 m effective reach in about 2.2 s, and this spends up to a third of that
before the set knows the missile is there. It changes when fire control shoots, so it is not
shippable until paired `scenario.sh gunnery` and `head-on` runs say what it costs.

## 2. Closed-loop spotting

**Today** the lead (`Sim/BallisticLead.cs`) is flown through a copy of the engine's own drag
(`Sim/DragShape.cs`) and fed the target's mass, fuel and body rates, none of which a radar could
measure. It is true to the game's physics rather than to a real director, which CLAUDE.md records as
a deliberate decision. So there is almost no aiming error for spotting to correct: adding it today
would correct an error that is not there.

**Realistic** is two changes, and they only make sense together:

1. **Aim only on what a radar can measure**: position and velocity, with acceleration from how the
   track has moved. `Sim/AccelerationEstimate.cs` already has that history reading beside the
   engine's accelerometer, and CLAUDE.md records what each costs. The gun gets worse at this step.
2. **Spot the rounds.** Measure where each shell passed the target, in the target's own directions,
   filter it across a burst, and move the aim point by the opposite. The measurement already exists:
   `Sim/LeadError.cs` splits a shell's miss along the target's track, up and right, and
   `GunneryScenario` scores every shell with it. What is missing is feeding it back into the aim.

**Why it waits:** step 1 costs accuracy that step 2 has to win back, and that takes paired gunnery
nights under `docs/SHOT-PROTOCOL.md`. It is only worth it if the CIWS should *behave* like a real
one rather than hit as well as the game allows.

**What would change that:** RocketWerkz are working on aerodynamics, and the lead's drag is a copy
of `PhysicsStates.ComputeDrag`; nothing fails when the engine's changes. When it moves, the copy is
wrong, and the lead is wrong with it. `docs/BLOCKED-ON-KSA.md` has the flown check that confirms the
copy. Spotting corrects a lead against whatever the shells actually do, so it keeps the gun
accurate after the copy goes stale. At that point step 2 alone, on top of today's lead, would be
worth building.
