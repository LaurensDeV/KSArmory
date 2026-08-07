#!/usr/bin/env python3
"""
Synthesises the mod's own sound effects.

    ./tools/sounds.py                    # into src/KSArmory/Sounds/
    ./tools/sounds.py --out /tmp/snd     # somewhere else
    ./tools/sounds.py --preview          # ...and print what each layer contributes

Generated rather than sourced, for the same reason the meshes and the palette are. A downloaded
sample carries a licence that has to travel with the archive, has to be auditioned by ear before
anyone can tell whether it changed, and cannot be re-cut for a different warhead size. A script is
MIT like the rest of the repository, diffs as text, and takes a parameter.

## What an explosion is made of

Four layers, because a single noise burst reads as static and a single sine reads as a drum:

    crack     thirty milliseconds of barely-filtered noise. The shock front, and the reason the
              whole thing reads as a detonation rather than a thud - a first pass at this had 6%
              of its energy above 2 kHz and sounded, correctly, dull.
    punch     a sine sweeping 90 Hz down to 25 Hz in a quarter second. This is the part you feel,
              and the part a small charge should not have much of.
    body      broadband noise through a one-pole low-pass, fast attack and exponential decay.
              The bulk of the sound.
    tail      the same, far more heavily filtered and much longer: the rumble off the terrain.
    crackle   sparse impulses in the first third of a second. Fragments and debris, and what
              stops the whole thing sounding like a sigh.

Two things matter more than the spectrum. The envelope: a real detonation has an attack of a
millisecond or two and no sustain whatsoever, and anything slower reads as a whoosh. And the
saturation: explosions clip whatever recorded them, so the mix is driven hard into a tanh rather
than politely normalised.
"""

import argparse
import math
import wave
from pathlib import Path

import numpy as np

RATE = 44100


def envelope(n, attack, decay, power=2.0):
    """Fast attack, exponential decay. `power` above 1 makes the tail fall away faster."""
    t = np.arange(n) / RATE
    rise = np.clip(t / max(attack, 1e-6), 0.0, 1.0)
    fall = np.exp(-t / max(decay, 1e-6)) ** power
    return rise * fall


def lowpass(x, cutoff):
    """One-pole low-pass. Cheap, and the slope is what makes a noise burst read as distant."""
    a = math.exp(-2.0 * math.pi * cutoff / RATE)
    out = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = ((1.0 - a) * v) + (a * acc)
        out[i] = acc
    return out


def crack(seconds, rng, cutoff=11000.0):
    """The shock front: bright, brutal, over in a few hundredths of a second."""
    n = int(RATE * seconds)
    return lowpass(rng.normal(0.0, 1.0, n), cutoff) * envelope(n, 0.0004, 0.030, 1.6)


def punch(seconds, rng, start=90.0, end=25.0):
    """The sub-bass thump: a sine sweeping down, which is what a pressure wave sounds like."""
    n = int(RATE * seconds)
    t = np.arange(n) / RATE

    # Sweep the frequency exponentially and integrate it, or the pitch steps rather than glides.
    f = start * ((end / start) ** np.clip(t / 0.28, 0.0, 1.0))
    phase = 2.0 * math.pi * np.cumsum(f) / RATE

    return np.sin(phase + rng.uniform(0, 2 * math.pi)) * envelope(n, 0.002, 0.20, 1.4)


def body(seconds, rng, cutoff=3600.0):
    n = int(RATE * seconds)
    return lowpass(rng.normal(0.0, 1.0, n), cutoff) * envelope(n, 0.0015, 0.16, 1.1)


def tail(seconds, rng, cutoff=260.0):
    n = int(RATE * seconds)
    return lowpass(rng.normal(0.0, 1.0, n), cutoff) * envelope(n, 0.02, 0.55, 0.8)


def crackle(seconds, rng, count=150):
    """Sparse impulses: fragments. Without these the whole thing exhales rather than bursts."""
    n = int(RATE * seconds)
    out = np.zeros(n)

    # Clustered early, because debris arrives with the blast rather than after it.
    for _ in range(count):
        at = int(abs(rng.normal(0.0, 0.10)) * RATE)
        if at >= n - 64:
            continue
        length = rng.integers(24, 220)
        end = min(n, at + int(length))
        grain = rng.normal(0.0, 1.0, end - at) * np.linspace(1.0, 0.0, end - at) ** 2
        out[at:end] += grain * rng.uniform(0.3, 1.0)

    return lowpass(out, 9000.0) * envelope(n, 0.001, 0.24, 1.0)


def explosion(seed, seconds=1.7):
    """One detonation. `seed` varies it, so a salvo is not the same click twelve times.

    1.7 s because measurement says the tail is inaudible past 1.58: -6dB at 0.13, -20dB at 0.37.
    """
    rng = np.random.default_rng(seed)

    mix = (crack(seconds, rng) * 0.90
           + punch(seconds, rng) * 0.75
           + body(seconds, rng) * 0.70
           + tail(seconds, rng) * 0.35
           + crackle(seconds, rng) * 0.60)

    # Soft clip rather than hard normalise: a detonation is supposed to sound saturated, and
    # tanh keeps the transient from simply being sliced flat.
    peak = float(np.max(np.abs(mix))) or 1.0
    return np.tanh((mix / peak) * 2.6) * 0.94


def write(path, samples):
    pcm = np.clip(samples, -1.0, 1.0)
    pcm = (pcm * 32767.0).astype("<i2")

    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)          # mono: the engine spatialises it, and a stereo file cannot be
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())

    print(f"  {path}  {len(samples) / RATE:.2f}s  {path.stat().st_size // 1024} KiB")


def main():
    ap = argparse.ArgumentParser(description="Generate the mod's sound effects.")
    ap.add_argument("--out", default="src/KSArmory/Sounds")
    args = ap.parse_args()

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    # Three variants, picked between at random by the sound declaration. One sample repeated is
    # the thing that makes a burst of cannon fire sound synthetic.
    print("explosions:")
    for i in range(3):
        write(out / f"KSArmory_Burst{i:02d}.wav", explosion(seed=1000 + i))


main()
