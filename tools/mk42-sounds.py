#!/usr/bin/env python3
"""
Cuts the Mk 42's gunshot out of its recording.

    ./tools/mk42-sounds.py             # tools/audio/mk42/ -> src/KSArmory/Sounds/
    ./tools/mk42-sounds.py --report    # print what would be written, write nothing

Mono, because the engine spatialises the source and a stereo file arrives already mixed.

One gunshot, played once per round. The shell's burst has no sound of its own.
"""

import argparse
import pathlib
import struct
import sys
import wave

REPO = pathlib.Path(__file__).resolve().parents[1]
SRC = REPO / "tools" / "audio" / "mk42"
OUT = REPO / "src" / "KSArmory" / "Sounds"

PLAN = {
    "KSArmory_Mk42_Gunshot.wav": "Mk42_Gunshot.wav",
}


def read_mono(path):
    with wave.open(str(path)) as w:
        channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        if width != 2:
            sys.exit(f"{path.name}: {width * 8}-bit, expected 16")
        raw = w.readframes(frames)
    samples = struct.unpack(f"<{frames * channels}h", raw)
    if channels == 1:
        return list(samples), rate
    return [sum(samples[i:i + channels]) // channels for i in range(0, len(samples), channels)], rate


def write_mono(path, samples, rate):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(struct.pack(f"<{len(samples)}h", *samples))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--report", action="store_true", help="print what would be written and write nothing")
    args = ap.parse_args()

    for out, source in PLAN.items():
        samples, rate = read_mono(SRC / source)
        print(f"  {out:28} from {source:18} {len(samples) / rate:5.2f} s")
        if not args.report:
            write_mono(OUT / out, samples, rate)
    if not args.report:
        print(f"wrote {len(PLAN)} files into {OUT}")


if __name__ == "__main__":
    main()
