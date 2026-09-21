using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Terrain = ClickDungeon.Domain.Terrain;

namespace ClickDungeon.Unity.Screens
{
    public sealed class CellInput : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public GridPos Pos;
        public Action<GridPos> Clicked;
        public Action<GridPos> Entered;
        public Action<GridPos> Exited;

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(Pos);
        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke(Pos);
        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke(Pos);
    }

    /// <summary>Draws the 5×5 board from state. Owns no rules: it renders what simulation already decided.</summary>
    public sealed class BoardView
    {
        /// <summary>A cell's height, and the size of everything drawn on it: tokens, items and icons stay square.</summary>
        public const float CellSize = 136f;
        /// <summary>
        /// A cell's width. The reference's tiles are wider than tall (D-034), so the tile itself stretches while what stands
        /// on it keeps its shape.
        /// </summary>
        public const float CellWidth = 184f;
        public const float Gap = 4f;
        public static readonly float FrameSize = BoardRules.Size * CellSize + (BoardRules.Size - 1) * Gap + 48f;
        public static readonly float FrameWidth = BoardRules.Size * CellWidth + (BoardRules.Size - 1) * Gap + 48f;
        const int HeroTokenId = ActorAnimations.HeroToken;
        static readonly Color WallArtTint = new Color(0.5f, 0.4f, 0.33f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        sealed class CellParts
        {
            public RectTransform Rect;
            public Image Base;
            public Image Edge;
            public RectTransform Icons;
            public RectTransform Overlay;
            public RectTransform Labels;
            public Image Highlight;
        }

        sealed class Token
        {
            public RectTransform Rect;
            public RectTransform Body;
            public Image Anim;
            public Image BadgeBack;
            public Image BadgeIcon;
            public Text Badge;
            public RectTransform HpBack;
            public RectTransform HpFill;
            public string VisualKey;
            public GridPos Pos;
            /// <summary>Incremented on every placement; a running move tween stops once it is no longer the latest.</summary>
            public int MoveVersion;
        }

        readonly MonoBehaviour _host;
        readonly RectTransform _shake;
        readonly RectTransform _tokenLayer;
        readonly RectTransform _labelLayer;
        readonly RectTransform _fxLayer;
        readonly CellParts[] _cells = new CellParts[BoardRules.CellCount];
        readonly Dictionary<int, Token> _tokens = new Dictionary<int, Token>();
        Dictionary<int, ActorAnimations.Cue> _pendingCues;
        List<BoardFx.Effect> _pendingFx;
        ContentCatalog _catalog;

        public BoardView(RectTransform parent, MonoBehaviour host, Vector2 position)
        {
            _host = host;
            Root = UiFactory.Rect(parent, "Board");
            Root.Place(Center, Center, position, new Vector2(FrameWidth, FrameSize));

            _shake = UiFactory.Rect(Root, "Shake");
            _shake.Stretch();
            _frame = new[]
            {
                UiFactory.Image(_shake, "FrameShadow", new Color(0f, 0f, 0f, 0.5f), Shapes.Rounded, true),
                UiFactory.Image(_shake, "Frame", Palette.Stone, Shapes.Rounded, true),
                UiFactory.Image(_shake, "FrameEdge", Palette.StoneLight, Shapes.Frame, true),
            };
            _frame[0].rectTransform.Stretch(-10, -4, -10, -16);
            _frame[1].rectTransform.Stretch();
            _frame[2].rectTransform.Stretch();
            _inner = UiFactory.Image(_shake, "Inner", Palette.StoneDark, Shapes.Rounded, true);
            _inner.rectTransform.Stretch(16, 16, 16, 16);

            var cellLayer = Layer("Cells");
            _tokenLayer = Layer("Tokens");
            _labelLayer = Layer("Labels");
            _fxLayer = Layer("Effects");

            foreach (var p in Board.AllCells) _cells[p.Index] = BuildCell(cellLayer, p);
        }

        public RectTransform Root { get; }

        readonly Image[] _frame;
        readonly Image _inner;

        /// <summary>
        /// Sits the board inside a frame the background already draws (the reference's stone walls, D-034): the board's own
        /// frame goes, and its dark floor fills the given size, covering the background's sample tiles.
        /// </summary>
        public void FitInside(Vector2 size)
        {
            Root.sizeDelta = size;
            foreach (var image in _frame) image.enabled = false;
            _inner.rectTransform.Stretch();
            _inner.sprite = null;
            _inner.type = Image.Type.Simple;
        }
        public event Action<GridPos> CellClicked;
        public event Action<GridPos> CellEntered;
        public event Action<GridPos> CellExited;

        public static Vector2 CellPosition(GridPos p) =>
            new Vector2((p.X - 2) * (CellWidth + Gap), (p.Y - 2) * (CellSize + Gap));

        RectTransform Layer(string name)
        {
            var layer = UiFactory.Rect(_shake, name);
            layer.Stretch();
            return layer;
        }

        CellParts BuildCell(RectTransform layer, GridPos p)
        {
            var rt = UiFactory.Rect(layer, $"Cell {p.X},{p.Y}");
            rt.Place(Center, Center, CellPosition(p), new Vector2(CellWidth, CellSize));

            var baseImage = rt.gameObject.AddComponent<Image>();
            baseImage.sprite = Shapes.Rounded;
            baseImage.type = Image.Type.Sliced;
            baseImage.pixelsPerUnitMultiplier = 3f;
            baseImage.raycastTarget = true;

            var edge = UiFactory.Image(rt, "Edge", Palette.StoneLight, Shapes.Frame, true);
            edge.pixelsPerUnitMultiplier = 3f;
            edge.rectTransform.Stretch();

            var icons = UiFactory.Rect(rt, "Icons");
            icons.Stretch();
            var overlay = UiFactory.Rect(rt, "Overlay");
            overlay.Stretch();

            var labels = UiFactory.Rect(_labelLayer, $"Labels {p.X},{p.Y}");
            labels.Place(Center, Center, CellPosition(p), new Vector2(CellWidth, CellSize));
            var highlight = UiFactory.Image(labels, "Highlight", Palette.Legal, Shapes.Frame, true);
            highlight.pixelsPerUnitMultiplier = 1.4f;
            highlight.rectTransform.Stretch(-2, -2, -2, -2);
            highlight.enabled = false;

            var input = rt.gameObject.AddComponent<CellInput>();
            input.Pos = p;
            input.Clicked = q => CellClicked?.Invoke(q);
            input.Entered = q => CellEntered?.Invoke(q);
            input.Exited = q => CellExited?.Invoke(q);

            return new CellParts { Rect = rt, Base = baseImage, Edge = edge, Icons = icons, Overlay = overlay, Labels = labels, Highlight = highlight };
        }

        /// <summary>Action animations and board effects for the next Render, chosen from the turn that just resolved.</summary>
        public void QueueActorAnimations(RunState run, IReadOnlyList<GameEvent> events)
        {
            _pendingCues = ActorAnimations.Pick(run, events);
            _pendingFx = BoardFx.Pick(events);
        }

        public void Render(RunState run, ContentCatalog catalog, List<Threat> threats, HashSet<GridPos> legal, bool strongHighlight,
            GridPos? hover, bool animate)
        {
            _catalog = catalog;
            var floor = run.Floor;

            var damage = new int[BoardRules.CellCount];
            var threatKinds = new List<ThreatKind>[BoardRules.CellCount];
            foreach (var threat in threats)
            {
                int i = threat.Cell.Index;
                damage[i] += threat.Damage;
                (threatKinds[i] ?? (threatKinds[i] = new List<ThreatKind>())).Add(threat.Kind);
            }

            bool exitOpen = Board.ExitReadsOpen(run);
            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                var view = _cells[p.Index];
                Clear(view.Icons);
                Clear(view.Overlay);
                ClearExcept(view.Labels, view.Highlight.transform);

                DrawCell(view, run, floor, cell, p, exitOpen);
                DrawThreats(view, threatKinds[p.Index], damage[p.Index], floor.EnemyAt(p)?.Awake == true);

                // A token hides the tile art beneath it, so repeat a hazard or exit underfoot as a badge above the token.
                bool occupied = run.Hero.Pos == p || floor.EnemyAt(p)?.Awake == true;
                if (occupied && cell.Knowledge == Knowledge.Revealed && (cell.Hazard != HazardKind.None || cell.IsExit))
                    Icons.Underfoot(Icons.Group(view.Labels, new Vector2(-CellWidth * 0.5f + 22f, 0f)), cell, exitOpen);

                ApplyHighlight(view, p, legal, strongHighlight, hover);
            }

            RenderTokens(run, catalog, animate);
            PlayPendingCues();
            PlayPendingFx();
        }

        /// <summary>
        /// Redraws only the legal-target and hover frames. Hovering changes nothing else on the board, so it skips the full
        /// rebuild of every tile.
        /// </summary>
        public void RenderHighlights(HashSet<GridPos> legal, bool strongHighlight, GridPos? hover)
        {
            foreach (var p in Board.AllCells) ApplyHighlight(_cells[p.Index], p, legal, strongHighlight, hover);
        }

        static void ApplyHighlight(CellParts view, GridPos p, HashSet<GridPos> legal, bool strongHighlight, GridPos? hover)
        {
            // Plain movement marks nothing: only an aimed SLASH or DASH lights its targets, and the tile under the pointer.
            bool isLegal = strongHighlight && legal != null && legal.Contains(p);
            bool isHover = hover.HasValue && hover.Value == p;
            bool showHighlight = isLegal || isHover;
            view.Highlight.enabled = showHighlight;
            if (!showHighlight) return;

            string highlightKey = isHover ? ArtKeys.HighlightHover : ArtKeys.HighlightTarget;
            if (UiArt.Apply(view.Highlight, highlightKey)) return;
            view.Highlight.sprite = Shapes.Frame;
            view.Highlight.type = Image.Type.Sliced;
            view.Highlight.pixelsPerUnitMultiplier = 1.4f;
            view.Highlight.color = isHover
                ? Color.white
                : strongHighlight ? Palette.Legal : Palette.Legal.WithAlpha(0.45f);
        }

        /// <summary>
        /// Decoration variants (rules §11) are picked from the cell itself, so a tile never changes look between renders and
        /// never implies a state the rules do not have.
        /// </summary>
        static string FloorVariant(GridPos p)
        {
            switch (Hash.Of((ulong)p.Index, 0x5FL) % 5)
            {
                case 0: return ArtKeys.FloorCracked;
                case 1: return ArtKeys.FloorMoss;
                default: return ArtKeys.FloorStone;
            }
        }

        static string WallVariant(FloorState floor, GridPos p)
        {
            int neighbours = 0;
            foreach (var d in Directions.All)
            {
                var n = p.Step(d);
                if (!n.InBounds || floor[n].Terrain == Terrain.Wall) neighbours++;
            }
            if (neighbours >= 3) return ArtKeys.WallCorner;
            return Hash.Of((ulong)p.Index, 0x70UL) % 4 == 0 ? ArtKeys.TorchWall : ArtKeys.Wall;
        }

        /// <summary>Cover stones sit lighter and cooler than a revealed wall, so the two never read as the same tile.</summary>
        static readonly Color CoverTint = new Color(0.78f, 0.80f, 0.88f);

        void DrawCell(CellParts view, RunState run, FloorState floor, CellState cell, GridPos p, bool exitOpen)
        {
            // Everything is covered until the hero clicks it, and every cover is drawn exactly alike whatever is under it,
            // the exit included (D-023 amendment). Step by Step's sensing adds its clue marker on top; the cover itself never changes.
            if (cell.Knowledge != Knowledge.Revealed)
            {
                if (!ApplyTileArt(view, ArtKeys.Wall, CoverTint)) SetPlaceholderBase(view, Palette.FloorUnseen, Palette.StoneDark);
                if (cell.Knowledge == Knowledge.Sensed) Icons.Clues(view.Icons, Board.ClueAt(floor, p));
                return;
            }
            if (cell.Terrain == Terrain.Door)
            {
                if (!ApplyTileArt(view, ArtKeys.FloorStone, Color.white)) SetPlaceholderBase(view, Palette.Stone, Palette.StoneDark);
                Icons.Door(view.Icons, cell.IsOpenDoor);
                return;
            }
            if (cell.Terrain == Terrain.Wall)
            {
                // Walls must read as blockers at a glance: darker and warmer than any floor state
                // (unseen floors are dark but cool-grey), with a dark border.
                if (ApplyTileArt(view, WallVariant(floor, p), WallArtTint) || ApplyTileArt(view, ArtKeys.Wall, WallArtTint))
                {
                    view.Edge.enabled = true;
                    view.Edge.color = new Color(0f, 0f, 0f, 0.6f);
                    return;
                }
                SetPlaceholderBase(view, Palette.Wall, Palette.WallEdge);
                Icons.WallBricks(view.Icons);
                return;
            }
            if (cell.Terrain == Terrain.Pit)
            {
                // Water is the sheet's second look for a pit: nobody crosses either.
                if (ApplyTileArt(view, ArtKeys.Pit, Color.white) || ApplyTileArt(view, ArtKeys.TrapPit, Color.white)
                    || ApplyTileArt(view, ArtKeys.Water, Color.white)) return;
                SetPlaceholderBase(view, Palette.Stone, Palette.StoneDark);
                Icons.Pit(view.Icons);
                return;
            }

            if (!ApplyTileArt(view, FloorVariant(p), Color.white) && !ApplyTileArt(view, ArtKeys.FloorStone, Color.white))
                SetPlaceholderBase(view, Palette.FloorRevealed, Palette.StoneLight.Dim(1.2f));
            if (cell.IsExit) Icons.Exit(view.Icons, exitOpen);
            // The way in: the reference's raised stone staircase, then the flat stair-up tile.
            else if (p == floor.Start && !floor.IsVault && !Icons.TryArt(view.Icons, ArtKeys.EntranceStairs, CellSize))
                Icons.TryArt(view.Icons, ArtKeys.StairUp, CellSize);
            if (cell.Hazard == HazardKind.Spikes) Icons.Spikes(view.Icons);
            else if (cell.Hazard == HazardKind.Bomb) Icons.Bomb(view.Icons, cell.BombArmed, cell.BombFuse);
            else if (cell.Hazard == HazardKind.Lava) Icons.Lava(view.Icons);
            if (cell.Content == ContentKind.Fountain) Icons.Fountain(view.Icons, cell.Used);
            else if (cell.Content == ContentKind.Teleport) Icons.Teleport(view.Icons);
            else if (cell.Content == ContentKind.PressurePlate) Icons.PressurePlate(view.Icons, cell.Used);
            else if (cell.Content == ContentKind.Key) Icons.Key(view.Icons);
            else if (cell.Content == ContentKind.Chest)
            {
                Icons.Chest(view.Icons, cell.ChestOpened, premium: cell.Premium);
                if (!cell.ChestOpened)
                {
                    // Above the tokens: in Free Roam the hero often stands on the chest it is opening, and the token
                    // would hide a meter drawn on the tile itself.
                    Icons.ChestProgress(view.Labels, cell.ChestTaps, ClickDungeon.Simulation.Chests.TapsToOpen(run, cell));
                }
            }
            else if (cell.Content == ContentKind.Potion) Icons.Potion(view.Icons);
            // A sleeping mimic is drawn exactly as a closed chest, meter and all - no token, badge or bar (D-061).
            else if (Board.SleepingMimicAt(floor, p))
            {
                Icons.Chest(view.Icons, false, premium: false);
                Icons.ChestProgress(view.Labels, 0, ClickDungeon.Simulation.Chests.TapsToOpen(run, cell));
            }
        }

        /// <summary>Uses tile art for the cell base when the catalog has it (tinted for knowledge state).</summary>
        static bool ApplyTileArt(CellParts view, string key, Color tint)
        {
            if (!Art.TryGetSprite(key, out var sprite)) return false;
            view.Base.sprite = sprite;
            view.Base.type = Image.Type.Simple;
            view.Base.color = tint;
            view.Edge.enabled = false;
            return true;
        }

        static void SetPlaceholderBase(CellParts view, Color fill, Color edge)
        {
            view.Base.sprite = Shapes.Rounded;
            view.Base.type = Image.Type.Sliced;
            view.Base.color = fill;
            view.Edge.enabled = true;
            view.Edge.color = edge;
        }

        void DrawThreats(CellParts view, List<ThreatKind> kinds, int damage, bool enemyHere)
        {
            if (kinds == null) return;
            bool slam = kinds.Contains(ThreatKind.Slam);
            bool attack = kinds.Contains(ThreatKind.Attack);
            bool fire = kinds.Contains(ThreatKind.Fire);
            bool blast = kinds.Contains(ThreatKind.BombBlast);
            bool armed = kinds.Contains(ThreatKind.BombArmed);
            bool summon = kinds.Contains(ThreatKind.Summon);
            // D-058. A charge is a blow, so it wears the melee warning; without this it fell through to the bomb-blast
            // style and a boar's line read as "explosion here". A throw marks where a bomb lands: no damage yet, so it
            // had no branch at all and drew nothing - the one warning the Bomber depends on.
            bool charge = kinds.Contains(ThreatKind.Charge);
            bool thrown = kinds.Contains(ThreatKind.Throw);
            // D-061/D-062: a web and a boss's arrival do no damage, but each must still be marked a turn ahead.
            bool web = kinds.Contains(ThreatKind.Web);
            bool arrive = kinds.Contains(ThreatKind.Arrive);

            if (damage > 0)
            {
                bool melee = attack || charge;
                var primary = slam ? ThreatKind.Slam : melee ? ThreatKind.Attack : fire ? ThreatKind.Fire : ThreatKind.BombBlast;
                var fill = slam ? Palette.Slam : melee ? Palette.Danger : fire ? Palette.FireLane : Palette.Fuse;
                if (TileOverlay(view, ArtKeys.DangerOverlay(primary)) == null)
                {
                    Icons.Shape(view.Overlay, Shapes.Rounded, fill.WithAlpha(0.32f), Vector2.zero, new Vector2(CellWidth - 6f, CellSize - 6f));
                    var frame = Icons.Shape(view.Overlay, Shapes.Frame, fill, Vector2.zero, new Vector2(CellWidth - 2f, CellSize - 2f));
                    frame.type = Image.Type.Sliced;
                    frame.pixelsPerUnitMultiplier = 1.2f;
                }

                // Telegraph labels sit in the tile's top band: the bottom edge is covered by the intent badge of an
                // enemy on the tile below. When an enemy stands here, drop the band below its own badge.
                float labelY = enemyHere ? CellSize * 0.5f - 38f : CellSize * 0.5f - 24f;

                // Shape cue independent of colour: warning icon in the corner.
                var corner = new Vector2(-CellWidth * 0.5f + 20f, labelY);
                if (!Icons.TryArt(view.Labels, ArtKeys.DangerWarning, 32f, corner))
                {
                    Icons.Shape(view.Labels, Shapes.Triangle, fill, corner, new Vector2(30f, 28f));
                    Icons.Label(view.Labels, "!", 18, Color.white, corner + new Vector2(0f, -3f), new Vector2(20f, 20f));
                }

                // Damage and threat type stay live text with or without art.
                string tag = slam ? "SLAM" : blast ? "BOOM" : fire ? "FIRE" : charge && !attack ? "GORE" : "HIT";
                string tagColor = ColorUtility.ToHtmlStringRGB(fill.Dim(1.4f));
                var threatLabel = Icons.Label(view.Labels, $"<color=#{tagColor}>{tag}</color> -{damage}", 22, Color.white,
                    new Vector2(12f, labelY), new Vector2(96f, 30f));
                threatLabel.alignment = TextAnchor.MiddleRight;
            }
            else if (armed || thrown)
            {
                if (TileOverlay(view, ArtKeys.DangerOverlay(ThreatKind.BombArmed)) == null)
                    Icons.Shape(view.Overlay, Shapes.Rounded, Palette.Fuse.WithAlpha(0.14f), Vector2.zero, new Vector2(CellWidth - 6f, CellSize - 6f));
                // A lit bomb lands here next turn. Worded apart from an armed bomb, which is already on the tile.
                if (thrown && !armed)
                {
                    float bandY = enemyHere ? CellSize * 0.5f - 38f : CellSize * 0.5f - 24f;
                    string fuse = ColorUtility.ToHtmlStringRGB(Palette.Fuse.Dim(1.4f));
                    var landing = Icons.Label(view.Labels, $"<color=#{fuse}>BOMB</color>", 22, Color.white,
                        new Vector2(12f, bandY), new Vector2(96f, 30f));
                    landing.alignment = TextAnchor.MiddleRight;
                }
            }

            if (damage <= 0 && (web || arrive))
            {
                var tint = web ? Palette.Steel : Palette.Summon;
                Icons.Shape(view.Overlay, Shapes.Rounded, tint.WithAlpha(0.18f), Vector2.zero, new Vector2(CellWidth - 6f, CellSize - 6f));
                float bandY = enemyHere ? CellSize * 0.5f - 38f : CellSize * 0.5f - 24f;
                string hex = ColorUtility.ToHtmlStringRGB(tint.Dim(1.4f));
                var mark = Icons.Label(view.Labels, $"<color=#{hex}>{(web ? "WEB" : "ARRIVES")}</color>", 22, Color.white,
                    new Vector2(12f, bandY), new Vector2(110f, 30f));
                mark.alignment = TextAnchor.MiddleRight;
            }

            if (summon && Icons.TryArtImage(view.Overlay, ArtKeys.DangerOverlay(ThreatKind.Summon), CellSize) == null)
            {
                Icons.Shape(view.Overlay, Shapes.Ring, Palette.Summon, Vector2.zero, new Vector2(CellSize - 20f, CellSize - 20f));
                Icons.Label(view.Labels, "+", 40, Palette.Summon, Vector2.zero, new Vector2(40f, 40f));
            }
        }

        /// <summary>A danger overlay covers the whole tile, however wide.</summary>
        static Image TileOverlay(CellParts view, string key)
        {
            var image = Icons.TryArtImage(view.Overlay, key, CellSize);
            if (image == null) return null;
            image.preserveAspect = false;
            image.rectTransform.Stretch();
            return image;
        }

        void RenderTokens(RunState run, ContentCatalog catalog, bool animate)
        {
            var alive = new HashSet<int> { HeroTokenId };
            string heroId = run.Hero.IdentityId;
            UpsertToken(HeroTokenId, "hero:" + heroId + ":" + run.Hero.Guard, run.Hero.Pos, animate,
                body => Icons.Hero(body, run.Hero.Guard, heroId), null, null, null, 0, 0);

            foreach (var enemy in run.Floor.Enemies)
            {
                if (!enemy.Awake) continue;
                alive.Add(enemy.Id);
                var def = catalog.Enemy(enemy.DefId);
                var e = enemy;
                UpsertToken(enemy.Id, def.Id + ":" + enemy.Mode + ":" + ActorAnimations.Pose(enemy, def) + ":" + enemy.CarriesKey, enemy.Pos, animate,
                    body =>
                    {
                        Icons.Enemy(body, def, e.Mode, e.Intent.Kind);
                        // The key warden shows what it carries, so the player knows whom to chase (D-061).
                        if (e.CarriesKey) Icons.Key(Icons.Group(body, new Vector2(34f, 30f)));
                    },
                    Lines.IntentBadge(enemy, def, Renown.Hit(run, catalog, 0) + EnemyAi.Fury(enemy)), ArtKeys.IntentIcon(enemy.Intent.Kind),
                    BadgeColor(enemy.Intent.Kind), enemy.Hp, enemy.MaxHp);
            }

            var dead = new List<int>();
            foreach (var pair in _tokens)
                if (!alive.Contains(pair.Key)) dead.Add(pair.Key);
            foreach (var id in dead)
            {
                var token = _tokens[id];
                _tokens.Remove(id);
                var group = token.Rect.gameObject.AddComponent<CanvasGroup>();
                // A defeat animation plays out before the token fades.
                float delay = 0f;
                if (_pendingCues != null && _pendingCues.TryGetValue(id, out var cue) && cue.Animation == "defeat")
                    delay = PlayCue(token, cue, true);
                _host.StartCoroutine(FadeAndDestroy(token.Rect, group, delay));
            }
        }

        static System.Collections.IEnumerator FadeAndDestroy(RectTransform rt, CanvasGroup group, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                if (rt == null) yield break;
                group.alpha = 1f - t / 0.35f;
                rt.localScale = Vector3.one * (1f - 0.3f * t / 0.35f);
                yield return null;
            }
            if (rt != null) UnityEngine.Object.Destroy(rt.gameObject);
        }

        void UpsertToken(int id, string visualKey, GridPos pos, bool animate, Action<RectTransform> draw,
            string badge, string badgeIconKey, Color? badgeColor, int hp, int maxHp)
        {
            if (!_tokens.TryGetValue(id, out var token))
            {
                token = CreateToken(id);
                token.Rect.anchoredPosition = CellPosition(pos);
                token.Pos = pos;
                _tokens[id] = token;
            }

            if (token.VisualKey != visualKey)
            {
                Clear(token.Body);
                draw(token.Body);
                token.VisualKey = visualKey;
            }

            if (token.Pos != pos)
            {
                // Each placement supersedes any move still running on this token, so the latest position always wins.
                int move = ++token.MoveVersion;
                if (animate) _host.StartCoroutine(Tween.MoveTo(token.Rect, CellPosition(pos), 0.14f, () => token.MoveVersion == move));
                else token.Rect.anchoredPosition = CellPosition(pos);
                token.Pos = pos;
            }

            bool hasBadge = !string.IsNullOrEmpty(badge);
            token.BadgeBack.gameObject.SetActive(hasBadge);
            if (hasBadge)
            {
                token.Badge.text = badge;
                token.BadgeBack.color = badgeColor ?? Palette.NavyLight;

                // Intent icon art sits at the left of the pill; the text stays so the badge never relies on the icon alone.
                bool hasIcon = Art.TryGetSprite(badgeIconKey, out var iconSprite);
                token.BadgeIcon.gameObject.SetActive(hasIcon);
                if (hasIcon) token.BadgeIcon.sprite = iconSprite;
                token.BadgeBack.rectTransform.sizeDelta = new Vector2(hasIcon ? 128f : 104f, 30f);
                token.Badge.rectTransform.Stretch(hasIcon ? 30f : 0f, 0f, hasIcon ? 6f : 0f, 0f);
            }

            token.HpBack.gameObject.SetActive(maxHp > 0);
            if (maxHp > 0) token.HpFill.anchorMax = new Vector2(Mathf.Clamp01(hp / (float)maxHp), 1f);
        }

        Token CreateToken(int id)
        {
            var rt = UiFactory.Rect(_tokenLayer, id == HeroTokenId ? "Hero" : $"Enemy {id}");
            rt.Place(Center, Center, Vector2.zero, new Vector2(CellSize, CellSize));
            var body = UiFactory.Rect(rt, "Body");
            body.Stretch();

            var badgeBack = UiFactory.Image(rt, "Badge", Palette.NavyLight, Shapes.Rounded, true);
            badgeBack.pixelsPerUnitMultiplier = 4f;
            badgeBack.rectTransform.Place(Center, Center, new Vector2(0f, CellSize * 0.5f - 6f), new Vector2(104f, 30f));
            var badge = UiFactory.Text(badgeBack.rectTransform, "Text", "", 18, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            badge.rectTransform.Stretch();
            badge.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiFactory.Outline(badge, new Color(0f, 0f, 0f, 0.7f), 1f);
            var badgeIcon = UiFactory.Image(badgeBack.rectTransform, "Icon", Color.white, null);
            badgeIcon.preserveAspect = true;
            badgeIcon.rectTransform.Place(new Vector2(0f, 0.5f), Center, new Vector2(17f, 0f), new Vector2(26f, 26f));
            badgeIcon.gameObject.SetActive(false);

            var hpBack = UiFactory.Image(rt, "HpBack", Palette.HpBack, null);
            // The monster pack's health bar when it is in the catalog, the flat bar otherwise.
            bool barArt = UiArt.Apply(hpBack, ArtKeys.EnemyHpBack);
            hpBack.rectTransform.Place(Center, Center, new Vector2(0f, -CellSize * 0.5f + 12f), new Vector2(84f, barArt ? 16f : 10f));
            var hpFill = UiFactory.Image(hpBack.rectTransform, "HpFill", Palette.Hp, null);
            if (barArt) UiArt.Apply(hpFill, ArtKeys.EnemyHpFill);
            hpFill.rectTransform.anchorMin = Vector2.zero;
            hpFill.rectTransform.anchorMax = Vector2.one;
            hpFill.rectTransform.offsetMin = Vector2.zero;
            hpFill.rectTransform.offsetMax = Vector2.zero;

            return new Token { Rect = rt, Body = body, BadgeBack = badgeBack, BadgeIcon = badgeIcon, Badge = badge, HpBack = hpBack.rectTransform, HpFill = hpFill.rectTransform };
        }

        void PlayPendingCues()
        {
            if (_pendingCues == null) return;
            var cues = _pendingCues;
            _pendingCues = null;
            foreach (var pair in cues)
                if (_tokens.TryGetValue(pair.Key, out var token))
                    PlayCue(token, pair.Value, ActorAnimations.Holds(pair.Key, pair.Value.Animation));
        }

        /// <summary>
        /// Plays a one-shot action animation over the token when art exists and returns its length (0 = nothing played).
        /// The token's standing art hides meanwhile and returns afterwards unless the pose holds.
        /// </summary>
        float PlayCue(Token token, ActorAnimations.Cue cue, bool hold)
        {
            if (token == null || token.Rect == null) return 0f;
            if (UserPrefs.ReducedMotion && !hold) return 0f;

            ArtCatalog.Entry entry = null;
            foreach (var animation in ActorAnimations.Chain(cue.Animation))
                if (Art.TryGet(ArtKeys.Actor(cue.ContentId, animation), out entry))
                    break;
            if (entry == null) return 0f;

            StopCue(token);
            bool boss = _catalog != null && _catalog.HasEnemy(cue.ContentId) && _catalog.Enemy(cue.ContentId).IsBoss;
            float size = boss ? CellSize * 1.4f : CellSize;
            var image = UiFactory.Image(token.Rect, "Anim " + entry.Key, Color.white, entry.Frames[0]);
            image.preserveAspect = true;
            image.rectTransform.Place(Center, Center, Vector2.zero, new Vector2(size, size));
            image.transform.SetSiblingIndex(token.Body.GetSiblingIndex() + 1);
            token.Body.gameObject.SetActive(false);
            token.Anim = image;

            var animator = image.gameObject.AddComponent<SpriteFrameAnimator>();
            animator.PlayOnce(image, entry.Frames, entry.Fps, hold ? (Action)null : () => StopCue(token));
            return entry.Frames.Length / Mathf.Max(1f, entry.Fps);
        }

        void PlayPendingFx()
        {
            if (_pendingFx == null) return;
            var effects = _pendingFx;
            _pendingFx = null;
            foreach (var effect in effects) PlayFx(effect);
        }

        /// <summary>
        /// One-shot board effect on the FX layer, below popups, when art exists. Reduced Motion skips effects:
        /// popups and the log still say what happened.
        /// </summary>
        void PlayFx(BoardFx.Effect effect)
        {
            if (UserPrefs.ReducedMotion || !effect.Cell.InBounds || !Art.TryGet(effect.Key, out var entry)) return;
            float size = effect.Tiles * CellSize + (effect.Tiles - 1) * Gap;
            var image = UiFactory.Image(_fxLayer, "FX " + entry.Key, Color.white, entry.Frames[0]);
            image.preserveAspect = true;
            image.rectTransform.Place(Center, Center, CellPosition(effect.Cell), new Vector2(size, size));
            image.transform.SetAsFirstSibling();
            var animator = image.gameObject.AddComponent<SpriteFrameAnimator>();
            animator.PlayOnce(image, entry.Frames, entry.Fps, () => UiFactory.SafeDestroy(image.gameObject));
        }

        static void StopCue(Token token)
        {
            if (token.Anim != null) UiFactory.SafeDestroy(token.Anim.gameObject);
            token.Anim = null;
            if (token.Body != null) token.Body.gameObject.SetActive(true);
        }

        static Color BadgeColor(IntentKind kind)
        {
            switch (kind)
            {
                case IntentKind.Attack:
                case IntentKind.Fire:
                case IntentKind.Slam:
                    return Palette.Danger.Dim(0.85f);
                case IntentKind.Summon:
                case IntentKind.PuffUp:
                    return Palette.Summon.Dim(0.7f);
                case IntentKind.Move:
                    return Palette.NavyLight;
                default:
                    return Palette.StoneLight;
            }
        }

        /// <summary>Vertical gap between popups that land on the same tile in one turn.</summary>
        public const float PopupSpacing = 46f;

        int _popupFrame = -1;
        readonly Dictionary<int, int> _popupsPerCell = new Dictionary<int, int>();

        /// <summary>Starts fresh popup stacks. Called once per turn, so popups from different turns never stack together.</summary>
        public void BeginPopupBatch()
        {
            _popupFrame = Time.frameCount;
            _popupsPerCell.Clear();
        }

        public void Popup(GridPos p, string text, Color color)
        {
            const float height = 60f;
            const float rise = 70f;
            const float margin = 12f;

            var label = UiFactory.Text(_fxLayer, "Popup", text, 40, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            float halfFrame = FrameSize * 0.5f - margin;
            float width = Mathf.Min(label.preferredWidth + 16f, halfFrame * 2f);

            // Keep the whole float path inside the board frame so popups never drift over the HUD
            // (top row) or the side panels (edge columns).
            var start = CellPosition(p) + new Vector2(0f, 24f);
            float top = halfFrame - height * 0.5f - rise;
            float bottom = -halfFrame + height * 0.5f;
            start.y = Mathf.Min(start.y, top);
            // Several popups on one tile in one turn (DAZED, -2, BLOCK!) stack instead of printing on top of each other:
            // downward while there is room, then upward above the first, so bottom-row tiles stay readable too.
            if (_popupFrame != Time.frameCount)
            {
                _popupFrame = Time.frameCount;
                _popupsPerCell.Clear();
            }
            _popupsPerCell.TryGetValue(p.Index, out int stacked);
            _popupsPerCell[p.Index] = stacked + 1;
            int roomBelow = Mathf.FloorToInt((start.y - bottom) / PopupSpacing);
            start.y = stacked <= roomBelow
                ? start.y - stacked * PopupSpacing
                : Mathf.Min(start.y + (stacked - roomBelow) * PopupSpacing, top);
            float maxX = halfFrame - width * 0.5f;
            start.x = Mathf.Clamp(start.x, -maxX, maxX);

            label.rectTransform.Place(Center, Center, start, new Vector2(width, height));
            UiFactory.Outline(label, new Color(0f, 0f, 0f, 0.9f), 2.5f);
            _host.StartCoroutine(Tween.Popup(label.rectTransform, label, rise, 1.0f));
        }

        /// <summary>
        /// Drops every popup and effect still floating. A new floor is all covers, so a popup left over from the floor
        /// before it ("KEY!", "-2") would hang over a covered tile and read as a hint (D-023 amendment).
        /// </summary>
        public void ClearEffects()
        {
            for (int i = _fxLayer.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(_fxLayer.GetChild(i).gameObject);
            _popupsPerCell.Clear();
        }

        public void Shake()
        {
            if (!UserPrefs.ScreenShake || UserPrefs.ReducedMotion) return;
            _host.StartCoroutine(Tween.Shake(_shake, 12f, 0.25f));
        }

        public void Nudge(GridPos p)
        {
            if (!p.InBounds) return;
            _host.StartCoroutine(Tween.Punch(_cells[p.Index].Rect, 0.08f, 0.2f));
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                child.SetActive(false);
                UiFactory.SafeDestroy(child);
            }
        }

        static void ClearExcept(Transform t, Transform keep)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i);
                if (child == keep) continue;
                child.gameObject.SetActive(false);
                UiFactory.SafeDestroy(child.gameObject);
            }
        }
    }
}
