# Guidance backlog

**A backlog, not a record.** The guidance and seeker types worth adding eventually, what each one
needs, and what already exists that it can build on. Collected from the KSA Discord's *Guidance
Systems* thread and from the notch and multipath discussion. Nothing here is scheduled.

[`WEAPON-TAXONOMY.md`](WEAPON-TAXONOMY.md) is the same question asked about whole weapon families;
where an item here depends on one of its gaps, the gap is named by its number there.

## What exists

| Real-world type | Here | Where |
| --- | --- | --- |
| Active radar homing (ARH) | `GuidanceMode.Seeker` with `SeekerBand.Radar` — AIM-120C | `Sim/Interceptor.cs`, `Sim/SeekerLock.cs` |
| Infrared homing | `GuidanceMode.Seeker` with `SeekerBand.Infrared` — AIM-9J | same |
| Passive radar homing / anti-radiation | `GuidanceMode.AntiRadiation` — AGM-88 | `Sim/MunitionProfile.cs` |
| Command guidance (target-referenced) | `GuidanceMode.CommandLink` — 57E6 | `MunitionProfile.NeedsUplink` |
| Inertial / coordinate | `GuidanceMode.Inertial` — B61-12 tail kit | `Sim/TailKit.cs` |
| Proportional navigation | one law, N·(ω × Vc) projected square to the flight path | `Interceptor.GuidanceAccel` |

## Seeker and guidance types to add

Roughly in order of how much existing code each one reuses.

1. **Semi-active radar homing (SARH).** A radar seeker that sees only what the launcher's own set is
   illuminating. It is `Seeker` with a condition borrowed from `CommandLink`: the launcher's radar must
   hold the track *and* be transmitting (`SystemConfig.RadarSilent`), or the round stops steering. So
   the launcher cannot turn away, go silent or die until impact, which is the whole tactical shape.
   The illuminator as a scarce channel (Aegis) is taxonomy gap 4 and not needed for the first version.

2. **Semi-active laser (SAL).** A seeker homing on a spot a designator holds on the target. The
   LITENING pod and the EO director already designate (`IOpticalHead.Designation`), so the designator
   exists; what is missing is a round whose aimpoint is re-read live from it and is lost when the
   designator loses line of sight or stops lasing. The laser may come from another craft — which
   touches taxonomy gap 1 — but self-designation works first.

3. **Seeker notch.** A Doppler seeker that loses a beaming target with no chaff involved, but only
   when **looking down**, with the ground behind the target; looking up against the sky there is no
   clutter to filter and beaming buys nothing. `SeekerLock` already computes both closing speeds for
   the chaff gate. Off at zero, like every discrimination field.

4. **TV / contrast seeker.** An imaging head locked before launch on a target's contrast
   against its background. No decoy fools it (`SeekerBand.None` already means that), but it needs
   light: it should fail at night and against a target in shadow. The sun factor `TracerLook` uses for
   ball rounds is the same question. Maverick, early Hellfire alternatives.

5. **Millimetre-wave radar.** A radar seeker with a small resolution cell, so chaff has to be much
   closer to the target to matter (`ChaffNotch.CellMetres` becomes per-munition), and one that can pick a
   ground vehicle out of clutter. Brimstone, Longbow Hellfire. Most of it is profile numbers once the
   cell size is a profile field.

6. **Augmented PN (APN).** PN plus a term for the target's own acceleration, so a manoeuvring target
   costs less miss. `Sim/AccelerationEstimate.cs` already produces a checked target acceleration for
   the gun's lead. Zero-effort-miss PN is the same idea stated differently. Worth doing only with a
   headless engagement that PN misses and APN hits, in the style of `GuidanceDiscriminationTests`.

7. **Mid-course plus terminal guidance.** Inertial or datalink updates first, the seeker switching on
   near the target — AMRAAM's real profile, lock-on-after-launch. Taxonomy gap 2: `GuidanceMode` as
   a sequence rather than a constant.

8. **Beam riding and SACLOS.** The launcher owns a line and the round flies along it: Kornet,
   Starstreak, TOW. Taxonomy cluster 3; a launcher-referenced law, not a bearing to the target.

9. **Multipath.** A radar seeker's elevation track degrading near a reflecting surface, worst over
   calm sea. Deferred until there is a low-level threat worth modelling (a sea-skimmer or a cruise
   missile). If it arrives before then, noise in the seeker's elevation estimate growing as height
   above the surface falls is enough; a two-path model is not needed.

## Out of scope for now

- **Procedural missiles**, built from parts with a guidance type assigned to a command part. Rounds are
  simulated by the mod rather than as KSA vehicles, for reasons in `CLAUDE.md` (*Design decisions*),
  so a missile assembled from parts would need a different model. Data-defined missiles through a
  weapon pack ([`WEAPON-PACKS.md`](WEAPON-PACKS.md)) are the supported way to make your own today.
- **Guidance for ordinary craft** — landing on a site or flying to meet another craft. That is an
  autopilot rather than a weapon; the ballistic computer's `VehicleCommand` is the only precedent.
- **Air-launched guidance from aircraft in atmospheric flight** waits on KSA's aerodynamics, which
  RocketWerkz are working on.
