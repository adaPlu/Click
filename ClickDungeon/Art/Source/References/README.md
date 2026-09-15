# Reference images

Save the reference images here with these exact names (PNG, JPG or WebP):

| File name | Image |
|---|---|
| `ref1-title` | 1. Title screen |
| `ref2-gameplay` | 2. Main gameplay window |
| `ref3-tiles-a` | 3. Dungeon tile set (top-down), "ClickDungeon PRODUCTION ASSETS" |
| `ref4-tiles-b` | 4. Core dungeon tiles, the sheet with cracked floor, water, lava… |
| `ref5-implementation-plan` | 5. 5×5 gameplay implementation plan |
| `ref6-vertical-slice` | 6. Vertical slice: expressions, Lord Blobert, reward animation sequence |
| `ref-monsters` | Supporting: monster roster (crowned slime, fire imp) |
| `ref-items` | Supporting: items and equipment library (potion) |

Then slice them into placeholder sprites (art brief decision D4):

```bash
python tools/art-slicer/slice_references.py
```

Slices go to `ClickDungeon/Assets/ClickDungeon/Art/Runtime/Placeholders/`, and a review sheet to
`ClickDungeon/Art/Source/Slices/contact-sheet.png`. Crop rectangles live in `tools/art-slicer/slices.json`.
