---
name: upgrade-ksa
description: Move this mod onto a new Kitten Space Agency build. Use when KSA has updated, when the ksa-version workflow opens an issue, when check-assemblies.sh reports the install no longer matches the lock, or when the user asks to upgrade/retarget KSA. Refreshes the assemblies and the decompiled corpus, finds the breaking changes, fixes them, and updates every place the build number is written down.
---

# Upgrading to a new KSA build

KSA is pre-release and has **no official code-modding API**. The surface this mod compiles
against genuinely moves between builds — members get renamed, and, worse, members keep their
name and change their meaning. This skill exists because the second kind is invisible to the
compiler and has to be read out of the decompiled sources.

Read `docs/KSA-MODDING-NOTES.md` and CLAUDE.md's *After a KSA update* before starting.

## What you need

- The new KSA build **installed** (`/mnt/c/Program Files/Kitten Space Agency`).
- A checkout of the private **`LaurensDeV/ksa-game-assemblies`** repository. Clone it next to
  this one so `Directory.Build.props` finds it: `git clone git@github.com:LaurensDeV/ksa-game-assemblies.git ../ksa-game-assemblies`
- `ilspycmd` (`dotnet tool install -g ilspycmd`). **Do not upgrade it as part of an upgrade** —
  a different decompiler version rewrites the whole corpus and buries the real diff.

Everything below needs `source tools/env.sh` or the `tools/*.sh` wrappers; bare `dotnet` is 8.0
and cannot build this.

## Order matters

The private repository must be pushed **before** this one, or CI here fails against a lock it
cannot satisfy yet.

## 1. Confirm the update is real

```bash
./tools/check-ksa-version.sh          # what RocketWerkz publish vs what we pin
./tools/check-assemblies.sh --game    # what is installed vs what we pin
```

If both say we are up to date, stop — there is nothing to upgrade, and the issue that sent you
here can be closed.

Write down the new build number. It is needed in three places later.

## 2. Refresh the binaries and the corpus

```bash
./tools/sync-import.sh                                    # local Import/
./tools/sync-assemblies.sh      ../ksa-game-assemblies    # the mirror's DLLs
./tools/decompile-assemblies.sh ../ksa-game-assemblies    # the mirror's sources
```

Then, **in the private repository**, set `current/KSA_BUILD` to the new build number and commit
the DLLs and sources **together** in one commit. Committing them apart makes the next diff
straddle two commits for no reason.

Do not push yet if you want to inspect the diff first — it is local either way.

## 3. Find what actually broke

```bash
./tools/ksa-api-diff.sh ../ksa-game-assemblies
```

This reads `docs/KSA-API-SURFACE.md` — the 396 members this mod genuinely binds to, extracted
from the compiled assembly's metadata — against the new corpus, and answers two questions:

**Missing members.** Mechanical and precise. Each one is a break you must fix. `MOVED` means it
is no longer declared on the type we use it through, which usually means a refactor into a base
class and usually still compiles — check, do not assume.

**Changed files.** The decompiled files defining types we use that this update touched. **Read
these.** This is the whole reason the corpus exists: a method that kept its name, its
parameters and its return type, and changed what it means, compiles perfectly and is wrong in
flight. This repository's own history is full of that class of bug — offsets measured from the
wrong origin, velocities carrying 29.8 km/s of ecliptic motion, a launch direction that ignored
the tube it came out of. A KSA update can reintroduce any of them silently.

Read the actual diff for each hit:

```bash
git -C ../ksa-game-assemblies diff HEAD~1 -- current/src/KSA/KSA/Vehicle.cs
```

Pay particular attention to anything touching:

- **Reference frames** — `Ecl`, `Ego`, `Asmb`, `VehicleAsmb`, `ParentAsmb`. A change in what a
  property is relative to is catastrophic and completely silent.
- **Units** — metres vs kilometres, radians vs degrees, seconds vs ticks.
- **`Part` transform plumbing** — `Asmb2ParentAsmb`, `PositionParentAsmb`, `Scale`, and
  especially `ResetCachedPosMatrixValues`. The mod writes subpart transforms every frame and
  depends on the cache being invalidated exactly as it is now.
- **Anything the mod calls once per frame** — the cost model matters as much as the semantics.

### The named contracts

These are the specific behaviours the mod is built on that **no tool can check**. Each is a fact
about what the engine does rather than what it declares, so `ksa-api-diff.sh` sees nothing, the
build succeeds and the tests pass. Read the cited file when the diff touches it, and confirm the
claim still holds.

| Contract | Where | What breaks if it moves |
| --- | --- | --- |
| `SetFieldOfView` takes **degrees**, `GetFieldOfView` answers in **radians** | `Camera.cs` | A factor of 57.3 between the two directions. Both are wrapped in `KsaWorld` for this reason. |
| `SetFieldOfView` does not clamp, and the projection **throws** outside `(0, π)` | `Camera.cs`, `ReverseDepthBufferUtils.cs` | An exception out of the frame hook. `SightZoom.MinFovDeg` is the guard. |
| `ChangeFieldOfView` clamps to 15–120° | `Camera.cs` | The player's zoom keys silently discard any narrower field. The whole reason the sight rewrites the field every frame. |
| `OnFrameViewports` runs **before** `OnDrawUiViewports` | `Program.cs` | `LevelHorizonController.OnFrame` stops being in phase with the frame, and `IViewPose` goes back to aiming the camera a frame late — which scales with simulation speed. |
| `FixedController` reads no input; `NextCameraMode` has no `Fixed` case | `FixedController.cs`, `Viewport.cs` | The panel's advice on reclaiming the view becomes wrong. |
| `EditorTag` is an open string, and `EditorTagDefinition` registers itself | `EditorTag.cs`, `EditorTagDefinition.cs` | The Weapons category silently disappears and the parts fall back to *All*. |
| Core's flags on `Radial`, `NoFaceSnapping` | `Content/Core/CoreEditorTagsGameData.xml` | Attachment behaviour changes with nothing failing. |
| `GetTerrainHeightFromDirCce`, `MaxTerrainHeightApprox` | `Celestial.cs` | Terrain masking and the bomb's ground test both answer against the wrong surface. |
| `GetPositionEgo` returns the **drawn** position, not the analytic one | `Camera.cs` | The sight's bracket and the head's aim go back to missing the target by metres. |

Add a row whenever a fix depends on the engine *doing* something rather than *declaring* it.

## 4. Diff Core's XML, because the compiler never sees it

`ksa-api-diff.sh` reads compiled metadata. The mod also binds to KSA's **XML serialisation
contract** — `PartGameData`, `EditorTagDef`, `Connector`/`Flags`, `MeshAtlas`, `PbrMaterial`, the
particle and sound definitions, the character attachment. None of that compiles against anything,
so a renamed element or attribute produces no error at all: the part loads with the field at its
default, or does not load, and the only trace is a line in KSA's own log.

Core's own content is the reference schema, and it ships in the install:

```bash
diff -ru "<old install>/Content/Core" "/mnt/c/Program Files/Kitten Space Agency/Content/Core" \
    --include='*.xml' | head -200
```

No old install to hand? The mirror's previous commit has the assemblies but not Core's content, so
the fallback is to read the deserialised types directly — `PartGameData.cs`, `PartTemplate.cs`,
`EditorTagDefinition.cs` — and compare their `[XmlElement]` and `[XmlAttribute]` names against what
`src/KSArmory/KSArmory*.xml` actually writes.

What to look for, in order of how quietly it fails:

- **A renamed or removed attribute** the mod sets. Silently defaults.
- **A new required element** on a type the mod declares. Usually a load error, so at least loud.
- **A renamed Core Id** the mod references — a mesh, a material, an editor tag.
  `./tools/validate-parts.py` catches this class, but **only run against the install**; with
  `--offline` it cannot see Core at all and passes regardless.

## 5. Build, fix, test

```bash
./tools/build.sh
./tools/test.sh
```

Fix compile errors against the decompiled sources rather than by guessing — the definitive
answer to "what is this now" is in `../ksa-game-assemblies/current/src`. `tools/apidump` is the
faster way to ask a narrow question:

```bash
cd tools/apidump && dotnet run -- ../../Import members KSA.Vehicle
```

The tests do not touch KSA, so they passing proves the simulation still works, **not** that
the game binding does. Step 8 is where that is settled. Anything found in step 3 that survives
into runtime behaviour needs a line in `CHECKLIST.md`.

## 6. Record the new build everywhere it is written down

```bash
./tools/check-assemblies.sh --update      # rewrites the digests in ksa-assemblies.lock
```

Two the tooling owns:

- `ksa-assemblies.lock` — the `build` line, **by hand**. Every other check reads it, so it is the
  source of truth for the rest.
- `../ksa-game-assemblies/current/KSA_BUILD` — in the private repository.

Then **five prose files**, every one of them enforced. `tools/check-docs.sh` fails if a file
mentions any build number and does not mention the lock's, so a missed one is a red CI run rather
than a quiet lie — but only if you run it, which is step 7.

- `CLAUDE.md` — the **KSA build** line under *Environment*
- `README.md`
- `docs/KSA-MODDING-NOTES.md`
- `docs/KSA-CAMERAS.md`
- `docs/BLOCKED-ON-KSA.md`

The last three each name the build they were read against, which is what makes their claims
datable. Do not sweep the number through with `sed` and call it done: a doc that says it was read
against the new build is asserting someone read it against the new build.

`CHECKLIST.md` also carries build numbers, in the status lines recording what was flown. Those are
**history and must not be updated** — "confirmed against 2026.8.5.5168" stays true. It is not in
the enforced list for exactly that reason.

`docs/KSA-CAMERAS.md` cites `file:line` throughout, and line numbers move on every update. Do not
try to refresh them all — spot-check the handful a fix actually depended on, and leave the rest
carrying the build number that says how old they are.

Then regenerate the surface, because fixing breakages usually changes what the mod binds to:

```bash
./tools/api-surface.sh
```

## 7. Verify the whole chain

```bash
./tools/check-all.sh                 # all 18, and the pre-push hook runs it anyway
./tools/validate-parts.py            # against the install, NOT --offline, so Core is readable
./tools/model/checkswept.py          # nothing adrift or passing through anything
```

`check-all.sh` is the one that matters and is easy to skip in favour of the handful of checks that
feel related. It carries `check-docs.sh`, which is what proves step 6 was done properly — fixing
five build numbers and never running the check that reads them is the obvious way to get this
wrong.

Then work through the recheck list at the top of **`docs/BLOCKED-ON-KSA.md`** and tick what has
changed. Nothing else in this procedure will surface those: they are calls the engine does not
make and modules it does not have, so `ksa-api-diff.sh` sees nothing, and a build that succeeds
proves nothing about them. This is the one moment any of them can become possible.

## 8. Fly it

**A green suite is not evidence.** The tests link `Sim/` and reference no KSA assembly at all, so
they pass identically whether the game binding works or is completely broken — which is the exact
failure an upgrade produces. Nothing above this line has run a single line of KSA.

```bash
./tools/scenario.sh head-on          # a whole engagement, unattended, pass/fail
```

That flies search, lock, slew, salvo, guidance, fuse and kill against the real game and exits
non-zero if the target survives. It is the cheapest proof the binding still works, and it needs
nobody at the keyboard.

Then look at it, because a scenario checks the outcome and not the picture:

```bash
./tools/run.sh                       # build, deploy, launch, follow the mod's log
```

Worth a minute each, because each rests on a contract from step 3 that no tool checks: the turret
traverses and the pods elevate (subpart transform writes); round bodies leave the tubes and are
not stuck at the launcher (the anchor); the overlay sits on the craft rather than beside it (the
draw anchor); the optical head centres its target at 16× (the frame ordering and the camera
basis); the parts still appear under **Weapons** in the editor (the editor tag).

Anything that misbehaves goes in `CHECKLIST.md` as a finding against the new build, not in the
retarget commit as a silent fix.

## 9. Push, private repository first

```bash
git -C ../ksa-game-assemblies push        # MUST be first
```

Then commit here. Use Conventional Commits — semantic-release parses them, and a message that
does not parse silently produces no release:

```
build(ksa): retarget 2026.9.1.5200
```

`build` is a patch bump. If the upgrade changed mod behaviour a player would notice, that part
belongs in its own `fix` or `feat` commit — do not bury it in the retarget.

**Release promptly, because reporting is off until you do.** The panel hides **Report bug** and
**Feedback** whenever the KSA build stamped into the assembly differs from the one running, and
the moment RocketWerkz ship a new build that is everyone. It is a silent switch: nobody can report
that reporting is missing. The mod's log says so at load —

```
KSArmory 0.8.13 built for KSA 2026.8.5.5168, running 2026.9.1.5200 - reporting off
```

— and shipping a release built against the new lock restores it, for players who update.

The endpoint has the matching rule from the other side: it refuses reports from mod versions older
than the newest release, resolved at deploy time, and `release.yml` redeploys it as part of
publishing. Neither needs touching here.

## If it goes wrong

- **Compile errors mentioning types that clearly still exist** — `Import/` and the mirror have
  drifted. `./tools/check-assemblies.sh` says which folder the build actually resolved.
- **A huge, unreadable corpus diff** — either ilspycmd was upgraded or the mirrored assembly set
  changed. Both rewrite everything. Redo the corpus with the pinned ilspycmd and the same set.
- **CI fails on the lock but it builds locally** — the private repository was not pushed, or was
  pushed after this one.
- **Tests pass and the game misbehaves** — expected, and exactly what steps 3 and 8 are for. The
  tests never load KSA, so they cannot tell you anything about it either way.
- **`check-docs.sh` fails on a build number after you updated them** — there are five prose files,
  not three. `README.md` and `docs/BLOCKED-ON-KSA.md` are the two that get forgotten.
- **A part loads but behaves differently, with nothing in any log** — an XML attribute was renamed
  and is now sitting at its default. Step 4.
