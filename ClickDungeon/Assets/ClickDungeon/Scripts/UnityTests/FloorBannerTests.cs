using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>Floor transition banner: live text, input pass-through and art fallback (Unity EditMode only).</summary>
    public class FloorBannerTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BannerTestRoot", typeof(RectTransform));
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

        FloorBanner NewBanner() =>
            new FloorBanner((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);

        Transform Banner() => _root.transform.Find("FloorBanner");

        [Test]
        public void ShowsLiveFloorTextWithoutBlockingInput()
        {
            UseArt();
            var banner = NewBanner();
            Assert.That(banner.IsShowing, Is.False);

            banner.Show(2, "The Damp Cellars", false);

            Assert.That(banner.IsShowing, Is.True);
            Assert.That(Banner().Find("Title").GetComponent<Text>().text, Is.EqualTo("FLOOR 2"));
            Assert.That(Banner().Find("Name").GetComponent<Text>().text, Is.EqualTo("THE DAMP CELLARS"));
            var group = Banner().GetComponent<CanvasGroup>();
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(group.interactable, Is.False);
        }

        [Test]
        public void BossFloorUsesBossArtThenTheDefaultPlate()
        {
            var boss = MakeSprite("boss");
            var plate = MakeSprite("plate");
            UseArt((ArtKeys.FloorBannerBoss, boss), (ArtKeys.FloorBanner, plate));
            var banner = NewBanner();

            banner.Show(5, "Blobert's Court", true);
            Assert.That(Banner().Find("Back").GetComponent<Image>().sprite, Is.SameAs(boss));
            Assert.That(Banner().Find("Border").GetComponent<Image>().enabled, Is.False);

            banner.Show(3, "The Ember Vaults", false);
            Assert.That(Banner().Find("Back").GetComponent<Image>().sprite, Is.SameAs(plate));
        }

        [Test]
        public void FloorWithoutArtRestoresTheProceduralPlate()
        {
            UseArt((ArtKeys.FloorBannerBoss, MakeSprite("boss")));
            var banner = NewBanner();

            banner.Show(5, "Blobert's Court", true);
            banner.Show(1, "The Upper Halls", false);

            var back = Banner().Find("Back").GetComponent<Image>();
            Assert.That(back.sprite, Is.SameAs(Shapes.Rounded));
            Assert.That(Banner().Find("Border").GetComponent<Image>().enabled, Is.True);
            Assert.That(Banner().Find("Title").GetComponent<Text>().color, Is.EqualTo(Palette.Gold));
        }

        [Test]
        public void HideStopsTheBanner()
        {
            UseArt();
            var banner = NewBanner();
            banner.Show(1, "The Upper Halls", false);
            banner.Hide();
            Assert.That(banner.IsShowing, Is.False);
        }

        [Test]
        public void BannerKeysAreWired()
        {
            var wired = ArtKeys.Wired(ContentCatalog.CreateDefault());
            Assert.That(wired, Does.Contain(ArtKeys.FloorBanner));
            Assert.That(wired, Does.Contain(ArtKeys.FloorBannerBoss));
            Assert.That(wired, Is.Unique);
        }
    }
}
