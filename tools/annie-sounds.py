#!/usr/bin/env python3
"""
Cuts a nuclear burst's two sounds out of the Upshot-Knothole ANNIE film's soundtrack.

    ./tools/annie-sounds.py             # tools/audio/annie/ -> src/KSArmory/Sounds/
    ./tools/annie-sounds.py --report    # print what would be written, write nothing

ANNIE was 16 kt, filmed from about 10.7 km: the recording equipment picks the burst up at the
instant of the flash, right after the countdown's "zero", and the bang arrives 31.3 s later.

  KSArmory_Nuclear_Pickup.wav  the equipment reacting to the burst, without the countdown
  KSArmory_Nuclear_Bang.wav    the bang, without the scream that overlaps its tail

The film's hiss and its 96 Hz hum are taken out of both by spectral subtraction, against a profile
of the background between them. Mono, because the bang is spatialised. The pickup's buzz is a
harmonic series on 24 Hz, the film's frame rate.
"""

import argparse
import pathlib

import numpy as np
import soundfile as sf
from scipy import ndimage, signal

REPO = pathlib.Path(__file__).resolve().parents[1]
SRC = REPO / "tools" / "audio" / "annie" / "Upshot-Knothole_ANNIE_1953.flac"
OUT = REPO / "src" / "KSArmory" / "Sounds"

# Seconds into the recording. The pickup starts at 2.27 s, where the hiss above 1 kHz drops and the
# buzz comes in; "zero" ends at 2.1 s.
PICKUP = (2.24, 5.2)
PICKUP_FADE_OUT = (4.0, 5.2)

# The buzz is a harmonic series on 24.035 Hz, measured over 2.4-4.4 s. It stands 20 dB over the noise
# between its harmonics to 3.75 s and sinks into it by 4.0 s, and that noise is heard as wind; so from
# COMB_FROM the clip keeps only the harmonics, fully by COMB_FULL, and nothing above COMB_TOP_HZ.
BUZZ_HZ = 24.035
COMB_FROM = 3.4
COMB_FULL = 3.8
COMB_WIDTH_HZ = 3.0
COMB_TOP_HZ = 2000.0
COMB_FLOOR = 0.08
COMB_NPERSEG = 8192

# Where the background is nothing but hiss and hum, after the burst changed the recording's floor.
NOISE = (5.0, 8.0)

# The scream starts at about 34.95 s, at 1.9 and 2.3 kHz over a rumble then mostly under 1 kHz;
# from just before it only the rumble is kept, eased in so the bang does not go dull in a frame. The
# recording ends mid-rumble, so it fades out.
SCREAM_FROM = 34.85
SCREAM_EASE = 0.3
SCREAM_LOWPASS_HZ = 900.0
BANG_FADE_OUT = 0.8

# The film's own hum, steady from before the burst, and its first harmonic: notched out of the bang,
# where the subtraction leaves a line standing over the hiss it takes from beside it. Not out of the
# pickup, which is a harmonic series on 24 Hz with these as its 4th and 8th.
HUM_HZ = (96.0, 192.0)
HUM_Q = 30.0

NPERSEG = 2048
OVERSUBTRACT = 1.5
FLOOR = 0.1
GAIN_SMOOTH_BINS = 5
GAIN_SMOOTH_FRAMES = 9


def denoise(x, rate, noise):
    _, _, n = signal.stft(noise, rate, nperseg=NPERSEG)
    profile = (np.abs(n) ** 2).mean(axis=1, keepdims=True)

    _, _, s = signal.stft(x, rate, nperseg=NPERSEG)
    power = np.abs(s) ** 2
    gain = np.maximum(1.0 - OVERSUBTRACT * profile / np.maximum(power, 1e-20), FLOOR ** 2) ** 0.5
    # Smoothed across neighbouring bins and frames: a gain decided per bin lets isolated bins of hiss
    # through, which is heard as a bubbling "musical noise" in place of the hiss.
    gain = ndimage.uniform_filter(gain, size=(GAIN_SMOOTH_BINS, GAIN_SMOOTH_FRAMES))
    _, y = signal.istft(s * gain, rate, nperseg=NPERSEG)
    return y[:len(x)]


def dehum(x, rate):
    for hz in HUM_HZ:
        b, a = signal.iirnotch(hz, HUM_Q, fs=rate)
        x = signal.filtfilt(b, a, x)
    return x


def comb(x, rate, start):
    f, t, s = signal.stft(x, rate, nperseg=COMB_NPERSEG)
    off = np.abs(f - BUZZ_HZ * np.round(f / BUZZ_HZ))
    keep = np.exp(-0.5 * (off / COMB_WIDTH_HZ) ** 2) * (f <= COMB_TOP_HZ)
    keep = COMB_FLOOR + (1.0 - COMB_FLOOR) * keep
    when = np.clip((t + start - COMB_FROM) / (COMB_FULL - COMB_FROM), 0.0, 1.0)
    gain = 1.0 - when[None, :] * (1.0 - keep[:, None])
    _, y = signal.istft(s * gain, rate, nperseg=COMB_NPERSEG)
    return y[:len(x)]


def ramp(n):
    return 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, n))


def normalise(x, peak_db=-1.0):
    return x * (10 ** (peak_db / 20.0) / np.abs(x).max())


def cut_pickup(m, rate, noise):
    x = comb(denoise(m[int(PICKUP[0] * rate):int(PICKUP[1] * rate)], rate, noise), rate, PICKUP[0])
    x[:int(0.01 * rate)] *= ramp(int(0.01 * rate))
    a, b = (int((t - PICKUP[0]) * rate) for t in PICKUP_FADE_OUT)
    x[a:b] *= ramp(b - a)[::-1]
    return normalise(x[:b])


def cut_bang(m, rate, noise):
    window = m[int(33.0 * rate):]
    onset = 33.0 + np.argmax(np.abs(window) > 0.25 * np.abs(window).max()) / rate
    start = onset - 0.02

    x = dehum(denoise(m[int(start * rate):], rate, noise), rate)

    low = signal.sosfiltfilt(signal.butter(6, SCREAM_LOWPASS_HZ, fs=rate, output="sos"), x)
    cross = np.clip((np.arange(len(x)) / rate - (SCREAM_FROM - SCREAM_EASE - start)) / SCREAM_EASE, 0.0, 1.0)
    x = x * (1.0 - cross) + low * cross

    x[:int(0.005 * rate)] *= ramp(int(0.005 * rate))
    x[-int(BANG_FADE_OUT * rate):] *= ramp(int(BANG_FADE_OUT * rate))[::-1]
    return normalise(x), onset


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--report", action="store_true", help="print what would be written and write nothing")
    args = ap.parse_args()

    data, rate = sf.read(str(SRC), always_2d=True)
    m = data.mean(axis=1)
    noise = m[int(NOISE[0] * rate):int(NOISE[1] * rate)]

    pickup = cut_pickup(m, rate, noise)
    bang, onset = cut_bang(m, rate, noise)

    for name, x, note in (("KSArmory_Nuclear_Pickup.wav", pickup, f"from {PICKUP[0]:.2f} s"),
                          ("KSArmory_Nuclear_Bang.wav", bang, f"from the bang at {onset:.3f} s")):
        print(f"  {name:30} {len(x) / rate:5.2f} s  {note}")
        if not args.report:
            sf.write(str(OUT / name), x.astype(np.float32), rate, subtype="PCM_16")


if __name__ == "__main__":
    main()
