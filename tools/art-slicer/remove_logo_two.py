"""
Writes copies of the reference title and gameplay screens with the "2" of their "ClickDungeon2" logos removed (D-033:
the brand reads "ClickDungeon"), so the backgrounds can keep the reference logos themselves instead of blurring them.

Each "2" is found by colour (its purple face, then its gold rim, highlight and dark outline around it); the "n" beside
it is kept, outline included. What was behind the "2" is rebuilt from the clean wall just to its right, reflected back
and forth so no seam shows, and blended at the edges.

    python tools/art-slicer/remove_logo_two.py
"""
from pathlib import Path

import cv2
import numpy as np

REFS = Path(__file__).resolve().parents[2] / "ClickDungeon" / "Art" / "Source" / "References"

# Per reference: the area around the "2" (x0, y0, x1, y1), where the "n" ends, and the clean wall beside the "2" (first
# column, width), all in the 1672 x 941 reference. "size" is the logo's scale: the gameplay logo is half the title's.
JOBS = [
    {"source": "title.png", "target": "title-no2.png", "area": (1100, 20, 1240, 205), "n_end": 1128,
     "donor": (1216, 32), "size": 1.0},
    {"source": "main.png", "target": "main-no2.png", "area": (400, 4, 478, 92), "n_end": 416,
     "donor": (464, 26), "size": 0.5},
]


def square(n: float) -> np.ndarray:
    side = max(1, round(n))
    side += 0 if side % 2 else 1
    return np.ones((side, side), np.uint8)


def remove(job: dict) -> None:
    image = cv2.imread(str(REFS / job["source"]))
    x0, y0, x1, y1 = job["area"]
    scale = job["size"]
    roi = image[y0:y1, x0:x1].astype(np.int16)
    b, g, r = roi[..., 0], roi[..., 1], roi[..., 2]
    lum = 0.3 * r + 0.59 * g + 0.11 * b

    purple = ((b > r - 10) & (b > g + 40) & (b > 90)).astype(np.uint8) * 255
    purple = cv2.morphologyEx(purple, cv2.MORPH_CLOSE, square(9 * scale))
    near = cv2.dilate(purple, square(15 * scale)) > 0

    # The "n": the large orange shapes left of the "2", with a ring of their own outline.
    orange = ((r > 110) & (r > b + 40)).astype(np.uint8)
    count, labels, stats, centroids = cv2.connectedComponentsWithStats(orange)
    letter = np.zeros_like(orange)
    for i in range(1, count):
        if stats[i, cv2.CC_STAT_AREA] > 300 * scale * scale and centroids[i][0] + x0 < job["n_end"]:
            letter[labels == i] = 1
    keep = (cv2.dilate(letter, square(9 * scale)) > 0) & ~(cv2.dilate(purple, square(5 * scale)) > 0)

    # Face, gold rim and its highlight, and the dark outline and shading around it.
    two = (purple > 0) | (near & ((lum < 70) | (lum > 140) | (orange > 0)))
    two = cv2.morphologyEx(two.astype(np.uint8), cv2.MORPH_CLOSE, square(7 * scale))
    two = cv2.dilate(two, square(5 * scale)) > 0
    two &= ~keep

    # Rebuild from the clean strip of wall to the right, reflected back and forth so no seam shows, a little darker so
    # it sits in the shadow the logo casts.
    donor_x, donor_w = job["donor"]
    filled = image.copy()
    ys, xs = np.nonzero(two)
    for y, x in zip(ys + y0, xs + x0):
        offset = (donor_x - x) % (2 * donor_w)
        source_x = donor_x + (offset if offset < donor_w else 2 * donor_w - offset - 1)
        filled[y, x] = (image[y, source_x] * 0.96).astype(np.uint8)

    # Feather the edge so the rebuilt wall runs into the real one.
    mask = np.zeros(image.shape[:2], np.float32)
    mask[y0:y1, x0:x1] = two
    soft = np.maximum(cv2.GaussianBlur(mask, (0, 0), 2.5 * scale), mask)
    out = (filled * soft[..., None] + image * (1 - soft[..., None])).astype(np.uint8)
    cv2.imwrite(str(REFS / job["target"]), out)
    print(f"Wrote {job['target']} ({int(two.sum())} pixels rebuilt)")


if __name__ == "__main__":
    for job in JOBS:
        remove(job)
