# Contributing

Thanks for looking. This is a mod for a **pre-release game with no official code-modding API**, so
some of the setup is unusual and a couple of the rules below exist because breaking them cost
whole evenings.

## Start here

```bash
./tools/doctor.sh
```

It checks everything this repository needs, says what is missing, and prints the one command that
fixes each thing. It exits non-zero only for things that actually block a build — a missing game
install or Blender are warnings, because plenty of useful work needs neither.

Then:

```bash
./tools/install-hooks.sh   # commit-msg hook: a message that does not parse silently cuts no release
./tools/build.sh           # needs .NET 10 - a distro dotnet 8 fails with NETSDK1045
./tools/test.sh            # the full suite, no game required
```

## What you need, and what you don't

| To do this | You need |
| --- | --- |
| Run the tests, change anything under `Sim/` | .NET 10 only |
| Compile the game-facing half | .NET 10 + KSA's assemblies |
| Actually play the mod | the above, plus KSA and [StarMap](https://github.com/StarMapLoader/StarMap) |
| Rebuild the 3D model | plus Blender 5.2 |

**A large amount of this repository is testable with nothing but the .NET SDK.** Everything under
`src/AirDefence/Sim/` is free of KSA types *by construction* — the test project links it and
references no game assembly, so a stray `using KSA;` there fails the test build rather than
slipping through. If you are fixing guidance, threat classification, tube geometry or the fuse,
you do not need the game at all.

### Getting KSA's assemblies

They are RocketWerkz's copyrighted files. **Never commit or publish them**; CI fails if a `.dll`
is tracked. Keeping your own copy is fine.

If you own KSA, point the build at it once:

```bash
./tools/sync-import.sh                  # copies them into Import/, which is gitignored
# ...or per-invocation:
KSA_DLL_DIR="/path/to/Kitten Space Agency" ./tools/build.sh
```

The build also finds a KSA install on its own — Windows, Steam on Linux, `~/Games`, `~/`, or WSL's
`/mnt/c`. If yours is somewhere else, `KSA_DLL_DIR` is the escape hatch, and the build says so
when it cannot find them.

## Platform notes

**The mod ships as one portable `net10.0` assembly** — no `RuntimeIdentifier`, no P/Invoke, no
Windows-only API — so the same archive works everywhere and there is nothing to build twice.

| | Windows | WSL | Native Linux / macOS |
| --- | --- | --- | --- |
| Build, test, package | yes | yes | yes |
| Compile against KSA | yes | yes | yes, with KSA installed |
| `tools/run.sh` (launch the game) | — | yes | no |
| Blender model pipeline | yes | yes | no |

The two gaps are honest ones rather than oversights: `run.sh` drives a Windows `StarMap.exe`
through WSL interop, and the model scripts drive a Windows Blender binary. StarMap does ship a
portable `StarMap.dll` that `dotnet StarMap.dll` should run on Linux — **that path is untested
here**, and confirming it would be a genuinely useful contribution.

**Case sensitivity bites across platforms.** A mismatched filename loads on Windows and fails on
Linux, so CI runs `validate-parts.py --offline` on Linux specifically, comparing against the real
directory listing rather than trusting `is_file()`.

## Before you open a PR

```bash
./tools/build.sh
./tools/test.sh
./tools/check-boundary.sh     # Sim/ must not reference KSA types
./tools/validate-parts.py     # if you touched part XML or launch geometry
```

CI runs all of these plus shellcheck, XML well-formedness, and a check that no binaries are
tracked. The `build` job needs the private assemblies mirror, so **on a fork it skips with a
notice instead of failing** — that is deliberate, not something you need to fix.

## Rules that are not style preferences

**Commit messages are [Conventional Commits](https://www.conventionalcommits.org/), and they are
the input to versioning.** semantic-release parses them, so a message that does not parse produces
no release and never reaches the changelog. `feat` is a minor, `fix`/`perf` a patch, `docs`,
`test`, `refactor` and `chore` cut no release. Pick the type by asking whether a *player* would
notice — developer tooling is `chore` however much work it was.

**A behaviour change is unverified until it has been flown.** Compiling and passing the suite are
not evidence. This mod's hardest bugs live in the gap between the maths and what KSA actually
does, and that gap is only visible in game. If you cannot test in game, say so in the PR — that is
a perfectly good contribution, it just means someone else confirms it before it ships.

**A regression test only counts if it fails against the old code.** Check that it does, every
time. Tests here have three times been written for a bug, passed against it, and looked like
proof — usually by asserting at the wrong instant. Reintroduce the bug, watch the test go red, put
it back.

**Read [`docs/FRAMES-AND-EPOCHS.md`](docs/FRAMES-AND-EPOCHS.md) before touching rounds, drawing or
timing.** Near Earth every position carries ~29.8 km/s of ecliptic motion, so two values a fraction
of a frame apart differ by hundreds of metres. Every hard bug this mod has had is that, wearing a
different disguise each time.

## Finding your way around

- [`CLAUDE.md`](CLAUDE.md) — the full map: layout, design decisions, traps, the KSA-update
  procedure. Long, but it is the accumulated cost of everything that has gone wrong.
- [`docs/FRAMES-AND-EPOCHS.md`](docs/FRAMES-AND-EPOCHS.md) — frames, epochs, and how to tell the
  four failure shapes apart.
- [`docs/KSA-MODDING-NOTES.md`](docs/KSA-MODDING-NOTES.md) — the reverse-engineered game API.
- [`docs/MODULARITY.md`](docs/MODULARITY.md) — how far the design generalises, and what is planned.
- [`README.md`](README.md) — installing and playing, plus a "How it works" section.

**Adding a weapon system is data plus art**, not new logic — one entry in `Sim/Arsenal.cs`. See
the section of the same name in `CLAUDE.md`.

## Licence

Code is under the repository's [LICENCE](LICENSE). Contributions are accepted under the same
terms. Do not add KSA's game files, decompiled game sources, or anything else RocketWerkz owns.
