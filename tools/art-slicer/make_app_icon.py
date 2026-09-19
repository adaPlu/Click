"""
Builds the app icon from the ClickDungeon wordmark (logo_clickdungeon.png): "Click" stacked over "Dungeon" (with its gem),
centred on a dark dungeon-purple tile with a gold rim. Writes the full icon plus the two layers of an Android adaptive icon
(a plain background and a foreground kept inside the 66% safe zone that launchers never crop).

  python tools/art-slicer/make_app_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1] / "ClickDungeon"
LOGO = PROJECT / "Assets" / "ClickDungeon" / "Art" / "Runtime" / "Placeholders" / "UI" / "logo_clickdungeon.png"
OUT = PROJECT / "Assets" / "ClickDungeon" / "Art" / "AppIcon"
SIZE = 1024
SPLIT_X = 377  # the clear column between "Click" and "Dungeon"


def background(size: int) -> Image.Image:
    """A radial glow from dungeon purple to near-black."""
    bg = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(bg)
    centre = size / 2
    for r in range(int(size * 0.75), 0, -2):
        t = r / (size * 0.75)
        colour = (int(58 * (1 - t) + 16 * t), int(34 * (1 - t) + 12 * t), int(84 * (1 - t) + 22 * t), 255)
        draw.ellipse((centre - r, centre - r, centre + r, centre + r), fill=colour)
    return bg


def stacked_logo(width: int) -> Image.Image:
    """The wordmark cut at the gap and stacked: "Click" on top, "Dungeon" (with the gem) below, scaled to width."""
    logo = Image.open(LOGO).convert("RGBA")
    click = logo.crop((0, 0, SPLIT_X, logo.height))
    dungeon = logo.crop((SPLIT_X, 0, logo.width, logo.height))
    click, dungeon = click.crop(click.getbbox()), dungeon.crop(dungeon.getbbox())
    scale = width / dungeon.width
    dungeon = dungeon.resize((width, round(dungeon.height * scale)), Image.LANCZOS)
    click = click.resize((round(click.width * scale), round(click.height * scale)), Image.LANCZOS)
    gap = round(-0.06 * dungeon.height)
    out = Image.new("RGBA", (width, click.height + gap + dungeon.height), (0, 0, 0, 0))
    out.alpha_composite(click, ((width - click.width) // 2, 0))
    out.alpha_composite(dungeon, (0, click.height + gap))
    return out


def with_shadow(layer: Image.Image, size: int, width: int) -> Image.Image:
    """The stacked wordmark centred on a transparent square, with a soft dark shadow so it lifts off any background."""
    art = stacked_logo(width)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    at = ((size - art.width) // 2, (size - art.height) // 2)
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 200), (at[0], at[1] + size // 90), art)
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(size / 90)))
    canvas.alpha_composite(art, at)
    layer.alpha_composite(canvas)
    return layer


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    # Full icon (Windows, older Android launchers, stores): background, gold rim, wordmark filling most of the tile.
    icon = background(SIZE)
    rim = ImageDraw.Draw(icon)
    rim.rounded_rectangle((18, 18, SIZE - 18, SIZE - 18), radius=150, outline=(214, 160, 60, 255), width=16)
    rim.rounded_rectangle((40, 40, SIZE - 40, SIZE - 40), radius=130, outline=(90, 60, 24, 255), width=6)
    with_shadow(icon, SIZE, int(SIZE * 0.8))
    icon.save(OUT / "app_icon.png")

    # Adaptive icon layers (Android 8+): launchers crop to their own shape, so the wordmark stays in the middle 66%.
    background(SIZE).save(OUT / "app_icon_background.png")
    foreground = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    with_shadow(foreground, SIZE, int(SIZE * 0.6))
    foreground.save(OUT / "app_icon_foreground.png")
    print("Wrote", OUT)


if __name__ == "__main__":
    main()
