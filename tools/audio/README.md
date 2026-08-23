# Source recordings

Recordings the shipped cannon sound is cut from. **Unlike everything else under
`src/KSArmory/Sounds/`, these are not synthesised**, which is why this folder exists rather than the
cuts simply appearing in `Sounds/`: a recording has a provenance, and the provenance has to live
where the next person will find it.

| File | Source | Licence |
| --- | --- | --- |
| `163119__qubodup__navy-mk-15-phalanx-ciws-anti-air-fast-burst.flac` | freesound.org/s/163119/, by qubodup | **CC0** (public domain dedication) |

CC0 places no condition on redistribution, so this ships inside the MIT archive with nothing to
carry alongside it. **No credit is shipped to players, by choice** — the record above is kept for
maintenance rather than obligation, so that a recording sitting among otherwise synthesised samples
can be shown to be safe without anyone having to re-derive where it came from.

Keep the filename as freesound produced it. It encodes the id, which is the only thing that leads
back to the source page and its licence.

## What it is

A real Mk 15 Phalanx. Measured at **4700 rpm**, against the 4500 the profile declares, so
`Config.CannonReferenceRpm` is 4500 and the CIWS plays it essentially untransposed.

Prefer the FLAC to a lossy copy even though the cuts are re-encoded anyway. An earlier pass used a
128 kbit ogg of the same recording and its cycle rate measured 6600 rpm — the codec had smeared the
envelope enough to put the autocorrelation on a harmonic, which is a wrong number that looks
perfectly plausible.

## Regenerating the cuts

    ./tools/cut-cannon.py

Writes the three parts into `src/KSArmory/Sounds/`. The cut points are measured off the envelope
rather than typed in, so a different recording of the same shape works without editing numbers.
`--report` prints them and writes nothing.

If this recording ever has to go, `tools/sounds.py --synth-cannon` regenerates the synthesised
cannon it replaced and `KSArmorySounds.xml` goes back to a single looping `<Sound>`.
