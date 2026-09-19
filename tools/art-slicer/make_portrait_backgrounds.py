"""
Portrait (9:16) backgrounds from the landscape scene art, for phones held upright.

The landscape backgrounds carry their HUD painted in, so portrait takes only the scene between the HUD rows: the gameplay
room's stone board frame with its torches, and the title's knight at the gate. Each crop is laid sharp across the middle
of a 1080 x 1920 canvas over a blurred, darkened, enlarged copy of itself that fills the rest.

The gameplay crop is placed so the board frame's inner edge lands where the portrait game screen puts its board
(GameScreen.PortraitBoardCenter / PortraitBoardInterior): keep those in step with the numbers here.

  python tools/art-slicer/make_portrait_backgrounds.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

HERE = Path(__file__).resolve().parent
UI = HERE.parents[1] / "ClickDungeon" / "Assets" / "ClickDungeon" / "Art" / "Runtime" / "Placeholders" / "UI"
W, H = 1080, 1920

# Gameplay: the crop (in the 1672 x 941 art), its scale, and where the board frame's interior centre must land.
GAME_CROP = (380, 112, 1300, 682)
GAME_SCALE = 1.2
FRAME_INTERIOR = (468, 150, 744, 510)  # the blanked board interior in bg_gameplay (slices.json)
BOARD_CENTRE_FROM_TOP = 612

# Title: the knight, the gate and the torches between the painted card, panels and button row.
TITLE_CROP = (470, 175, 1210, 760)
TITLE_TOP = 430


def ambience(scene: Image.Image) -> Image.Image:
    """The crop enlarged to cover the whole canvas, blurred and darkened: the room's colour without its detail."""
    scale = max(W / scene.width, H / scene.height)
    big = scene.resize((round(scene.width * scale), round(scene.height * scale)), Image.LANCZOS)
    left, top = (big.width - W) // 2, (big.height - H) // 2
    big = big.crop((left, top, left + W, top + H)).filter(ImageFilter.GaussianBlur(28))
    return ImageEnhance.Brightness(big).enhance(0.42)


def feathered(image: Image.Image, fade: int) -> Image.Image:
    """Fades an image's top and bottom edges to transparent over `fade` pixels."""
    mask = Image.new("L", image.size, 255)
    draw = ImageDraw.Draw(mask)
    for i in range(fade):
        alpha = round(255 * i / fade)
        draw.line([(0, i), (image.width, i)], fill=alpha)
        draw.line([(0, image.height - 1 - i), (image.width, image.height - 1 - i)], fill=alpha)
    out = image.copy()
    out.putalpha(mask)
    return out


def compose(source: str, crop: tuple, scale: float, at: tuple, target: str) -> None:
    art = Image.open(UI / source).convert("RGBA")
    scene = art.crop(crop)
    canvas = ambience(scene).convert("RGBA")
    sharp = scene.resize((round(scene.width * scale), round(scene.height * scale)), Image.LANCZOS)
    canvas.alpha_composite(feathered(sharp, 60), (round(at[0]), round(at[1])))
    canvas.convert("RGB").save(UI / target)
    print("Wrote", UI / target)


def main() -> None:
    # Where the frame interior's centre falls inside the scaled crop, and so where the crop's corner goes.
    fx, fy, fw, fh = FRAME_INTERIOR
    centre_x = (fx + fw / 2 - GAME_CROP[0]) * GAME_SCALE
    centre_y = (fy + fh / 2 - GAME_CROP[1]) * GAME_SCALE
    compose("bg_gameplay.png", GAME_CROP, GAME_SCALE, (W / 2 - centre_x, BOARD_CENTRE_FROM_TOP - centre_y), "bg_gameplay_portrait.png")
    print(f"Board interior on the portrait stage: {fw * GAME_SCALE:.0f} x {fh * GAME_SCALE:.0f}, centre {BOARD_CENTRE_FROM_TOP} from the top")

    title_scale = W * 1.02 / (TITLE_CROP[2] - TITLE_CROP[0])
    compose("bg_title.png", TITLE_CROP, title_scale, (W / 2 - (TITLE_CROP[2] - TITLE_CROP[0]) * title_scale / 2, TITLE_TOP),
            "bg_title_portrait.png")


if __name__ == "__main__":
    main()
