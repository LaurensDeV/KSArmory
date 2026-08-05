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

This reads `docs/KSA-API-SURFACE.md` — the 210 members this mod genuinely binds to, extracted
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

## 4. Build, fix, test

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
the game binding does. Anything found in step 3 that survives into runtime behaviour needs a
line in `CHECKLIST.md`.

## 5. Record the new build in all four places

Three are prose and one is enforced. Miss the enforced one and CI fails; miss the others and
the next person is misled.

```bash
./tools/check-assemblies.sh --update      # rewrites the digests in ksa-assemblies.lock
```

- `ksa-assemblies.lock` — the `build` line, by hand. **This is the one CI enforces.**
- `../ksa-game-assemblies/current/KSA_BUILD`
- CLAUDE.md, the **KSA build** line under *Environment*.
- `docs/KSA-MODDING-NOTES.md`, if it names the build.

Then regenerate the surface, because fixing breakages usually changes what the mod binds to:

```bash
./tools/api-surface.sh
```

## 6. Verify the whole chain

```bash
./tools/build.sh && ./tools/test.sh
./tools/validate-parts.py
./tools/check-assemblies.sh          # the lock now matches
./tools/api-surface.sh --check       # the surface now matches
./tools/check-boundary.sh
./tools/model/checkswept.py          # nothing adrift or passing through anything
```

Then work through the recheck list at the top of **`docs/BLOCKED-ON-KSA.md`** and tick what has
changed. Nothing else in this procedure will surface those: they are calls the engine does not
make and modules it does not have, so `ksa-api-diff.sh` sees nothing, and a build that succeeds
proves nothing about them. This is the one moment any of them can become possible.

## 7. Push, private repository first

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

## If it goes wrong

- **Compile errors mentioning types that clearly still exist** — `Import/` and the mirror have
  drifted. `./tools/check-assemblies.sh` says which folder the build actually resolved.
- **A huge, unreadable corpus diff** — either ilspycmd was upgraded or the mirrored assembly set
  changed. Both rewrite everything. Redo the corpus with the pinned ilspycmd and the same set.
- **CI fails on the lock but it builds locally** — the private repository was not pushed, or was
  pushed after this one.
- **Tests pass and the game misbehaves** — expected, and exactly what step 3 is for. The tests
  never load KSA.
