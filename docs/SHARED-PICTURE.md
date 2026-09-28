# A shared picture

**A plan, not a record.** Nothing here is built. It is what a team's scopes would need in order to
show contacts that another installation's set holds, and the order to build it in.

## What exists today

Every weapons system scans with its own set and keeps its own track list (`Radar.Tracks`), rebuilt
every frame from live state. The scope draws that list and nothing else. So a Phalanx beside a
Pantsir sees 6 km while the Pantsir beside it sees 36, and an AMRAAM rail on an aircraft sees what
its own seeker head can see, which on the rail is nothing much.

What a shared picture needs is mostly already there:

- **Who is on which side** is declared once a frame from the switcher's flags, in
  `Sim/TeamRoster.cs`. "Every set on my side" is a question with an answer already.
- **The same craft seen twice is easy to recognise.** `IContact.Handle` is the craft itself, so two
  sets' tracks of one aircraft match by reference. No position gating, no correlation window, and no
  way for two close contacts to merge into one.
- **Every set has already scanned by the time anything draws.** Merging the lists costs a walk over
  tracks that exist anyway, not another scan.

## The decision: shown, or also fired at

The two are different sizes, and the first is useful without the second.

### Step 1: shown only

A scope draws the union of every friendly set's tracks. A contact its own set also holds is drawn as
today. A contact **only another set holds** is drawn differently (hollow or dimmer, with the name of
the set that holds it on hover) and is never handed to fire control.

- **Who counts as friendly** is `IffPolicy.Classify` on the other installation's own team, which
  is the same answer the scope already uses for the contact symbols. Two sites with no team set
  share nothing, because neither is known to be on the other's side.
- **A silent set contributes nothing**, since it is not scanning. A silent set may still *receive*,
  which is the real reason to want this: one site transmits and the rest stay dark.
- **A shared track keeps the holder's classification.** Its `Team` was resolved by the holder's
  radar, and the viewer re-classifies it against its own policy, as it already does for its own
  tracks.
- **Range steps.** The scope's steps come from the set's own reach
  (`ScopeGeometry.RangeSteps`). With sharing on, the widest step should come from the widest reach
  among the sets feeding the picture, or contacts from a long-range set pile up on the rim.
- **Where it lives:** the merge in `Ksa/` beside `WeaponSystems`, with no KSA types needed for its
  rules. The rules (whose tracks count, and how a duplicate resolves) go in a `Sim/` class so they
  are tested headlessly, the same split as `TargetAllocation`.

**Frames and epochs.** A shared track is a position the holder sampled this frame. Craft positions
are the same for every system within a frame. Rounds are not: each system steps its own rounds during
its update. `RoundContact` is already a snapshot taken before any system steps, so sharing its tracks
is safe; what must not happen is a shared round track re-reading the live round.
`docs/FRAMES-AND-EPOCHS.md` has the rule.

### Step 2: fired at (cooperative engagement)

Fire control launching on a track only another set holds. This is much bigger, because every gate
on the way to a launch assumes the set that sees a contact is the one shooting:

- **The lock and the dwell.** `HeldSeconds` gates release, and it belongs to one set's track.
- **Reach and envelope** are measured from the shooter, while the track's range and CPA are
  measured from the holder.
- **Guidance after launch.** A seeker round can be launched on a remote track only if its own head
  will acquire before it needs to steer. A command-link round (`MunitionProfile.NeedsUplink`) needs
  the link to come from whoever still holds the target, which today is always the launcher.
- **Anti-radiation** homes on emission, which a shared track does not change, so an AGM-88 gains
  nothing here.

Do not start step 2 until step 1 has shipped and been flown. Showing the picture tells you whether
anyone wants to shoot off it.

## What to measure

- The merge's cost with several armed craft and a CIWS burst in the air. The round contacts are the
  large term, since a burst is 150 shells. `FrameBudget` should get its own line for it.
- Whether a shared picture makes a silent site useful in play: one Pantsir transmitting, a second
  silent, the second engaging on its own set only once the target is inside its short-range cone.
