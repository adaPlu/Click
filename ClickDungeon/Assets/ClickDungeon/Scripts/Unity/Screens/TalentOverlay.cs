using System;
using System.Collections.Generic;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// The TALENTS screen (D-037): a class's tree drawn as a constellation. Each path rises from tier 1 at the bottom to its
    /// capstone at the top; every talent is an icon node, lit when learned, gold-ringed when it can be learned now and dark
    /// while locked. Tapping a node shows it in the detail panel, which says what learning it changes before LEARN confirms.
    /// Built from the catalog, so a new class with a tree needs no change here.
    /// </summary>
    public sealed class TalentOverlay
    {
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        const float NodeSize = 108f, CapstoneSize = 136f;

        readonly RectTransform _root;
        readonly RectTransform _tree;
        readonly RectTransform _tabs;
        readonly Image _portrait;
        readonly Text _title, _level, _points, _footer;
        readonly RectTransform _detailIcon;
        readonly Text _detailName, _detailPath, _detailRank, _detailSummary, _detailEffect, _detailChange, _detailLock;
        readonly UiFactory.ButtonParts _learn, _reset;
        ContentCatalog _catalog;
        ProfileState _profile;
        Action _changed;
        string _classId;
        string _selected;
        string _note;
        /// <summary>Built for a phone held upright (D-042): header, tree and detail stacked on a 1080 × 1920 stage.</summary>
        readonly bool _upright;

        public TalentOverlay(RectTransform parent)
        {
            _upright = RefLayout.Portrait;
            _root = UiFactory.Rect(parent, "Talents");
            _root.Stretch();
            var dim = UiFactory.Image(_root, "Dim", new Color(0f, 0f, 0f, 0.8f));
            RefLayout.StretchPastStage(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UiFactory.Rect(_root, "Panel");
            panel.Place(Center, Center, Vector2.zero, new Vector2(1840f, 1040f));
            var back = UiFactory.Image(panel, "Back", new Color(0.04f, 0.05f, 0.09f), Shapes.Rounded, true);
            back.rectTransform.Stretch();
            var border = UiFactory.Image(panel, "Border", Palette.Gold, Shapes.Frame, true);
            border.rectTransform.Stretch();

            // Header: whose tree, the level and the points to spend.
            var crest = UiFactory.Rect(panel, "Crest");
            crest.Place(TopLeft, TopLeft, new Vector2(34f, -24f), new Vector2(104f, 104f));
            UiFactory.Image(crest, "Back", Palette.Navy, Shapes.Circle).rectTransform.Stretch();
            // No runtime masks here (they crashed the player on exit): the square portrait sits inside the ring.
            _portrait = UiFactory.Image(crest, "Face", Color.white);
            _portrait.rectTransform.Stretch(16, 16, 16, 16);
            _portrait.preserveAspect = true;
            var crestRing = UiFactory.Image(panel, "CrestRing", Palette.Gold, Shapes.Ring);
            crestRing.rectTransform.Place(TopLeft, TopLeft, new Vector2(30f, -20f), new Vector2(112f, 112f));

            _title = UiFactory.Text(panel, "Title", "", 46, Palette.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            _title.rectTransform.Place(TopLeft, TopLeft, new Vector2(160f, -22f), new Vector2(1000f, 60f));
            _title.resizeTextForBestFit = true;
            _title.resizeTextMinSize = 28;
            _title.resizeTextMaxSize = 46;
            UiFactory.Shadow(_title, Color.black, 3f);
            _level = UiFactory.Text(panel, "Level", "", 24, Palette.TextLight, TextAnchor.MiddleLeft);
            _level.rectTransform.Place(TopLeft, TopLeft, new Vector2(162f, -84f), new Vector2(700f, 34f));
            _points = UiFactory.Text(panel, "Points", "", 28, Palette.Safe, TextAnchor.MiddleLeft, FontStyle.Bold);
            _points.rectTransform.Place(TopLeft, TopLeft, new Vector2(162f, -116f), new Vector2(700f, 36f));

            _tabs = UiFactory.Rect(panel, "ClassTabs");
            _tabs.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -42f), new Vector2(600f, 80f));

            _tree = UiFactory.Rect(panel, "Tree");
            _tree.Place(TopLeft, TopLeft, new Vector2(30f, -160f), new Vector2(1150f, 820f));

            // Detail panel: the chosen talent, what learning it changes, and LEARN.
            var detail = UiFactory.Rect(panel, "Detail");
            detail.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -160f), new Vector2(600f, 820f));
            UiFactory.Image(detail, "Back", Palette.Navy.WithAlpha(0.9f), Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(detail, "Edge", Palette.GoldDark, Shapes.Frame, true).rectTransform.Stretch();
            _detailIcon = UiFactory.Rect(detail, "Icon");
            _detailIcon.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(170f, 170f));
            _detailName = Line(detail, "Name", -206f, 40, Palette.Gold, FontStyle.Bold, 52f);
            _detailPath = Line(detail, "Path", -256f, 22, Palette.TextDim, FontStyle.Bold, 30f);
            _detailRank = Line(detail, "Rank", -290f, 26, Palette.TextLight, FontStyle.Bold, 34f);
            _detailSummary = Line(detail, "Summary", -330f, 24, Palette.Parchment, FontStyle.Italic, 70f);
            _detailEffect = Line(detail, "Effect", -406f, 25, Palette.TextLight, FontStyle.Normal, 90f);
            _detailChange = Line(detail, "Change", -500f, 25, Palette.Safe, FontStyle.Bold, 80f);
            _detailLock = Line(detail, "Lock", -584f, 23, Palette.Danger, FontStyle.Bold, 60f);
            _learn = UiFactory.Button(detail, "Learn", "LEARN", Palette.PlayGreen, 32, Learn);
            _learn.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 108f), new Vector2(420f, 78f));
            _reset = UiFactory.Button(detail, "Reset", "RESET TREE (FREE)", Palette.QuitRed, 22, Reset);
            _reset.Rect.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 26f), new Vector2(260f, 60f));
            var done = UiFactory.Button(detail, "Done", "DONE", Palette.NavyLight, 28, Hide);
            done.Rect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 26f), new Vector2(260f, 60f));

            _footer = UiFactory.Text(panel, "Footer", "", 20, Palette.TextDim, TextAnchor.MiddleLeft);
            _footer.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 14f), new Vector2(1140f, 40f));

            if (_upright) LayoutPortrait(panel, detail, done);
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Upright phone: the header and class tabs on top, the tree (names under the nodes) filling the middle, and the chosen
        /// talent's detail under it with RESET, LEARN and DONE along its foot.
        /// </summary>
        void LayoutPortrait(RectTransform panel, RectTransform detail, UiFactory.ButtonParts done)
        {
            panel.Place(Center, Center, Vector2.zero, new Vector2(1060f, 1900f));
            RefLayout.Top(panel, "Title", 150f, 24f, 880f, 56f);
            RefLayout.Top(panel, "Level", 152f, 84f, 880f, 32f);
            RefLayout.Top(panel, "Points", 152f, 116f, 880f, 34f);
            RefLayout.Top(panel, "ClassTabs", 436f, 166f, 600f, 80f);
            RefLayout.Top(panel, "Tree", 24f, 256f, 1012f, 960f);
            RefLayout.Top(panel, "Detail", 24f, 1230f, 1012f, 570f);
            RefLayout.Top(panel, "Footer", 32f, 1808f, 996f, 80f);
            _footer.alignment = TextAnchor.UpperLeft;
            _footer.horizontalOverflow = HorizontalWrapMode.Wrap;

            // The detail reads across: icon, then name, path and rank beside it; the rest full width below.
            RefLayout.Top(detail, "Icon", 24f, 24f, 130f, 130f);
            foreach (var (text, y, h) in new[] { (_detailName, 22f, 52f), (_detailPath, 80f, 30f), (_detailRank, 114f, 34f) })
            {
                text.rectTransform.Place(TopLeft, TopLeft, new Vector2(176f, -y), new Vector2(812f, h));
                text.alignment = TextAnchor.UpperLeft;
            }
            foreach (var (text, y, h) in new[] { (_detailSummary, 172f, 60f), (_detailEffect, 238f, 84f), (_detailChange, 326f, 64f), (_detailLock, 394f, 56f) })
                text.rectTransform.Place(TopLeft, TopLeft, new Vector2(24f, -y), new Vector2(964f, h));
            _learn.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(380f, 78f));
            _reset.Rect.Place(Vector2.zero, Vector2.zero, new Vector2(24f, 26f), new Vector2(250f, 64f));
            done.Rect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 26f), new Vector2(250f, 64f));
        }

        static Text Line(RectTransform parent, string name, float y, int size, Color color, FontStyle style, float height)
        {
            var text = UiFactory.Text(parent, name, "", size, color, TextAnchor.UpperCenter, style);
            text.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(540f, height));
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = size;
            return text;
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        /// <summary>Opens on one class's tree. <paramref name="note"/> is added to the footer (the game screen's "next run").</summary>
        public void Open(ContentCatalog catalog, ProfileState profile, string classId, Action changed, string note = null)
        {
            _catalog = catalog;
            _profile = profile;
            _changed = changed;
            _note = note;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            ShowClass(classId);
        }

        public void Hide() => _root.gameObject.SetActive(false);

        public void ShowClass(string classId)
        {
            _classId = classId;
            _selected = null;
            // The first talent that can be learned, else the first talent: something is always in the detail panel.
            foreach (var t in _catalog.TalentsOf(classId))
                if (Progression.CanLearn(_profile, _catalog, t.Id)) { _selected = t.Id; break; }
            _selected = _selected ?? _catalog.TalentsOf(classId)[0].Id;
            Redraw();
        }

        public void Select(string talentId)
        {
            _selected = talentId;
            Redraw();
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>The first hero identity of a class: whose face and name head the tree.</summary>
        HeroIdentityDefinition HeroOf(string classId)
        {
            foreach (var identity in _catalog.HeroIdentities.Values)
                if (identity.ClassId == classId) return identity;
            return null;
        }

        void Redraw()
        {
            var heroClass = _catalog.HeroClass(_classId);
            var hero = HeroOf(_classId);
            Color theme = Parse(heroClass.Theme);
            // Just the hero: with eight class tabs beside it (D-063) the old "HERO · CLASS TALENTS" ran under them.
            _title.text = $"{hero?.DisplayName.ToUpperInvariant()}";
            _title.color = theme;
            int level = Progression.Level(_profile);
            _level.text = $"{heroClass.DisplayName.ToUpperInvariant()}  ·  Level {level}  ·  {_profile.Xp} / {Progression.XpForLevel(level + 1)} XP";
            int free = Progression.PointsFree(_profile, _catalog, _classId);
            _points.text = free > 0 ? $"{free} talent point{(free == 1 ? "" : "s")} to spend" : "No points to spend: every level gives one";
            _points.color = free > 0 ? Palette.Safe : Palette.TextDim;
            if (hero != null && (Art.TryGetSprite(ArtKeys.Portrait(hero.Id, "happy"), out var face) || Art.TryGetSprite(ArtKeys.Portrait(hero.Id, "neutral"), out face)))
            {
                _portrait.sprite = face;
                _portrait.color = Color.white;
            }
            _footer.text = "Talents shape every run this class starts and are never used up. Each class has its own points; resetting is free."
                           + (string.IsNullOrEmpty(_note) ? "" : "  " + _note);

            DrawTabs();
            DrawTree(heroClass, theme);
            DrawDetail(heroClass);
        }

        void DrawTabs()
        {
            Clear(_tabs);
            var classes = new List<HeroClassDefinition>();
            foreach (var c in _catalog.HeroClasses.Values)
                if (_catalog.TalentsOf(c.Id).Count > 0) classes.Add(c);
            // The strip runs leftwards from the panel's right edge, so it has to fit every class that has a tree without
            // reaching the title: eight of them at the old fixed 300 ran off the screen (D-063).
            float budget = RefLayout.Portrait ? 940f : 1050f;
            float step = Mathf.Min(300f, budget / Mathf.Max(1, classes.Count));
            int font = step >= 260f ? 26 : step >= 200f ? 22 : step >= 150f ? 18 : 15;
            for (int i = 0; i < classes.Count; i++)
            {
                var c = classes[i];
                bool on = c.Id == _classId;
                int free = Progression.PointsFree(_profile, _catalog, c.Id);
                var parts = UiFactory.Button(_tabs, "Class " + c.Id, c.DisplayName.ToUpperInvariant() + (free > 0 ? $"  ({free})" : ""),
                    on ? Parse(c.Theme).Dim(0.55f) : Palette.NavyLight, font, () => ShowClass(c.Id));
                parts.Rect.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(classes.Count - 1 - i) * step, 0f), new Vector2(step - 12f, 68f));
                parts.Border.color = on ? Parse(c.Theme) : Palette.GoldDark.WithAlpha(0.5f);
                parts.Label.color = on ? Color.white : Palette.TextDim;
            }
        }

        void DrawTree(HeroClassDefinition heroClass, Color theme)
        {
            Clear(_tree);
            // A faint field of stars in the class's colour, the same every time.
            var rng = new System.Random(heroClass.Id.GetHashCode() & 0x7FFFFFFF);
            for (int i = 0; i < 90; i++)
            {
                float size = 2f + (float)rng.NextDouble() * 4f;
                Icons.Shape(_tree, Shapes.Circle, theme.WithAlpha(0.08f + (float)rng.NextDouble() * 0.25f),
                    _upright ? new Vector2((float)rng.NextDouble() * 980f - 490f, (float)rng.NextDouble() * 920f - 460f)
                              : new Vector2((float)rng.NextDouble() * 1120f - 560f, (float)rng.NextDouble() * 780f - 390f), new Vector2(size, size));
            }
            // THE TRIANGLE (D-087). The apex skill sits at the top and two edges run down to a corner skill each;
            // the third edge is the base between those corners. A build walks one edge or the other, which is what
            // makes the tree a choice - so the picture has to show the fork, not three columns side by side.
            var tree = _catalog.TalentsOf(_classId);
            var apexTalent = tree.Find(t => t.BranchId == "apex");
            float top = _upright ? 330f : 272f, bottom = _upright ? -300f : -276f, cx = _upright ? 0f : 40f;
            float half = _upright ? 280f : 430f;
            var apex = new Vector2(cx, top);
            var cornerL = new Vector2(cx - half, bottom);
            var cornerR = new Vector2(cx + half, bottom);

            var skillEdges = new List<string>();
            foreach (var branch in heroClass.Branches)
                if (tree.Exists(t => t.BranchId == branch.Id && t.SkillId != null)) skillEdges.Add(branch.Id);

            foreach (var branch in heroClass.Branches)
            {
                Color color = Parse(branch.Color);
                var path = tree.FindAll(t => t.BranchId == branch.Id);
                if (path.Count == 0) continue;
                path.Sort((l, r) => l.Tier.CompareTo(r.Tier));

                int which = skillEdges.IndexOf(branch.Id);
                bool isBase = which < 0;
                Vector2 from = isBase ? cornerL : apex;
                Vector2 to = isBase ? cornerR : (which == 0 ? cornerL : cornerR);

                // A skill edge ends ON its corner, because that corner IS the skill. The base rail belongs to neither
                // corner - either capstone opens it (D-088) - so its nodes take interior steps.
                int steps = isBase ? path.Count + 1 : path.Count;
                var at = new List<Vector2>();
                for (int i = 0; i < path.Count; i++) at.Add(Vector2.Lerp(from, to, (i + 1) / (float)steps));

                var prev = from;
                for (int i = 0; i < path.Count; i++)
                {
                    // Lit by the NODE'S OWN gate, read from the data. Deriving it from branch order instead agreed with
                    // the catalog only by the accident of how the branches happened to be declared.
                    bool lit = Opened(path[i]);
                    Link(prev, at[i], lit ? color : Palette.StoneLight.WithAlpha(0.35f), lit);
                    prev = at[i];
                }
                // THE FAR END OF THE BASE IS NOT A PREREQUISITE (D-088). Nothing in the tree connects the last bonus to
                // the opposite corner, and drawing it in the same language as a road said a build could walk the rail
                // into the skill it turned down. The triangle still closes, in a line that cannot be mistaken for one.
                if (isBase) Closing(prev, to);

                int side = isBase ? 0 : which == 0 ? -1 : 1;
                float labelWidth = isBase ? half * 2f / steps - 10f : 0f;
                for (int i = 0; i < path.Count; i++)
                {
                    // The node a skill edge ENDS on is a bottom corner, so its name goes under it like the base's do:
                    // beside it, the right-hand corner's name ran off the tree and under the detail panel.
                    bool corner = !isBase && i == path.Count - 1;
                    Node(path[i], at[i], color, corner ? 0 : side, corner ? (_upright ? 180f : 260f) : labelWidth);
                }

                // The branch names have to clear the node names, which now fan outwards: the two skill paths are headed
                // from the top corners they descend from, and the base is named in the empty middle of the triangle.
                var heading = isBase
                    ? new Vector2(cx, bottom + 226f)
                    : new Vector2(cx + (which == 0 ? -1f : 1f) * (_upright ? 300f : 400f), top - 30f);
                var name = UiFactory.Text(_tree, "Branch " + branch.Id, branch.Name, 26, color, TextAnchor.MiddleCenter, FontStyle.Bold);
                name.rectTransform.Place(Center, Center, heading, new Vector2(250f, 32f));
                UiFactory.Shadow(name, Color.black, 2f);
                var focus = UiFactory.Text(_tree, "Focus " + branch.Id, branch.Focus, 15, Palette.TextDim, TextAnchor.MiddleCenter);
                focus.rectTransform.Place(Center, Center, heading + new Vector2(0f, -26f), new Vector2(260f, 22f));
                focus.resizeTextForBestFit = true;
                focus.resizeTextMinSize = 11;
                focus.resizeTextMaxSize = 15;
            }

            if (apexTalent != null) Node(apexTalent, apex, theme, 2, 0f);
        }

        /// <summary>
        /// One edge of the tree, at any angle (D-087). It used to draw a vertical bar and nothing else, because three
        /// parallel branches only ever needed straight down; a triangle's edges run diagonally and its base runs flat,
        /// so the bar is rotated to face its destination.
        /// </summary>
        void Link(Vector2 from, Vector2 to, Color color, bool lit)
        {
            var mid = (from + to) * 0.5f;
            float length = Vector2.Distance(from, to);
            var delta = to - from;
            // The bar is drawn tall and rotated, so zero degrees means "straight down" as it always did.
            float angle = -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg + 180f;

            if (lit)
            {
                var glow = UiFactory.Image(_tree, "Glow", color.WithAlpha(0.25f), null);
                glow.rectTransform.Place(Center, Center, mid, new Vector2(22f, length));
                glow.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
                glow.raycastTarget = false;
            }
            var line = UiFactory.Image(_tree, "Link", color, null);
            line.rectTransform.Place(Center, Center, mid, new Vector2(lit ? 8f : 4f, length));
            line.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
            line.raycastTarget = false;
        }

        /// <summary>Whether this node's own prerequisite is satisfied: a named one, any one of several, or none (the apex).</summary>
        bool Opened(TalentDefinition talent)
        {
            if (talent.Requires != null) return Progression.Rank(_profile, talent.Requires) > 0;
            if (talent.RequiresAny == null || talent.RequiresAny.Length == 0) return true;
            foreach (var id in talent.RequiresAny) if (Progression.Rank(_profile, id) > 0) return true;
            return false;
        }

        /// <summary>
        /// The triangle's closing edge (D-088): the picture's third side, drawn as a row of faint dots so it reads as the
        /// shape's outline and never as a road. It is never lit, because there is nothing behind it to open.
        /// </summary>
        void Closing(Vector2 from, Vector2 to)
        {
            float length = Vector2.Distance(from, to);
            int dots = Mathf.Max(2, Mathf.RoundToInt(length / 34f));
            for (int i = 0; i < dots; i++)
            {
                var at = Vector2.Lerp(from, to, (i + 0.5f) / dots);
                var dot = Icons.Shape(_tree, Shapes.Circle, Palette.StoneLight.WithAlpha(0.22f), at, new Vector2(5f, 5f));
                dot.raycastTarget = false;
            }
        }

        void Node(TalentDefinition talent, Vector2 pos, Color branchColor, int side, float labelWidth)
        {
            int rank = Progression.Rank(_profile, talent.Id);
            bool learned = rank > 0;
            bool available = Progression.CanLearn(_profile, _catalog, talent.Id);
            bool selected = talent.Id == _selected;
            float size = talent.Capstone ? CapstoneSize : NodeSize;

            var parts = UiFactory.Button(_tree, "Node " + talent.Id, "", Palette.Navy, 10, () => Select(talent.Id));
            parts.Rect.Place(Center, Center, pos, new Vector2(size, size));
            parts.Background.sprite = Shapes.Circle;
            parts.Background.type = Image.Type.Simple;
            parts.Background.color = learned ? branchColor.Dim(0.35f) : new Color(0.08f, 0.09f, 0.13f);
            parts.Border.enabled = false;
            parts.Label.enabled = false;

            if (selected)
            {
                var halo = UiFactory.Image(parts.Rect, "Selected", Color.white.WithAlpha(0.9f), Shapes.Ring);
                halo.rectTransform.Stretch(-16, -16, -16, -16);
                halo.raycastTarget = false;
            }

            // The icon (already round, D-037) dimmed while locked.
            var face = UiFactory.Rect(parts.Rect, "Face");
            face.Stretch(6, 6, 6, 6);
            var icon = Icons.TryArtImage(face, ArtKeys.TalentIcon(talent.Id), size - 12f);
            if (icon != null) icon.raycastTarget = false;
            if (icon == null) Icons.Label(face, talent.Name.Substring(0, 1), 40, Palette.TextLight, Vector2.zero, new Vector2(size, size));
            else if (!learned && !available) icon.color = new Color(0.38f, 0.38f, 0.42f, 1f);

            var ring = UiFactory.Image(parts.Rect, "Ring", learned ? branchColor : available ? Palette.Gold : Palette.StoneLight.WithAlpha(0.6f), Shapes.Ring);
            ring.rectTransform.Stretch(-4, -4, -4, -4);
            ring.raycastTarget = false;
            if (talent.Capstone)
            {
                var star = Icons.Shape(parts.Rect, Shapes.Diamond, learned ? branchColor : Palette.GoldDark, new Vector2(0f, size * 0.5f + 6f), new Vector2(26f, 26f));
                star.raycastTarget = false;
            }
            if (!learned && !available)
            {
                var lockIcon = Icons.TryArtImage(parts.Rect, ArtKeys.LockIcon, 38f, new Vector2(size * 0.34f, -size * 0.34f));
                if (lockIcon != null) lockIcon.raycastTarget = false;
            }

            // Rank pill under the node.
            var pill = UiFactory.Image(parts.Rect, "Rank", learned ? branchColor.Dim(0.6f) : new Color(0.1f, 0.1f, 0.14f, 0.95f), Shapes.Rounded, true);
            pill.rectTransform.Place(new Vector2(0.5f, 0f), Center, new Vector2(0f, -6f), new Vector2(64f, 26f));
            pill.raycastTarget = false;
            var count = UiFactory.Text(pill.rectTransform, "Text", $"{rank}/{talent.MaxRank}", 18, learned ? Color.white : Palette.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            count.rectTransform.Stretch();
            count.raycastTarget = false;

            var name = UiFactory.Text(_tree, "Name " + talent.Id, talent.Name, 18, learned ? Palette.TextLight : available ? Palette.Gold : Palette.TextDim,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            // Each name points AWAY from the triangle on its own side: the left edge's to the left, the right edge's to
            // the right, the base's under it and the apex's above. One fixed side suited columns and piled up here.
            float reach = size * 0.5f + 14f;
            float wide = _upright ? 140f : 172f;
            if (side == -1)
            {
                name.alignment = TextAnchor.MiddleRight;
                name.rectTransform.Place(Center, Center, pos + new Vector2(-reach - wide * 0.5f, 0f), new Vector2(wide, 46f));
            }
            else if (side == 1)
            {
                name.alignment = TextAnchor.MiddleLeft;
                name.rectTransform.Place(Center, Center, pos + new Vector2(reach + wide * 0.5f, 0f), new Vector2(wide, 46f));
            }
            else if (side == 2)
            {
                name.rectTransform.Place(Center, Center, pos + new Vector2(0f, reach + 42f), new Vector2(300f, 30f));
            }
            else
            {
                // A fixed drop, not one measured from this node: the capstone corner is wider than the rest, and
                // measuring from it hung its name alone below the others and into the footer.
                name.rectTransform.Place(Center, Center, pos + new Vector2(0f, -NodeSize * 0.5f - 46f), new Vector2(Mathf.Max(96f, labelWidth), 30f));
            }
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 10;
            name.resizeTextMaxSize = 18;
            name.raycastTarget = false;
        }

        void DrawDetail(HeroClassDefinition heroClass)
        {
            var talent = _catalog.Talent(_selected);
            Clear(_detailIcon);
            if (talent == null) return;
            TalentBranch branch = null;
            foreach (var b in heroClass.Branches) if (b.Id == talent.BranchId) branch = b;
            Color color = Parse(branch?.Color ?? heroClass.Theme);

            UiFactory.Image(_detailIcon, "Back", color.Dim(0.3f), Shapes.Circle).rectTransform.Stretch();
            var face = UiFactory.Rect(_detailIcon, "Face");
            face.Stretch(10, 10, 10, 10);
            Icons.TryArtImage(face, ArtKeys.TalentIcon(talent.Id), 150f);
            UiFactory.Image(_detailIcon, "Ring", color, Shapes.Ring).rectTransform.Stretch(-6, -6, -6, -6);

            int rank = Progression.Rank(_profile, talent.Id);
            _detailName.text = talent.Name.ToUpperInvariant();
            _detailName.color = color;
            // The apex belongs to no declared branch, so naming its path printed a leading blank - on the node the
            // screen selects by default, which put it in every screenshot. It says what it is instead.
            string where = branch != null ? $"{branch.Name} PATH" : "WHERE EVERY BUILD STARTS";
            _detailPath.text = where + "  ·  " + (talent.Capstone ? "CAPSTONE (choose one, the other closes)" : $"TIER {talent.Tier}");
            _detailRank.text = $"Rank {rank} / {talent.MaxRank}";
            _detailSummary.text = talent.Summary;
            _detailEffect.text = (talent.MaxRank > 1 ? "Each rank: " : "") + talent.PerRank + ".";
            string locked = Progression.Locked(_profile, _catalog, talent.Id);
            if (rank >= talent.MaxRank)
                _detailChange.text = "Fully learned.";
            else if (talent.MaxRank > 1)
                _detailChange.text = $"Learning it: rank {rank} → {rank + 1}" + (talent.Amount > 1 ? $" (now {rank * talent.Amount}, then {(rank + 1) * talent.Amount})." : ".");
            else
                _detailChange.text = "Learning it adds this to every run this class starts.";
            _detailLock.text = locked != null && rank < talent.MaxRank ? locked : "";

            bool can = locked == null;
            _learn.Button.interactable = can;
            _learn.Background.color = can ? Palette.PlayGreen : Palette.StoneDark;
            _learn.Label.text = rank >= talent.MaxRank ? "LEARNED" : can ? (rank == 0 ? "LEARN" : $"LEARN RANK {rank + 1}") : "LOCKED";
            _reset.Rect.gameObject.SetActive(Progression.PointsSpent(_profile, _catalog, _classId) > 0);
        }

        void Learn()
        {
            if (_selected == null || !Progression.TryLearn(_profile, _catalog, _selected)) return;
            _changed?.Invoke();
            Redraw();
        }

        void Reset()
        {
            Progression.Reset(_profile, _catalog, _classId);
            _changed?.Invoke();
            ShowClass(_classId);
        }

        static void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(rt.GetChild(i).gameObject);
        }

        public static Color Parse(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Palette.Gold;
    }
}
