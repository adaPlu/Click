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
    /// HERO SELECT (D-024, D-037): the hero's full-body art fills the left of the screen; the right tells who they are, how
    /// they play, what they start with and a glimpse of their talent paths. A roster strip along the bottom holds every
    /// hero, playable ones first and those still coming shown locked. Everything is read from the catalog, so a new hero
    /// needs only its data and art.
    /// </summary>
    public sealed class HeroSelectOverlay
    {
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        /// <summary>One entry of the roster: a playable identity, or a hero still coming.</summary>
        sealed class Entry
        {
            public string Id;
            public string Name;
            public HeroIdentityDefinition Identity;
            public HeroPreview Preview;
            public bool Playable => Identity != null;
        }

        readonly RectTransform _root;
        readonly RectTransform _art;
        readonly RectTransform _roster;
        readonly RectTransform _stats;
        readonly RectTransform _paths;
        readonly RectTransform _stars;
        readonly Text _name, _title, _classLine, _playstyle, _quote, _pathsLabel;
        readonly UiFactory.ButtonParts _choose, _talents;
        readonly List<Entry> _entries = new List<Entry>();
        ContentCatalog _catalog;
        ProfileState _profile;
        Action<string> _choose2;
        Action<string> _openTalents;
        string _current;
        int _index;

        public HeroSelectOverlay(RectTransform parent)
        {
            _root = UiFactory.Rect(parent, "HeroSelect");
            _root.Stretch();
            var dim = UiFactory.Image(_root, "Dim", new Color(0f, 0f, 0f, 0.82f));
            RefLayout.StretchPastStage(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UiFactory.Rect(_root, "Panel");
            panel.Place(Center, Center, Vector2.zero, new Vector2(1860f, 1050f));
            UiFactory.Image(panel, "Back", new Color(0.05f, 0.06f, 0.1f), Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(panel, "Border", Palette.Gold, Shapes.Frame, true).rectTransform.Stretch();

            var heading = UiFactory.Text(panel, "Heading", "CHOOSE YOUR HERO", 34, Palette.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            heading.rectTransform.Place(TopLeft, TopLeft, new Vector2(44f, -16f), new Vector2(700f, 50f));
            UiFactory.Shadow(heading, Color.black, 2f);

            // The art: as large as the left side allows, never stretched.
            var frame = UiFactory.Rect(panel, "ArtFrame");
            frame.Place(TopLeft, TopLeft, new Vector2(110f, -74f), new Vector2(820f, 800f));
            UiFactory.Image(frame, "Back", new Color(0.02f, 0.02f, 0.04f), Shapes.Rounded, true).rectTransform.Stretch();
            _art = UiFactory.Rect(frame, "Art");
            _art.Stretch(10, 10, 10, 10);
            UiFactory.Image(frame, "Edge", Palette.GoldDark, Shapes.Frame, true).rectTransform.Stretch();

            var prev = UiFactory.Button(panel, "Previous", "<", Palette.NavyLight, 60, () => Step(-1));
            prev.Rect.Place(TopLeft, TopLeft, new Vector2(24f, -420f), new Vector2(72f, 120f));
            var next = UiFactory.Button(panel, "Next", ">", Palette.NavyLight, 60, () => Step(1));
            next.Rect.Place(TopLeft, TopLeft, new Vector2(944f, -420f), new Vector2(72f, 120f));

            // Who they are.
            float x = 1060f;
            _name = Text(panel, "Name", x, -70f, 780f, 84f, 76, Palette.Gold, FontStyle.Bold, TextAnchor.MiddleLeft);
            UiFactory.Outline(_name, Palette.Ink, 3f);
            _title = Text(panel, "Title", x, -152f, 780f, 36f, 28, Palette.Parchment, FontStyle.Italic, TextAnchor.MiddleLeft);
            _classLine = Text(panel, "Class", x, -194f, 560f, 34f, 26, Palette.TextLight, FontStyle.Bold, TextAnchor.MiddleLeft);
            _stars = UiFactory.Rect(panel, "Difficulty");
            _stars.Place(TopLeft, TopLeft, new Vector2(x + 560f, -194f), new Vector2(220f, 34f));
            _playstyle = Text(panel, "Playstyle", x, -240f, 760f, 120f, 26, Palette.TextLight, FontStyle.Normal, TextAnchor.UpperLeft);

            var startLabel = Text(panel, "StartLabel", x, -372f, 760f, 30f, 22, Palette.GoldDark, FontStyle.Bold, TextAnchor.MiddleLeft);
            startLabel.text = "STARTS WITH";
            _stats = UiFactory.Rect(panel, "Stats");
            _stats.Place(TopLeft, TopLeft, new Vector2(x, -406f), new Vector2(760f, 150f));

            _pathsLabel = Text(panel, "PathsLabel", x, -566f, 760f, 30f, 22, Palette.GoldDark, FontStyle.Bold, TextAnchor.MiddleLeft);
            _paths = UiFactory.Rect(panel, "Paths");
            _paths.Place(TopLeft, TopLeft, new Vector2(x, -618f), new Vector2(760f, 170f));
            _talents = UiFactory.Button(panel, "ViewTalents", "VIEW TALENTS", Palette.NavyLight, 24, () => _openTalents?.Invoke(Current.Identity?.ClassId));
            _talents.Rect.Place(TopLeft, TopLeft, new Vector2(x + 530f, -562f), new Vector2(230f, 46f));
            _quote = Text(panel, "Quote", x, -800f, 760f, 40f, 22, Palette.TextDim, FontStyle.Italic, TextAnchor.MiddleLeft);

            _roster = UiFactory.Rect(panel, "Roster");
            _roster.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110f, 40f), new Vector2(940f, 110f));

            _choose = UiFactory.Button(panel, "Choose", "CHOOSE", Palette.PlayGreen, 40, Choose);
            _choose.Rect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-300f, 40f), new Vector2(460f, 100f));
            var back = UiFactory.Button(panel, "Back", "BACK", Palette.NavyLight, 30, Hide);
            back.Rect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(240f, 100f));

            _root.gameObject.SetActive(false);
        }

        static Text Text(RectTransform parent, string name, float x, float y, float w, float h, int size, Color color, FontStyle style, TextAnchor anchor)
        {
            var text = UiFactory.Text(parent, name, "", size, color, anchor, style);
            text.rectTransform.Place(TopLeft, TopLeft, new Vector2(x, y), new Vector2(w, h));
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = size;
            UiFactory.Shadow(text, Color.black, 2f);
            return text;
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        Entry Current => _entries[_index];

        /// <summary>
        /// Opens on the chosen hero. <paramref name="choose"/> receives the hero picked; <paramref name="openTalents"/> opens a
        /// class's talent tree.
        /// </summary>
        public void Open(ContentCatalog catalog, ProfileState profile, string current, Action<string> choose, Action<string> openTalents)
        {
            _catalog = catalog;
            _profile = profile;
            _current = current;
            _choose2 = choose;
            _openTalents = openTalents;
            _entries.Clear();
            foreach (var identity in catalog.HeroIdentities.Values)
                _entries.Add(new Entry { Id = identity.Id, Name = identity.DisplayName, Identity = identity });
            foreach (var preview in catalog.ComingSoon)
                _entries.Add(new Entry { Id = preview.Id, Name = preview.DisplayName, Preview = preview });
            _index = Math.Max(0, _entries.FindIndex(e => e.Id == current));
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Redraw();
        }

        public void Hide() => _root.gameObject.SetActive(false);

        public void Show(string heroId)
        {
            int i = _entries.FindIndex(e => e.Id == heroId);
            if (i < 0) return;
            _index = i;
            Redraw();
        }

        void Step(int by)
        {
            _index = (_index + by + _entries.Count) % _entries.Count;
            Redraw();
        }

        void Choose()
        {
            if (!Current.Playable) return;
            _current = Current.Id;
            _choose2?.Invoke(Current.Id);
            Redraw();
        }

        // ------------------------------------------------------------------ drawing

        void Redraw()
        {
            var entry = Current;
            var heroClass = entry.Playable ? _catalog.HeroClass(entry.Identity.ClassId) : null;
            Color theme = heroClass != null ? TalentOverlay.Parse(heroClass.Theme) : Palette.StoneLight;

            DrawArt(entry);
            _name.text = entry.Name.ToUpperInvariant();
            _name.color = entry.Playable ? theme : Palette.TextDim;
            _title.text = entry.Playable ? entry.Identity.Title ?? entry.Identity.Tagline : "Coming soon";
            _classLine.text = entry.Playable
                ? $"{heroClass.DisplayName.ToUpperInvariant()}  ·  {heroClass.Role}"
                : $"{entry.Preview.ClassName.ToUpperInvariant()}  ·  {entry.Preview.Role}";
            _playstyle.text = entry.Playable ? $"{entry.Identity.Tagline}  {heroClass.Playstyle}" : entry.Preview.Blurb + " Not yet playable.";
            _quote.text = entry.Playable && !string.IsNullOrEmpty(entry.Identity.Quote) ? $"“{entry.Identity.Quote}”" : "";
            DrawStars(heroClass);
            DrawStats(heroClass);
            DrawPaths(heroClass);
            DrawRoster();

            bool chosen = entry.Id == _current;
            _choose.Button.interactable = entry.Playable && !chosen;
            _choose.Background.color = !entry.Playable ? Palette.StoneDark : chosen ? Palette.GoldDark : Palette.PlayGreen;
            _choose.Label.text = !entry.Playable ? "LOCKED" : chosen ? "CHOSEN" : $"CHOOSE {entry.Name.ToUpperInvariant()}";
            _talents.Rect.gameObject.SetActive(entry.Playable);
        }

        void DrawArt(Entry entry)
        {
            Clear(_art);
            if (Art.TryGetSprite(ArtKeys.HeroArt(entry.Id), out var sprite))
            {
                var image = UiFactory.Image(_art, "Hero", entry.Playable ? Color.white : new Color(0.3f, 0.3f, 0.36f), sprite);
                image.rectTransform.Stretch();
                image.preserveAspect = true;
            }
            else if (entry.Playable)
            {
                Icons.TryArtImage(_art, ArtKeys.Portrait(entry.Id, "happy"), 600f);
            }
            if (!entry.Playable)
            {
                var lockIcon = Icons.TryArtImage(_art, ArtKeys.LockIcon, 180f, new Vector2(0f, 40f));
                if (lockIcon == null) Icons.Shape(_art, Shapes.Rounded, Palette.StoneLight, new Vector2(0f, 40f), new Vector2(140f, 140f));
                var soon = UiFactory.Text(_art, "Soon", "COMING SOON", 56, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                soon.rectTransform.Place(Center, Center, new Vector2(0f, -110f), new Vector2(700f, 80f));
                UiFactory.Outline(soon, Color.black, 3f);
            }
        }

        void DrawStars(HeroClassDefinition heroClass)
        {
            Clear(_stars);
            if (heroClass == null) return;
            var label = UiFactory.Text(_stars, "Label", "DIFFICULTY", 18, Palette.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            label.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(120f, 30f));
            for (int i = 0; i < 3; i++)
                Icons.Shape(_stars, Shapes.Diamond, i < heroClass.Difficulty ? Palette.Gold : Palette.StoneLight.WithAlpha(0.5f),
                    new Vector2(-110f + 130f + i * 30f + 15f, 0f), new Vector2(22f, 22f));
        }

        void DrawStats(HeroClassDefinition heroClass)
        {
            Clear(_stats);
            if (heroClass == null)
            {
                var none = UiFactory.Text(_stats, "None", "This hero's class is still being forged.", 24, Palette.TextDim, TextAnchor.UpperLeft);
                none.rectTransform.Stretch();
                return;
            }
            var stats = new (string key, string value, string label)[]
            {
                (ArtKeys.Heart, heroClass.MaxHp.ToString(), "HEARTS"),
                (ArtKeys.ManaIcon, heroClass.MaxMana.ToString(), "MANA"),
                (ArtKeys.TalentIcon("k_cleave"), heroClass.SlashDamage.ToString(), "SLASH"),
                (ArtKeys.ShopIcon(ShopItem.PotionRation), $"{heroClass.StartingPotions}×{heroClass.PotionHeal}", "POTIONS"),
                (ArtKeys.TalentIcon("p_unyielding"), heroClass.ShieldCost.ToString(), "SHIELD MP"),
                (ArtKeys.TalentIcon("k_light_step"), $"{heroClass.DashDistance}t/{heroClass.DashCost}", "DASH"),
            };
            for (int i = 0; i < stats.Length; i++)
            {
                var cell = UiFactory.Rect(_stats, "Stat " + stats[i].label);
                cell.Place(TopLeft, TopLeft, new Vector2(i * 126f, 0f), new Vector2(118f, 146f));
                UiFactory.Image(cell, "Back", Palette.Navy.WithAlpha(0.9f), Shapes.Rounded, true).rectTransform.Stretch();
                UiFactory.Image(cell, "Edge", Palette.GoldDark.WithAlpha(0.6f), Shapes.Frame, true).rectTransform.Stretch();
                if (Icons.TryArtImage(cell, stats[i].key, 60f, new Vector2(0f, 30f)) == null)
                    Icons.Shape(cell, Shapes.Circle, Palette.Gold, new Vector2(0f, 30f), new Vector2(44f, 44f));
                var value = UiFactory.Text(cell, "Value", stats[i].value, 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                value.rectTransform.Place(Center, Center, new Vector2(0f, -24f), new Vector2(116f, 40f));
                UiFactory.Shadow(value, Color.black, 2f);
                var label = UiFactory.Text(cell, "Label", stats[i].label, 15, Palette.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                label.rectTransform.Place(Center, Center, new Vector2(0f, -56f), new Vector2(116f, 24f));
            }
        }

        /// <summary>A glimpse of the class's progression: each path's name and its capstone's icon, never the whole tree.</summary>
        void DrawPaths(HeroClassDefinition heroClass)
        {
            Clear(_paths);
            if (heroClass == null)
            {
                _pathsLabel.text = "";
                return;
            }
            var tree = _catalog.TalentsOf(heroClass.Id);
            int free = Progression.PointsFree(_profile, _catalog, heroClass.Id);
            _pathsLabel.text = $"TALENT PATHS  ·  {tree.Count} talents" + (free > 0 ? $"  ·  {free} point{(free == 1 ? "" : "s")} to spend" : "");
            for (int b = 0; b < heroClass.Branches.Length; b++)
            {
                var branch = heroClass.Branches[b];
                var color = TalentOverlay.Parse(branch.Color);
                var capstone = tree.Find(t => t.BranchId == branch.Id && t.Capstone);
                var card = UiFactory.Rect(_paths, "Path " + branch.Id);
                card.Place(TopLeft, TopLeft, new Vector2(b * 256f, 0f), new Vector2(244f, 164f));
                UiFactory.Image(card, "Back", color.Dim(0.22f).WithAlpha(0.95f), Shapes.Rounded, true).rectTransform.Stretch();
                UiFactory.Image(card, "Edge", color, Shapes.Frame, true).rectTransform.Stretch();
                var holder = UiFactory.Rect(card, "Icon");
                holder.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(84f, 84f));
                UiFactory.Image(holder, "Circle", Palette.Navy, Shapes.Circle).rectTransform.Stretch();
                var face = UiFactory.Rect(holder, "Face");
                face.Stretch(5, 5, 5, 5);
                if (capstone != null) Icons.TryArtImage(face, ArtKeys.TalentIcon(capstone.Id), 76f);
                UiFactory.Image(holder, "Ring", color, Shapes.Ring).rectTransform.Stretch(-3, -3, -3, -3);
                var name = UiFactory.Text(card, "Name", branch.Name, 22, color, TextAnchor.MiddleCenter, FontStyle.Bold);
                name.rectTransform.Place(Center, Center, new Vector2(0f, -30f), new Vector2(236f, 28f));
                var focus = UiFactory.Text(card, "Focus", branch.Focus, 15, Palette.TextLight, TextAnchor.MiddleCenter);
                focus.rectTransform.Place(Center, Center, new Vector2(0f, -60f), new Vector2(232f, 36f));
                focus.resizeTextForBestFit = true;
                focus.resizeTextMinSize = 11;
                focus.resizeTextMaxSize = 15;
            }
        }

        void DrawRoster()
        {
            Clear(_roster);
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                int index = i;
                bool on = i == _index;
                var parts = UiFactory.Button(_roster, "Roster " + entry.Id, "", Palette.Navy, 10, () =>
                {
                    _index = index;
                    Redraw();
                });
                parts.Rect.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * 102f, 0f), new Vector2(94f, 94f));
                parts.Border.color = on ? Palette.Gold : entry.Id == _current ? Palette.PlayGreen : Palette.GoldDark.WithAlpha(0.5f);
                parts.Label.enabled = false;
                var face = Icons.TryArtImage(parts.Rect, ArtKeys.Portrait(entry.Id, "neutral"), 84f)
                           ?? Icons.TryArtImage(parts.Rect, ArtKeys.HeroArt(entry.Id), 84f);
                if (face != null)
                {
                    face.raycastTarget = false;
                    if (!entry.Playable) face.color = new Color(0.42f, 0.42f, 0.48f);
                }
                if (!entry.Playable)
                {
                    var lockIcon = Icons.TryArtImage(parts.Rect, ArtKeys.LockIcon, 34f, new Vector2(28f, -28f));
                    if (lockIcon != null) lockIcon.raycastTarget = false;
                }
                if (on) parts.Border.transform.SetAsLastSibling();
            }
        }

        static void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(rt.GetChild(i).gameObject);
        }
    }
}
