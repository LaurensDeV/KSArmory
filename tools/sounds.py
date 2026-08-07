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

## What a cannon is made of

Not a series of gunshots. A Phalanx cycles at 4500 rounds a minute, which is 75 a second, and 75 Hz
is a *pitch* rather than a rhythm: the ear stops resolving the individual reports somewhere around
20 and hears a buzzsaw instead. That is the whole character of the sound, and it is why a gun this
fast is synthesisable at all while a rifle would not be.

So the loop is a pulse train at the cycle rate, not a sample of a shot repeated:

    report    two milliseconds of bright noise per round, the muzzle blast itself
    thump     a damped low sine per round, the part that carries
    roar      continuous broadband noise under the pulses, the gas leaving the barrels

The file holds a whole number of cycles so that restarting it is phase-continuous, which is what
lets a two-second sample stand in for a burst of any length without a click at the seam. And the
rate is a *pitch* multiplier at playback: a gun cycling at half the reference rate is the same
sound an octave down, so one sample serves every cannon in the mod.
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


def highpass(x, cutoff):
    """One-pole high-pass, as the complement of the low-pass. Used to keep the cannon out of the
    sub-bass, where a pulse train piles up into a drone rather than a gun."""
    return x - lowpass(x, cutoff)


def bandpass(x, low, high, order=1):
    """Cascaded one-poles. `order` above 1 is what makes a *band* rather than a gentle tilt: at
    one pole a nominal 125-250 Hz band leaks most of its energy into the octaves either side, and
    the layer that was supposed to carry the body ends up carrying almost nothing."""
    for _ in range(order):
        x = lowpass(highpass(x, low), high)
    return x


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


# The rate the cannon loop is synthesised at. Playback pitch scales from here, so this is the one
# gun that comes out unshifted; everything else is this sample retuned.
CANNON_REFERENCE_RPM = 4500.0


def cannon(seed, rounds_per_minute=CANNON_REFERENCE_RPM, cycles=150,
           body_w=1.0, report_w=0.70, roar_w=0.20, thud_w=0.50,
           verb_hi=9000.0, top=9000.0):
    """A seamless loop of cyclic cannon fire.

    Length is `cycles` whole rounds rather than a round number of seconds, so the end of the file
    lands exactly one period after the last pulse and a restart continues the rhythm.

    ## Two things this got wrong before, both audible and both measurable

    **Nothing between 250 Hz and 2 kHz.** The first pass was a bright click over a low thump, which
    put 65% of its energy below 250 Hz and 6% across the three octaves where a gun actually lives.
    That reads as a boom and a fizz with a hole in the middle, and it sounded exactly like that.

    **Pure sine resonances ring.** The second pass filled the middle with four decaying sinusoids
    per round, at fixed frequencies, repeating 75 times a second. Coherent repetition of a fixed
    pitch is an organ pipe, not a mechanism: it read as hollow and high. Every layer here is
    noise driven through a band instead, so each round has body without having a *note*.

    The weights are fitted to a recording of real gunfire, octave band by octave band, because
    nobody in this loop can hear the result. Mean band error 1.6 points, against 5.8 for the
    ringing version and 8.7 for the first one. Fit them against *this whole function* if they are
    retuned: an earlier attempt fitted a simplified copy of the layers and landed on weights that
    were wrong for the real thing, since the reverb and the output filter move the balance as much
    as the layers do.
    """
    rng = np.random.default_rng(seed)

    period = 60.0 / rounds_per_minute
    n = int(round(cycles * period * RATE))
    out = np.zeros(n)

    body_n = int(0.030 * RATE)
    report_n = int(0.004 * RATE)

    for i in range(cycles):
        at = int(round(i * period * RATE))
        level = rng.uniform(0.80, 1.0)

        # The body of the round: a short noise burst through the low-mid, which is where the
        # reference has most of its energy and where a click has none.
        end = min(at + body_n, n)
        span = end - at
        burst = rng.normal(0.0, 1.0, span) * np.exp(-np.arange(span) / (0.008 * RATE))
        low = rng.uniform(150.0, 200.0)
        out[at:end] += bandpass(burst, low, low * 4.0, order=2) * level * body_w

        # The muzzle report over the top of it.
        end = min(at + report_n, n)
        span = end - at
        crackle_ = rng.normal(0.0, 1.0, span) * np.linspace(1.0, 0.0, span) ** 1.2
        out[at:end] += bandpass(crackle_, 700.0, 6000.0) * level * report_w

    # The gas roar under the pulses. Without it the loop is a rhythm with silence between beats,
    # which is a slow gun and this is not one.
    roar = bandpass(rng.normal(0.0, 1.0, n), 200.0, 3000.0, order=2)
    roar /= float(np.max(np.abs(roar))) or 1.0

    # The thump you feel. Two poles a side, because at one it does not survive its own filters.
    thud = bandpass(rng.normal(0.0, 1.0, n), 95.0, 240.0, order=2)
    thud /= float(np.max(np.abs(thud))) or 1.0

    mix = out + roar * roar_w + thud * thud_w

    # Cheap reverb, wrapped. Gunfire in the open still arrives with the ground and the vehicle
    # under it, and a dead-dry loop sounds like it is happening in a box of cotton wool. Convolved
    # CIRCULARLY so the tail of the last round lands on the first: a linear convolution would put
    # a silent ramp at the head of the file and the loop would pulse once a pass.
    ir_n = int(0.20 * RATE)
    ir = rng.normal(0.0, 1.0, ir_n) * np.exp(-np.arange(ir_n) / (0.045 * RATE))
    ir = bandpass(ir, 200.0, verb_hi)
    ir[0] += 3.0                                  # the direct sound, well above the reflections
    wet = np.fft.irfft(np.fft.rfft(mix) * np.fft.rfft(ir, n), n)
    mix = wet / (float(np.max(np.abs(wet))) or 1.0)

    # Out of the sub-bass, where 75 overlapping pulses a second pile into a drone, and off the
    # very top, where a synthesised report has far more energy than a real one and reads as static.
    mix = lowpass(highpass(mix, 70.0), top)

    peak = float(np.max(np.abs(mix))) or 1.0
    return np.tanh((mix / peak) * 2.4) * 0.94


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
    ap.add_argument("--synth-cannon", action="store_true",
                    help="also write the synthesised cannon loop, which the recording replaced")
    args = ap.parse_args()

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    # Three variants, picked between at random by the sound declaration. One sample repeated is
    # the thing that makes a burst of cannon fire sound synthetic.
    print("explosions:")
    for i in range(3):
        write(out / f"KSArmory_Burst{i:02d}.wav", explosion(seed=1000 + i))

    # Not written by default. The shipped cannon is cut from a recording by tools/cut-cannon.py,
    # because a synthesised one was auditioned three times and never stopped sounding synthetic --
    # measurement got its octave balance to within 1.6 points of real gunfire and it still read as
    # a texture rather than as a gun. This stays as the fallback for the case where the recording's
    # licence turns out not to permit redistribution; see tools/audio/README.md.
    if args.synth_cannon:
        print("cannon (synthesised fallback):")
        write(out / "KSArmory_Cannon.wav", cannon(seed=2000))


main()
