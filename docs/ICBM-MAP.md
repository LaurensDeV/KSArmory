# A planet map to set ICBM targets on

**Part record, part plan.** Phases 0 to 2 are built and on `dev`'s line; phase 3 is not. KSA
`2026.10.13.5602`.

## What is built

| | |
| --- | --- |
| the surface | three patches on `GroundTrackWindow.DrawContent` and `GenericImGuiViewport.CreateDraggableZoomableViewport` — the rect, the draw, and KSA's own crop taken as it is worked out |
| the marks | the target set with the lead filled and warhead counts, and the divert footprint walked round and projected |
| the click | a release near its own press reads back a place and hands it to `TargetEdit.ClickDoes` and `ReachDisplay.Verdict`, the same rules a world click obeys |
| the controls | on the Ballistic tab, beside the clicking mode they extend; the calibration grid stays developer-only |
| the levers | `ground_track` and `designate` on the bridge, so a map can be opened, centred on a place and targeted without a mouse |

**Flown:** the crop agrees with KSA's at zoom 1, 2, 7, 25, 120, 900 and 5000; the lead's screen
position is consistent to a pixel between zoom 1 and 300 against an image rect solved from the
crop; centring puts a place at the crop's midpoint to five decimals.

**Not flown:** the click gesture and the cursor's line, which want a mouse.

## What it cost to learn

- **KSA already had the map.** `PlanetMapExporter` renders it equirectangular at up to
  2048x1024 and `GroundTrackWindow.ProjectOnImage` is public and static. Building one would have
  meant driving a Vulkan pass for a picture the game already draws.
- **KSA's grid is adaptive** — 25 meridians and 13 parallels spread across whatever is visible,
  not round degrees. So a mark on a round coordinate coincides with it only unzoomed, and a
  calibration designed against the grid can only pass where the arithmetic is trivial.
- **Three checks caught three real faults**, each of which looked right on screen: the crop
  derived rather than captured; the centring written before the check that reads it, so the mod
  reported a disagreement it had caused; and the dateline rule measured against the visible crop
  instead of the world, which cuts an outline into nothing at the zoom somebody inspects it at.
  The last was found by extracting the rule and asking what would make the test fail.
- **An unclipped overlay escapes its window.** Marks cull themselves against the crop; the line
  between two points that are both off it does not.

## What is left

Ranked by what each is worth, not by size. KSA already draws the ground track and the predicted
impact, so neither is on this list.

1. **A hover readout** naming the place under the pointer and the terrain height there. KSA's own
   window computes both for its cursor line, so this is a matter of saying it in the mod's terms —
   and the height is what decides whether an aim point is on a slope (`Sim/GroundSlope.cs`).
2. **This mod's flown impact prediction beside KSA's.** The red cross is a patched conic;
   `Sim/ImpactPredictor.cs` and `Ksa/Icbm/IcbmComputer.Prediction.cs` fly the warhead through its
   own drag, and that is what the aim correction reads. Two marks make the conic-against-flown gap
   watchable live — the discriminator `Ksa/WarheadTrace.cs` exists for, answerable today only by
   trawling the log.
3. **An impact clock per target**, once a release itinerary has more than one stop.
4. **The height map as an alternative base.** `PlanetMapExporter` already renders it and the
   window already offers it; elevation is closer to what a shot depends on than colour is.

## Acceptance

For a targeting map this is not a judgement call — it is a flown shot:

> **A click on the map and a latitude and longitude typed into the pane must produce the same
> impact.**

Click a target, read back what it produced, type the same figures into a fresh computer, and fly
both with `./tools/scenario.sh mirv`, which already scores a group against its aim
(`./tools/scenario.sh 'mirv:24S,62W;24.1S,62W'` scores several sites separately). It passes when
the two agree inside the scenario's own bar and both agree with what the map displayed.

That catches the three faults this feature can actually have — a projection subtly wrong, a
round-trip that loses precision, and a click that names the wrong body — and it reuses a harness
that exists.

**Precision.** The read-back is `double` throughout, because `AimSite` is: a `float` latitude
costs 0.21 m and a `float` longitude 1.70 m (`docs/KINETIC-FLOOR.md` §7). What a click cannot beat
is its own pixel, and the exporter re-renders for the current crop rather than magnifying — so
zooming adds real detail, and a full-planet pixel of tens of kilometres shrinks in proportion. The
map places a target; the pane is still where an exact figure is typed.

**The right body.** `AimSite` carries a body name and the map is of one body. Every save is on
Earth today, so a set that silently mixed them would not show up in testing.

## Risks that remain

| Risk | Consequence | Mitigation |
| --- | --- | --- |
| A KSA update moves `DrawContent` or the viewport | the marks stop appearing | both are public, so `PinTheSignature` makes it a build error |
| A KSA update moves the crop arithmetic | marks placed with a stale crop sit somewhere plausible | the live check warns once and the marks use KSA's own answer |
| A KSA update moves a private field read by reflection | the bridge cannot drive the map | refuses with a reason; the feature itself does not depend on it |
| Clicking at full-planet zoom | a click reads as precise and is not | show its resolution at the current zoom; the pane stays the exact route |
| Drawing per-target rings at true scale | a lethal radius is sub-pixel at planet zoom | a minimum pixel size, as the world rings already do |
| Target set mixing bodies | a warhead aimed at a place not on this planet | assert on `AimSite.BodyName` |
