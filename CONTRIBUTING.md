# Contributing

Thanks for looking. This is a mod for a **pre-release game with no official code-modding API**, so
some of the setup is unusual and a couple of the rules below exist because breaking them cost
whole evenings.

**Modded Kerbal Space Program before?** Read
[docs/FROM-KSP-MODDING.md](docs/FROM-KSP-MODDING.md) first. Most of what you know transfers; three
things actively mislead, and that page is the translation.

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
| Run the tests, change anything under `Sim/` | .NET 10 + KSA's assemblies |
| Compile the game-facing half | the same |
| Actually play the mod | the above, plus KSA and [StarMap](https://github.com/StarMapLoader/StarMap) |
| Rebuild the 3D model | plus Blender 5.2 |

**A large amount of this repository is testable without the game running.** Everything under
`src/KSArmory/Sim/` is free of KSA types *by construction* — the test project links it and
references no game assembly, so a stray `using KSA;` there fails the test build rather than
slipping through. If you are fixing guidance, threat classification, tube geometry or the fuse,
you never have to launch KSA.

You do still need its assemblies to *build* that half: `double3` comes from
`Brutal.Core.Numerics.dll` and appears throughout `Sim/`, so the test project references it and
`tools/test.sh` errors out without it.

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

| | Windows (Git Bash) | WSL | Native Linux / macOS |
| --- | --- | --- | --- |
| Build, test, package | yes | yes | yes |
| Compile against KSA | yes | yes | yes, with KSA installed |
| `tools/deploy.sh` | yes | yes | yes |
| `tools/run.sh` (launch the game) | no — run `StarMap.exe` | yes | no |
| Blender model pipeline | set `BLENDER` | yes | no |

The gaps are honest ones rather than oversights: `run.sh` drives a Windows `StarMap.exe` through
WSL interop, and the model scripts drive a Windows Blender binary. StarMap does ship a portable
`StarMap.dll` that `dotnet StarMap.dll` should run on Linux — **that path is untested here**, and
confirming it would be a genuinely useful contribution.

### On Windows

**The tooling is bash, so use [Git Bash](https://git-scm.com/download/win)** — it ships with Git
for Windows and has everything the scripts need. PowerShell and `cmd` will not run them.

```bash
./tools/doctor.sh          # recognises Git Bash and reports what works here
./tools/build.sh
./tools/test.sh
```

Two things to know, both already handled but worth understanding:

- **Line endings.** Git for Windows defaults to `core.autocrlf=true`, which would rewrite every
  script to CRLF and make bash fail with `$'\r': command not found` — a message that names neither
  the file nor the cause. `.gitattributes` forces LF on scripts and sources, so a fresh clone is
  correct whatever your global Git config says. `doctor.sh` checks it anyway, because a clone made
  *before* that file existed is still broken.
- **Paths.** The build finds a KSA install at `C:\Program Files\Kitten Space Agency` by itself.
  Several of the older helper scripts still assume WSL's `/mnt/c` rather than Git Bash's `/c`; the
  ones that matter for building take `KSA_DIR` or `KSA_DLL_DIR`, so set those if a script cannot
  find your install.

**Launch the game by running `StarMap.exe` directly.** `tools/run.sh` exists to reach a Windows
StarMap *from WSL*, which is a problem a Windows developer does not have. `./tools/deploy.sh` puts
the mod where KSA will load it, and works from Git Bash.

> The Windows-native path has been **reasoned through and made correct, but not executed on a
> Windows machine** — this repository is developed from WSL. If you hit something it gets wrong,
> that is a bug worth reporting rather than something you are doing wrong.

**Case sensitivity bites across platforms.** A mismatched filename loads on Windows and fails on
Linux, so CI runs `validate-parts.py --offline` on Linux specifically, comparing against the real
directory listing rather than trusting `is_file()`.

## Before you open a PR

**Open it against `dev`, not `main`.** `main` is the release branch: a push to it cuts a version
and publishes it to SpaceDock. Everything lands on `dev` first and rides to `main` in a merge when
a release is wanted. CI runs on every branch and every pull request either way, so nothing is less
gated for being on `dev`.

```bash
./tools/check-all.sh          # everything CI runs, about 8 seconds
```

That is the whole list, and it is the same script CI calls — so the two cannot drift, which is
what three hand-maintained lists in three files could not manage. `./tools/check-all.sh --list`
names the checks; `--with-sweep` adds the drive sweep, which is left out by default because it
is ~43 s against the rest of the set's ~7 s.

`./tools/install-hooks.sh` wires it to `pre-push`, so you get it without remembering. Push
anyway with `git push --no-verify`.

Anything needing the game assemblies — the build, the C# tests, the API surface — is **skipped
with a notice** rather than failed, so the script is useful without owning KSA. It says which
ones it skipped.

On a fork, the `tooling` job runs in full: it needs no secret. Only the `build` job is gated on
the private assemblies mirror, and it skips with a notice instead of failing — that is
deliberate, not something you need to fix.

## Rules that are not style preferences

**Commit messages are [Conventional Commits](https://www.conventionalcommits.org/), and they are
the input to versioning.** semantic-release parses them, so a message that does not parse produces
no release and never reaches the changelog. **`feat`, `fix` and `perf` all cut a patch**; `docs`,
`test`, `refactor` and `chore` cut nothing. Minor versions are never automatic — the maintainer
tags them by hand when something genuinely lands.

So the type is a changelog decision, not a release-size one. `feat` still means a player can
**observe** the difference by installing the new archive; capability that nothing yet uses is
`refactor` and becomes a feature in the commit that uses it. Developer tooling is `chore` however
much work it was.

**A behaviour change is unverified until it has been flown.** Compiling and passing the suite are
not evidence. This mod's hardest bugs live in the gap between the maths and what KSA actually
does, and that gap is only visible in game. If you cannot test in game, say so in the PR — that is
a perfectly good contribution, it just means someone else confirms it before it ships.

**Docs are part of the change.** If your commit makes a line in `CLAUDE.md`, `docs/`, `README.md`
or a comment untrue, fix it in the same commit — a stale line is worse than a missing one, because
it is trusted. Comment *why*, never *what*. State the fact, not the history: what broke and when
belongs in git and in `docs/`, not in a comment. Keep it to a sentence or two, and if a comment is
not strictly necessary, delete it. See "Comments and documentation" in `CLAUDE.md`.

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
