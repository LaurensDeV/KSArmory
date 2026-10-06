# What is still outstanding for the ballistic computer and the MIRV bus

**A plan, not a record.** Drawn up on 2026-10-06 from four surveys of the docs, the config and the code, then
fact-checked, risk-reviewed and re-ranked by three reviews. Each item names where its evidence lives. Items come off
as they land, and the order is re-ranked when one does.

## Where it stands

Worst warhead per rocket, median over rockets, on `dev` as of `e0195cef`. Short-range cells are one flight each.

| range | n | median | worst |
| --- | --- | --- | --- |
| 25 km | 3 | 1.4 mm | 6.3 mm -- Real Liquid2 lands only after a 12-minute hover; Real Liquid3 never completed |
| 100–418 km | 4 each | 4.0–6.9 mm | 18.6 mm |
| 500 / 700 km | 4 each | 1.8 / 2.8 mm | 3.9 mm |
| 6,179 km (Chaco) | 8 | 5.8 mm | 8.1 mm |
| 12,900 km | 32 | 2.0 mm | 16.2 mm (seats 2–8; seat 1 below) |

**A level, not a floor, and not yet a promise.** SHORT-RANGE.md's own bar is 3–6 flights before naming a level; only
`SOLVER SCALE 8` on Earth has flown long; nothing between 2,000 and 6,000 km has flown on current code; every night
ran at 47–63 fps on one machine; and **a median hides the shots that never land** -- a 12-minute hover and a stack that
never finished are both inside the 25 km row. Today's default-on fixes rest on one to four flights each. Anything that
reaches players or the changelog should say "a level" too.

**Seat 1 is the player's case.** It is the controlled craft, the only one KSA reports stage delta-v for, and the only
one a player flies; the 12,900 km median is over seats 2–8. It now lands like the rest (two worlds, after
`AimWaitsForTheSolids`), but every long-range number should report it on its own.

## The release gate

`dev` is 188 commits past v0.9.4, so players already fly the computer that shipped; a release *fixes* their faults
rather than advertising new ones. The minimum to merge `dev` into `main`:

1. **Tier 0** below.
2. **1.1** fixed, or the hover turned into an honest refusal (SHORT-RANGE "Step 3") so a player is told rather than
   watching a rocket hang over its target.
3. **A half-hour look at 1.2** (held controls dropped under a modal or a focused field) to know whether it needs to
   block.
4. ~~**`SaveRepairs`**~~ shipped in v0.9.5 and is removed on `dev` for the release after.

1.3–1.5 follow the release; they are robustness a player can live without for one version.

## Tier 0 -- cheap, and every later night runs through them

| # | item | evidence and corrected approach | effort |
| --- | --- | --- | --- |
| ~~0.1~~ | **Done, `f8de3bcd`.** A heredoc-fed loop around the tools ends after one flight.** WSL-interop executables read stdin: the wrapper's own `taskkill.exe` ate the list on 10-06, and `scenario.sh` (tasklist/taskkill L255-280, cmd.exe L299, tasklist L367), `deploy.sh:56` and `screenshot.sh` (powershell ×3) would too. **Put `exec </dev/null` at the top of `scenario.sh` and `deploy.sh`**, which never read stdin, rather than patching call sites; run.sh is already backgrounded. `shot-batch.sh` is safe (`mapfile`). Proof: `printf 'a\nb\n' \| while read x; do ./tools/scenario.sh …; echo $x; done` prints both. | tools review | S |
| ~~0.2~~ | **Done, `ff322a12`: the report names the quantum and points at `--endpoint landing`.** `shot-report.py --paired` misreports a millimetre night.** The default `miss` endpoint scores `mean` in km to three places, so a kick arm landing at ~1 mm scores 0.0; `_seat_levels` drops zeros (L1422) and calls seats 2–8 one-armed, and the pair loop (L1289) prints "no shot flew both". `--endpoint landing` levels all eight. Make the default endpoint metres (or say "N flights at this endpoint's quantum"), fix both messages, and check the `release_km` 1e-6 values are real before reading any night on them. | tools/shot-report.py L1246-1469; 10-06 long-kick night | S |
| ~~0.3~~ | **Done, `f8de3bcd`: 0.17 s a pass over 27,000 lines.** `scenario.sh` runs minutes behind on eight-rocket logs.** It re-reads the whole log every 2 s and dedups each SCENARIO line against one ever-growing `SEEN` string -- quadratic, 7 s a pass at 3,000 lines on ext4, worse on /mnt/c -- and its substring match can suppress a verdict line. Replace with `grep -F SCENARIO` and a printed-line count (`tail -n +N`), reset on truncation; drop `SEEN`. | scenario.sh ~L333; 10-06 longrange | S–M |
| ~~0.4~~ | **Done, `58127937`, flown.** A spare computer was crewed on the spent stack at every split (`IcbmComputers.Sync` crews off `Ui._systems`, a survey refreshed every 60 draws). Skipping one frame is not enough, and retiring on "lost guidance" could retire the armed computer flying the shot if a handover lands a frame late. **Re-survey a craft fresh (`LauncherPart.FindAll`, as `WeaponSystems.Sync` does) before crewing it**, and retire only computers that are unarmed, idle, and owe no trace or salvo. A behaviour change: one MIRV flight proves it (no "crewed on" line after the split, same verdict). Separately, `Sync` runs only from `Ui.Draw`, so with F2 down nothing is followed or crewed. | Ksa/IcbmComputers.cs:85-110; Ui.cs:313-337 | S + 1 flight |
| ~~0.5~~ | **Done, `ed6f6dfe`.** Stale lines that mislead the next session**: `AimWithinTrimBudget` "never lost" (lost, 1.11x, 3br); `TrimCeilingFromBudget` "has not been flown" (harmful, 0 of 32, ACCURACY-PLAN ~L11117); `AscentReserveSeconds` "unflown" (flown at 418 km; implicit 15 s under `FlyAnyRange`, and its "0 flies wide open" tooltip is false); `WarheadFootprintMetres` sized on the 10 mm/s cap; ACCURACY-PLAN header item 6 "unreleased"; MIRV-TARGETS' per-target countdown question (built, IcbmOverlay L200-248); SHORT-RANGE's title and "Not in scope" excluding the 25–60 km now flown; ICBM-GUIDANCE "Not done" lines answered since; MIRV-NEXT's top table and EIGHT-ROCKETS' ranked list marked historical. | fact-check | S |
| ~~0.6~~ | **Done, `ed6f6dfe`: 24/33f/33g closed unbuilt.** 3cr's read-back is done (2026-09-29: 37 shots on 5438+, largest pre-split push 0.20 m/s against 1.95–6.0 before); 33f/33g/24 are still "held" at ACCURACY-PLAN ~L11011. Close them, and read the 5482 rails half off a 10-06 night. | ACCURACY-PLAN 3cr | S |
| 0.7 | **Pull 2.4 and 2.6 into the same sitting** (both S, both make later nights readable). 2.6's metres tail is in (`ff322a12`); its per-target paired split and 2.4 are not. | below | S |

## Tier 1 -- what a player can hit

| # | item | evidence | effort |
| --- | --- | --- | --- |
| 1.1 | **Liquid stacks hover at 25 km.** Reproduced in `ShortShotHoverTests` (30 and 45 m²); five attempts logged as not in. The release-gate answer is in: the panel and the log now say when the throttle-down has stalled (flown, 0.2-0.4 mm after the hover). Not the re-pin -- never re-pinning still hovers in the rig. Next: give the rig the flown aim-walk rate at the end (~100 m/s, where the rig's has died away), then fly. | SHORT-RANGE "Open: a liquid stack hovers" | M–L |
| 1.2 | **Held controls are dropped on the controlled craft** whenever the UI holds the keyboard -- KSA's own update modal as well as a focused text field. The scenario closes popups; a player's game does not. A rocket under it keeps full throttle through a staging that needs it lowered. **Looked at 2026-10-06: it blocks the release, and it is fixable.** `PrepareWorker` clears only the held Up/Down flags; the throttle itself is `Vehicle._manualControlInputs.EngineThrottle`, a public float on a private field, so writing it directly from `AttitudeHook`'s window survives the clear. Needs a developer-only way to hold the keyboard mid-flight to prove it, since the update modal only opens when a newer KSA is published. The bus trim's thruster flags have the same exposure on a flown bus. **Throttle done, flown 2026-10-06:** with the keyboard held for a whole Real Liquid2 150 km shot the throttle stuck at 1.000 through cutoff and the warheads landed 21-25 mm out; with the stand-in it followed the command to the engine's 0.116 floor and they landed 0.2-2.4 mm. `IcbmConfig.ThrottleThroughTheKeyboardClear` (on) makes, from `AttitudeHook`'s prefix, the step the held key would have made (`0.7 x` the player's frame, KSA's own servo) whenever `KsaWorld.DiscardsHeldControls` says the clear will run; `KSARMORY_SCENARIO_HOLDKEYS=1` holds the keyboard for a whole scenario through `ImGui.SetNextFrameWantCaptureKeyboard`. The trim's jets are *not* covered: `ClearHeldPlayerInput` zeroes `ThrusterCommandFlags` after the prefix, so writing them there is undone, and they need a write after the clear. | CLAUDE.md VehicleCommand; memory of the 09 nights | M |
| 1.3 | **The arrival-angle floor is a broken control at 15°**: it flies its search but not its coast, and two shots landed 2.28–2.68 km out. Fix it or hide the slider until fixed. **Re-flown 2026-10-06, and not reachable from a pad:** the shipped `ArrivalPreference` 0.5 latches its own floor at half the steepest affordable arrival, ~37° at 3,459 km, so eight rockets (four with the 15° floor) all arrived at 37.8-38.1° and landed 2.8-8.1 mm, the arms indistinguishable. The fault was found deorbiting from orbit, where the natural arrival is a few degrees and the slider binds; nothing in the scenario flies from orbit, so what is open is an orbital-start save, not the guidance. | ICBM-GUIDANCE; ARRIVAL-ANGLE.md | M**From orbit too** (`ORBIT 300`, a bus picked up in a 300 km orbit, 2,416 km downrange): the preference floors it at 38.3°, so a 15° floor does not bind there either; 1.3 is reachable only with `ArrivalPreference` 0. What the orbital start found instead is 1.8. |
| 1.4 | **Reload loses the target, the target set and every computer setting**; also save and reload mid-flight with the computer armed. `SettingsStore` keys per ordinal, the roster per craft. | ICBM-GUIDANCE "Not done"; GUIDANCE-SECTION | M |
| 1.5 | **Staging robustness**: the computer fires whatever the player put next; it will not stage past a launcher that could come off; staging under warp unwatched; whether a cut engine reads dry is one flight. | ICBM-GUIDANCE "Not done" | M |
| 1.6 | **CHECKLIST §12: 63 of 84 lines unticked**, including abort/disarm handing the craft back, the spent stack drifting clear, RCS propellant, the divert outline's frame cost, all of timewarp -- the coast warp a player drives. A morning, alongside 1.5. | CHECKLIST.md 2396-2697 | M |
| 1.7 | **The "Light" stack cannot finish** (4.2 km/s first stage against a 1.2 km/s shot; handed over at 1 km). | SHORT-RANGE ~L460-475 | M |
| 1.8 | **A bus picked up in orbit sometimes steers back into its own spent stack.** From `ORBIT 300` (a bus picked up in a 300 km orbit, 2,416 km downrange, made with the bridge's `orbit` verb on `SOLVER SCALE 1`), five flights: three landed 2-12 mm, two 347 and 613 m. What separates them is the velocity the bus owes its solution at the split. It brakes nose-retrograde with the stack directly behind, so the decoupler throws it forward and the trim, putting it back on the solution, drives it back toward the stack: owed 0.27 and 0.36 m/s, closest approach 21.2 and 17.5 m; 0.57, 13.4 m; 0.73 and 1.04, 10.6 m and contact ~30 s after the split, which knocks the bus 0.5 m/s off and sets it turning. The trim then holds off the stack until the 120 s release timeout, so the post-cutoff aim loop never takes the reading that removes the 425-660 m of bias every flight carries out of the burn. **Not** the bias: skipping burning readings whose projected cutoff jumped >10 m/s (53-57 a flight) left it at 452 m, and was reverted. **Built, off: `IcbmConfig.TrimWaitsOutTheStack`** holds the trim until the separation has carried the bus far enough that paying what it owes cannot close the pair to the keep-out before the post-boost passes run out (`SeparationClearance.ForTheTrimMetres`). Flown from `ORBIT 300`: shipped, 4 of 10 touched, every one owing 0.73-1.38 m/s, and missed 347-708 m; gated, 4 of 4 stayed 21.2 m clear and landed in millimetres, owing 0.47-1.10 m/s (one at 1.10 waited for 115 m). Before it ships: pad shots paired, since a pad bus's shove is 7 m/s and the gate should never engage there. Logs in `~/shots/2026-10-06-orbit`. | flown | M |

## Tier 2 -- measurement, before any more accuracy claims

Four nights, realistically five (the long-range re-fly factor of 1.4–1.6 and the bug every new regime turns up on its
first night), before Tier 3.

| # | item | evidence | effort |
| --- | --- | --- | --- |
| 2.1 | **Completion, then accuracy, on the short-range matrix.** Report per cell what released against what was aboard, TIMEOUTs and never-arrived, before any median. 3–6 flights only at the worst cells (25, 418 and 1,000 km on the Real stacks, ~4.4 h + 2.5 h), one elsewhere. SCALE 8 is a range ladder under 500 km (AimSpread), so it stays single-rocket. **Fly it only after 1.1 lands and the short-range code is frozen**, or it measures a tree about to move. | SHORT-RANGE "Step 4", characterisation | M (2 nights) |
| 2.2 | **2,000–6,000 km and an off-equator site on current code**, seat 1 reported on its own, plus one cell at a deliberately poor frame rate (the frame-dependent terms in FRAME-DEPENDENCE-AUDIT). METRE-LEVEL §3's orbit matrix was never built. Independent of short-range code, so it flies first, while 1.1 is in the rig. | METRE-LEVEL §3 | M (1–2 nights) |
| 2.3 | **"Any rocket": a categorical sweep of player-shaped stacks** built by `tools/make-rocket.py` -- low to high TWR, solids against liquids, no throttle, no or weak RCS, one stage against three, a launcher part in the staging sequence. One flight each; the question is whether it finishes. It is what finds the next Light. | make-rocket.py | M (1 night) |
| 2.4 | **A cutoff rig off the orbit plane** (every fixture flies equator to equator, where the off-plane term is zero), merged with the question 3.2 asks of the residual's frame dependence. | FRAME-DEPENDENCE-AUDIT §1; ACCURACY-PLAN 3fl | S–M |
| 2.5 | **A rig that flies past cutoff** -- trim, aim correction and release are testable only in flight, and every bus-side fault this month was found at night. The daytime work during the Tier 2 nights. | ACCURACY-PLAN 09-20 item 3 | L |
| 2.6 | **Per-target paired report and metre-resolution TARGET lines** before any multi-target night (today every target reads 0.000 km). | ACCURACY-PLAN 3fn | S |
| 2.7 | **Regression tests for today's fixes**: the hot core's floor overshoot (needs thrust lag at the floor in the rig), the controlled craft alone reporting stage delta-v, and the ShortShot* family, backed today only by studies; convert `TheShortShotOnTheGamesStack` and `AnyStackAtAnyRange` to assertions. **Partly done 2026-10-06:** `ShortShotCompletionTests` asserts every stack in `GameStackShortRangeTests` cuts off at 50-2,000 km on the shipped settings and lands within about twice today's ascent floor, the game stack also without its stack delta-v. The hot core's overshoot is still unguarded: a Real SRB4 guessed from its parts cut off clean at 500 km fix or no fix, so the rig needs the flown core's numbers before a test can fail against the old code. | STUDIES.md (20, all ICBM) | M |

## Tier 3 -- accuracy levers, once Tier 2 says where the millimetres are

| # | item | evidence | effort |
| --- | --- | --- | --- |
| 3.1 | **Why one or two rockets a world owe 3.3–5.3 m/s after separation** against a 1.15–1.4 floor (the frame the decoupler's shove lands on). | ACCURACY-PLAN 3fd/3fe | M |
| 3.2 | **Short-range in-air cutoff cost**: the core spends propellant at its floor while turning; a drag-aware arc, solved rather than corrected after. | SHORT-RANGE "Still open" | L |
| 3.3 | `WarheadSubStepMs` (45b, 14 mm of sloped-ground walk): fly only if 2.1 shows sloped-ground walk as the largest term left; otherwise delete. | ACCURACY-PLAN 3ew | M |

## Tier 4 -- features

| # | item | evidence | effort |
| --- | --- | --- | --- |
| 4.1 | **Expected-miss readout**, shown only in cells flown for completion and accuracy, at more than one frame rate -- or it advertises millimetres on rockets and hardware never flown. Depends on 2.1–2.3. | WHAT-THE-PLAYER-SETS §4-6 | L |
| 4.2 | **One delta-v line, need against have, before launch** (KSA reports one stage, so it understates multi-stage stacks). | WHAT-THE-PLAYER-SETS §6.3 | S–M |
| 4.3 | **A six-target walk released all the way down** (never flown; revise its declaration first, after 2.6). | MIRV-TARGETS last section | M |
| 4.4 | **Per-stop pricing in the walk planner**, and the trim reserve after stop 1 spent. | MIRV-TARGETS | S–M |
| 4.5 | **The missile's own reach before target 1** (37–68 ms a ring, spread over frames). | MIRV-TARGETS Phase 2 | M |
| 4.6 | **How large an `ArrivalPreference` is safe**: 0.5 has flown as the default since 3aa (0.48x against 0.8); the safe fraction, and 0.5 at the long geometry, are what is open. | WHAT-THE-PLAYER-SETS §6.2; ACCURACY-PLAN 3aa | M |
| 4.7 | **Height of burst on the Ballistic tab** (exists on `MunitionProfile.BurstHeightMetres`, reachable only from developer panes). | -- | S |
| 4.8 | **MIRV phase 5**: area targets, the list saved with the craft, reordering by hand. | MIRV-TARGETS phases | M–L |
| 4.9 | **The AIRS guidance ring** and its component pane (art, the `Guidance` role moved off the bus, a mass split needing its own night). | GUIDANCE-SECTION | L |
| 4.10 | **The mod spawning the craft on the pad** (frame ordering of `CreateVehicleBuffer`; control past `FillSeats`). | CLAUDE.md | M |
| 4.11 | **Other bodies**: out of scope by design, but a CHECKLIST line for the panel refusing another body, and a launch from Luna in the rig. | CLAUDE.md; memory "fly it on an airless body" | S |

## Tier 5 -- code health, after the release and after Tier 2

Behaviour-neutral, but a split mid-measurement invalidates every arm built before it.

| # | item | evidence and approach | effort |
| --- | --- | --- | --- |
| 5.1 | **Prune `IcbmConfig`** (76 fields, ~16 off). Delete outright -- pre-1.0, a flown verdict or a judgement is enough: `RepointBetweenReleases`, `QuietCoastAfterCorrection` (UI plus one branch each); `AimWithinTrimBudget` (ShotArmsTests:174); `TrimCeilingFromBudget` (the example field in ShotArmsTests ×7, shot-batch's usage line and a shot-report comment -- repoint them; `PostCutoffSequence.fromBudget`, PostCutoffRig and `IsRunaway` go dead); `StoppingInsideTheBandIsDone` (BusTrimPulseTests and the `BusTrim` parameter); `AimThresholdTracksTheMiss` (**keep the `AimCorrection` parameter** -- AimPatienceTests passes true to reach the 3bz counter-reset guard; rewrite CLAUDE.md ~L1783); `HoldDirectionSeconds` only once 2.4 rules the freeze out (CutoffResidualTests, CLAUDE.md ~L1815, FRAME-DEPENDENCE-AUDIT). Delete unless something flown argues otherwise: `SeparateAtCutoff`, `RailsDuringCoast` + `QuietCoast`, `ShrinkMissKickToTheGroup`, `WalkStartsAtCutoff`. Keep `WarheadFootprintMetres`. Make `AscentReserveSeconds` a named 15 s const with a test override rather than folding it away. **Guard**: the golden test sees only the cutoff, so replace each `Config.X` with its literal default first, run `test.sh --all` and diff the study output, then delete the dead branches. Old nights naming a deleted flag fail to resume, loudly, which is right. | the risk review | M |
| 5.2 | **Cut `IcbmConfig`'s docs to verdict plus pointer** (~1,300 lines for 76 fields, duplicating ACCURACY-PLAN and SHORT-RANGE). | CODE-HEALTH comment hygiene | M |
| 5.3 | **Split `IcbmComputer`** (4,502 lines, ~106 methods) into partials -- trim, coast probe, release/separation, prediction -- then `IcbmProgram` (1,879) by phase. A pure-move commit per file; **each new file needs a CLAUDE.md Layout row** (`check-docs.sh` matches basenames); `file:line` citations in docs and ~12 test comments go stale. | the risk review | L |
| 5.4 | **An ICBM section in CODE-HEALTH.md** tracking the god classes, the flags and the lunar body fall (MIRV-NEXT 2g, one link only in `KsaWorld.BodyFallEcl`). | CODE-HEALTH.md | S |

## Blocked on KSA

The per-part loadout saved with the craft; a solver hook to replace the attitude prefix; an aerodynamic moment
(which makes `FlyAnyRange`'s any-angle steering risky the day it arrives); the one-stage delta-v readout.
BLOCKED-ON-KSA.md has each. Re-check after every KSA update.

## Not carried forward

The 20 s clearance knife-edge (2b, answered by 3ey); the bus-divergence family 13/17–20c (fixed upstream; 0.6 closes
the held 24/33f/33g); the long-range trim stall (`StallFallsBackToHolding`); `StallWaitsForThePulseGuard`; the
arrival-angle ladder (parked, 3cx); EIGHT-ROCKETS, METRE-LEVEL and MIRV-NEXT 0c/0d/2/7f as plans; the per-target
countdown (built); today's kick caps, solids hold, backstop and structure scoring.

## Order of work

1. **Tier 0 in one sitting**, with 2.4 and 2.6. 0.4 then wants one MIRV flight.
2. **Nights: 2.2 first** (long range does not touch the short-range code), then **2.3**. **Days: 1.1 in the rig**, then
   a look at **1.2**, then **2.5**.
3. **1.1's flight, then freeze the short-range code, then 2.1** over two nights.
4. **The release gate**: merge `dev` into `main` once Tier 0, 1.1 (or its refusal) and the 1.2 look are in.
5. **After the release**: 1.3–1.7, Tier 5, and 2.7 alongside 5.1.
6. **Tier 3 only once Tier 2 names the largest term left**, and Tier 4 by what players ask for -- 4.1 last, since it is
   the one that makes a promise.
