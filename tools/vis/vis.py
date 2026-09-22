"""The pictures the bridge brings back, turned into what can be judged.

Crops come from the capture's manifest -- where the burst and the cap fall on screen, projected by
the camera that took the picture -- so nothing is cropped by a guessed coordinate. Diffs and the
temporal map are what a still cannot show on its own: what one change did, and what moves between
frames with nothing in the world moving.
"""

from __future__ import annotations

import base64
import io
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


def load(path: str | Path) -> Image.Image:
    return Image.open(path).convert("RGB")


def manifest_for(png: str | Path) -> dict:
    side = Path(png).with_suffix(".json")
    return json.loads(side.read_text()) if side.exists() else {}


def burst_box(manifest: dict, size: tuple[int, int], margin: float = 0.35) -> tuple[int, int, int, int] | None:
    """The box round the cloud as it stands, widened by a margin."""
    w, h = size
    if box := manifest.get("cloud_box"):
        x0, y0, x1, y1 = box[0] * w, box[1] * h, box[2] * w, box[3] * h
        mx, my = (x1 - x0) * margin * 0.5, (y1 - y0) * margin * 0.5
        out = (int(x0 - mx), int(y0 - my), int(x1 + mx), int(y1 + my * 0.5))
        return (max(out[0], 0), max(out[1], 0), min(out[2], w), min(out[3], h))

    points = [manifest.get(k) for k in ("burst_screen", "cap_screen", "top_screen")]
    points = [p for p in points if p]
    if not points:
        return None

    xs = [p[0] * w for p in points]
    ys = [p[1] * h for p in points]
    tall = max(max(ys) - min(ys), 0.08 * h)
    half = max(tall * (0.5 + margin), 0.08 * w)
    cx = sum(xs) / len(xs)
    top = min(ys) - tall * margin
    bottom = max(ys) + tall * margin * 0.5

    box = (int(cx - half), int(top), int(cx + half), int(bottom))
    return (max(box[0], 0), max(box[1], 0), min(box[2], w), min(box[3], h))


def crop(img: Image.Image, manifest: dict) -> Image.Image:
    box = burst_box(manifest, img.size)
    return img.crop(box) if box and box[2] > box[0] and box[3] > box[1] else img


def fit(img: Image.Image, width: int) -> Image.Image:
    if img.width <= width:
        return img
    return img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)


def jpeg_b64(img: Image.Image, width: int = 960, quality: int = 85) -> str:
    buf = io.BytesIO()
    fit(img, width).save(buf, "JPEG", quality=quality)
    return base64.b64encode(buf.getvalue()).decode("ascii")


def diff(a: Image.Image, b: Image.Image, gain: float = 4.0) -> tuple[Image.Image, dict]:
    """What changed between two pictures of the same instant, amplified, and how much."""
    x = np.asarray(a, dtype=np.int16)
    y = np.asarray(b, dtype=np.int16)
    d = np.abs(x - y).max(axis=2)
    stats = {
        "mean": round(float(d.mean()), 3),
        "px_over_8": int((d > 8).sum()),
        "px_over_24": int((d > 24).sum()),
        "max": int(d.max()),
    }
    return Image.fromarray(np.clip(d * gain, 0, 255).astype(np.uint8)).convert("RGB"), stats


def temporal(images: list[Image.Image], gain: float = 8.0) -> tuple[Image.Image, dict]:
    """How much each pixel moves across a series. With the world paused, anything bright here is
    the renderer's own noise: flicker, crawl, a history that will not settle."""
    stack = np.stack([np.asarray(i.convert("L"), dtype=np.float32) for i in images])
    sd = stack.std(axis=0)
    stats = {
        "frames": len(images),
        "mean_sd": round(float(sd.mean()), 3),
        "p99_sd": round(float(np.percentile(sd, 99)), 3),
        "px_sd_over_4": int((sd > 4).sum()),
    }
    return Image.fromarray(np.clip(sd * gain, 0, 255).astype(np.uint8)).convert("RGB"), stats


def grain(img: Image.Image, box: tuple[int, int, int, int] | None = None) -> float:
    """High-pass energy: the speckle a dither leaves, which a smooth cloud has little of."""
    g = np.asarray((img.crop(box) if box else img).convert("L"), dtype=np.float32)
    if g.shape[0] < 3 or g.shape[1] < 3:
        return 0.0
    blur = (g[:-2, 1:-1] + g[2:, 1:-1] + g[1:-1, :-2] + g[1:-1, 2:] + g[1:-1, 1:-1]) / 5.0
    return round(float(np.abs(g[1:-1, 1:-1] - blur).mean()), 3)


def sheet(images: list[Image.Image], labels: list[str] | None = None, cols: int = 2,
          width: int = 720) -> Image.Image:
    """Pictures side by side, labelled, for a person or an agent to read at a glance."""
    cells = [fit(i, width) for i in images]
    cw = max(c.width for c in cells)
    ch = max(c.height for c in cells)
    rows = (len(cells) + cols - 1) // cols
    out = Image.new("RGB", (cw * cols, ch * rows), (20, 20, 20))
    draw = ImageDraw.Draw(out)
    for k, cell in enumerate(cells):
        x, y = (k % cols) * cw, (k // cols) * ch
        out.paste(cell, (x, y))
        if labels and k < len(labels):
            draw.rectangle((x, y, x + 8 + 7 * len(labels[k]), y + 16), fill=(0, 0, 0))
            draw.text((x + 4, y + 2), labels[k], fill=(255, 255, 0))
    return out


def gif(images: list[Image.Image], path: str | Path, width: int = 720, ms: int = 100) -> Path:
    """An animation of a series, for judging motion the way a sheet cannot."""
    frames = [fit(i, width) for i in images]
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=ms, loop=0)
    return Path(path)
