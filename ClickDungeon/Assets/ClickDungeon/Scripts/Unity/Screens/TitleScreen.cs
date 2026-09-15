using ClickDungeon.Domain;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// Title screen laid out after the reference title art: hero card top-left, big logo and tagline plank,
    /// CONTINUE panel on the left, Sir Clickington under the arch, PLAY / SETTINGS / QUIT along the bottom.
    /// Systems that don't exist yet (gems, shop, talents, mail, daily reward) are intentionally absent.
    /// </summary>
    public sealed class TitleScreen
    {
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

        readonly ClickDungeonApp _app;
        readonly ModalOverlay _modal;
        readonly RectTransform _continuePanel;
        readonly Text _continueFloor;
        readonly Text _continueName;
        readonly Text _flash;
        RunState _saved;

        public TitleScreen(ClickDungeonApp app, RectTransform parent)
        {
            _app = app;
            Root = UiFactory.Rect(parent, "TitleScreen");
            Root.Stretch();

            Backdrop.Build(Root, ArtKeys.TitleBackground, new[] { new Vector2(-345f, 120f), new Vector2(345f, 120f), new Vector2(-345f, -200f), new Vector2(345f, -200f) });

            // Placeholder scenery. A composite bg_title (arch, banners, characters) replaces all of it.
            bool compositeBackground = Art.Has(ArtKeys.TitleBackground);
            if (!compositeBackground)
            {
                Icons.Shape(Root, Shapes.Rounded, Palette.StoneLight, new Vector2(0f, -40f), new Vector2(660f, 780f));
                Icons.Shape(Root, Shapes.Rounded, Palette.Navy.Dim(0.55f), new Vector2(0f, -50f), new Vector2(610f, 750f));
                for (int i = -2; i <= 2; i++)
                    Icons.Shape(Root, Shapes.Square, Palette.StoneDark.WithAlpha(0.8f), new Vector2(i * 90f, -40f), new Vector2(12f, 600f));

                Banner(new Vector2(-850f, -60f), "SMALL\nCLICKS\n\nBIG\nADVENTURES");
                Banner(new Vector2(470f, -60f), "DUNGEONS\nMAKE\nBETTER\nHEROES");
            }

            BuildHeroCard();
            BuildLogo();
            if (!compositeBackground) BuildCharacters();

            _continuePanel = BuildContinuePanel(out _continueFloor, out _continueName);
            BuildHowTo();
            BuildBottomBar();

            _flash = UiFactory.Text(Root, "Flash", "", 30, Palette.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            _flash.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 180f), new Vector2(1000f, 50f));
            UiFactory.Outline(_flash, Color.black, 2f);

            _modal = new ModalOverlay(Root, app);
        }

        public RectTransform Root { get; }

        public void Refresh()
        {
            _flash.text = "";
            _saved = null;
            if (_app.Store.TryLoad(out var run, out _) && run.Status == RunStatus.InProgress) _saved = run;

            _continuePanel.gameObject.SetActive(_saved != null);
            if (_saved != null)
            {
                _continueFloor.text = $"FLOOR {_saved.Floor.FloorIndex}";
                _continueName.text = (_app.Catalog.ProfileFor(_saved.Floor.FloorIndex).Name ?? "").ToUpperInvariant();
            }
        }

        public void Flash(string message) => _flash.text = message;

        public void Tick()
        {
            if (!Root.gameObject.activeInHierarchy) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (_modal.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _modal.Back();
                return;
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                if (_saved != null) _app.ContinueRun();
                else Play();
            }
        }

        void Play()
        {
            if (_saved == null)
            {
                _app.StartNewRun();
                return;
            }
            _modal.Show("START A NEW RUN?", $"Your run on floor {_saved.Floor.FloorIndex} will be lost.", _modal.Hide,
                Menus.B("NEW RUN", Palette.QuitRed, _app.StartNewRun),
                Menus.B("CONTINUE INSTEAD", Palette.PlayGreen, _app.ContinueRun),
                Menus.B("CANCEL", Palette.NavyLight, _modal.Hide));
        }

        // ------------------------------------------------------------------ layout

        void Banner(Vector2 pos, string text)
        {
            var rt = UiFactory.Rect(Root, "Banner");
            rt.Place(Center, Center, pos, new Vector2(180f, 540f));
            UiFactory.Image(rt, "Back", Palette.Navy, Shapes.Rounded, true).rectTransform.Stretch(0, 0, 0, 40);
            UiFactory.Image(rt, "Border", Palette.GoldDark, Shapes.Frame, true).rectTransform.Stretch(0, 0, 0, 40);
            var tail = Icons.Shape(rt, Shapes.Triangle, Palette.Navy, new Vector2(0f, -230f), new Vector2(180f, 80f), 180f);
            tail.rectTransform.SetAsFirstSibling();
            Icons.Shape(rt, Shapes.Square, Palette.GoldDark, new Vector2(0f, 236f), new Vector2(210f, 12f));
            var label = UiFactory.Text(rt, "Text", text, 24, Palette.Gold.Dim(0.9f), TextAnchor.MiddleCenter, FontStyle.Bold);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.rectTransform.Stretch(8, 20, 8, 80);
            Icons.Crown(rt, new Vector2(0f, -170f), 0.9f);
        }

        void BuildHeroCard()
        {
            var card = UiFactory.Rect(Root, "HeroCard");
            card.Place(TopLeft, TopLeft, new Vector2(28f, -22f), new Vector2(430f, 136f));
            UiFactory.Image(card, "Back", Palette.Navy.WithAlpha(0.95f), Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(card, "Border", Palette.GoldDark, Shapes.Frame, true).rectTransform.Stretch();

            var portrait = UiFactory.Rect(card, "Portrait");
            portrait.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(108f, 108f));
            var face = Icons.Portrait(portrait, 108f);
            face.text = ":D";
            Icons.TryArtImage(portrait, ArtKeys.Portrait(ArtKeys.HeroId, "happy"), 108f);

            var identity = _app.Catalog.HeroIdentity(Content.ContentCatalog.DefaultHeroId);
            var name = UiFactory.Text(card, "Name", identity.DisplayName, 38, Palette.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            name.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(136f, -16f), new Vector2(290f, 50f));
            UiFactory.Shadow(name, Color.black, 2f);
            var tagline = UiFactory.Text(card, "Tagline", identity.Tagline, 24, Palette.TextLight, TextAnchor.MiddleLeft);
            tagline.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(136f, -70f), new Vector2(290f, 40f));
        }

        void BuildLogo()
        {
            var logo = UiFactory.Text(Root, "Logo", "ClickDungeon", 164, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            logo.rectTransform.Place(TopCenter, TopCenter, new Vector2(40f, -60f), new Vector2(1200f, 190f));
            logo.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiFactory.Outline(logo, Palette.Ink, 5f);
            UiFactory.Shadow(logo, new Color(0f, 0f, 0f, 0.85f), 9f);
            if (Icons.ReplaceTextWithArt(logo, ArtKeys.Logo) == null)
            {
                var gem = Icons.Shape(Root, Shapes.Diamond, Palette.Summon, Vector2.zero, new Vector2(44f, 44f));
                gem.rectTransform.Place(TopCenter, Center, new Vector2(40f, -52f), new Vector2(44f, 44f));
            }

            var plank = UiFactory.Rect(Root, "Tagline");
            plank.Place(TopCenter, TopCenter, new Vector2(40f, -250f), new Vector2(860f, 76f));
            UiFactory.Image(plank, "Back", Palette.Stone, Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(plank, "Border", Palette.StoneLight, Shapes.Frame, true).rectTransform.Stretch();
            var text = UiFactory.Text(plank, "Text", "EXPLORE.   SURVIVE.   LOOT.   REPEAT.", 36, Palette.Parchment, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.rectTransform.Stretch();
            UiFactory.Shadow(text, Color.black, 2f);
        }

        void BuildCharacters()
        {
            // Lord Blobert lounging on his gold.
            var blob = Icons.Group(Root, new Vector2(-470f, -300f));
            for (int i = 0; i < 7; i++)
                Icons.Shape(blob, Shapes.Circle, Palette.Gold.Dim(0.8f + i * 0.04f), new Vector2(-120f + i * 40f, -120f + (i % 2) * 8f), new Vector2(46f, 26f));
            Icons.Shape(blob, Shapes.Circle, Palette.Boss, Vector2.zero, new Vector2(300f, 240f));
            Icons.Shape(blob, Shapes.Circle, Palette.Boss.Dim(1.2f), new Vector2(-60f, 50f), new Vector2(60f, 40f));
            Icons.Shape(blob, Shapes.Circle, Color.white, new Vector2(-40f, 20f), new Vector2(56f, 40f));
            Icons.Shape(blob, Shapes.Circle, Color.white, new Vector2(40f, 20f), new Vector2(56f, 40f));
            Icons.Shape(blob, Shapes.Square, Palette.Boss.Dim(0.7f), new Vector2(-40f, 34f), new Vector2(60f, 16f));
            Icons.Shape(blob, Shapes.Square, Palette.Boss.Dim(0.7f), new Vector2(40f, 34f), new Vector2(60f, 16f));
            Icons.Shape(blob, Shapes.Circle, Color.black, new Vector2(-40f, 16f), new Vector2(16f, 16f));
            Icons.Shape(blob, Shapes.Circle, Color.black, new Vector2(40f, 16f), new Vector2(16f, 16f));
            Icons.Shape(blob, Shapes.Square, Palette.Boss.Dim(0.5f), new Vector2(0f, -30f), new Vector2(60f, 8f));
            Icons.Crown(blob, new Vector2(10f, 140f), 2f);
            Icons.Shape(blob, Shapes.Square, Palette.Gold, new Vector2(150f, 40f), new Vector2(10f, 180f), -10f);
            Icons.Shape(blob, Shapes.Diamond, Palette.Summon, new Vector2(165f, 140f), new Vector2(40f, 40f));

            // A goblin peeking over a chest.
            var goblin = Icons.Group(Root, new Vector2(640f, -330f));
            Icons.Shape(goblin, Shapes.Triangle, Palette.Goblin.Dim(0.8f), new Vector2(-80f, 40f), new Vector2(70f, 50f), 70f);
            Icons.Shape(goblin, Shapes.Triangle, Palette.Goblin.Dim(0.8f), new Vector2(80f, 40f), new Vector2(70f, 50f), -70f);
            Icons.Shape(goblin, Shapes.Circle, Palette.Goblin, new Vector2(0f, 20f), new Vector2(150f, 140f));
            Icons.Shape(goblin, Shapes.Circle, Color.white, new Vector2(-30f, 40f), new Vector2(40f, 44f));
            Icons.Shape(goblin, Shapes.Circle, Color.white, new Vector2(30f, 40f), new Vector2(40f, 44f));
            Icons.Shape(goblin, Shapes.Circle, Color.black, new Vector2(-26f, 36f), new Vector2(16f, 18f));
            Icons.Shape(goblin, Shapes.Circle, Color.black, new Vector2(34f, 36f), new Vector2(16f, 18f));
            Icons.Chest(goblin, false, 2.2f);

            // Sir Clickington under the arch.
            var knight = Icons.Group(Root, new Vector2(0f, -200f));
            Icons.Shape(knight, Shapes.Rounded, Palette.Hp.Dim(0.8f), new Vector2(-30f, -60f), new Vector2(300f, 320f), 8f);
            Icons.Shape(knight, Shapes.Rounded, Palette.Steel, new Vector2(0f, -80f), new Vector2(210f, 230f));
            Icons.Shape(knight, Shapes.Rounded, Palette.Hp, new Vector2(0f, 10f), new Vector2(190f, 40f));
            Icons.Shape(knight, Shapes.Square, Palette.ChestWood, new Vector2(0f, -140f), new Vector2(210f, 26f));
            Icons.Shape(knight, Shapes.Rounded, Palette.Gold, new Vector2(0f, -140f), new Vector2(40f, 30f));
            Icons.Shape(knight, Shapes.Rounded, Palette.Steel.Dim(0.8f), new Vector2(-60f, -240f), new Vector2(70f, 80f));
            Icons.Shape(knight, Shapes.Rounded, Palette.Steel.Dim(0.8f), new Vector2(60f, -240f), new Vector2(70f, 80f));

            var sword = Icons.Group(knight, new Vector2(-170f, 110f), 20f);
            Icons.Shape(sword, Shapes.Square, Palette.Steel.Dim(1.1f), new Vector2(0f, 120f), new Vector2(46f, 250f));
            Icons.Shape(sword, Shapes.Triangle, Palette.Steel.Dim(1.1f), new Vector2(0f, 262f), new Vector2(46f, 40f));
            Icons.Shape(sword, Shapes.Rounded, Palette.Gold, new Vector2(0f, -10f), new Vector2(110f, 24f));
            Icons.Shape(sword, Shapes.Rounded, Palette.ChestWood, new Vector2(0f, -50f), new Vector2(26f, 70f));

            Icons.Shape(knight, Shapes.Triangle, Palette.Hp, new Vector2(70f, 250f), new Vector2(130f, 120f), -30f);
            Icons.Shape(knight, Shapes.Circle, Palette.Steel, new Vector2(0f, 120f), new Vector2(240f, 240f));
            Icons.Shape(knight, Shapes.Circle, Palette.Parchment, new Vector2(0f, 90f), new Vector2(170f, 140f));
            Icons.Shape(knight, Shapes.Rounded, Palette.StoneDark, new Vector2(0f, 180f), new Vector2(150f, 34f));
            Icons.Shape(knight, Shapes.Circle, Palette.Ink, new Vector2(-36f, 104f), new Vector2(22f, 26f));
            Icons.Shape(knight, Shapes.Circle, Palette.Ink, new Vector2(36f, 104f), new Vector2(22f, 26f));
            Icons.Shape(knight, Shapes.Rounded, Color.white, new Vector2(0f, 60f), new Vector2(80f, 26f));
            Icons.Shape(knight, Shapes.Square, Palette.Ink, new Vector2(0f, 60f), new Vector2(80f, 3f));

            var shield = Icons.Group(knight, new Vector2(160f, -70f), -8f);
            Icons.Shape(shield, Shapes.Rounded, Palette.Steel, Vector2.zero, new Vector2(190f, 230f));
            Icons.Shape(shield, Shapes.Rounded, Palette.Hero.Dim(0.75f), Vector2.zero, new Vector2(160f, 200f));
            Icons.Shape(shield, Shapes.Square, Palette.Gold, Vector2.zero, new Vector2(26f, 150f));
            Icons.Shape(shield, Shapes.Square, Palette.Gold, new Vector2(0f, 20f), new Vector2(110f, 26f));
        }

        RectTransform BuildContinuePanel(out Text floor, out Text name)
        {
            var panel = UiFactory.Rect(Root, "Continue");
            panel.Place(Center, Center, new Vector2(-560f, 150f), new Vector2(400f, 340f));
            UiFactory.Image(panel, "Back", Palette.Navy.WithAlpha(0.96f), Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(panel, "Border", Palette.Gold, Shapes.Frame, true).rectTransform.Stretch();
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel.GetComponentInChildren<Image>();
            button.onClick.AddListener(() => _app.ContinueRun());

            var title = UiFactory.Text(panel, "Title", "CONTINUE", 44, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -12f), new Vector2(380f, 60f));
            UiFactory.Shadow(title, Color.black, 2f);

            var doorway = Icons.Group(panel, new Vector2(0f, 20f));
            Icons.Shape(doorway, Shapes.Rounded, Palette.Stone, Vector2.zero, new Vector2(340f, 150f));
            Icons.Shape(doorway, Shapes.Rounded, Palette.Pit, new Vector2(0f, -10f), new Vector2(110f, 120f));
            Backdrop.Torch(doorway, new Vector2(-100f, 0f));
            Backdrop.Torch(doorway, new Vector2(100f, 0f));

            floor = UiFactory.Text(panel, "Floor", "", 38, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            floor.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-10f, 50f), new Vector2(320f, 46f));
            name = UiFactory.Text(panel, "Name", "", 22, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-10f, 18f), new Vector2(320f, 32f));
            Icons.Shape(panel, Shapes.Triangle, Palette.Gold, new Vector2(165f, -110f), new Vector2(30f, 34f), -90f);
            return panel;
        }

        void BuildHowTo()
        {
            var panel = UiFactory.Rect(Root, "HowTo");
            panel.Place(Center, Center, new Vector2(760f, 150f), new Vector2(340f, 380f));
            UiFactory.Image(panel, "Back", Palette.Navy.WithAlpha(0.96f), Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(panel, "Border", Palette.Gold, Shapes.Frame, true).rectTransform.Stretch();
            var title = UiFactory.Text(panel, "Title", "HOW TO PLAY", 36, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -12f), new Vector2(320f, 52f));
            var body = UiFactory.Text(panel, "Body",
                "Read the sensed tiles.\nWake enemies on purpose.\nDodge the marked tiles.\nGrab the key. Find the exit.\nBeat Lord Blobert on floor 5.",
                24, Palette.TextLight, TextAnchor.UpperCenter);
            body.rectTransform.Stretch(16, 76, 16, 76);
            body.lineSpacing = 1.3f;
            var more = UiFactory.Button(panel, "More", "FULL RULES", Palette.NavyLight, 26,
                () => _modal.Show("HOW TO PLAY", Menus.HelpText, _modal.Hide, Menus.B("GOT IT", Palette.PlayGreen, _modal.Hide)));
            more.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(260f, 56f));
        }

        void BuildBottomBar()
        {
            var play = UiFactory.Button(Root, "Play", "PLAY", Palette.PlayGreen, 60, Play);
            play.Rect.Place(Vector2.zero, Vector2.zero, new Vector2(60f, 28f), new Vector2(320f, 136f));
            play.Border.color = Palette.Gold;
            var swords = UiFactory.Rect(play.Rect, "Swords");
            swords.Place(new Vector2(0f, 0.5f), Center, new Vector2(54f, 0f), new Vector2(60f, 60f));
            Icons.Ability(Icons.Group(swords, Vector2.zero, 0f, 0.8f), CommandKind.Slash);
            play.Label.rectTransform.Stretch(80, 4, 8, 4);

            var settings = UiFactory.Button(Root, "Settings", "SETTINGS", Palette.Navy, 34, () => Menus.OpenSettings(_modal, _modal.Hide, _app.ApplyTelemetrySetting));
            settings.Rect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-356f, 28f), new Vector2(280f, 136f));
            settings.Label.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(270f, 44f));
            var gear = UiFactory.Rect(settings.Rect, "Gear");
            gear.Place(Center, Center, new Vector2(0f, 22f), new Vector2(70f, 70f));
            Icons.Gear(gear, Palette.Steel, 64f);

#if !UNITY_IOS
            var quit = UiFactory.Button(Root, "Quit", "QUIT", Palette.QuitRed, 40, _app.Quit);
            quit.Rect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 28f), new Vector2(280f, 136f));
#endif
        }
    }
}
