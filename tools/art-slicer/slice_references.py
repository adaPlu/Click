#!/usr/bin/env python3
"""Slice ClickDungeon reference images into placeholder sprites.

Art brief decision D4: until production art exists, crops of the concept references stand in for it.
Slices are written to Assets/ClickDungeon/Art/Runtime/Placeholders/; a production file with the same key
anywhere else under Art/Runtime automatically takes priority in the art catalog.

Usage (from the repo root):
  python tools/art-slicer/slice_references.py                    slice everything in slices.json
  python tools/art-slicer/slice_references.py --only tile_key    slice selected keys
  python tools/art-slicer/slice_references.py --detect ref4-tiles-b
                                                                 print bright boxes to calibrate rects
  python tools/art-slicer/slice_references.py --self-test        verify the tool on a synthetic sheet

Rects in slices.json are in the reference's declared pixel size; they scale if the file is resized.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import tempfile
from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
PROJECT = REPO / "ClickDungeon"
DEFAULT_MANIFEST = HERE / "slices.json"
DEFAULT_REFS = PROJECT / "Art" / "Source" / "References"
DEFAULT_OUT = PROJECT / "Assets" / "ClickDungeon" / "Art" / "Runtime" / "Placeholders"
DEFAULT_SHEET = PROJECT / "Art" / "Source" / "Slices" / "contact-sheet.png"
EXTENSIONS = (".png", ".jpg", ".jpeg", ".webp")
ACTOR_BOTTOM_PADDING = 32 / 256  # art brief: token pivot at bottom-centre (128, 32)


# ---------------------------------------------------------------------------------------------- geometry

def find_source(refs: Path, stem: str, sources: dict | None = None) -> Path | None:
    """Resolves a source id to a file: an explicit "file" in the manifest wins, else <stem>.<ext>."""
    explicit = (sources or {}).get(stem, {}).get("file")
    if explicit:
        candidate = refs / explicit
        return candidate if candidate.exists() else None
    for ext in EXTENSIONS:
        candidate = refs / f"{stem}{ext}"
        if candidate.exists():
            return candidate
    return None


def scale_rect(rect, image_size, reference_size):
    sx = image_size[0] / reference_size[0]
    sy = image_size[1] / reference_size[1]
    x, y, w, h = rect
    return (round(x * sx), round(y * sy), round(w * sx), round(h * sy))


def iou(a, b) -> float:
    ax, ay, aw, ah = a
    bx, by, bw, bh = b
    ix = max(0, min(ax + aw, bx + bw) - max(ax, bx))
    iy = max(0, min(ay + ah, by + bh) - max(ay, by))
    inter = ix * iy
    union = aw * ah + bw * bh - inter
    return inter / union if union else 0.0


def bright_boxes(image: Image.Image, threshold: int = 70, min_side: int = 60):
    """Bounding boxes (full-res x, y, w, h) of bright connected regions, e.g. stone tiles on a dark sheet."""
    factor = 2
    small = image.convert("L").resize((max(1, image.width // factor), max(1, image.height // factor)))
    w, h = small.size
    pixels = small.load()
    seen = bytearray(w * h)
    boxes = []
    for sy in range(h):
        for sx in range(w):
            if seen[sy * w + sx] or pixels[sx, sy] < threshold:
                continue
            seen[sy * w + sx] = 1
            queue = deque([(sx, sy)])
            x0 = x1 = sx
            y0 = y1 = sy
            while queue:
                x, y = queue.popleft()
                x0, x1, y0, y1 = min(x0, x), max(x1, x), min(y0, y), max(y1, y)
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx] and pixels[nx, ny] >= threshold:
                        seen[ny * w + nx] = 1
                        queue.append((nx, ny))
            box = (x0 * factor, y0 * factor, (x1 - x0 + 1) * factor, (y1 - y0 + 1) * factor)
            if box[2] >= min_side and box[3] >= min_side:
                boxes.append(box)
    return boxes


def snap(rect, boxes, minimum: float = 0.45):
    best = max(boxes, key=lambda b: iou(rect, b), default=None)
    return best if best is not None and iou(rect, best) >= minimum else rect


# ---------------------------------------------------------------------------------------------- pixels

def remove_background(image: Image.Image, tolerance: int, local_tolerance: int = 10) -> Image.Image:
    """Clears border-connected pixels that match the border colour (or smoothly continue it), then trims."""
    rgba = image.convert("RGBA")
    w, h = rgba.size
    px = rgba.load()
    border = [px[x, 0] for x in range(w)] + [px[x, h - 1] for x in range(w)]
    border += [px[0, y] for y in range(h)] + [px[w - 1, y] for y in range(h)]
    reference = tuple(sorted(c[i] for c in border)[len(border) // 2] for i in range(3))

    def distance(a, b):
        return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)

    seen = bytearray(w * h)
    queue = deque()

    def seed(x, y):
        if not seen[y * w + x] and distance(px[x, y], reference) <= tolerance:
            seen[y * w + x] = 1
            queue.append((x, y))

    for x in range(w):
        seed(x, 0)
        seed(x, h - 1)
    for y in range(h):
        seed(0, y)
        seed(w - 1, y)

    while queue:
        x, y = queue.popleft()
        colour = px[x, y]
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx]:
                candidate = px[nx, ny]
                if distance(candidate, reference) <= tolerance or distance(candidate, colour) <= local_tolerance:
                    seen[ny * w + nx] = 1
                    queue.append((nx, ny))
        px[x, y] = (colour[0], colour[1], colour[2], 0)

    bbox = rgba.getchannel("A").getbbox()
    return rgba.crop(bbox) if bbox else rgba


def keep_largest_component(image: Image.Image, alpha_threshold: int = 16, min_fraction: float = 1.0) -> Image.Image:
    """Clears opaque islands smaller than min_fraction of the largest one (stray specks and panel dividers beside a
    cut-out), then trims. The default keeps only the largest island."""
    rgba = image.convert("RGBA")
    w, h = rgba.size
    px = rgba.load()
    label = [0] * (w * h)
    sizes = [0]
    for y in range(h):
        for x in range(w):
            if label[y * w + x] or px[x, y][3] <= alpha_threshold:
                continue
            current = len(sizes)
            sizes.append(0)
            label[y * w + x] = current
            queue = deque([(x, y)])
            while queue:
                cx, cy = queue.popleft()
                sizes[current] += 1
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < w and 0 <= ny < h and not label[ny * w + nx] and px[nx, ny][3] > alpha_threshold:
                        label[ny * w + nx] = current
                        queue.append((nx, ny))
    if len(sizes) <= 2:
        return rgba
    largest = max(sizes[1:])
    keep = {i for i in range(1, len(sizes)) if sizes[i] >= largest * min_fraction}
    for y in range(h):
        for x in range(w):
            if label[y * w + x] and label[y * w + x] not in keep:
                r, g, b, _ = px[x, y]
                px[x, y] = (r, g, b, 0)
    bbox = rgba.getchannel("A").getbbox()
    return rgba.crop(bbox) if bbox else rgba


def cover(image: Image.Image, width: int, height: int) -> Image.Image:
    scale = max(width / image.width, height / image.height)
    resized = image.resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.LANCZOS)
    left = (resized.width - width) // 2
    top = (resized.height - height) // 2
    return resized.crop((left, top, left + width, top + height))


def contain(image: Image.Image, size: int, anchor: str) -> Image.Image:
    padding = round(size * ACTOR_BOTTOM_PADDING) if anchor == "bottom" else 0
    available = size - padding
    scale = min(size / image.width, available / image.height)
    resized = image.convert("RGBA").resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    x = (size - resized.width) // 2
    y = size - padding - resized.height if anchor == "bottom" else (size - resized.height) // 2
    canvas.paste(resized, (x, y), resized)
    return canvas


def hollow(image: Image.Image, spec: dict) -> Image.Image:
    """Clears the middle of a frame (a tile highlight keeps only its glowing border, so the tile shows through)."""
    if "hollow" not in spec:
        return image
    inset = round(min(image.size) * float(spec["hollow"]))
    if inset <= 0:
        return image
    px = image.load()
    for y in range(inset, image.height - inset):
        for x in range(inset, image.width - inset):
            r, g, b, _ = px[x, y]
            px[x, y] = (r, g, b, 0)
    return image


def frame(crop: Image.Image, spec: dict) -> tuple:
    """
    A 9-slice frame built from artwork that has text or a picture baked into it: the border is kept, the middle is
    replaced by the colour just inside it, so the middle can stretch to any size. Returns the image and its border in
    output pixels (left, bottom, right, top) for the sprite importer.
    """
    band = float(spec["band"])
    size = int(spec.get("size", 128))
    rgba = crop.convert("RGBA")
    scale = size / min(rgba.width, rgba.height)
    out = rgba.resize((max(1, round(rgba.width * scale)), max(1, round(rgba.height * scale))), Image.LANCZOS)
    border = max(2, round(band * scale))
    border = min(border, (min(out.size) - 2) // 2)
    px = out.load()
    # The fill is the median of the whole interior, so baked text or a picture cannot drag the colour off the panel's own.
    ring = []
    for y in range(border + 1, out.height - border, 2):
        ring += [px[x, y] for x in range(border + 1, out.width - border, 2)]
    # "clear" is for a frame that something else draws inside it (a portrait, an HP fill): its middle is see-through.
    # "fill" names the middle's colour outright, for a panel whose picture (a glowing chest) outweighs its own colour.
    fill = (0, 0, 0, 0) if spec.get("clear") else tuple(spec["fill"]) + (255,) if "fill" in spec else (
        tuple(sorted(c[i] for c in ring)[len(ring) // 2] for i in range(4)) if ring else (0, 0, 0, 255))
    for y in range(border, out.height - border):
        for x in range(border, out.width - border):
            px[x, y] = fill
    return out, border


def tint(image: Image.Image, spec: dict) -> Image.Image:
    """
    Recolours a cut-out so one crop can serve several colours (the three tile highlights, a sparkle that is gold for the
    key and green for a potion). "tint" multiplies the colours; with "colorize" the brightness is kept and the colour is
    replaced, which is the only way to turn a blue glow gold.
    """
    if "tint" not in spec:
        return image
    r, g, b = (float(v) for v in spec["tint"])
    colorize = bool(spec.get("colorize"))
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            cr, cg, cb, ca = px[x, y]
            if ca == 0:
                continue
            if colorize:
                lum = 0.299 * cr + 0.587 * cg + 0.114 * cb
                cr = cg = cb = lum
            px[x, y] = (min(255, round(cr * r)), min(255, round(cg * g)), min(255, round(cb * b)), ca)
    return image


def scene(crop: Image.Image, spec: dict) -> Image.Image:
    """
    A full screen of painted scenery with the reference's own interface removed: each "blank" rectangle is blurred until
    nothing of it can be read and darkened, which keeps the room's colour and light where a tiled patch would repeat
    visibly. The game draws its own HUD, panels and board over the result, so none of the reference's interface — its
    logo above all — may survive here.
    """
    out = crop.convert("RGBA")
    # "erase_color" first paints out one colour inside a rectangle (the purple "2" of the reference logo), so the blur
    # after it cannot leave that colour behind as a smudge.
    for erase in spec.get("erase_color", []):
        import cv2
        import numpy as np
        rgb = np.array(out)[..., :3]
        x, y, w, h = erase["rect"]
        region = rgb[y:y + h, x:x + w].astype(np.int32)
        distance = np.sqrt(((region - np.array(erase["color"])) ** 2).sum(axis=2))
        mask = np.zeros(rgb.shape[:2], np.uint8)
        grow = int(erase.get("grow", 6))
        mask[y:y + h, x:x + w] = cv2.dilate((distance < float(erase.get("tolerance", 90))).astype(np.uint8) * 255,
                                             np.ones((grow * 2 + 1, grow * 2 + 1), np.uint8))
        filled = cv2.inpaint(cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR), mask, 12, cv2.INPAINT_TELEA)
        out = Image.fromarray(np.dstack([cv2.cvtColor(filled, cv2.COLOR_BGR2RGB), np.array(out)[..., 3]]), "RGBA")
    radius = float(spec.get("blur", 30))
    dim = float(spec.get("dim", 0.7))
    for rect in spec.get("blank", []):
        x, y, w, h = rect
        box = (max(0, x), max(0, y), min(out.width, x + w), min(out.height, y + h))
        # Blur a margin around the rectangle as well, so its own pixels are smeared into the scenery around it.
        margin = round(radius * 2)
        wide = (max(0, box[0] - margin), max(0, box[1] - margin), min(out.width, box[2] + margin), min(out.height, box[3] + margin))
        blurred = out.crop(wide).filter(ImageFilter.GaussianBlur(radius)).point(lambda v: round(v * dim))
        region = blurred.crop((box[0] - wide[0], box[1] - wide[1], box[2] - wide[0], box[3] - wide[1]))
        # Feathered edges: a hard rectangle of blur would draw its own outline across the scenery.
        feather = round(float(spec.get("feather", 18)))
        mask = Image.new("L", region.size, 0)
        ImageDraw.Draw(mask).rectangle([feather, feather, region.width - feather, region.height - feather], fill=255)
        out.paste(region, (box[0], box[1]), mask.filter(ImageFilter.GaussianBlur(feather * 0.6)))
    size = int(spec.get("size", out.width))
    if size != out.width:
        out = out.resize((size, max(1, round(out.height * size / out.width))), Image.LANCZOS)
    return out


def mirror_fix(image: Image.Image, spec: dict) -> Image.Image:
    """
    Paints over part of a symmetric button with its mirror image: "mirror_fix" is [x, y, w, h] as fractions of the output.
    Used to lift a baked-in badge (the TALENTS button's red "!") off a frame whose other side is clean.
    """
    if "mirror_fix" not in spec:
        return image
    fx, fy, fw, fh = spec["mirror_fix"]
    w, h = image.size
    box = (round(fx * w), round(fy * h), round((fx + fw) * w), round((fy + fh) * h))
    mirrored = image.transpose(Image.FLIP_LEFT_RIGHT).crop(box)
    image.paste(mirrored, box[:2])
    return image


def clone(crop: Image.Image, spec: dict) -> Image.Image:
    """
    Paints over part of the crop with another part of it: "clone" is a list of [x, y, w, h, dx, dy] in the crop's own
    pixels, each box filled from the box shifted by (dx, dy). Lifts a baked-in badge (the padlock on the reference's
    staircase) off art that repeats beside it.
    """
    if "clone" not in spec:
        return crop
    source = crop.copy()
    for x, y, w, h, dx, dy in spec["clone"]:
        crop.paste(source.crop((x + dx, y + dy, x + dx + w, y + dy + h)), (x, y))
    return crop


def upscale(crop: Image.Image, spec: dict) -> Image.Image:
    """
    "upscale": a detail far smaller than its output is doubled step by step, each step smoothed along edges (bilateral)
    and unsharp-masked, then its edges are pushed toward their neighbours' light and dark (a mild shock filter), so it
    reads crisper than one Lanczos stretch with an unsharp mask on top. Needs OpenCV.
    """
    if not spec.get("upscale"):
        return crop
    import cv2
    import numpy as np
    size = spec.get("size", 256)
    a = np.array(crop.convert("RGB"))
    while a.shape[0] < size or a.shape[1] < size:
        a = cv2.resize(a, None, fx=2, fy=2, interpolation=cv2.INTER_LANCZOS4)
        a = cv2.bilateralFilter(a, 5, 30, 3)
        a = cv2.addWeighted(a, 1.8, cv2.GaussianBlur(a, (0, 0), 1.2), -0.8, 0)
    a = cv2.resize(a, (round(size * crop.width / max(crop.width, crop.height)), round(size * crop.height / max(crop.width, crop.height))),
                   interpolation=cv2.INTER_AREA).astype(np.float32)
    for _ in range(2):
        lap = cv2.Laplacian(cv2.cvtColor(cv2.GaussianBlur(a, (0, 0), 1.0), cv2.COLOR_RGB2GRAY), cv2.CV_32F)
        light, dark = cv2.dilate(a, np.ones((3, 3))), cv2.erode(a, np.ones((3, 3)))
        a = np.where((lap < 0)[..., None], a * 0.75 + light * 0.25, a * 0.75 + dark * 0.25)
    a = cv2.bilateralFilter(np.clip(a, 0, 255).astype(np.uint8), 5, 20, 3)
    return Image.fromarray(cv2.addWeighted(a, 1.5, cv2.GaussianBlur(a, (0, 0), 1.0), -0.5, 0))


def smear(image: Image.Image, spec: dict) -> Image.Image:
    """
    Paints out a baked-in badge on a plain background: "smear" is a list of [x, y, w, h] fractions of the output, and each
    row of a region takes the colour just left of it. Used where the badge has no clean mirror image (the nav bar's "!").
    """
    for fx, fy, fw, fh in spec.get("smear", []):
        w, h = image.size
        x0, y0 = max(1, round(fx * w)), round(fy * h)
        x1, y1 = min(w, round((fx + fw) * w)), min(h, round((fy + fh) * h))
        px = image.load()
        for y in range(y0, y1):
            left = px[x0 - 1, y]
            for x in range(x0, x1):
                px[x, y] = left
    return image


def inpaint(image: Image.Image, spec: dict) -> Image.Image:
    """
    Removes sample text baked into a plate so the game can print its own: "inpaint_text" is a list of [x, y, w, h]
    fractions of the output, and inside each only the text is rebuilt from the plate around it (pixels much brighter than
    the region's median, grown a little to take their outline). "inpaint_all" rebuilds whole rectangles. Needs OpenCV;
    row smearing ("smear") is the fallback that works without it but leaves bands on textured plates.
    """
    if "inpaint_text" not in spec and "inpaint_all" not in spec:
        return image
    import cv2
    import numpy as np
    rgba = np.array(image.convert("RGBA"))
    bgr = cv2.cvtColor(rgba[..., :3], cv2.COLOR_RGB2BGR)
    h, w = bgr.shape[:2]
    lum = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY).astype(np.int16)
    mask = np.zeros((h, w), np.uint8)
    grow = max(3, round(min(w, h) * 0.012))

    def box(rect):
        fx, fy, fw, fh = rect
        return max(0, round(fx * w)), max(0, round(fy * h)), min(w, round((fx + fw) * w)), min(h, round((fy + fh) * h))

    for rect in spec.get("inpaint_text", []):
        x0, y0, x1, y1 = box(rect)
        region = lum[y0:y1, x0:x1]
        contrast = float(spec.get("text_contrast", 45))
        # Light text on a dark plate by default; "text_dark" for dark lettering on parchment.
        text = ((region < np.median(region) - contrast) if spec.get("text_dark")
                else (region > np.median(region) + contrast)).astype(np.uint8) * 255
        # Baked text has a dark outline and shadow as wide as a fifth of its height: grow the mask over them.
        g = max(grow, round((y1 - y0) * float(spec.get("text_grow", 0.14))))
        mask[y0:y1, x0:x1] |= cv2.dilate(text, np.ones((g * 2 + 1, g * 2 + 1), np.uint8))[: y1 - y0, : x1 - x0]
    for rect in spec.get("inpaint_all", []):
        x0, y0, x1, y1 = box(rect)
        mask[y0:y1, x0:x1] = 255
    filled = cv2.inpaint(bgr, mask, grow * 2, cv2.INPAINT_TELEA)
    rgba[..., :3] = cv2.cvtColor(filled, cv2.COLOR_BGR2RGB)
    return Image.fromarray(rgba, "RGBA")


def circle(image: Image.Image, spec: dict) -> Image.Image:
    """
    "circle": the picture cut to a disc (a soft edge, transparent corners), for icons only ever shown in round nodes: the
    game then needs no runtime mask to clip them. "circle_inset" trims the disc inside the picture's own frame.
    """
    if not spec.get("circle"):
        return image
    w, h = image.size
    side = min(w, h)
    left, top = (w - side) // 2, (h - side) // 2
    square = image.crop((left, top, left + side, top + side)).convert("RGBA")
    inset = round(side * float(spec.get("circle_inset", 0.0)))
    if inset:
        square = square.crop((inset, inset, side - inset, side - inset)).resize((side, side), Image.LANCZOS)
    scale = 4
    mask = Image.new("L", (side * scale, side * scale), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, side * scale - 1, side * scale - 1], fill=255)
    mask = mask.resize((side, side), Image.LANCZOS)
    alpha = Image.eval(square.split()[3], lambda v: v)
    square.putalpha(Image.composite(alpha, Image.new("L", square.size, 0), mask))
    return square


def grabcut_background(image: Image.Image, core=None, iterations: int = 10, margin: int = 6) -> Image.Image:
    """
    Separates a figure from its backdrop with GrabCut, which models colour and edges together, then trims.

    For a dark figure on a dark backdrop, where remove_background's single colour threshold cannot tell armour from
    wall: any tolerance loose enough to clear the floor also eats the armour (Ironheart's dark steel sits within ~20
    colour units of the dungeon). "grabcut_core" is [x, y, w, h] as fractions of the crop, marked as certainly the
    figure — needed where a limb matches the floor it stands on and would otherwise be handed to the background.
    """
    import cv2
    import numpy as np

    rgb = np.array(image.convert("RGB"))
    bgr = cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)
    h, w = bgr.shape[:2]
    m = max(1, min(margin, w // 4, h // 4))
    mask = np.full((h, w), cv2.GC_BGD, np.uint8)
    mask[m:h - m, m:w - m] = cv2.GC_PR_FGD
    if core:
        fx, fy, fw, fh = core
        x0, y0 = int(fx * w), int(fy * h)
        mask[y0:y0 + max(1, int(fh * h)), x0:x0 + max(1, int(fw * w))] = cv2.GC_FGD
    bg_model = np.zeros((1, 65), np.float64)
    fg_model = np.zeros((1, 65), np.float64)
    cv2.grabCut(bgr, mask, None, bg_model, fg_model, iterations, cv2.GC_INIT_WITH_MASK)
    alpha = np.where((mask == cv2.GC_FGD) | (mask == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
    rgba = Image.fromarray(np.dstack([rgb, alpha]), "RGBA")
    bbox = rgba.getchannel("A").getbbox()
    return rgba.crop(bbox) if bbox else rgba


def render(crop: Image.Image, spec: dict) -> Image.Image:
    mode = spec.get("mode", "tile")
    size = int(spec.get("size", 256))
    if mode == "scene":
        return scene(crop, spec)
    if mode == "frame":
        return frame(crop, spec)[0]
    if mode in ("tile", "portrait"):
        return cover(crop.convert("RGBA"), size, size)
    if mode == "wide":
        scale = size / crop.width
        return crop.convert("RGBA").resize((size, max(1, round(crop.height * scale))), Image.LANCZOS)
    if mode == "logo":
        cleaned = remove_background(crop, int(spec.get("tolerance", 40)))
        scale = size / cleaned.width
        return cleaned.resize((size, max(1, round(cleaned.height * scale))), Image.LANCZOS)
    if mode == "sprite":
        if spec.get("grabcut"):
            cleaned = grabcut_background(crop, spec.get("grabcut_core"))
        else:
            cleaned = remove_background(crop, int(spec.get("tolerance", 40)), int(spec.get("local_tolerance", 10)))
        if "min_island" in spec:
            cleaned = keep_largest_component(cleaned, min_fraction=float(spec["min_island"]))
        return contain(cleaned, size, spec.get("anchor", "bottom"))
    if mode == "icon":
        source = remove_background(crop, int(spec["tolerance"])) if "tolerance" in spec else crop
        if spec.get("largest_only"):
            source = keep_largest_component(source)
        return hollow(contain(source, size, "center"), spec)
    raise ValueError(f"Unknown mode '{mode}' for {spec.get('key')}")


# ---------------------------------------------------------------------------------------------- run

def run(manifest: dict, refs: Path, out: Path, sheet_path: Path | None, only=None) -> dict:
    sources = manifest["sources"]
    images, boxes = {}, {}
    written, missing_sources, warnings, sheet_entries = [], set(), [], []
    borders = {}

    for spec in manifest["slices"]:
        key = spec["key"]
        if only and key not in only:
            continue
        stem = spec["source"]
        if stem not in images:
            path = find_source(refs, stem, sources)
            # "alpha": the sheet is already cut out on transparency (the clean item sheets); keep it.
            mode = "RGBA" if sources.get(stem, {}).get("alpha") else "RGB"
            images[stem] = Image.open(path).convert(mode) if path else None
            if path and stem in sources:
                ref_w, ref_h = sources[stem]["size"]
                image = images[stem]
                if abs(image.width / image.height - ref_w / ref_h) > 0.02:
                    warnings.append(f"{path.name} is {image.width}x{image.height}; slices assume a {ref_w}x{ref_h} layout.")
        image = images[stem]
        if image is None:
            missing_sources.add(stem)
            sheet_entries.append((key, None, spec.get("wired", False), f"missing {stem}"))
            continue

        reference_size = sources.get(stem, {}).get("size", [image.width, image.height])
        rect = scale_rect(spec["rect"], image.size, reference_size)
        if spec.get("snap"):
            if stem not in boxes:
                boxes[stem] = bright_boxes(image)
            rect = snap(rect, boxes[stem])
        x, y, w, h = rect
        crop = image.crop((max(0, x), max(0, y), min(image.width, x + w), min(image.height, y + h)))
        # "sharpen": a small reference detail scaled up a long way gets an unsharp mask so its edges hold.
        rendered = render(upscale(clone(crop, spec), spec), spec)
        if spec.get("sharpen"):
            rendered = rendered.convert("RGBA").filter(ImageFilter.UnsharpMask(2, 80, 2))
        result = circle(inpaint(smear(mirror_fix(tint(rendered.convert("RGBA"), spec), spec), spec), spec), spec)

        if spec.get("mode") == "frame":
            borders[key] = frame(crop, spec)[1]
        target = out / spec.get("folder", "Misc") / f"{key}.png"
        target.parent.mkdir(parents=True, exist_ok=True)
        result.save(target)
        written.append(target)
        sheet_entries.append((key, result, spec.get("wired", False), None))

    if sheet_path is not None and sheet_entries:
        write_contact_sheet(sheet_entries, sheet_path)
    write_borders(out, borders, only)
    return {"written": written, "missing_sources": sorted(missing_sources), "warnings": warnings}


def write_borders(out: Path, borders: dict, only) -> None:
    """
    Records each frame's 9-slice border for the Unity sprite importer (it cannot be stored in a PNG). Slicing a subset
    updates only those keys.
    """
    path = out / "borders.json"
    known = {}
    if path.exists():
        known = json.loads(path.read_text(encoding="utf-8")).get("borders", {})
    if only:
        known.update(borders)
    else:
        known = borders
    payload = {"comment": "key -> 9-slice border in pixels, written by slice_references.py",
               "borders": dict(sorted(known.items()))}
    path.write_text(json.dumps(payload, indent=2) + chr(10), encoding="utf-8")


def write_contact_sheet(entries, path: Path) -> None:
    columns, cell, label = 8, 180, 34
    rows = math.ceil(len(entries) / columns)
    sheet = Image.new("RGB", (columns * cell, rows * (cell + label)), (28, 28, 34))
    draw = ImageDraw.Draw(sheet)
    for index, (key, image, wired, problem) in enumerate(entries):
        cx, cy = (index % columns) * cell, (index // columns) * (cell + label)
        for ty in range(0, cell - 10, 12):
            for tx in range(0, cell - 10, 12):
                shade = (70, 70, 78) if (tx + ty) // 12 % 2 == 0 else (52, 52, 60)
                draw.rectangle([cx + 5 + tx, cy + 5 + ty, cx + 5 + min(tx + 11, cell - 11), cy + 5 + min(ty + 11, cell - 11)], fill=shade)
        if image is not None:
            thumb = image.copy()
            thumb.thumbnail((cell - 14, cell - 14))
            sheet.paste(thumb, (cx + (cell - thumb.width) // 2, cy + (cell - thumb.height) // 2), thumb)
        colour = (240, 90, 80) if problem else (150, 220, 150) if wired else (170, 170, 180)
        draw.text((cx + 6, cy + cell + 2), key[:30], fill=colour)
        draw.text((cx + 6, cy + cell + 16), problem or ("wired" if wired else "not wired (D1/future)"), fill=colour)
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def detect(manifest: dict, refs: Path, stem: str) -> int:
    path = find_source(refs, stem, manifest["sources"])
    if path is None:
        print(f"No reference named {stem} in {refs}")
        return 1
    image = Image.open(path).convert("RGB")
    ref_w, ref_h = manifest["sources"].get(stem, {}).get("size", [image.width, image.height])
    print(f"{path.name}: {image.width}x{image.height} (manifest layout {ref_w}x{ref_h})")
    for box in sorted(bright_boxes(image), key=lambda b: (b[1] // 40, b[0])):
        print("  rect in manifest coords:", list(scale_rect(box, (ref_w, ref_h), image.size)))
    return 0


def self_test() -> int:
    with tempfile.TemporaryDirectory() as temp:
        temp = Path(temp)
        refs = temp / "refs"
        refs.mkdir()
        sheet = Image.new("RGB", (400, 200), (20, 24, 40))
        draw = ImageDraw.Draw(sheet)
        draw.rectangle([20, 30, 139, 149], fill=(130, 130, 135))
        draw.ellipse([220, 40, 320, 150], fill=(200, 40, 40))
        sheet.save(refs / "synthetic.png")

        manifest = {
            "sources": {"synthetic": {"size": [800, 400]}},
            "slices": [
                {"key": "tile_test", "source": "synthetic", "rect": [30, 70, 250, 230], "mode": "tile", "size": 64, "folder": "Tiles", "snap": True, "wired": True},
                {"key": "actor_test_idle", "source": "synthetic", "rect": [420, 60, 240, 260], "mode": "sprite", "size": 64, "folder": "Actors", "tolerance": 30},
                {"key": "tile_missing", "source": "nope", "rect": [0, 0, 10, 10], "mode": "tile", "size": 64, "folder": "Tiles"},
            ],
        }
        out = temp / "out"
        result = run(manifest, refs, out, temp / "sheet.png")

        tile = Image.open(out / "Tiles" / "tile_test.png")
        assert tile.size == (64, 64), tile.size
        assert tile.getpixel((32, 32))[:3] == (130, 130, 135), tile.getpixel((32, 32))
        assert bright_boxes(sheet)[0][:2] in ((20, 30), (20, 28), (18, 30), (18, 28)), bright_boxes(sheet)

        actor = Image.open(out / "Actors" / "actor_test_idle.png")
        assert actor.size == (64, 64), actor.size
        assert actor.getpixel((0, 0))[3] == 0, "background should be transparent"
        bottom_red = actor.getpixel((32, 64 - round(64 * ACTOR_BOTTOM_PADDING) - 6))
        assert bottom_red[3] == 255 and bottom_red[0] > 150, bottom_red

        assert result["missing_sources"] == ["nope"], result["missing_sources"]
        assert len(result["written"]) == 2
        assert (temp / "sheet.png").exists()

        speck = Image.new("RGBA", (40, 40), (0, 0, 0, 0))
        ImageDraw.Draw(speck).ellipse([2, 2, 30, 30], fill=(255, 0, 0, 255))
        speck.putpixel((37, 37), (120, 0, 0, 255))
        cleaned = keep_largest_component(speck)
        assert cleaned.size == (29, 29), cleaned.size
    print("self-test passed")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--refs", type=Path, default=DEFAULT_REFS)
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--sheet", type=Path, default=DEFAULT_SHEET)
    parser.add_argument("--only", nargs="*")
    parser.add_argument("--detect", metavar="SOURCE")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    if args.detect:
        return detect(manifest, args.refs, args.detect)

    result = run(manifest, args.refs, args.out, args.sheet, set(args.only) if args.only else None)
    for warning in result["warnings"]:
        print("WARNING:", warning)
    if result["missing_sources"]:
        print(f"Missing references in {args.refs}: {', '.join(result['missing_sources'])}")
    print(f"Wrote {len(result['written'])} slices to {args.out}")
    if result["written"]:
        print(f"Contact sheet: {args.sheet}")
    return 0 if result["written"] else 1


if __name__ == "__main__":
    sys.exit(main())
