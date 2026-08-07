#!/usr/bin/env python3
"""
Cuts a recording of cannon fire into the three parts KSA's <CompoundSound> wants.

    ./tools/cut-cannon.py                          # tools/audio/*.flac -> Sounds/
    ./tools/cut-cannon.py --source other.flac
    ./tools/cut-cannon.py --report                 # measure and print, write nothing

A burst of gunfire is three sounds, not one, and the engine already knows that: <StartSound>,
<Sound> and <StopSound>, which is exactly how Core drives an engine. Played as a single looping
sample you get the spin-up on every pass and never get the tail.

    spin      the barrels coming up to speed, roughly the first 150 ms
    loop      steady fire, cut to loop against itself for as long as the trigger is held
    tail      the reports arriving back off the terrain after firing stops

## The cut points are measured, not typed

An envelope at 5 ms resolution says where each part begins: the spin ends where the level stops
climbing and plateaus, the fire ends where it leaves that plateau, the tail ends where it drops
into the noise floor. Written-down timestamps would be wrong for any other recording and would
silently drift if this one were ever re-exported.

## The loop is crossfaded, and has to be

A cut taken straight out of the middle clicks once a pass, because its last sample has no
relationship to its first. The fix is to blend the material that *would have followed* the
segment onto the segment's own head: then the end genuinely flows into the beginning. Anything
shorter than about 50 ms of overlap is audible as a flutter at the seam.
"""

import argparse
import wave
from pathlib import Path

import numpy as np
import soundfile as sf

RATE = 44100

# Loop length. Long enough that the ear does not hear the repeat, short enough that letting go of
# the trigger is answered promptly, and it must sit entirely inside the steady part.
LOOP_SECONDS = 0.9
CROSSFADE_SECONDS = 0.06


def envelope(x, step=0.005):
    h = int(step * RATE)
    return np.array([np.sqrt((x[i:i + h] ** 2).mean()) for i in range(0, len(x) - h, h)]), h


def segments(x):
    """(spin_end, fire_end, tail_end) in samples, from the envelope."""
    env, h = envelope(x)
    peak = env.max()

    # The plateau: everything within 25% of the loudest. Its first sample ends the spin-up and its
    # last ends the firing.
    plateau = np.where(env >= 0.75 * peak)[0]
    spin_end = int(plateau[0] * h)
    fire_end = int((plateau[-1] + 1) * h)

    # The tail runs until the level falls into the noise floor. 3% of peak rather than zero: an
    # encoded file never reaches silence, and chasing it to zero appends a second of nothing.
    above = np.where(env >= 0.03 * peak)[0]
    tail_end = int(min((above[-1] + 1) * h, len(x)))

    return spin_end, fire_end, tail_end


def loop_from(x, start, end):
    """A seamless loop out of [start, end), crossfaded against what follows it."""
    length = int(LOOP_SECONDS * RATE)
    xf = int(CROSSFADE_SECONDS * RATE)

    if end - start < length + xf:
        length = max(int(0.25 * RATE), (end - start) - xf)

    # Centred in the steady part, so neither edge borrows from the spin-up or the run-down.
    at = start + max(0, ((end - start) - (length + xf)) // 2)

    core = x[at:at + length].copy()
    follows = x[at + length:at + length + xf]

    w = np.linspace(0.0, 1.0, len(follows))
    core[:len(follows)] = core[:len(follows)] * w + follows * (1.0 - w)
    return core


def fade(x, seconds_in, seconds_out):
    y = x.copy()
    n_in, n_out = int(seconds_in * RATE), int(seconds_out * RATE)
    if n_in > 0:
        y[:n_in] *= np.linspace(0.0, 1.0, n_in)
    if n_out > 0:
        y[-n_out:] *= np.linspace(1.0, 0.0, n_out)
    return y


def write(path, samples):
    pcm = np.clip(samples, -1.0, 1.0)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)          # mono: the engine spatialises it, and a stereo file cannot be
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((pcm * 32767.0).astype("<i2").tobytes())

    print(f"  {path.name:<28} {len(samples) / RATE:5.2f}s  {path.stat().st_size // 1024:>4} KiB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    ap.add_argument("--source",
                    default="tools/audio/163119__qubodup__navy-mk-15-phalanx-ciws-anti-air-fast-burst.flac")
    ap.add_argument("--out", default="src/KSArmory/Sounds")
    ap.add_argument("--report", action="store_true", help="measure and print, write nothing")
    args = ap.parse_args()

    data, rate = sf.read(args.source, always_2d=True)

    # Down to mono by averaging. The channels correlate at 0.82, so the two carry mostly the same
    # event and the average keeps it; a hard pick of one channel throws away half the room.
    x = data.mean(axis=1)

    if rate != RATE:
        # Linear resample. Good enough for a sound effect, and it avoids a scipy dependency for a
        # tool the rest of the pipeline does not need one for.
        n = int(round(len(x) * RATE / rate))
        x = np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x)

    x = x / (float(np.max(np.abs(x))) or 1.0)

    spin_end, fire_end, tail_end = segments(x)
    print(f"  source {args.source}  {len(x) / RATE:.2f}s")
    print(f"    spin  0.000 - {spin_end / RATE:.3f}s")
    print(f"    fire  {spin_end / RATE:.3f} - {fire_end / RATE:.3f}s")
    print(f"    tail  {fire_end / RATE:.3f} - {tail_end / RATE:.3f}s")

    if args.report:
        return

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    # No fade-in on the spin: its first sample is the first round, and easing into that is the one
    # thing that would stop it sounding like a gun starting.
    write(out / "KSArmory_Cannon_Spin.wav", fade(x[:spin_end], 0.0, 0.01))
    write(out / "KSArmory_Cannon_Loop.wav", loop_from(x, spin_end, fire_end))
    write(out / "KSArmory_Cannon_Tail.wav", fade(x[fire_end:tail_end], 0.01, 0.05))

    seam = loop_from(x, spin_end, fire_end)
    print(f"\n  loop seam step {abs(seam[-1] - seam[0]):.4f}  (0 = continuous)")


main()
