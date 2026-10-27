# Source recordings

The recordings every shipped sound in `src/KSArmory/Sounds/` is cut from, with the provenance
of each, kept here so the cuts can be remade and their licences found.

| File | Source | Licence |
| --- | --- | --- |
| `163119__qubodup__navy-mk-15-phalanx-ciws-anti-air-fast-burst.flac` | freesound.org/s/163119/, by qubodup | **CC0** (public domain dedication) |

CC0 places no condition on redistribution, so this ships inside the MIT archive with nothing to
carry alongside it. **No credit is shipped to players, by choice** — the record above is kept for
maintenance rather than obligation.

Keep the filename as freesound produced it. It encodes the id, which is the only thing that leads
back to the source page and its licence.

## What it is

A real Mk 15 Phalanx. Measured at **4700 rpm**, against the 4500 the profile declares, so
`Config.CannonReferenceRpm` is 4500 and the CIWS plays it essentially untransposed.

Prefer the FLAC to a lossy copy even though the cuts are re-encoded anyway: a 128 kbit ogg of the
same recording measured 6600 rpm, the codec smearing the envelope enough to put the autocorrelation
on a harmonic.

## Regenerating the cuts

    ./tools/cut-cannon.py

Writes the three parts into `src/KSArmory/Sounds/`. The cut points are measured off the envelope
rather than typed in, so a different recording of the same shape works without editing numbers.
`--report` prints them and writes nothing.

If this recording ever has to go, `tools/sounds.py --synth-cannon` regenerates the synthesised
cannon it replaced and `KSArmorySounds.xml` goes back to a single looping `<Sound>`.

## The 5"/54 Mk 42

| File | Source | Licence |
| --- | --- | --- |
| `mk42/Mk42_Gunshot.wav` | recorded for this mod by its author | ships under the mod's own MIT licence |

The gun's gunshot, one per round. Kept stereo as recorded, so the cut can be remade:
`./tools/mk42-sounds.py` writes the mono `src/KSArmory/Sounds/KSArmory_Mk42_Gunshot.wav` from it, and
`--report` prints what it would write. The shell's burst has no sound of its own.

## The M197

| File | Source | Licence |
| --- | --- | --- |
| `m197/M197_740rpm.wav` | sent by Mallikas with the model, 2026-09-29 | cleared for shipping with the mod by its author, 2026-09-29 |
| `m197/M197_Tail.wav` | as above | as above |

A firing loop and its tail, kept stereo as sent. `./tools/m197-sounds.py` writes the mono
`src/KSArmory/Sounds/KSArmory_M197_{Loop,Tail}.wav`; the loop is cut where the recording repeats its
own head, so the seam lands inside the shot rhythm. `--report` prints what it would write.

## A nuclear burst: Upshot-Knothole ANNIE

| File | Source | Licence |
| --- | --- | --- |
| `annie/Upshot-Knothole_ANNIE_1953.flac` | the soundtrack of the 1953 ANNIE test footage, from the US National Archives, as posted with Alex Wellerstein's *The Sound of the Bomb (1953)*, Restricted Data, 2012-07-13 (`nuclearsecrecy.com/blog/2012/07/13/the-sound-of-the-bomb-1953/`, gone by 2026-10-25); sent by the mod's author as a WAV trimmed by happyscribe.com, file name `Operation_Upshot-Knothole_ANNIE_nuclear_test,_1953` | **public domain** — the post credits the US National Archives, and a US federal government work carries no copyright |

ANNIE was 16 kt, filmed from about 10.7 km: a countdown ("one, zero"), the equipment picking up the
burst at the instant of the flash, and the bang 31.3 s later, with voices and a scream around it.
Mono in the recording, both channels identical. `./tools/annie-sounds.py` writes
`src/KSArmory/Sounds/KSArmory_Nuclear_{Pickup,Bang}.wav`: the pickup without the countdown, the bang
without the scream over its tail, and the film's hiss taken out of both. The pickup's buzz is a
harmonic series on 24 Hz, the film's frame rate, so the hum notch is not put on it. `--report`
prints what it would write.
