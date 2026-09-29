#!/usr/bin/env python3
"""
Cuts the M197's firing loop and tail out of its recordings.

    ./tools/m197-sounds.py             # tools/audio/m197/ -> src/KSArmory/Sounds/
    ./tools/m197-sounds.py --report    # print what would be written, write nothing

Mono, because the engine spatialises the source and a stereo file arrives already mixed.

The loop is cut where the recording comes back round to how it starts, found by correlating its
head against the last second, and the seam is crossfaded over a few milliseconds. The recording
alone clicks once a pass: its last sample has nothing to do with its first.
"""

import argparse
import pathlib
import sys
import wave

import numpy as np

REPO = pathlib.Path(__file__).resolve().parents[1]
SRC = REPO / "tools" / "audio" / "m197"
OUT = REPO / "src" / "KSArmory" / "Sounds"

HEAD_SECONDS = 0.25
SEARCH_SECONDS = 1.0
CROSSFADE_SECONDS = 0.01


def read_mono(path):
    with wave.open(str(path)) as w:
        channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        if width != 2:
            sys.exit(f"{path.name}: {width * 8}-bit, expected 16")
        raw = w.readframes(frames)
    return np.frombuffer(raw, dtype="<i2").reshape(-1, channels).astype(np.float64).mean(axis=1), rate


def write_mono(path, samples, rate):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(np.clip(np.round(samples), -32768, 32767).astype("<i2").tobytes())


def loop_cut(x, rate):
    """The loop's length in samples: where the tail best repeats the head."""
    head = x[:int(HEAD_SECONDS * rate)]
    head = (head - head.mean()) / (head.std() + 1e-9)
    n = len(head)
    best, best_score = len(x) - n, -2.0
    for end in range(len(x) - int(SEARCH_SECONDS * rate), len(x) - n):
        seg = x[end:end + n]
        score = float(np.dot(head, (seg - seg.mean()) / (seg.std() + 1e-9))) / n
        if score > best_score:
            best, best_score = end, score
    return best, best_score


def make_loop(x, rate):
    end, score = loop_cut(x, rate)
    fade = int(CROSSFADE_SECONDS * rate)
    loop = x[:end].copy()
    ramp = np.linspace(0.0, 1.0, fade)
    # What would have followed the cut is blended onto the head, so the end flows into the start.
    loop[:fade] = x[:fade] * ramp + x[end:end + fade] * (1.0 - ramp)
    return loop, score


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--report", action="store_true", help="print what would be written and write nothing")
    args = ap.parse_args()

    x, rate = read_mono(SRC / "M197_740rpm.wav")
    loop, score = make_loop(x, rate)
    tail, _ = read_mono(SRC / "M197_Tail.wav")

    plan = {"KSArmory_M197_Loop.wav": loop, "KSArmory_M197_Tail.wav": tail}
    print(f"  loop seam matches its head at r = {score:.3f}")
    for out, samples in plan.items():
        print(f"  {out:26} {len(samples) / rate:5.2f} s")
        if not args.report:
            write_mono(OUT / out, samples, rate)
    if not args.report:
        print(f"wrote {len(plan)} files into {OUT}")


if __name__ == "__main__":
    main()
