using ClickDungeon.Application;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static ClickDungeon.Unity.Ui.RefLayout;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// Title screen laid out after the reference title art: hero card top-left, big logo and tagline plank,
    /// CONTINUE panel on the left, DAILY REWARD on the right, settings and the menu top-right, and the seven buttons along
    /// the bottom. The top-right row is the reference's: the crown (achievements), mail, settings and the menu.
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
        RectTransform _heroPortrait;
        RectTransform _heroButtonIcon;
        Text _coins;
        Text _gems;
        Text _level;
        UiFactory.ButtonParts _talentsButton;
        InventoryOverlay _inventory;
        AchievementsOverlay _achievements;
        MailOverlay _mail;
        ShopOverlay _shop;
        GameObject _mailBadge;
        /// <summary>The reference's mail button without its "!", laid over the background's while nothing is waiting.</summary>
        GameObject _mailClean;
        Text _continueTitle;
        /// <summary>
        /// True when the background is the reference title itself (D-033): its card, CONTINUE panel and buttons are then
        /// the background's own pixels, and the screen only adds live text and cleaned patches in their exact places.
        /// </summary>
        readonly bool _matched;
        RectTransform _dailyChest;
        Text _dailyLine;
        GameObject _claim;
        GameObject _claimed;
        Text _heroName;
        Text _heroTagline;
        RunState _saved;

        public TitleScreen(ClickDungeonApp app, RectTransform parent)
        {
            _app = app;
            Root = UiFactory.Rect(parent, "TitleScreen");
            RefLayout.Stage(Root);

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

            _matched = compositeBackground && Art.Has(ArtKeys.TitleNamePlate) && Art.Has(ArtKeys.TitleContinueClean);
            if (_matched) BuildMatchedCard();
            else BuildHeroCard();
            BuildLogo();
            if (!compositeBackground) BuildCharacters();

            _continuePanel = _matched ? BuildMatchedContinue(out _continueFloor, out _continueName)
                : BuildContinuePanel(out _continueFloor, out _continueName);
            BuildDailyReward();
            if (_matched) BuildMatchedTopRight();
            else BuildTopRight();
            BuildBottomBar();

            _flash = UiFactory.Text(Root, "Flash", "", 30, Palette.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            _flash.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 180f), new Vector2(1000f, 50f));
            UiFactory.Outline(_flash, Color.black, 2f);

            _modal = new ModalOverlay(Root, app);
            _inventory = new InventoryOverlay(Root);
            _achievements = new AchievementsOverlay(Root);
            _mail = new MailOverlay(Root);
            _shop = new ShopOverlay(Root);
        }

        public RectTransform Root { get; }

        public void Refresh()
        {
            // A modal left open when a run started (for example CONTINUE INSTEAD) must not greet the player on return.
            if (_modal.IsOpen) _modal.Hide();
            if (_inventory.IsOpen) _inventory.Hide();
            if (_achievements.IsOpen) _achievements.Hide();
            if (_mail.IsOpen) _mail.Hide();
            if (_shop.IsOpen) _shop.Hide();
            _flash.text = "";
            RefreshHeroCard();
            RefreshPurse();
            RefreshDaily();
            _saved = null;
            if (_app.Store.TryLoad(out var run, out _) && run.Status == RunStatus.InProgress) _saved = run;

            // The reference always shows the panel; with no run to continue it offers a new one from floor 1.
            _continuePanel.gameObject.SetActive(_saved != null || _matched);
            int floor = _saved?.Floor.FloorIndex ?? 1;
            _continueFloor.text = $"FLOOR {floor}";
            _continueName.text = (_app.Catalog.ProfileFor(floor).Name ?? "").ToUpperInvariant();
            if (_continueTitle != null) _continueTitle.text = _saved != null ? "CONTINUE" : "NEW RUN";
        }

        public void Flash(string message) => _flash.text = message;

        /// <summary>Automation hook (screenshots): opens a title overlay without changing any state.</summary>
        public void AutomationOverlay(string name)
        {
            if (name == "settings") Menus.OpenSettings(_modal, _modal.Hide, _app.ApplyTelemetrySetting);
            else if (name == "rules") OpenHelp(_modal.Hide);
            else if (name == "menu") OpenMenu();
            else if (name == "difficulty") ChooseDifficulty(_modal.Hide);
            else if (name == "heroes") OpenHeroSelect();
            else if (name == "shop") OpenShop();
            else if (name == "talents") OpenTalents();
            else if (name == "inventory") OpenInventory();
            else if (name == "coins") OpenPurse(true);
            else if (name == "shopgear") OpenShop(ShopTab.Gear);
            else if (name == "shopchests") OpenShop(ShopTab.Chests);
            else if (name == "gems") OpenPurse(false);
            else if (name == "crown") OpenAchievements();
            else if (name == "mail") OpenMail();
        }

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
            if (_inventory.IsOpen || _achievements.IsOpen || _mail.IsOpen || _shop.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    _shop.Hide();
                    _inventory.Hide();
                    _achievements.Hide();
                    _mail.Hide();
                }
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
                ChooseDifficulty(_modal.Hide);
                return;
            }
            _modal.Show("START A NEW RUN?", $"Your run on floor {_saved.Floor.FloorIndex} will be lost.", _modal.Hide,
                Menus.B("NEW RUN", Palette.QuitRed, () => ChooseDifficulty(Play)),
                Menus.B("CONTINUE INSTEAD", Palette.PlayGreen, _app.ContinueRun),
                Menus.B("CANCEL", Palette.NavyLight, _modal.Hide));
        }

        void ChooseDifficulty(System.Action back) =>
            Menus.OpenDifficulty(_modal, _app.Catalog, UserPrefs.LastDifficulty, tier =>
            {
                _modal.Hide();
                _app.StartNewRun(tier);
            }, back);

        // ------------------------------------------------------------------ layout

        static Image Panel(RectTransform rt, Color fill, Color edge, string artKey)
        {
            var back = UiFactory.Image(rt, "Back", fill, Shapes.Rounded, true);
            back.rectTransform.Stretch();
            var border = UiFactory.Image(rt, "Border", edge, Shapes.Frame, true);
            border.rectTransform.Stretch();
            UiArt.ApplyPanel(back, border, artKey);
            return back;
        }

        /// <summary>Places the first key that has art at pos; false means the caller draws its placeholder.</summary>
        static bool ArtAt(Transform parent, Vector2 pos, float size, params string[] keys)
        {
            foreach (var key in keys)
                if (Icons.TryArtImage(parent, key, size, pos) != null)
                    return true;
            return false;
        }

        void Banner(Vector2 pos, string text)
        {
            var rt = UiFactory.Rect(Root, "Banner");
            rt.Place(Center, Center, pos, new Vector2(180f, 540f));
            // Banner art is the cloth, rod and emblem; the slogan stays live text.
            bool art = Icons.TryArtImage(rt, ArtKeys.TitleBanner, 540f) != null;
            if (!art)
            {
                UiFactory.Image(rt, "Back", Palette.Navy, Shapes.Rounded, true).rectTransform.Stretch(0, 0, 0, 40);
                UiFactory.Image(rt, "Border", Palette.GoldDark, Shapes.Frame, true).rectTransform.Stretch(0, 0, 0, 40);
                var tail = Icons.Shape(rt, Shapes.Triangle, Palette.Navy, new Vector2(0f, -230f), new Vector2(180f, 80f), 180f);
                tail.rectTransform.SetAsFirstSibling();
                Icons.Shape(rt, Shapes.Square, Palette.GoldDark, new Vector2(0f, 236f), new Vector2(210f, 12f));
            }
            var label = UiFactory.Text(rt, "Text", text, 24, Palette.Gold.Dim(0.9f), TextAnchor.MiddleCenter, FontStyle.Bold);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.rectTransform.Stretch(8, 20, 8, 80);
            if (!art) Icons.Crown(rt, new Vector2(0f, -170f), 0.9f);
        }

        /// <summary>The saved hero, or the default when that identity is gone from the catalog.</summary>
        string HeroChoice()
        {
            string id = UserPrefs.Hero;
            return _app.Catalog.HeroIdentities.ContainsKey(id) ? id : Content.ContentCatalog.DefaultHeroId;
        }

        void OpenAchievements() => _achievements.Open(_app.Catalog, _app.Session.Profile);

        void OpenMail() =>
            _mail.Open(_app.Session.Profile, () =>
            {
                _app.Session.SaveProfile();
                RefreshPurse();
            });

        void OpenInventory() =>
            _inventory.Open(_app.Catalog, _app.Session.Profile, () => _app.Session.SaveProfile());

        void OpenTalents()
        {
            Menus.OpenTalents(_modal, _app.Session.Profile, id =>
            {
                if (Progression.TryLearn(_app.Session.Profile, id)) _app.Session.SaveProfile();
                RefreshPurse();
                OpenTalents();
            }, () =>
            {
                Progression.Reset(_app.Session.Profile);
                _app.Session.SaveProfile();
                RefreshPurse();
                OpenTalents();
            }, _modal.Hide);
        }

        /// <summary>The purse's "+" opens the shop on its exchange (D-036).</summary>
        void OpenPurse(bool coins) => OpenShop(ShopTab.Exchange);

        void OpenShop() => OpenShop(ShopTab.Boosts);

        void OpenShop(ShopTab tab) =>
            _shop.Open(_app.Catalog, _app.Session.Profile, () => System.DateTime.Now, () =>
            {
                _app.Session.SaveProfile();
                RefreshPurse();
            }, tab);

        void OpenHeroSelect()
        {
            Menus.OpenHeroSelect(_modal, _app.Catalog, HeroChoice(), id =>
            {
                UserPrefs.Hero = id;
                RefreshHeroCard();
                OpenHeroSelect();
            }, _modal.Hide);
        }

        void BuildHeroCard()
        {
            var card = UiFactory.Rect(Root, "HeroCard");
            card.Place(TopLeft, TopLeft, new Vector2(28f, -22f), new Vector2(430f, 136f));
            Panel(card, Palette.Navy.WithAlpha(0.95f), Palette.GoldDark, ArtKeys.TitleHeroCard);

            _heroPortrait = UiFactory.Rect(card, "Portrait");
            _heroPortrait.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(108f, 108f));

            _heroName = UiFactory.Text(card, "Name", "", 38, Palette.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            _heroName.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(136f, -16f), new Vector2(290f, 50f));
            UiFactory.Shadow(_heroName, Color.black, 2f);
            _heroTagline = UiFactory.Text(card, "Tagline", "", 24, Palette.TextLight, TextAnchor.MiddleLeft);
            _heroTagline.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(136f, -70f), new Vector2(290f, 40f));

            // The purse sits under the card, as in the reference. Both counters are real: coins and gems come out of runs.
            // The level badge sits on the portrait's corner, as in the reference.
            var badge = UiFactory.Image(card, "LevelBadge", Palette.Navy, Shapes.Rounded, true);
            badge.rectTransform.Place(new Vector2(0f, 0f), Center, new Vector2(26f, 26f), new Vector2(46f, 46f));
            var badgeEdge = UiFactory.Image(badge.rectTransform, "Edge", Palette.Gold, Shapes.Frame, true);
            badgeEdge.rectTransform.Stretch();
            _level = UiFactory.Text(badge.rectTransform, "Level", "1", 28, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            _level.rectTransform.Stretch();

            _coins = Purse(card, new Vector2(140f, -112f), ArtKeys.CoinIcon, Palette.Gold);
            _gems = Purse(card, new Vector2(300f, -112f), ArtKeys.GemIcon, Palette.Summon);
            RefreshHeroCard();
            RefreshPurse();
        }

        /// <summary>The card shows whoever takes the next run, so picking a hero is visible straight away (D-024).</summary>
        void RefreshHeroCard()
        {
            string id = HeroChoice();
            var identity = _app.Catalog.HeroIdentity(id);
            _heroName.text = identity.DisplayName;
            _heroTagline.text = identity.Tagline;

            if (_matched) MatchedFace(id);
            else Face(_heroPortrait, id, 108f);
            // The button carries the same face, so the choice reads without opening the menu.
            Face(_heroButtonIcon, id, 76f);
        }

        /// <summary>One counter: its icon, then its number.</summary>
        Text Purse(RectTransform card, Vector2 pos, string iconKey, Color fallback)
        {
            var slot = UiFactory.Rect(card, "Purse " + iconKey);
            slot.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(150f, 44f));
            var icon = UiFactory.Rect(slot, "Icon");
            icon.Place(new Vector2(0f, 0.5f), Center, new Vector2(20f, 0f), new Vector2(38f, 38f));
            if (!ArtAt(icon, Vector2.zero, 38f, iconKey))
                Icons.Shape(icon, Shapes.Circle, fallback, Vector2.zero, new Vector2(30f, 30f));
            var text = UiFactory.Text(slot, "Text", "0", 26, Palette.TextLight, TextAnchor.MiddleLeft, FontStyle.Bold);
            text.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(100f, 40f));
            UiFactory.Shadow(text, Color.black, 2f);
            return text;
        }

        /// <summary>Coins and gems as they stand, after a run banks or the shop spends.</summary>
        void RefreshPurse()
        {
            var profile = _app.Session.Profile;
            // Thousands separated, as the reference writes 1,248.
            if (_coins != null) _coins.text = profile.Coins.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            if (_gems != null) _gems.text = profile.Gems.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            if (_level != null) _level.text = Progression.Level(profile).ToString();
            if (_mailBadge != null) _mailBadge.SetActive(Mailbox.Unread(profile) > 0);
            // The background's mail button carries the reference's "!"; the clean copy hides it while nothing waits.
            if (_mailClean != null) _mailClean.SetActive(Mailbox.Unread(profile) == 0);
            // The red "!" only when a talent point is waiting, as the reference's badge means.
            if (_talentsButton != null)
                UiArt.ApplyPanel(_talentsButton.Background, _talentsButton.Border,
                    Progression.PointsFree(profile) > 0 ? ArtKeys.ButtonTalentsAlert : ArtKeys.ButtonTalents);
        }

        void Face(RectTransform parent, string heroId, float size)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(parent.GetChild(i).gameObject);
            if (!ArtAt(parent, Vector2.zero, size, ArtKeys.Portrait(heroId, "happy"), ArtKeys.Portrait(heroId, "neutral"),
                    ArtKeys.Actor(heroId)))
                Icons.Portrait(parent, size).text = ":D";
        }

        // ------------------------------------------------------------------ matched to the reference (D-033)

        static readonly Color CardName = new Color(0.96f, 0.87f, 0.66f);
        static readonly Color CardTagline = new Color(0.72f, 0.7f, 0.66f);

        /// <summary>
        /// The reference's hero card: its portrait frame, name plate, level shield and purse are the background's; the name,
        /// tagline, level and amounts are live, and each "+" opens the exchange for that currency.
        /// </summary>
        void BuildMatchedCard()
        {
            _heroPortrait = AtRef(Root, "Portrait", 57f, 32f, 76f, 74f);
            PatchAt(Root, ArtKeys.TitleNamePlate, 141f, 28f, 295f, 72f);
            _heroName = TextIn(AtRef(Root, "Name", 152f, 30f, 280f, 38f), "Name", 34, CardName, TextAnchor.MiddleLeft);
            _heroTagline = TextIn(AtRef(Root, "Tagline", 153f, 72f, 280f, 24f), "Tagline", 22, CardTagline, TextAnchor.MiddleLeft, FontStyle.Normal);

            PatchAt(Root, ArtKeys.TitleLevelBadge, 46f, 80f, 44f, 50f);
            _level = TextIn(AtRef(Root, "Level", 46f, 86f, 44f, 34f), "Level", 32, CardName, TextAnchor.MiddleCenter);

            PatchAt(Root, ArtKeys.TitleCoinField, 176f, 104f, 72f, 30f);
            _coins = TextIn(AtRef(Root, "Coins", 176f, 104f, 68f, 30f), "Coins", 25, Color.white, TextAnchor.MiddleRight);
            PatchAt(Root, ArtKeys.TitleGemField, 332f, 104f, 46f, 30f);
            _gems = TextIn(AtRef(Root, "Gems", 332f, 104f, 42f, 30f), "Gems", 25, Color.white, TextAnchor.MiddleRight);
            HotspotAt(Root, "CoinsPlus", 252f, 103f, 32f, 33f, () => OpenPurse(true));
            HotspotAt(Root, "GemsPlus", 382f, 103f, 32f, 33f, () => OpenPurse(false));
            RefreshHeroCard();
            RefreshPurse();
        }

        /// <summary>The background already shows Sir Clickington in the frame; another hero's face is laid over him.</summary>
        void MatchedFace(string heroId)
        {
            for (int i = _heroPortrait.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(_heroPortrait.GetChild(i).gameObject);
            if (heroId == Content.ContentCatalog.DefaultHeroId) return;
            var back = UiFactory.Image(_heroPortrait, "Back", new Color(0.06f, 0.07f, 0.1f), Shapes.Rounded, true);
            back.rectTransform.Stretch();
            if (!ArtAt(_heroPortrait, Vector2.zero, 84f, ArtKeys.Portrait(heroId, "happy"), ArtKeys.Portrait(heroId, "neutral"), ArtKeys.Actor(heroId)))
                Icons.Portrait(_heroPortrait, 84f).text = ":D";
        }

        /// <summary>
        /// The reference's CONTINUE panel with its sample floor painted out. It is always there, as in the reference: with
        /// no run to continue it reads NEW RUN and starts one.
        /// </summary>
        RectTransform BuildMatchedContinue(out Text floor, out Text name)
        {
            var panel = AtRef(Root, "Continue", 186f, 220f, 312f, 282f);
            var art = UiFactory.Image(panel, "Art", Color.white);
            art.rectTransform.Stretch();
            UiArt.Apply(art, ArtKeys.TitleContinueClean);
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = art;
            button.onClick.AddListener(() =>
            {
                if (_saved != null) _app.ContinueRun();
                else Play();
            });

            _continueTitle = TextIn(AtRef(panel, "Title", 40f, 11f, 232f, 40f), "Title", 38, CardName, TextAnchor.MiddleCenter);
            floor = TextIn(AtRef(panel, "Floor", 40f, 199f, 230f, 32f), "Floor", 30, CardName, TextAnchor.MiddleCenter);
            name = TextIn(AtRef(panel, "Name", 40f, 229f, 230f, 22f), "Name", 18, CardName, TextAnchor.MiddleCenter);
            return panel;
        }

        /// <summary>The reference's crown, mail, settings and menu buttons are the background's; these are their taps.</summary>
        void BuildMatchedTopRight()
        {
            _mailClean = PatchAt(Root, ArtKeys.TitleMailClean, 1356f, 20f, 98f, 86f).rectTransform.parent.gameObject;
            HotspotAt(Root, "Crown", 1275f, 28f, 76f, 73f, OpenAchievements);
            HotspotAt(Root, "MailButton", 1366f, 28f, 78f, 73f, OpenMail);
            HotspotAt(Root, "Settings", 1462f, 28f, 77f, 73f, () => Menus.OpenSettings(_modal, _modal.Hide, _app.ApplyTelemetrySetting));
            HotspotAt(Root, "Menu", 1554f, 28f, 77f, 73f, OpenMenu);
            RefreshPurse();
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

            if (_matched)
            {
                // Over the reference logo's place, which the background blurs because its logo reads "ClickDungeon2".
                logo.rectTransform.Place(TopCenter, TopCenter, new Vector2(-2f, -24f), new Vector2(900f, 176f));
                foreach (var art in Root.GetComponentsInChildren<Image>())
                    if (art.name == "Art " + ArtKeys.Logo) art.rectTransform.Place(TopCenter, TopCenter, new Vector2(-2f, -24f), new Vector2(900f, 176f));
                return;
            }
            var plank = UiFactory.Rect(Root, "Tagline");
            plank.Place(TopCenter, TopCenter, new Vector2(40f, -250f), new Vector2(860f, 76f));
            Panel(plank, Palette.Stone, Palette.StoneLight, ArtKeys.TitlePlank);
            var text = UiFactory.Text(plank, "Text", "EXPLORE.   SURVIVE.   LOOT.   REPEAT.", 36, Palette.Parchment, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.rectTransform.Stretch();
            UiFactory.Shadow(text, Color.black, 2f);
        }

        void BuildCharacters()
        {
            // Lord Blobert lounging on his gold.
            var blob = Icons.Group(Root, new Vector2(-470f, -300f));
            if (ArtAt(Root, new Vector2(-470f, -290f), 420f, ArtKeys.TitleBlobert, ArtKeys.Actor("lord_blobert"))) blob.gameObject.SetActive(false);
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
            if (ArtAt(Root, new Vector2(640f, -330f), 320f, ArtKeys.TitleGoblin)) goblin.gameObject.SetActive(false);
            Icons.Shape(goblin, Shapes.Triangle, Palette.Goblin.Dim(0.8f), new Vector2(-80f, 40f), new Vector2(70f, 50f), 70f);
            Icons.Shape(goblin, Shapes.Triangle, Palette.Goblin.Dim(0.8f), new Vector2(80f, 40f), new Vector2(70f, 50f), -70f);
            Icons.Shape(goblin, Shapes.Circle, Palette.Goblin, new Vector2(0f, 20f), new Vector2(150f, 140f));
            Icons.Shape(goblin, Shapes.Circle, Color.white, new Vector2(-30f, 40f), new Vector2(40f, 44f));
            Icons.Shape(goblin, Shapes.Circle, Color.white, new Vector2(30f, 40f), new Vector2(40f, 44f));
            Icons.Shape(goblin, Shapes.Circle, Color.black, new Vector2(-26f, 36f), new Vector2(16f, 18f));
            Icons.Shape(goblin, Shapes.Circle, Color.black, new Vector2(34f, 36f), new Vector2(16f, 18f));
            Icons.Chest(goblin, false, 2.2f, useArt: false);

            // Sir Clickington under the arch.
            var knight = Icons.Group(Root, new Vector2(0f, -200f));
            if (ArtAt(Root, new Vector2(0f, -190f), 600f, ArtKeys.TitleHero, ArtKeys.Actor(ArtKeys.HeroId))) knight.gameObject.SetActive(false);
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
            var back = Panel(panel, Palette.Navy.WithAlpha(0.96f), Palette.Gold, ArtKeys.TitleContinuePanel);
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            button.onClick.AddListener(() => _app.ContinueRun());

            var title = UiFactory.Text(panel, "Title", "CONTINUE", 44, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -12f), new Vector2(380f, 60f));
            UiFactory.Shadow(title, Color.black, 2f);

            if (!ArtAt(panel, new Vector2(0f, 20f), 340f, ArtKeys.ContinuePreview))
            {
                var doorway = Icons.Group(panel, new Vector2(0f, 20f));
                Icons.Shape(doorway, Shapes.Rounded, Palette.Stone, Vector2.zero, new Vector2(340f, 150f));
                Icons.Shape(doorway, Shapes.Rounded, Palette.Pit, new Vector2(0f, -10f), new Vector2(110f, 120f));
                Backdrop.Torch(doorway, new Vector2(-100f, 0f));
                Backdrop.Torch(doorway, new Vector2(100f, 0f));
            }

            floor = UiFactory.Text(panel, "Floor", "", 38, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            floor.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-10f, 50f), new Vector2(320f, 46f));
            name = UiFactory.Text(panel, "Name", "", 22, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-10f, 18f), new Vector2(320f, 32f));
            Icons.Shape(panel, Shapes.Triangle, Palette.Gold, new Vector2(165f, -110f), new Vector2(30f, 34f), -90f);
            return panel;
        }

        void OpenHelp(System.Action back) =>
            _modal.Show("HOW TO PLAY", Menus.HelpText, back, Menus.B("GOT IT", Palette.PlayGreen, back));

        /// <summary>The reference's menu button: HOW TO PLAY lives here now that its panel's place is the daily reward's.</summary>
        void OpenMenu() =>
            _modal.Show("MENU", "", _modal.Hide,
                Menus.B("HOW TO PLAY", Palette.NavyLight, () => OpenHelp(OpenMenu)),
                Menus.B("SETTINGS", Palette.NavyLight, () => Menus.OpenSettings(_modal, OpenMenu, _app.ApplyTelemetrySetting)),
                Menus.B("CLOSE", Palette.PlayGreen, _modal.Hide));

        /// <summary>The reference's top-right row: crown, mail, settings and the menu, each in its slot.</summary>
        void BuildTopRight()
        {
            var topRight = new Vector2(1f, 1f);
            var crown = UiFactory.Button(Root, "Crown", "", Palette.Navy, 10, OpenAchievements);
            crown.Rect.Place(topRight, Center, new Vector2(-413f, -74f), new Vector2(88f, 88f));
            if (!UiArt.ApplyPanel(crown.Background, crown.Border, ArtKeys.CrownButton))
                Icons.Crown(crown.Rect, Vector2.zero, 0.9f);

            var mail = UiFactory.Button(Root, "MailButton", "", Palette.Navy, 10, OpenMail);
            mail.Rect.Place(topRight, Center, new Vector2(-306f, -74f), new Vector2(88f, 88f));
            if (!UiArt.ApplyPanel(mail.Background, mail.Border, ArtKeys.MailButton))
                Icons.Shape(mail.Rect, Shapes.Rounded, Palette.Gold, Vector2.zero, new Vector2(52f, 36f));
            // The red "!" where the reference paints it, overhanging the corner.
            var badge = Icons.TryArtImage(mail.Rect, ArtKeys.AlertBadge, 38f, new Vector2(40f, 40f));
            _mailBadge = badge != null ? badge.gameObject
                : Icons.Shape(mail.Rect, Shapes.Circle, Palette.Danger, new Vector2(40f, 40f), new Vector2(30f, 30f)).gameObject;
            _mailBadge.GetComponent<Image>().raycastTarget = false;
            RefreshPurse();

            var gear = UiFactory.Button(Root, "Settings", "", Palette.Navy, 10, () => Menus.OpenSettings(_modal, _modal.Hide, _app.ApplyTelemetrySetting));
            gear.Rect.Place(topRight, Center, new Vector2(-197f, -74f), new Vector2(88f, 88f));
            if (!UiArt.ApplyPanel(gear.Background, gear.Border, ArtKeys.SettingsButton))
                Icons.Gear(gear.Rect, Palette.Gold, 60f);

            var menu = UiFactory.Button(Root, "Menu", "", Palette.Navy, 10, OpenMenu);
            menu.Rect.Place(topRight, Center, new Vector2(-92f, -74f), new Vector2(88f, 88f));
            if (!UiArt.ApplyPanel(menu.Background, menu.Border, ArtKeys.MenuButton))
                for (int i = -1; i <= 1; i++)
                    Icons.Shape(menu.Rect, Shapes.Rounded, Palette.Gold, new Vector2(0f, i * 16f), new Vector2(46f, 8f));
        }

        /// <summary>The reference's DAILY REWARD panel (D-029), on the right where it stands in the title art.</summary>
        void BuildDailyReward()
        {
            var panel = UiFactory.Rect(Root, "DailyReward");
            panel.Place(Center, Center, new Vector2(758f, 131f), new Vector2(308f, 388f));
            Panel(panel, Palette.Navy.WithAlpha(0.96f), Palette.Gold, ArtKeys.TitleDailyPanel);
            var title = UiFactory.Text(panel, "Title", "DAILY REWARD", 34, Palette.Parchment, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -18f), new Vector2(290f, 50f));
            UiFactory.Shadow(title, Color.black, 2f);

            _dailyChest = UiFactory.Rect(panel, "Chest");
            _dailyChest.Place(Center, Center, new Vector2(0f, 26f), new Vector2(210f, 196f));

            _dailyLine = UiFactory.Text(panel, "Line", "", 24, Palette.TextLight, TextAnchor.MiddleCenter);
            _dailyLine.rectTransform.Place(Center, Center, new Vector2(0f, -92f), new Vector2(290f, 36f));
            UiFactory.Shadow(_dailyLine, Color.black, 2f);

            var claim = UiFactory.Button(panel, "Claim", "CLAIM", Palette.PlayGreen, 34, ClaimDaily);
            claim.Rect.Place(Center, Center, new Vector2(0f, -146f), new Vector2(236f, 70f));
            // The sample button carries its label and its red "!", which is only true while there is something to claim.
            if (UiArt.ApplyPanel(claim.Background, claim.Border, ArtKeys.ButtonClaim)) claim.Label.enabled = false;
            _claim = claim.Rect.gameObject;

            var claimed = UiFactory.Button(panel, "Claimed", "TOMORROW", Palette.StoneDark, 28, null);
            claimed.Rect.Place(Center, Center, new Vector2(0f, -146f), new Vector2(236f, 62f));
            claimed.Button.interactable = false;
            claimed.Label.color = Palette.TextDim;
            _claimed = claimed.Rect.gameObject;
            RefreshDaily();
        }

        /// <summary>Claimable: the glowing chest and CLAIM. Claimed: the open chest and what tomorrow brings.</summary>
        void RefreshDaily()
        {
            if (_dailyChest == null) return;
            var today = System.DateTime.Now;
            var profile = _app.Session.Profile;
            bool ready = DailyReward.CanClaim(profile, today);
            _claim.SetActive(ready);
            _claimed.SetActive(!ready);

            for (int i = _dailyChest.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(_dailyChest.GetChild(i).gameObject);
            bool art = ready ? ArtAt(_dailyChest, Vector2.zero, 196f, ArtKeys.DailyRewardChest, ArtKeys.ChestLargeClosed)
                             : ArtAt(_dailyChest, Vector2.zero, 170f, ArtKeys.ChestLargeOpen);
            if (!art) Icons.Chest(Icons.Group(_dailyChest, Vector2.zero), !ready, 2.4f);

            var next = DailyReward.Reward(_app.Catalog, DailyReward.NextDay(profile, _app.Catalog, ready ? today : today.AddDays(1)));
            _dailyLine.text = ready ? "Claim Your Reward!" : $"Next: {next?.Label}";
        }

        void ClaimDaily()
        {
            var today = System.DateTime.Now;
            var profile = _app.Session.Profile;
            var reward = DailyReward.Claim(profile, _app.Catalog, today);
            if (reward != null)
            {
                _app.Session.SaveProfile();
                int week = _app.Catalog.DailyRewards.Count;
                int tomorrow = DailyReward.NextDay(profile, _app.Catalog, today.AddDays(1));
                string carried = reward.PotionRations + reward.HeartTokens + reward.SpecialKeys > 0 ? "\nIt goes with you into your next run." : "";
                _modal.Show("DAILY REWARD", $"Day {profile.DailyStreak} of {week}: {reward.Label}.{carried}\n\n"
                        + $"Come back tomorrow for day {tomorrow}: {DailyReward.Reward(_app.Catalog, tomorrow).Label}.\nMiss a day and the week starts over.",
                    _modal.Hide, Menus.B("NICE!", Palette.PlayGreen, _modal.Hide));
            }
            RefreshPurse();
            RefreshDaily();
        }

        // The reference title's bottom row, as left edge and width on a 1920-wide screen (its 1672-wide art scaled up).
        // INVENTORY and TALENTS keep their slots empty until those systems exist.
        static readonly (float x, float w) PlaySlot = (67f, 324f), HeroSlot = (414f, 201f), InventorySlot = (636f, 215f), TalentsSlot = (871f, 212f), ShopSlot = (1103f, 207f),
            SettingsSlot = (1333f, 209f), QuitSlot = (1572f, 274f);
        const float ButtonRowHeight = 154f, ButtonRowBottom = 33f;

        /// <summary>
        /// One bottom-row button in its reference slot. The sample art's button carries its own icon and label, so with art the
        /// drawn label is hidden and the fallback icon is skipped; without it, the placeholder draws both.
        /// </summary>
        UiFactory.ButtonParts TitleButton(string name, string label, Color color, System.Action action, (float x, float w) slot,
            string artKey, System.Action<RectTransform> drawIcon, int fontSize = 34)
        {
            var button = UiFactory.Button(Root, name, label, color, fontSize, action);
            button.Rect.Place(Vector2.zero, Vector2.zero, new Vector2(slot.x, ButtonRowBottom), new Vector2(slot.w, ButtonRowHeight));
            if (UiArt.ApplyPanel(button.Background, button.Border, artKey))
            {
                button.Label.enabled = false;
                return button;
            }
            button.Label.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(slot.w - 10f, 44f));
            var icon = UiFactory.Rect(button.Rect, "Icon");
            icon.Place(Center, Center, new Vector2(0f, 24f), new Vector2(72f, 72f));
            drawIcon?.Invoke(icon);
            return button;
        }

        void BuildBottomBar()
        {
            var play = TitleButton("Play", "PLAY", Palette.PlayGreen, Play, PlaySlot, ArtKeys.ButtonPlay, icon =>
            {
                if (!ArtAt(icon, Vector2.zero, 64f, ArtKeys.PlayIcon))
                    Icons.Ability(Icons.Group(icon, Vector2.zero, 0f, 0.8f), CommandKind.Slash);
            }, 60);
            play.Border.color = Palette.Gold;

            TitleButton("HeroSelect", "HERO SELECT", Palette.Navy, OpenHeroSelect, HeroSlot, ArtKeys.ButtonHeroSelect, icon =>
            {
                // Without the sample button, the button shows whoever takes the next run.
                _heroButtonIcon = icon;
                RefreshHeroCard();
            }, 30);

            TitleButton("Inventory", "INVENTORY", Palette.Navy, OpenInventory, InventorySlot, ArtKeys.ButtonInventory, icon =>
                Icons.Shape(icon, Shapes.Rounded, Palette.ChestWood, Vector2.zero, new Vector2(60f, 56f)));

            _talentsButton = TitleButton("Talents", "TALENTS", Palette.Navy, OpenTalents, TalentsSlot, ArtKeys.ButtonTalents, icon =>
                Icons.Shape(icon, Shapes.Diamond, Palette.Gold, Vector2.zero, new Vector2(56f, 56f)));

            TitleButton("Shop", "SHOP", Palette.Navy, OpenShop, ShopSlot, ArtKeys.ButtonShop, icon =>
            {
                if (!ArtAt(icon, Vector2.zero, 72f, ArtKeys.CoinIcon, ArtKeys.ChestLargeClosed))
                    Icons.Shape(icon, Shapes.Circle, Palette.Gold, Vector2.zero, new Vector2(52f, 52f));
            });

            TitleButton("Settings", "SETTINGS", Palette.Navy, () => Menus.OpenSettings(_modal, _modal.Hide, _app.ApplyTelemetrySetting),
                SettingsSlot, ArtKeys.ButtonTitleSettings, icon =>
                {
                    if (!ArtAt(icon, Vector2.zero, 70f, ArtKeys.SettingsIcon)) Icons.Gear(icon, Palette.Steel, 64f);
                });

#if !UNITY_IOS
            TitleButton("Quit", "QUIT", Palette.QuitRed, _app.Quit, QuitSlot, ArtKeys.ButtonQuit, icon => ArtAt(icon, Vector2.zero, 70f, ArtKeys.QuitIcon), 40);
#endif
        }
    }
}
