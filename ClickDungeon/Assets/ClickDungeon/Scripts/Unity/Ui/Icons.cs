using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>
    /// Board, actor and UI icons. Each one uses production art from the art catalog when its key exists and
    /// otherwise draws programmer art from procedural shapes. Every gameplay meaning uses shape and a glyph
    /// as well as colour, so nothing is communicated by colour alone.
    /// </summary>
    public static class Icons
    {
        public const float TileSize = 136f;
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        public static Image Shape(Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size, float rotation = 0f)
        {
            var image = UiFactory.Image(parent, sprite != null ? sprite.name : "Rect", color, sprite);
            image.rectTransform.Place(Center, Center, pos, size);
            image.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
            return image;
        }

        public static Text Label(Transform parent, string value, int size, Color color, Vector2 pos, Vector2 box, bool outline = true)
        {
            var text = UiFactory.Text(parent, "Label", value, size, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.rectTransform.Place(Center, Center, pos, box);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (outline) UiFactory.Outline(text, new Color(0f, 0f, 0f, 0.85f), 1.5f);
            return text;
        }

        public static RectTransform Group(Transform parent, Vector2 pos, float rotation = 0f, float scale = 1f)
        {
            var rt = UiFactory.Rect(parent, "Group");
            rt.Place(Center, Center, pos, new Vector2(10f, 10f));
            rt.localEulerAngles = new Vector3(0f, 0f, rotation);
            rt.localScale = Vector3.one * scale;
            return rt;
        }

        // ---------------------------------------------------------------- production art

        /// <summary>Places catalog art for the key (animated when it has several frames). False = draw the placeholder.</summary>
        public static bool TryArt(Transform parent, string key, float size, Vector2 pos = default)
        {
            return TryArtImage(parent, key, size, pos) != null;
        }

        public static bool TryArtFirst(Transform parent, float size, params string[] keys)
        {
            foreach (var key in keys)
                if (TryArt(parent, key, size))
                    return true;
            return false;
        }

        public static Image TryArtImage(Transform parent, string key, float size, Vector2 pos = default)
        {
            return Art.TryGet(key, out var entry) ? ArtImage(parent, entry, new Vector2(size, size), pos) : null;
        }

        public static Image ArtImage(Transform parent, ArtCatalog.Entry entry, Vector2 size, Vector2 pos)
        {
            var image = UiFactory.Image(parent, "Art " + entry.Key, Color.white, entry.Frames[0]);
            image.preserveAspect = true;
            image.rectTransform.Place(Center, Center, pos, size);
            if (entry.Frames.Length > 1) image.gameObject.AddComponent<SpriteFrameAnimator>().Play(image, entry.Frames, entry.Fps);
            return image;
        }

        /// <summary>Swaps a text logo for its image in the same rect, if the art exists.</summary>
        public static Image ReplaceTextWithArt(Text text, string key)
        {
            if (!Art.TryGet(key, out var entry)) return null;
            var source = text.rectTransform;
            var image = UiFactory.Image(source.parent, "Art " + key, Color.white, entry.Frames[0]);
            var rt = image.rectTransform;
            rt.anchorMin = source.anchorMin;
            rt.anchorMax = source.anchorMax;
            rt.pivot = source.pivot;
            rt.anchoredPosition = source.anchoredPosition;
            rt.sizeDelta = source.sizeDelta;
            image.preserveAspect = true;
            text.enabled = false;
            return image;
        }

        // ---------------------------------------------------------------- board objects

        public static void WallBricks(Transform t)
        {
            Shape(t, Shapes.Square, Palette.WallEdge.WithAlpha(0.35f), new Vector2(0f, 18f), new Vector2(112f, 4f));
            Shape(t, Shapes.Square, Palette.WallEdge.WithAlpha(0.35f), new Vector2(0f, -20f), new Vector2(112f, 4f));
            Shape(t, Shapes.Square, Palette.WallEdge.WithAlpha(0.35f), new Vector2(-20f, 0f), new Vector2(4f, 34f));
            Shape(t, Shapes.Square, Palette.WallEdge.WithAlpha(0.35f), new Vector2(26f, 38f), new Vector2(4f, 36f));
            Shape(t, Shapes.Square, Palette.WallEdge.WithAlpha(0.35f), new Vector2(30f, -38f), new Vector2(4f, 34f));
        }

        public static void Pit(Transform t)
        {
            Shape(t, Shapes.Rounded, Palette.Pit, Vector2.zero, new Vector2(104f, 104f));
            Shape(t, Shapes.Rounded, Color.black, new Vector2(0f, -6f), new Vector2(84f, 80f));
        }

        public static void Spikes(Transform t)
        {
            if (TryArt(t, ArtKeys.Spikes, TileSize)) return;
            var spots = new[] { new Vector2(-34f, -10f), new Vector2(0f, 12f), new Vector2(34f, -10f) };
            foreach (var s in spots)
            {
                Shape(t, Shapes.Triangle, Palette.Steel, s, new Vector2(40f, 58f));
                Shape(t, Shapes.Triangle, Palette.StoneDark.WithAlpha(0.55f), s + new Vector2(7f, -8f), new Vector2(16f, 30f));
            }
        }

        public static void Bomb(Transform t, bool armed, int fuse)
        {
            bool art = armed ? TryArtFirst(t, TileSize, ArtKeys.BombArmed, ArtKeys.Bomb) : TryArt(t, ArtKeys.Bomb, TileSize);
            if (!art)
            {
                if (armed) Shape(t, Shapes.Ring, Palette.Fuse, Vector2.zero, new Vector2(122f, 122f));
                Shape(t, Shapes.Circle, Palette.Bomb, new Vector2(-4f, -6f), new Vector2(72f, 72f));
                Shape(t, Shapes.Circle, Color.white.WithAlpha(0.22f), new Vector2(-18f, 8f), new Vector2(18f, 18f));
                Shape(t, Shapes.Square, Palette.StoneLight, new Vector2(20f, 28f), new Vector2(10f, 20f), -35f);
                Shape(t, Shapes.Circle, Palette.Fuse, new Vector2(28f, 40f), new Vector2(18f, 18f));
            }
            // The fuse state is gameplay information, so it stays as live text even with art.
            if (armed) Label(t, fuse <= 0 ? "BOOM!" : "ARMED", 20, Palette.Fuse, new Vector2(0f, -50f), new Vector2(120f, 24f));
        }

        public static void Key(Transform t)
        {
            if (TryArt(t, ArtKeys.Key, TileSize)) return;
            Shape(t, Shapes.Ring, Palette.Gold, new Vector2(-24f, 12f), new Vector2(48f, 48f));
            Shape(t, Shapes.Square, Palette.Gold, new Vector2(18f, 12f), new Vector2(52f, 12f));
            Shape(t, Shapes.Square, Palette.Gold, new Vector2(34f, 0f), new Vector2(10f, 20f));
            Shape(t, Shapes.Square, Palette.Gold, new Vector2(20f, 2f), new Vector2(8f, 14f));
            Label(t, "KEY", 18, Palette.Gold, new Vector2(0f, -38f), new Vector2(100f, 24f));
        }

        /// <param name="useArt">False for scenery: chest tile art includes its stone floor frame.</param>
        public static void Chest(Transform t, bool opened, float scale = 1f, bool useArt = true)
        {
            if (useArt && TryArt(t, opened ? ArtKeys.ChestOpen : ArtKeys.ChestClosed, TileSize * scale)) return;
            var g = Group(t, Vector2.zero, 0f, scale);
            var wood = opened ? Palette.ChestWood.Dim(0.55f) : Palette.ChestWood;
            if (opened) Shape(g, Shapes.Rounded, wood.Dim(0.8f), new Vector2(0f, 34f), new Vector2(88f, 26f));
            Shape(g, Shapes.Rounded, wood, new Vector2(0f, -6f), new Vector2(88f, 64f));
            Shape(g, Shapes.Square, Palette.Gold, new Vector2(0f, opened ? 22f : 10f), new Vector2(90f, 6f));
            Shape(g, Shapes.Square, Palette.GoldDark, new Vector2(-32f, -6f), new Vector2(6f, 62f));
            Shape(g, Shapes.Square, Palette.GoldDark, new Vector2(32f, -6f), new Vector2(6f, 62f));
            if (opened)
            {
                Shape(g, Shapes.Circle, Palette.Gold.WithAlpha(0.7f), new Vector2(-10f, 14f), new Vector2(20f, 20f));
                Shape(g, Shapes.Diamond, Palette.Summon.WithAlpha(0.7f), new Vector2(12f, 16f), new Vector2(18f, 18f));
            }
            else
            {
                Shape(g, Shapes.Rounded, Palette.Gold, new Vector2(0f, 0f), new Vector2(18f, 22f));
            }
        }

        public static void Potion(Transform t, float scale = 1f)
        {
            if (TryArt(t, ArtKeys.Potion, TileSize * scale)) return;
            var g = Group(t, Vector2.zero, 0f, scale);
            Shape(g, Shapes.Square, Palette.Steel, new Vector2(0f, 28f), new Vector2(18f, 22f));
            Shape(g, Shapes.Square, Palette.ChestWood, new Vector2(0f, 42f), new Vector2(22f, 10f));
            Shape(g, Shapes.Circle, Palette.Potion, new Vector2(0f, -8f), new Vector2(62f, 62f));
            Shape(g, Shapes.Circle, Color.white.WithAlpha(0.35f), new Vector2(-12f, 4f), new Vector2(14f, 14f));
            Label(g, "+", 30, Color.white, new Vector2(0f, -10f), new Vector2(40f, 40f));
        }

        public static void Exit(Transform t, bool unlocked)
        {
            if (TryArt(t, unlocked ? ArtKeys.ExitOpen : ArtKeys.ExitLocked, TileSize)) return;
            Shape(t, Shapes.Rounded, Palette.StoneDark, Vector2.zero, new Vector2(112f, 112f));
            for (int i = 0; i < 3; i++)
                Shape(t, Shapes.Square, Palette.StoneLight.Dim(1f - i * 0.22f), new Vector2(0f, 28f - i * 22f), new Vector2(86f - i * 14f, 14f));
            if (unlocked)
            {
                Shape(t, Shapes.Triangle, Palette.Gold, new Vector2(0f, -32f), new Vector2(36f, 30f), 180f);
                Label(t, "EXIT", 20, Palette.Gold, new Vector2(0f, -52f), new Vector2(110f, 22f));
            }
            else
            {
                Shape(t, Shapes.Ring, Palette.Gold, new Vector2(0f, 12f), new Vector2(42f, 42f));
                Shape(t, Shapes.Rounded, Palette.Gold, new Vector2(0f, -8f), new Vector2(52f, 40f));
                Shape(t, Shapes.Circle, Palette.Ink, new Vector2(0f, -6f), new Vector2(12f, 12f));
                Label(t, "LOCKED", 16, Palette.Gold, new Vector2(0f, -46f), new Vector2(110f, 20f));
            }
        }

        /// <summary>Small badge for a hazard or exit that an actor's token is covering.</summary>
        public static void Underfoot(Transform t, CellState cell, bool exitUnlocked)
        {
            const float badge = 44f;

            // State-specific art is the whole badge.
            if (TryArt(t, ArtKeys.Underfoot(cell, exitUnlocked), badge)) return;

            // An armed bomb can reuse plain bomb badge art inside the procedural fuse ring, so the armed state
            // never depends on having its own art.
            if (cell.BombArmed && Art.Has(ArtKeys.UnderfootBomb))
            {
                Shape(t, Shapes.Ring, Palette.Fuse, Vector2.zero, new Vector2(badge + 6f, badge + 6f));
                TryArt(t, ArtKeys.UnderfootBomb, badge - 6f);
                return;
            }

            var ring = cell.BombArmed ? Palette.Fuse : Palette.GoldDark;
            Shape(t, Shapes.Circle, Palette.Navy, Vector2.zero, new Vector2(42f, 42f));
            Shape(t, Shapes.Ring, ring, Vector2.zero, new Vector2(44f, 44f));

            if (cell.Hazard == HazardKind.Spikes)
            {
                Shape(t, Shapes.Triangle, Palette.Steel, new Vector2(-9f, -4f), new Vector2(11f, 18f));
                Shape(t, Shapes.Triangle, Palette.Steel, new Vector2(0f, 3f), new Vector2(11f, 20f));
                Shape(t, Shapes.Triangle, Palette.Steel, new Vector2(9f, -4f), new Vector2(11f, 18f));
            }
            else if (cell.Hazard == HazardKind.Bomb)
            {
                Shape(t, Shapes.Circle, Palette.Bomb, new Vector2(-2f, -3f), new Vector2(22f, 22f));
                Shape(t, Shapes.Circle, Palette.Fuse, new Vector2(8f, 9f), new Vector2(8f, 8f));
            }
            else if (cell.IsExit)
            {
                for (int i = 0; i < 3; i++)
                    Shape(t, Shapes.Square, Palette.StoneLight, new Vector2(-2f, 7f - i * 7f), new Vector2(22f - i * 5f, 4f));
                if (!exitUnlocked) Shape(t, Shapes.Rounded, Palette.Gold, new Vector2(9f, -8f), new Vector2(12f, 11f));
            }
        }

        public static void Clues(Transform t, Clue clue)
        {
            var marks = new List<(Clue flag, Sprite sprite, Color color, string glyph)>();
            if ((clue & Clue.Enemy) != 0) marks.Add((Clue.Enemy, Shapes.Diamond, Palette.Danger, "!"));
            if ((clue & Clue.Danger) != 0) marks.Add((Clue.Danger, Shapes.Triangle, Palette.Fuse, "!"));
            if ((clue & Clue.Objective) != 0) marks.Add((Clue.Objective, Shapes.Ring, Palette.Gold, "K"));
            if ((clue & Clue.Treasure) != 0) marks.Add((Clue.Treasure, Shapes.Diamond, Palette.Gold, "$"));

            if (marks.Count == 0)
            {
                if (!TryArt(t, ArtKeys.ClueIcon(Clue.Safe), 28f))
                    Shape(t, Shapes.Circle, Palette.Safe.WithAlpha(0.7f), Vector2.zero, new Vector2(16f, 16f));
                return;
            }

            const float spacing = 48f;
            float x0 = -(marks.Count - 1) * spacing * 0.5f;
            for (int i = 0; i < marks.Count; i++)
            {
                var pos = new Vector2(x0 + i * spacing, 0f);
                if (TryArt(t, ArtKeys.ClueIcon(marks[i].flag), 44f, pos)) continue;
                Shape(t, marks[i].sprite, marks[i].color, pos, new Vector2(44f, 44f));
                Label(t, marks[i].glyph, 22, Color.white, pos + new Vector2(0f, -2f), new Vector2(40f, 40f));
            }
        }

        // ---------------------------------------------------------------- actors

        public static void Hero(Transform t, bool guard)
        {
            if (guard) Shape(t, Shapes.Ring, Palette.Gold, Vector2.zero, new Vector2(128f, 128f));
            if (TryArtFirst(t, TileSize, guard ? ArtKeys.Actor(ArtKeys.HeroId, "guard") : ArtKeys.Actor(ArtKeys.HeroId), ArtKeys.Actor(ArtKeys.HeroId))) return;
            Shape(t, Shapes.Triangle, Palette.Hp, new Vector2(10f, 48f), new Vector2(36f, 32f), -15f);
            Shape(t, Shapes.Circle, Palette.Steel, Vector2.zero, new Vector2(98f, 98f));
            Shape(t, Shapes.Circle, Palette.Hero, new Vector2(0f, -4f), new Vector2(80f, 80f));
            Shape(t, Shapes.Rounded, Palette.StoneDark, new Vector2(0f, 10f), new Vector2(58f, 12f));
            Label(t, "SC", 26, Color.white, new Vector2(0f, -16f), new Vector2(80f, 30f));
        }

        /// <param name="intent">Selects standing poses: Lord Blobert boasts while a slam is telegraphed, slimes rest.</param>
        public static void Enemy(Transform t, EnemyDefinition def, EnemyMode mode, IntentKind intent = IntentKind.None)
        {
            if (def.IsBoss)
            {
                // Immunity must stay readable without relying on the art: keep the steel ring.
                if (mode == EnemyMode.Puffed) Shape(t, Shapes.Ring, Palette.Steel, Vector2.zero, new Vector2(134f, 134f));
                string state = mode == EnemyMode.Puffed ? "puffed"
                    : mode == EnemyMode.Deflated ? "deflated"
                    : intent == IntentKind.Slam ? "boast" : "idle";
                if (TryArtFirst(t, TileSize * 1.4f, ArtKeys.Actor(def.Id, state), ArtKeys.Actor(def.Id))) return;
            }
            else if (TryArtFirst(t, TileSize, intent == IntentKind.Rest ? ArtKeys.Actor(def.Id, "rest") : ArtKeys.Actor(def.Id), ArtKeys.Actor(def.Id)))
            {
                return;
            }

            switch (def.Id)
            {
                case "goblin":
                    Shape(t, Shapes.Triangle, Palette.Goblin.Dim(0.8f), new Vector2(-40f, 20f), new Vector2(30f, 30f), 60f);
                    Shape(t, Shapes.Triangle, Palette.Goblin.Dim(0.8f), new Vector2(40f, 20f), new Vector2(30f, 30f), -60f);
                    Shape(t, Shapes.Diamond, Palette.Goblin, Vector2.zero, new Vector2(94f, 94f));
                    Label(t, "G", 34, Color.white, new Vector2(0f, -4f), new Vector2(60f, 40f));
                    break;
                case "crowned_slime":
                    Shape(t, Shapes.Circle, Palette.Slime, new Vector2(0f, -8f), new Vector2(94f, 84f));
                    Crown(t, new Vector2(0f, 38f), 0.8f);
                    Label(t, "S", 34, Color.white, new Vector2(0f, -10f), new Vector2(60f, 40f));
                    break;
                case "fire_imp":
                    Shape(t, Shapes.Triangle, Palette.Imp.Dim(0.7f), new Vector2(-24f, 40f), new Vector2(18f, 26f), 15f);
                    Shape(t, Shapes.Triangle, Palette.Imp.Dim(0.7f), new Vector2(24f, 40f), new Vector2(18f, 26f), -15f);
                    Shape(t, Shapes.Triangle, Palette.Imp, new Vector2(0f, -4f), new Vector2(96f, 92f));
                    Shape(t, Shapes.Circle, Palette.Fuse, new Vector2(34f, -30f), new Vector2(22f, 22f));
                    Label(t, "I", 32, Color.white, new Vector2(0f, -16f), new Vector2(60f, 40f));
                    break;
                case "slimelet":
                    Shape(t, Shapes.Circle, Palette.Slime.Dim(1.1f), new Vector2(0f, -10f), new Vector2(58f, 52f));
                    Label(t, "s", 26, Color.white, new Vector2(0f, -12f), new Vector2(40f, 30f));
                    break;
                case "lord_blobert":
                    if (mode == EnemyMode.Puffed)
                    {
                        Shape(t, Shapes.Circle, Palette.Boss.Dim(1.15f), Vector2.zero, new Vector2(122f, 122f));
                    }
                    else if (mode == EnemyMode.Deflated)
                    {
                        Shape(t, Shapes.Circle, Palette.Boss.Dim(0.7f), new Vector2(0f, -30f), new Vector2(124f, 60f));
                    }
                    else
                    {
                        Shape(t, Shapes.Circle, Palette.Boss, new Vector2(0f, -6f), new Vector2(112f, 104f));
                    }
                    Crown(t, new Vector2(0f, mode == EnemyMode.Deflated ? 6f : 48f), 1f);
                    Label(t, "LB", 30, Color.white, new Vector2(0f, mode == EnemyMode.Deflated ? -30f : -10f), new Vector2(80f, 40f));
                    break;
                default:
                    Shape(t, Shapes.Square, Palette.Danger, Vector2.zero, new Vector2(80f, 80f));
                    Label(t, "?", 34, Color.white, Vector2.zero, new Vector2(60f, 40f));
                    break;
            }
        }

        public static void Crown(Transform t, Vector2 pos, float scale)
        {
            var g = Group(t, pos, 0f, scale);
            Shape(g, Shapes.Square, Palette.Gold, new Vector2(0f, -6f), new Vector2(56f, 14f));
            Shape(g, Shapes.Triangle, Palette.Gold, new Vector2(-20f, 6f), new Vector2(18f, 22f));
            Shape(g, Shapes.Triangle, Palette.Gold, new Vector2(0f, 10f), new Vector2(18f, 28f));
            Shape(g, Shapes.Triangle, Palette.Gold, new Vector2(20f, 6f), new Vector2(18f, 22f));
            Shape(g, Shapes.Diamond, Palette.Summon, new Vector2(0f, -6f), new Vector2(10f, 10f));
        }

        // ---------------------------------------------------------------- UI icons

        public static Text Portrait(Transform t, float size)
        {
            Shape(t, Shapes.Rounded, Palette.NavyLight, Vector2.zero, new Vector2(size, size));
            Shape(t, Shapes.Triangle, Palette.Hp, new Vector2(size * 0.2f, size * 0.36f), new Vector2(size * 0.32f, size * 0.26f), -25f);
            Shape(t, Shapes.Circle, Palette.Steel, new Vector2(0f, -size * 0.04f), new Vector2(size * 0.78f, size * 0.78f));
            Shape(t, Shapes.Rounded, Palette.StoneDark, new Vector2(0f, size * 0.16f), new Vector2(size * 0.5f, size * 0.1f));
            Shape(t, Shapes.Circle, Palette.Parchment, new Vector2(0f, -size * 0.12f), new Vector2(size * 0.56f, size * 0.46f));
            return Label(t, ":)", Mathf.RoundToInt(size * 0.24f), Palette.Ink, new Vector2(0f, -size * 0.12f), new Vector2(size * 0.6f, size * 0.4f), false);
        }

        public static void Gear(Transform t, Color color, float size)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Vector2 dir = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
                Shape(t, Shapes.Square, color, dir * (size * 0.36f), new Vector2(size * 0.17f, size * 0.2f), angle);
            }
            Shape(t, Shapes.Ring, color, Vector2.zero, new Vector2(size * 0.72f, size * 0.72f));
        }

        public static void Ability(Transform t, CommandKind kind)
        {
            if (TryArt(t, ArtKeys.AbilityIcon(kind), 96f)) return;
            switch (kind)
            {
                case CommandKind.Move:
                    Shape(t, Shapes.Rounded, Palette.ChestWood.Dim(1.2f), new Vector2(-8f, 10f), new Vector2(40f, 54f));
                    Shape(t, Shapes.Rounded, Palette.ChestWood.Dim(1.2f), new Vector2(8f, -16f), new Vector2(72f, 28f));
                    Shape(t, Shapes.Square, Palette.ChestWood.Dim(0.7f), new Vector2(-8f, 30f), new Vector2(46f, 8f));
                    break;
                case CommandKind.Slash:
                {
                    var g = Group(t, Vector2.zero, -45f);
                    Shape(g, Shapes.Square, Palette.Steel, new Vector2(0f, 14f), new Vector2(16f, 68f));
                    Shape(g, Shapes.Triangle, Palette.Steel, new Vector2(0f, 54f), new Vector2(16f, 16f));
                    Shape(g, Shapes.Square, Palette.Gold, new Vector2(0f, -22f), new Vector2(44f, 10f));
                    Shape(g, Shapes.Square, Palette.ChestWood, new Vector2(0f, -38f), new Vector2(10f, 22f));
                    break;
                }
                case CommandKind.Shield:
                    Shape(t, Shapes.Rounded, Palette.Steel, Vector2.zero, new Vector2(62f, 72f));
                    Shape(t, Shapes.Rounded, Palette.NavyLight, Vector2.zero, new Vector2(50f, 60f));
                    Shape(t, Shapes.Square, Palette.Gold, Vector2.zero, new Vector2(10f, 46f));
                    Shape(t, Shapes.Square, Palette.Gold, new Vector2(0f, 6f), new Vector2(34f, 10f));
                    break;
                case CommandKind.Dash:
                    Shape(t, Shapes.Ring, Palette.Hero.Dim(1.4f), new Vector2(-10f, 0f), new Vector2(60f, 60f));
                    Shape(t, Shapes.Triangle, Color.white, new Vector2(26f, 0f), new Vector2(34f, 34f), -90f);
                    break;
                case CommandKind.Potion:
                    Potion(t, 0.8f);
                    break;
            }
        }
    }
}
