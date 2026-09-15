using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>Modal panels, modal buttons and chest overlay art with procedural fallback (Unity EditMode only).</summary>
    public class ModalAndTitleArtTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ModalTestRoot", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Art.Reset();
        }

        static Sprite MakeSprite(string name)
        {
            var sprite = Sprite.Create(new Texture2D(16, 16), new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        static void UseArt(params (string key, Sprite sprite)[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<ArtCatalog>();
            catalog.SetEntries(entries.Select(e => new ArtCatalog.Entry { Key = e.key, Frames = new[] { e.sprite } }));
            Art.Override(catalog);
        }

        MonoBehaviour Host()
        {
            // Explicit Unity null check: GetComponent can return a "fake null" that ?? treats as present.
            var host = _root.GetComponent<SpriteFrameAnimator>();
            return host != null ? host : _root.AddComponent<SpriteFrameAnimator>();
        }

        Transform Named(string name) => _root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        Image PanelPart(string part) => Named("Panel").Find(part).GetComponent<Image>();

        [Test]
        public void ModalUsesStyleArtThenTheDefaultPanel()
        {
            var victory = MakeSprite("victory");
            var panel = MakeSprite("panel");
            UseArt((ArtKeys.ModalPanelStyle("victory"), victory), (ArtKeys.ModalPanel, panel));
            var modal = new ModalOverlay((RectTransform)_root.transform, Host());

            modal.Show(ModalStyle.Victory, "VICTORY!", "", null);
            Assert.That(PanelPart("Back").sprite, Is.SameAs(victory));
            Assert.That(PanelPart("Border").enabled, Is.False);

            modal.Show(ModalStyle.Defeat, "DEFEATED", "", null);
            Assert.That(PanelPart("Back").sprite, Is.SameAs(panel), "Defeat has no art of its own, so it uses ui_modal_panel.");
        }

        [Test]
        public void ModalRestoresTheProceduralPanelWhenAStyleHasNoArt()
        {
            UseArt((ArtKeys.ModalPanelStyle("victory"), MakeSprite("victory")));
            var modal = new ModalOverlay((RectTransform)_root.transform, Host());

            modal.Show(ModalStyle.Victory, "VICTORY!", "", null);
            modal.Show("PAUSED", "", null);

            Assert.That(PanelPart("Back").sprite, Is.SameAs(Shapes.Rounded));
            Assert.That(PanelPart("Back").color, Is.EqualTo(Palette.Navy));
            Assert.That(PanelPart("Border").enabled, Is.True);
        }

        [Test]
        public void ModalButtonsPickArtByRole()
        {
            var primary = MakeSprite("primary");
            var danger = MakeSprite("danger");
            UseArt((ArtKeys.ButtonPrimary, primary), (ArtKeys.ButtonDanger, danger));
            var modal = new ModalOverlay((RectTransform)_root.transform, Host());

            modal.Show("CHOOSE", "", null,
                Menus.B("GO", Palette.PlayGreen, null), Menus.B("STOP", Palette.QuitRed, null), Menus.B("BACK", Palette.NavyLight, null));

            Image ButtonImage(string label) => _root.GetComponentsInChildren<Button>(true).First(b => b.name == label).GetComponent<Image>();
            Assert.That(ButtonImage("GO").sprite, Is.SameAs(primary));
            Assert.That(ButtonImage("STOP").sprite, Is.SameAs(danger));
            Assert.That(ButtonImage("BACK").sprite, Is.SameAs(Shapes.Rounded), "No ui_button_secondary art: placeholder stays.");
        }

        [Test]
        public void ChestOverlayUsesLargeChestMeterAndCardArt()
        {
            var chest = MakeSprite("chest");
            var fill = MakeSprite("fill");
            var card = MakeSprite("card");
            UseArt((ArtKeys.ChestLargeClosed, chest), (ArtKeys.ChestProgressFill, fill), (ArtKeys.ChestRewardCard, card));

            var overlay = new ChestOverlay((RectTransform)_root.transform, Host());
            overlay.Open(new RewardRecord { Kind = RewardKind.Potion, Amount = 1 }, null);

            Assert.That(Named("Chest").GetComponentsInChildren<Image>(true).Select(i => i.sprite), Is.EqualTo(new[] { chest }));
            Assert.That(Named("Fill").GetComponent<Image>().sprite, Is.SameAs(fill));
            Assert.That(Named("RewardCard").Find("Back").GetComponent<Image>().sprite, Is.SameAs(card));
            Assert.That(Named("RewardCard").Find("Border").GetComponent<Image>().enabled, Is.False);
        }

        [Test]
        public void ModalAndTitleKeysAreWired()
        {
            var wired = ArtKeys.Wired(ContentCatalog.CreateDefault());
            foreach (var key in new[]
                     {
                         ArtKeys.ModalPanel, "ui_modal_panel_victory", "ui_modal_panel_defeat", ArtKeys.ButtonPrimary,
                         ArtKeys.ChestLargeOpen, ArtKeys.TitleBanner, ArtKeys.ContinuePreview, ArtKeys.PlayIcon, ArtKeys.QuitIcon,
                     })
                Assert.That(wired, Does.Contain(key));
            Assert.That(ArtKeys.ModalButton(Palette.PlayGreen), Is.EqualTo(ArtKeys.ButtonPrimary));
            Assert.That(ArtKeys.ModalButton(Palette.QuitRed), Is.EqualTo(ArtKeys.ButtonDanger));
            Assert.That(ArtKeys.ModalButton(Palette.NavyLight), Is.EqualTo(ArtKeys.ButtonSecondary));
        }
    }
}
