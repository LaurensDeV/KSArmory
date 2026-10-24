#!/usr/bin/env python3
"""
Cuts a recording of cannon fire into the three parts KSA's <CompoundSound> wants.

    ./tools/cut-cannon.py                          # tools/audio/*.flac -> Sounds/
    ./tools/cut-cannon.py --source other.flac
    ./tools/cut-cannon.py --report                 # measure and print, write nothing

Core drives an engine with <StartSound>, <Sound> and <StopSound>; one looping sample would play the
spin-up on every pass and never the tail.

    spin      the barrels coming up to speed
    loop      steady fire, looped for as long as the trigger is held
    tail      the reports arriving back off the terrain after firing stops

The cut points are measured off a 5 ms envelope rather than typed, so another recording or a
re-export cuts correctly. The loop is crossfaded against what follows it, since a cut taken
straight out clicks once a pass; under about 50 ms of overlap the seam flutters.
"""

import argparse
import wave
from pathlib import Path

import numpy as np
import soundfile as sf

RATE = 44100

# Long enough that the repeat is not heard, short enough that letting go is answered promptly, and
# inside the steady part.
LOOP_SECONDS = 0.9
CROSSFADE_SECONDS = 0.06


def envelope(x, step=0.005):
    h = int(step * RATE)
    return np.array([np.sqrt((x[i:i + h] ** 2).mean()) for i in range(0, len(x) - h, h)]), h


def segments(x):
    """(spin_end, fire_end, tail_end) in samples."""
    env, h = envelope(x)
    peak = env.max()

    # The plateau, within 25% of the loudest, runs from the end of the spin-up to the end of firing.
    plateau = np.where(env >= 0.75 * peak)[0]
    spin_end = int(plateau[0] * h)
    fire_end = int((plateau[-1] + 1) * h)

    # 3% of peak rather than zero: an encoded file never reaches silence.
    above = np.where(env >= 0.03 * peak)[0]
    tail_end = int(min((above[-1] + 1) * h, len(x)))

    return spin_end, fire_end, tail_end


def loop_from(x, start, end):
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
        w.setnchannels(1)          # mono: the engine spatialises it
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

    # The channels correlate at 0.82, so averaging keeps the event where picking one loses half the room.
    x = data.mean(axis=1)

    if rate != RATE:
        # Linear: good enough for a sound effect, and no scipy dependency.
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

    # No fade-in on the spin: its first sample is the first round.
    write(out / "KSArmory_Cannon_Spin.wav", fade(x[:spin_end], 0.0, 0.01))
    write(out / "KSArmory_Cannon_Loop.wav", loop_from(x, spin_end, fire_end))
    write(out / "KSArmory_Cannon_Tail.wav", fade(x[fire_end:tail_end], 0.01, 0.05))

    seam = loop_from(x, spin_end, fire_end)
    print(f"\n  loop seam step {abs(seam[-1] - seam[0]):.4f}  (0 = continuous)")


main()
