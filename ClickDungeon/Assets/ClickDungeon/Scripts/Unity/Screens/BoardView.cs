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
        public const float CellSize = 136f;
        public const float Gap = 4f;
        public static readonly float FrameSize = BoardRules.Size * CellSize + (BoardRules.Size - 1) * Gap + 48f;
        const int HeroTokenId = -1;
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
            public Image BadgeBack;
            public Text Badge;
            public RectTransform HpBack;
            public RectTransform HpFill;
            public string VisualKey;
            public GridPos Pos;
        }

        readonly MonoBehaviour _host;
        readonly RectTransform _shake;
        readonly RectTransform _tokenLayer;
        readonly RectTransform _labelLayer;
        readonly RectTransform _fxLayer;
        readonly CellParts[] _cells = new CellParts[BoardRules.CellCount];
        readonly Dictionary<int, Token> _tokens = new Dictionary<int, Token>();

        public BoardView(RectTransform parent, MonoBehaviour host, Vector2 position)
        {
            _host = host;
            Root = UiFactory.Rect(parent, "Board");
            Root.Place(Center, Center, position, new Vector2(FrameSize, FrameSize));

            _shake = UiFactory.Rect(Root, "Shake");
            _shake.Stretch();
            UiFactory.Image(_shake, "FrameShadow", new Color(0f, 0f, 0f, 0.5f), Shapes.Rounded, true).rectTransform.Stretch(-10, -4, -10, -16);
            UiFactory.Image(_shake, "Frame", Palette.Stone, Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(_shake, "FrameEdge", Palette.StoneLight, Shapes.Frame, true).rectTransform.Stretch();
            UiFactory.Image(_shake, "Inner", Palette.StoneDark, Shapes.Rounded, true).rectTransform.Stretch(16, 16, 16, 16);

            var cellLayer = Layer("Cells");
            _tokenLayer = Layer("Tokens");
            _labelLayer = Layer("Labels");
            _fxLayer = Layer("Effects");

            foreach (var p in Board.AllCells) _cells[p.Index] = BuildCell(cellLayer, p);
        }

        public RectTransform Root { get; }
        public event Action<GridPos> CellClicked;
        public event Action<GridPos> CellEntered;
        public event Action<GridPos> CellExited;

        public static Vector2 CellPosition(GridPos p) =>
            new Vector2((p.X - 2) * (CellSize + Gap), (p.Y - 2) * (CellSize + Gap));

        RectTransform Layer(string name)
        {
            var layer = UiFactory.Rect(_shake, name);
            layer.Stretch();
            return layer;
        }

        CellParts BuildCell(RectTransform layer, GridPos p)
        {
            var rt = UiFactory.Rect(layer, $"Cell {p.X},{p.Y}");
            rt.Place(Center, Center, CellPosition(p), new Vector2(CellSize, CellSize));

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
            labels.Place(Center, Center, CellPosition(p), new Vector2(CellSize, CellSize));
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

        public void Render(RunState run, ContentCatalog catalog, List<Threat> threats, HashSet<GridPos> legal, bool strongHighlight,
            GridPos? hover, bool animate)
        {
            var floor = run.Floor;

            var damage = new int[BoardRules.CellCount];
            var threatKinds = new List<ThreatKind>[BoardRules.CellCount];
            foreach (var threat in threats)
            {
                int i = threat.Cell.Index;
                damage[i] += threat.Damage;
                (threatKinds[i] ?? (threatKinds[i] = new List<ThreatKind>())).Add(threat.Kind);
            }

            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                var view = _cells[p.Index];
                Clear(view.Icons);
                Clear(view.Overlay);
                ClearExcept(view.Labels, view.Highlight.transform);

                DrawCell(view, floor, cell, p);
                DrawThreats(view, threatKinds[p.Index], damage[p.Index]);

                // A token hides the tile art beneath it, so repeat a hazard or exit underfoot as a badge above the token.
                bool occupied = run.Hero.Pos == p || floor.EnemyAt(p)?.Awake == true;
                if (occupied && cell.Knowledge == Knowledge.Revealed && (cell.Hazard != HazardKind.None || cell.IsExit))
                    Icons.Underfoot(Icons.Group(view.Labels, new Vector2(-CellSize * 0.5f + 22f, 0f)), cell, floor.ExitUnlocked);

                bool isLegal = legal != null && legal.Contains(p);
                bool isHover = hover.HasValue && hover.Value == p;
                view.Highlight.enabled = isLegal || isHover;
                view.Highlight.color = isHover
                    ? Color.white
                    : strongHighlight ? Palette.Legal : Palette.Legal.WithAlpha(0.45f);
            }

            RenderTokens(run, catalog, animate);
        }

        void DrawCell(CellParts view, FloorState floor, CellState cell, GridPos p)
        {
            if (cell.Terrain == Terrain.Wall)
            {
                if (ApplyTileArt(view, ArtKeys.Wall, Color.white)) return;
                SetPlaceholderBase(view, Palette.Wall, Palette.WallEdge);
                Icons.WallBricks(view.Icons);
                return;
            }
            if (cell.Terrain == Terrain.Pit)
            {
                if (ApplyTileArt(view, ArtKeys.Pit, Color.white)) return;
                SetPlaceholderBase(view, Palette.Stone, Palette.StoneDark);
                Icons.Pit(view.Icons);
                return;
            }

            switch (cell.Knowledge)
            {
                case Knowledge.Revealed:
                    if (!ApplyTileArt(view, ArtKeys.FloorStone, Color.white))
                        SetPlaceholderBase(view, Palette.FloorRevealed, Palette.StoneLight.Dim(1.2f));
                    if (cell.IsExit) Icons.Exit(view.Icons, floor.ExitUnlocked);
                    if (cell.Hazard == HazardKind.Spikes) Icons.Spikes(view.Icons);
                    else if (cell.Hazard == HazardKind.Bomb) Icons.Bomb(view.Icons, cell.BombArmed, cell.BombFuse);
                    if (cell.Content == ContentKind.Key) Icons.Key(view.Icons);
                    else if (cell.Content == ContentKind.Chest) Icons.Chest(view.Icons, cell.ChestOpened);
                    else if (cell.Content == ContentKind.Potion) Icons.Potion(view.Icons);
                    break;
                case Knowledge.Sensed:
                    if (!ApplyTileArt(view, ArtKeys.FloorStone, new Color(0.55f, 0.55f, 0.6f)))
                        SetPlaceholderBase(view, Palette.FloorSensed, Palette.Stone);
                    Icons.Clues(view.Icons, Board.ClueAt(floor, p));
                    break;
                default:
                    if (!ApplyTileArt(view, ArtKeys.FloorStone, new Color(0.3f, 0.3f, 0.34f)))
                        SetPlaceholderBase(view, Palette.FloorUnseen, Palette.StoneDark.Dim(1.3f));
                    break;
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

        void DrawThreats(CellParts view, List<ThreatKind> kinds, int damage)
        {
            if (kinds == null) return;
            bool slam = kinds.Contains(ThreatKind.Slam);
            bool attack = kinds.Contains(ThreatKind.Attack);
            bool fire = kinds.Contains(ThreatKind.Fire);
            bool blast = kinds.Contains(ThreatKind.BombBlast);
            bool armed = kinds.Contains(ThreatKind.BombArmed);
            bool summon = kinds.Contains(ThreatKind.Summon);

            if (damage > 0)
            {
                var fill = slam ? Palette.Slam : attack ? Palette.Danger : fire ? Palette.FireLane : Palette.Fuse;
                Icons.Shape(view.Overlay, Shapes.Rounded, fill.WithAlpha(0.32f), Vector2.zero, new Vector2(CellSize - 6f, CellSize - 6f));
                var frame = Icons.Shape(view.Overlay, Shapes.Frame, fill, Vector2.zero, new Vector2(CellSize - 2f, CellSize - 2f));
                frame.type = Image.Type.Sliced;
                frame.pixelsPerUnitMultiplier = 1.2f;

                // Shape cue independent of colour: warning triangle in the corner.
                var corner = new Vector2(-CellSize * 0.5f + 20f, CellSize * 0.5f - 20f);
                Icons.Shape(view.Labels, Shapes.Triangle, fill, corner, new Vector2(30f, 28f));
                Icons.Label(view.Labels, "!", 18, Color.white, corner + new Vector2(0f, -3f), new Vector2(20f, 20f));
                Icons.Label(view.Labels, $"-{damage}", 28, Color.white, new Vector2(CellSize * 0.5f - 26f, CellSize * 0.5f - 20f), new Vector2(60f, 30f));
                string tag = slam ? "SLAM" : blast ? "BOOM" : fire ? "FIRE" : "HIT";
                Icons.Label(view.Labels, tag, 16, fill.Dim(1.4f), new Vector2(0f, -CellSize * 0.5f + 14f), new Vector2(CellSize, 20f));
            }
            else if (armed)
            {
                Icons.Shape(view.Overlay, Shapes.Rounded, Palette.Fuse.WithAlpha(0.14f), Vector2.zero, new Vector2(CellSize - 6f, CellSize - 6f));
            }

            if (summon)
            {
                Icons.Shape(view.Overlay, Shapes.Ring, Palette.Summon, Vector2.zero, new Vector2(CellSize - 20f, CellSize - 20f));
                Icons.Label(view.Labels, "+", 40, Palette.Summon, Vector2.zero, new Vector2(40f, 40f));
            }
        }

        void RenderTokens(RunState run, ContentCatalog catalog, bool animate)
        {
            var alive = new HashSet<int> { HeroTokenId };
            UpsertToken(HeroTokenId, "hero:" + run.Hero.Guard, run.Hero.Pos, animate,
                body => Icons.Hero(body, run.Hero.Guard), null, null, 0, 0);

            foreach (var enemy in run.Floor.Enemies)
            {
                if (!enemy.Awake) continue;
                alive.Add(enemy.Id);
                var def = catalog.Enemy(enemy.DefId);
                var e = enemy;
                UpsertToken(enemy.Id, def.Id + ":" + enemy.Mode, enemy.Pos, animate,
                    body => Icons.Enemy(body, def, e.Mode), Lines.IntentBadge(enemy, def), BadgeColor(enemy.Intent.Kind), enemy.Hp, enemy.MaxHp);
            }

            var dead = new List<int>();
            foreach (var pair in _tokens)
                if (!alive.Contains(pair.Key)) dead.Add(pair.Key);
            foreach (var id in dead)
            {
                var token = _tokens[id];
                _tokens.Remove(id);
                var group = token.Rect.gameObject.AddComponent<CanvasGroup>();
                _host.StartCoroutine(FadeAndDestroy(token.Rect, group));
            }
        }

        static System.Collections.IEnumerator FadeAndDestroy(RectTransform rt, CanvasGroup group)
        {
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
            string badge, Color? badgeColor, int hp, int maxHp)
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
                if (animate) _host.StartCoroutine(Tween.MoveTo(token.Rect, CellPosition(pos), 0.14f));
                else token.Rect.anchoredPosition = CellPosition(pos);
                token.Pos = pos;
            }

            bool hasBadge = !string.IsNullOrEmpty(badge);
            token.BadgeBack.gameObject.SetActive(hasBadge);
            if (hasBadge)
            {
                token.Badge.text = badge;
                token.BadgeBack.color = badgeColor ?? Palette.NavyLight;
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

            var hpBack = UiFactory.Image(rt, "HpBack", Palette.HpBack, null);
            hpBack.rectTransform.Place(Center, Center, new Vector2(0f, -CellSize * 0.5f + 12f), new Vector2(84f, 10f));
            var hpFill = UiFactory.Image(hpBack.rectTransform, "HpFill", Palette.Hp, null);
            hpFill.rectTransform.anchorMin = Vector2.zero;
            hpFill.rectTransform.anchorMax = Vector2.one;
            hpFill.rectTransform.offsetMin = Vector2.zero;
            hpFill.rectTransform.offsetMax = Vector2.zero;

            return new Token { Rect = rt, Body = body, BadgeBack = badgeBack, Badge = badge, HpBack = hpBack.rectTransform, HpFill = hpFill.rectTransform };
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
            start.y = Mathf.Min(start.y, halfFrame - height * 0.5f - rise);
            float maxX = halfFrame - width * 0.5f;
            start.x = Mathf.Clamp(start.x, -maxX, maxX);

            label.rectTransform.Place(Center, Center, start, new Vector2(width, height));
            UiFactory.Outline(label, new Color(0f, 0f, 0f, 0.9f), 2.5f);
            _host.StartCoroutine(Tween.Popup(label.rectTransform, label, rise, 1.0f));
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
                UnityEngine.Object.Destroy(child);
            }
        }

        static void ClearExcept(Transform t, Transform keep)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i);
                if (child == keep) continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }
}
