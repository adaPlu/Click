# CLICKDUNGEON — ART PREPARATION BRIEF
## Goal: match the reference screens exactly, with game-ready assets

You are the art director and technical artist for ClickDungeon, a landscape, turn-based 5×5 dungeon
crawler built in Unity 6 (uGUI, 1920×1080 reference resolution).

Your job: produce clean, isolated, production-ready 2D art so the title screen and the gameplay
screen match the reference images in layout, style and polish. Every gameplay state the game already
supports must stay readable. You are preparing and integrating art. Do not change gameplay rules.

---

## 1. Reference images (in this order)

1. Title screen: layout and menu reference.
2. Main gameplay window: HUD, board frame and ability bar reference.
3. Dungeon tile set (top-down), set A: core 5×5 tiles.
4. Core dungeon tiles, set B: the core tiles again plus additional and future tiles.
5. 5×5 gameplay implementation plan: visual rules, chest-opening flow, definition of done.
6. Vertical slice sheet: Sir Clickington expressions, Lord Blobert states, reward animation sequence.

Supporting references (style and animation states only): hero roster, monster roster, items and
equipment library.

The references are concept composites. Do NOT copy into assets: sheet labels, captions,
"ClickDungeon2" branding, baked-in numbers or text, inconsistent scale, or perspective mismatches.

---

## 2. Non-negotiables

- Brand text is "ClickDungeon". Never "2".
- Landscape only. Reference 1920×1080. Must also work at 19.5:9 phones and 4:3 tablets: keep critical
  content inside the central 1920×1080 frame, and let backgrounds bleed to at least 2340×1440.
- The board is 5×5. Reference 2 shows 5×4; extend to 5 rows at the same tile scale.
- Board readability beats scenery. Tiles, tokens and telegraphs must read at phone size
  (a tile is 136 px at 1080p and about 60 px on a small phone). Keep decorative props low-contrast
  and never place them on the board.
- Never communicate by colour alone: every state needs a shape, icon or motion.
- Gameplay text stays live text (HP, numbers, names, floor names, button labels, logs, reward
  amounts). Only the logo is baked into an image.
- One lighting model: warm torchlight key from the upper left, cool shadow fill. Consistent outline
  weight across all sprites.
- Palette anchors (match the existing UI): navy #141B33 / #22305A, gold #E8B84A / #8F6320,
  parchment #D9C08F, ink #3B2A14, stone #3A3840 / #24222A, HP red #D23A2E, play green #3E9B35,
  quit red #9B2A22.

---

## 3. Technical specs

- PNG, sRGB, transparent unless full-bleed. Keep layered sources (PSD, Krita or Aseprite) in
  `ClickDungeon/Art/Source/` (outside Assets).
- Author at 2× display size:

| Asset type | Source size | Notes |
|---|---|---|
| Board tile | 256×256 | content inside a 240×240 safe area; edges sit cleanly next to any neighbour |
| Actor token frame (hero, enemies) | 256×256 | pivot bottom-centre (128, 32); must fit one tile |
| Lord Blobert frame | 384×384 | may overhang its tile up to 1.4× |
| Portrait | 256×256 | per expression |
| Icons (clues, intents, badges) | 128×128 | readable at 32 px |
| Ability icons | 192×192 | |
| Panels, buttons, bars, board frame | 9-slice | document border sizes per file |
| Backgrounds | 3840×2160 | layered: wall, arch, banners, props as separate files |
| Logo | 2048×512 | transparent |
| Animations | uniform frame size | frames named `_000`, `_001`… with fps listed |

- Naming: lowercase snake case with prefixes `tile_`, `actor_<id>_<anim>_<frame>`, `portrait_`,
  `icon_`, `ui_`, `fx_`, `bg_`, `logo_`.
  Content ids must match the game: `sir_clickington`, `goblin`, `crowned_slime`, `fire_imp`,
  `slimelet`, `lord_blobert`.
- Runtime location: `ClickDungeon/Assets/ClickDungeon/Art/Runtime/{Tiles,Actors,Portraits,Icons,UI,FX,Backgrounds}/`
- Unity import: Sprite (2D and UI), 100 pixels per unit, bilinear filtering, no mipmaps for UI,
  high-quality compression, one sprite atlas per folder.
- Provide an asset manifest (`Art/manifest.csv`): file, size, pivot, 9-slice borders, source
  reference image, priority, status.

---

## 4. Deliverable A: Title screen (reference 1)

Recreate the layout exactly. Items marked * depend on Decision D1.

- Background: stone dungeon wall, central arch with barred window, 4 animated torch flames,
  side banners ("SMALL CLICKS BIG ADVENTURES", "DUNGEONS MAKE BETTER HEROES"; banner art only,
  text live).
- Top-left hero card: frame, portrait, name and tagline (live), level badge*, gold counter*,
  gem counter*, "+" buttons*.
- Logo "ClickDungeon" with the crown gem; tagline plank "EXPLORE. SURVIVE. LOOT. REPEAT."
  (plank art; text live).
- Top-right icon buttons: crown*, mail* with notification badge*, settings, menu.
- Left CONTINUE panel: frame, floor preview thumbnail, arrow (floor text live).
- Centre: full-body Sir Clickington hero pose as a separate layer (optional idle breathing loop).
- Props: Lord Blobert on his treasure pile (left); goblin peeking behind a chest with gems (right);
  skull, barrels, sword in stone.
- Right DAILY REWARD panel* with chest art and a CLAIM button*.
- Bottom bar: PLAY (large, green), HERO SELECT*, INVENTORY*, TALENTS*, SHOP*, SETTINGS,
  QUIT (red). Icon per button, normal / hover / pressed / disabled states, notification badge*.

---

## 5. Deliverable B: Gameplay screen (reference 2)

- Small logo top-left.
- Portrait frame with level badge*. HP bar: heart icon, red fill, frame (value live).
  Mana bar* (see D3).
- Gold* and gem* counters with "+".
- Floor plaque, parchment style (floor number and name live).
- Settings (gear) and menu buttons, top-right.
- Side banners in the reference style (see D2: the game currently uses these areas for the
  WHAT HAPPENED log and the INSPECT panel).
- Board: stone wall surround with the perspective top wall and side walls, torches on the frame,
  5×5 grid with gaps. Deliver as a 9-sliceable frame plus separate wall and torch props.
- Ability bar: MOVE, SLASH, SHIELD, DASH, POTION. States: normal, hover, pressed, selected (gold
  frame), disabled. Cooldown badge, potion count badge, hotkey number slot.
- Bottom bar: INVENTORY*, TALENTS*, SHOP* with notification badge*.
- Sir Clickington speech strip (not in the reference): same frame language as the bottom bar.
- Background room dressing: barrels, crates, candles, skull, sword in stone.

---

## 6. Deliverable C: Board tiles (references 3–4)

Priority A: used by the current game. Produce and integrate.

| Game object | Assets and states |
|---|---|
| Stone floor | 3 subtle variants (plain, cracked, mossy detail) |
| Wall | raised block that reads top-down on any cell (walls appear anywhere in 5×5 layouts) |
| Pit | open pit that works as a single isolated cell |
| Spikes | idle; trigger animation |
| Bomb | idle; armed (fuse loop, pulsing ring); explosion FX covering a 3×3 area |
| Key | idle sparkle; collect FX |
| Chest | closed; opening frames; open; empty |
| Potion pickup | not on the sheets: design it to match the set |
| Exit (stairs down) | locked (chains and padlock); unlocking animation; unlocked |

Where set A and set B differ (bomb with skull, pit rim, lock style), follow Decision D5.

Priority B: future mechanics and biomes. Prepare, but do not integrate.
Stair up (locked and unlocked), door (closed and open), torch tile, cracked floor, mossy floor,
water, lava, shadow/void, pressure plate, teleport, healing fountain.

---

## 7. Deliverable D: Gameplay-state art (required by the rules, absent from the references)

- Knowledge overlays: unseen (dark fog), sensed (dimmed tile), revealed (clean tile).
- Clue icons for sensed tiles: enemy (claw marks), danger (warning triangle), objective (keyhole),
  treasure (sparkle), safe (small dot).
- Enemy intent badges: hit (sword), move (footsteps), fire (flame arrow), rest (zzz),
  dazed (stars), summon (magic circle), slam (cracked ground), puff up (inflate arrows).
  Damage numbers are live text.
- Danger overlays (tile-sized frame, translucent fill, corner warning icon): attack tile,
  fire lane, slam area, bomb blast, armed-bomb preview.
- Highlights: legal move, selected ability target, hover.
- Underfoot badge: spikes, bomb or exit under a token.
- Token HP bar.
- Pop-up text style for BLOCK!, MISS, IMMUNE, DAZED, KEY!, BOOM!, -N and +N: font, outline and
  shadow spec only, not images.
- Modal frames: pause, settings, help, victory, defeat; floor-transition banner; chest overlay
  backdrop.

---

## 8. Deliverable E: Characters and animations (reference 6, plus rosters)

Every animation only reacts to results the game has already decided. Under Reduced Motion each one
must snap to its final pose, and none may block input for more than about 0.3 s.
Existing timings: tile step 0.14 s; pop-ups 1.0 s; defeat fade 0.35 s.

| Actor | Animations (frames, fps, loop) | Game trigger |
|---|---|---|
| Sir Clickington token | idle (6f, 8 fps, loop); step (6f, 14 fps); slash (8f); shield raise (6f) + guard hold (loop); dash (6f + smear FX); hit (4f); drink potion (8f); victory (10f); defeat (8f) | move, slash, shield, dash, potion, damage, run won, run lost |
| Portraits (8) | neutral, happy, confident, worried, shocked, angry, victorious, defeated | speech reactions |
| Goblin | wake, idle, move, attack, hit, defeat | enemy woke / moved / attacked / damaged / died |
| Crowned Slime | wake, idle, move, attack, rest pose, hit, defeat | also its resting turn |
| Fire Imp | wake, idle, move, fire cast + fire-lane FX, reload/rest, hit, defeat | fire intent and execution |
| Slimelet | spawn from summon circle, idle, move, attack, defeat | summoned by the boss |
| Lord Blobert | idle; boast (slam telegraph); slam; summon; puff up; puffed idle (immune); immune hit bounce; deflate; deflated idle (vulnerable); hit; defeated (again) | boss script |
| FX | spike trigger, bomb fuse, 3×3 explosion, key collect, potion collect, exit unlock (chains drop), enemy wake "!" | matching game events |

---

## 9. Deliverable F: Chest opening and reward sequence (references 5 and 6)

The reward is committed the moment the chest is opened. Taps are presentation only and cost no turns.

1. Found: chest on its tile with a subtle shimmer.
2. Overlay: dim backdrop, soft glow, large chest (about 700 px) centred.
3. Taps 1/3, 2/3, 3/3: shake and lock rattle, progress meter fills (meter frame + fill art).
4. Burst: lid blast, coin and gem particles, light rays, reward card frame (reward text live,
   e.g. "+1 POTION").
5. Collect: the reward icon flies to its HUD target, then return to the board.

- Reward icons: potion, max HP (heart with +), slash damage (sword with +).
- Sir Clickington reaction poses from reference 6: anticipation → burst/reveal →
  "too much to handle" → triumph, timed to steps 2–5.
- Special-key chests bought from a store: DEFERRED (no store exists). Do not produce unless D1
  changes.

---

## 10. Work order

1. Style frame: one finished 5×5 board mock (tiles, hero, 2 enemies, clues, one telegraph) at
   1920×1080 and on a phone. Approve before mass production.
2. Priority A tiles plus gameplay-state art (Deliverables C and D).
3. Tokens and core animations for Sir Clickington, Goblin, Crowned Slime, Fire Imp (Deliverable E).
4. Gameplay HUD and ability bar (Deliverable B).
5. Chest sequence (Deliverable F).
6. Title screen (Deliverable A).
7. Lord Blobert and slimelet, then remaining FX and portraits.
8. Priority B tiles (prepare only).

---

## 11. Integration requirements (engineering)

- The UI is built in code. Art is looked up through the art registry (see the appendix) by key, and
  the game falls back to the current placeholder art whenever a key is missing.
- Do not change simulation or rules code. The state is already exposed: knowledge, clues, intents,
  threats, underfoot, events.
- Match current layout anchors to references 1–2 (board centre at (0, +66) at 1920×1080, 136 px
  tiles with 4 px gaps, ability bar at y = −378), adjusting positions to the references where
  they differ.

---

## 12. Acceptance criteria

- Side-by-side screenshots of the title and gameplay screens against references 1–2 at 1920×1080:
  same layout, element positions within about 2% of screen size, same style.
- All Priority A tiles and all gameplay-state art integrated. No "ClickDungeon2" anywhere. No baked
  gameplay text.
- Readability check at 1280×720 and on a 6.1" phone in landscape: every tile type, clue, intent and
  danger can be identified in greyscale.
- Every animation behaves correctly under Reduced Motion.
- The 6-seed bot demo still runs with zero errors, and all automated tests still pass.
- The asset manifest is complete.

---

## 13. Decisions to confirm before production

- D1. **DECIDED (2026-09-14): default.** Reference elements for systems that don't exist yet (level, gold,
  gems, mana, crown, mail, daily reward, hero select, inventory, talents, shop, notification badges):
  produce all of the art now; hide those elements in the build until each system exists.
- D2. Side banners vs. the WHAT HAPPENED / INSPECT panels.
  Default: restyle the panels as the reference banners and keep their function.
- D3. Mana bar. Default: none (the Knight uses cooldowns).
- D4. **DECIDED (2026-09-14): slice the reference images into temporary placeholders.** Placeholder
  sprites live in `Art/Runtime/Placeholders/`; any production file with the same key elsewhere under
  `Art/Runtime/` replaces its placeholder automatically. Reference slices are not final art (low
  resolution, baked backgrounds, mixed perspective).
- D5. Tile style where sets A and B differ. Default: set B.

## 14. Out of scope

Additional heroes, the item and equipment icon library, extra biomes, store and monetization art,
marketing illustrations.

---

## Appendix: art registry (implemented)

Decision D-016. Drop-in steps are also in `ClickDungeon/Assets/ClickDungeon/Art/Runtime/README.md`.

### How art gets into the game

1. Put PNGs anywhere under `Assets/ClickDungeon/Art/Runtime/` (subfolders as in section 3).
2. The file name (without extension) is the **key**. Numbered files `<key>_000.png`, `<key>_001.png`…
   become one animation for `<key>`.
3. New files are imported as UI sprites automatically (100 PPU, bilinear, no mipmaps, clamp,
   high-quality compression). Later manual changes to import settings (e.g. 9-slice borders) are kept.
4. The catalog (`Assets/ClickDungeon/Art/Resources/ArtCatalog.asset`) rebuilds itself when art changes.
   Menu: **ClickDungeon → Art → Rebuild Art Catalog**. Animation fps can be edited in the catalog asset
   and survives rebuilds (defaults: 8 fps for `*_idle`, otherwise 12).
5. **ClickDungeon → Art → Report Art Coverage** writes `ClickDungeon/Art/art-coverage.md`:
   which wired keys have art, which are still placeholders, and which files aren't wired yet.

Any key without art keeps drawing the current placeholder, so art can land piece by piece.

Frames, panels and buttons: set 9-slice borders in the sprite importer. Art with borders draws sliced
(at half its pixel size, since art is authored at 2×); art without borders stretches to fit. The reference slice
`ui_floor_plaque` has baked sample text, so the floor plaque looks up `ui_plaque_floor` instead.

### Keys wired today

| Area | Keys |
|---|---|
| Board tiles | `tile_floor_stone` (tinted for sensed and unseen), `tile_wall`, `tile_pit` |
| Board objects | `tile_spikes`, `tile_bomb`, `tile_bomb_armed`, `tile_key`, `tile_chest_closed`, `tile_chest_open`, `tile_potion`, `tile_exit_locked`, `tile_exit_open` |
| Actors (idle, animated if numbered) | `actor_sir_clickington_idle`, `actor_goblin_idle`, `actor_crowned_slime_idle`, `actor_fire_imp_idle`, `actor_slimelet_idle`, `actor_lord_blobert_idle`, `actor_lord_blobert_puffed`, `actor_lord_blobert_deflated` |
| Clue icons | `icon_clue_enemy`, `icon_clue_danger`, `icon_clue_objective`, `icon_clue_treasure`, `icon_clue_safe` |
| Ability icons | `icon_ability_move`, `icon_ability_slash`, `icon_ability_shield`, `icon_ability_dash`, `icon_ability_potion` |
| Portraits | `portrait_sir_clickington_<neutral\|happy\|confident\|worried\|shocked\|angry\|victorious\|defeated>` |
| Intent badges (icon at the left of the live badge text) | `icon_intent_<attack\|move\|fire\|rest\|recover\|summon\|slam\|puffup>` (128×128, readable at 26 px) |
| Danger telegraphs (tile-sized; `-N` and HIT/FIRE/SLAM/BOOM stay live text) | `ui_danger_<attack\|fire\|slam\|blast\|armed\|summon>` (256×256, mostly transparent centre), `icon_danger_warning` (corner icon, readable at 32 px) |
| Board highlights (tile-sized frames; keep the frame within the outer 8 px of 256, because highlights draw above tokens and an enemy's intent badge sits on the tile's top edge) | `ui_highlight_legal` (MOVE-mode steps), `ui_highlight_target` (SLASH/DASH targets), `ui_highlight_hover` |
| HUD frames (frame and fill only; all text stays live) | `ui_frame_portrait`, `ui_hp_back`, `ui_hp_fill`, `ui_hp_frame`, `ui_icon_heart`, `ui_chip` (KEY/SLASH/TURN), `ui_plaque_floor`, `ui_panel` (WHAT HAPPENED / INSPECT), `ui_speech_strip`, `ui_badge_count` |
| Gameplay buttons | `ui_button_ability_<move\|slash\|shield\|dash\|potion>` (falls back to `ui_button_ability`; icon, label and hotkey draw on top), `ui_button_ability_selected`, `ui_button_settings` (whole button, gear included), `ui_button_help` (frame only; "?" stays live) |
| Modals (pause, settings, help, victory, defeat; title, body and button labels stay live) | `ui_modal_panel`, `ui_modal_panel_victory`, `ui_modal_panel_defeat` (fall back to `ui_modal_panel`); buttons by role: `ui_button_primary` (green), `ui_button_secondary`, `ui_button_danger` (red) |
| Chest overlay | `ui_chest_large_closed`, `ui_chest_large_open` (fall back to the tile chest), `fx_chest_glow`, `ui_chest_progress_back`, `ui_chest_progress_fill`, `ui_chest_reward_card` (reward text live) |
| Title screen | `ui_title_hero_card`, `ui_title_tagline_plank`, `ui_title_banner` (slogan live), `ui_title_continue_panel`, `ui_continue_preview`, `ui_title_howto_panel`, `ui_title_hero` (falls back to `actor_sir_clickington_idle`), `ui_title_blobert` (falls back to `actor_lord_blobert_idle`), `ui_title_goblin`, `ui_button_play`, `ui_button_title_settings`, `ui_button_quit`, `ui_icon_play`, `ui_icon_settings`, `ui_icon_quit`. A full `bg_title` still hides the arch, banners and characters |
| Chest reward sequence | Reactions beside the chest: `ui_chest_reaction_<anticipation\|reveal\|heavy\|triumph>` (without art, the confident / shocked / worried / victorious portrait shows in a gold frame). Reward card icons: `icon_reward_<potion\|maxhp\|slashdamage>` (fall back to `tile_potion`, `ui_icon_heart`, `icon_ability_slash`). `fx_chest_rays` behind the burst, particles `fx_chest_coin` and `fx_chest_gem`, `fx_chest_shimmer` over closed chests on the board. Timing: anticipation when the overlay opens, reveal on the third tap, then heavy and triumph 0.45 s apart (triumph immediately with Reduced Motion). The `fx_chest_reward_*` reference slices have baked captions and stay unwired |
| Underfoot badges (hazard or exit under a token, drawn at the tile's left edge above the token; 128×128, readable at 44 px) | `icon_underfoot_spikes`, `icon_underfoot_bomb`, `icon_underfoot_bomb_armed`, `icon_underfoot_exit_locked`, `icon_underfoot_exit_open`. Each is the whole badge. An armed bomb without its own art shows `icon_underfoot_bomb` inside the procedural orange fuse ring, so the armed state stays readable |
| Action animations (one-shot frames over the token; each actor plays only its most important event of the turn) | Hero `actor_sir_clickington_<step\|slash\|shield\|dash\|hit\|potion\|victory\|defeat>`, plus the `actor_sir_clickington_guard` pose while shielded. Every enemy `actor_<id>_<wake\|move\|attack\|hit\|defeat>`. Extras: `actor_crowned_slime_rest` (pose on resting turns), `actor_fire_imp_fire`, `actor_slimelet_spawn`, `actor_lord_blobert_<boast\|slam\|summon\|puffup\|immune\|deflate>` (boast is the pose while a slam is telegraphed). Numbered frames play once at the catalog fps, then the standing pose returns. Fallbacks: dash → step, fire / slam / summon → attack, immune → hit, spawn → wake. Hero victory and defeat hold their last frame; an enemy's defeat plays before its token fades. Reduced Motion skips one-shots and shows final poses. Animations never delay input |
| Board FX (one-shot frames on the board's effects layer, below popups; popups and the log still carry the information) | `fx_spikes_trigger`, `fx_key_collect`, `fx_potion_collect`, `fx_exit_unlock`, `fx_enemy_wake` (one tile, on the event's cell); `fx_explosion` (3×3 tiles, centred on the bomb). `fx_bomb_fuse` loops over armed bomb tiles; the ARMED / BOOM! text stays live. Numbered frames play once at the catalog fps and remove themselves; Reduced Motion skips one-shot effects and holds the fuse loop on its first frame |
| Floor transition banner (above the board, over the top HUD band, for about 1.5 s when a run starts, resumes or reaches a new floor; never blocks input; floor number and name stay live) | `ui_banner_floor`, `ui_banner_floor_boss` (floor 5; falls back to `ui_banner_floor`). Wide plate about 760×116 at 1080p, 9-slice borders recommended. Reduced Motion shows and hides it without the fade |
| Screens | `logo_clickdungeon`, `bg_gameplay`, `bg_title` (a full `bg_title` composite also hides the placeholder arch, banners and characters) |

Not wired yet (needs layout work when the art arrives):
D1 systems (gold, gems, shop, mail…), the reward fly-to-HUD step, chest opening frames,
Priority B tiles.

## Appendix: production tile set keys (rules §11)

Drop PNGs into `Assets/ClickDungeon/Art/Runtime/Tiles/`; the file name is the key. Both sheet spellings are accepted for the
tiles that already existed, so either naming works:

| Feature | Key | Also accepted |
|---|---|---|
| Stone floor | `tile_floor_stone` | — |
| Floor variants (decor) | `tile_floor_cracked`, `tile_floor_moss` | — |
| Wall, corner, torch (decor) | `tile_wall`, `tile_wall_corner`, `tile_torch` | — |
| Pit / water (decor) | `tile_pit` | `tile_trap_pit`, `tile_water` |
| Spikes | `tile_spikes` | `tile_trap_spike` |
| Bomb | `tile_bomb`, `tile_bomb_armed` | `tile_trap_bomb` |
| Lava | `tile_lava` | — |
| Vault door | `tile_door_locked`, `tile_door_open` | — |
| Pressure plate | `tile_pressure_plate` | — |
| Teleport pad | `tile_teleport` | `tile_shadow` (the second swirl) |
| Healing fountain | `tile_fountain_heal` | — |
| Key, chests | `tile_key`, `tile_chest_closed`, `tile_chest_open` | — |
| Exit (stair down) | `tile_exit_locked`, `tile_exit_open` | `tile_stair_down_locked`, `tile_stair_down` |
| Floor entrance (decor) | `tile_stair_up` | — |

The sheets' "locked stair up" has no rule in this game and is not wired: the way back out of a vault is its own stair, and
floor entrances are never locked.

## Appendix: newer production sheets — what is wired and what is held (D1)

Three later sheets (core ability & consumable UI, special chest & key system, common encounter monsters) contain art for
systems this game does not have. Per D1 that art stays on disk, unwired, until the system exists — the board and HUD never
show a feature the simulation cannot back.

| Sheet asset | Status |
|---|---|
| `btn_move`, `btn_slash`, `btn_shield`, `btn_dash`, `btn_potion` | **wired as icons**: the sheet's framed small icon variants are `icon_ability_<move\|slash\|shield\|dash\|potion>`. The large buttons are not used because their labels are baked in and the game draws live labels |
| `btn_open_chest`, `btn_interact` | wire to the Interact command (rules §7) |
| `icon_heart`, `icon_lock`, `icon_alert` | **wired**: `ui_icon_heart`, `icon_underfoot_exit_locked`, `icon_danger_warning` |
| `tile_highlight`, `enemy_health_bar` | not converted: the highlight's glow is about 20 px deep where highlights must stay in the outer 8 px, and there is no enemy HP bar yet |
| `ui_tap_progress` (tap-to-open meter) | not converted: the chest and pointing hand are baked into the meter. Board pips draw the 2/3/4 taps (D-022) |
| `btn_use_key` | **held** — the floor key is spent by walking onto the exit, not by a button |
| `btn_bomb`, `btn_heal` | **held** — bombs are armed by entering or slashing the tile; healing is the Potion command |
| `icon_mana` | **held** — no mana system (D3 default: no mana bar) |
| `icon_coin`, `icon_gem`, `ui_store_key_card`, `icon_reward_bundle` | **held** — no currency, shop or economy |
| `item_key_special`, `item_key_premium`, `badge_special_lock` | **held** — the sheet states special keys are a premium currency item, not found in regular dungeon gameplay |
| `chest_premium_*`, `chest_mega_*`, `vfx_reward_burst` | **held** — premium and mega chests need the special-key economy above before they can open |
| `goblin_raider_*` | **wired** as `actor_goblin_<idle\|wake\|attack\|hit\|defeat>` (wake uses the ALERT frame) |
| `slime_minion_*` | **wired** as `actor_slimelet_<idle\|wake\|attack\|hit\|defeat>` |
| `goblin_bomber_*`, `goblin_key_warden_*` | **held** — these are new enemies with no behaviour yet; each needs a rules entry and tests first |
| `enemy_alert.png` | **wired** as `fx_enemy_wake` |

Mimic chests are in the same position as the two new goblins: a Mimic is an enemy disguised as a chest, so it needs an
enemy definition and its own rule (first tap reveals it, that tap is the player's action, and it may strike in that same
enemy phase) before any art is wired.

Character and boss encyclopedia sheets (animation-state key frames) are converted the same way, one frame per state:
`actor_sir_clickington_<slash|hit|victory|defeat>`, `actor_crowned_slime_<wake|attack|hit|defeat>` and
`actor_fire_imp_<wake|attack|hit|defeat>` (wake uses the SPAWN frame), and Lord Blobert's `boast`, `summon`, `puffup`
and `defeat` poses from the vertical slice. Monsters with no rules yet (Goblin Brute King, Skeleton Warrior, Bat Swarm
Leader, Mimic Chest, Armored Boar, Spooky Spellbook, Cave Spider, Theater Curtain Demon, Goblin Bomber, Key Warden) stay
unconverted (D1).
