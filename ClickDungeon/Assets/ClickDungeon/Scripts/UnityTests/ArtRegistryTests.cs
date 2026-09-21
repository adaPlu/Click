using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.EditorTools;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>Presentation-side checks for the art registry (Unity EditMode only).</summary>
    public class ArtRegistryTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ArtTestRoot", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Art.Reset();
        }

        static Sprite MakeSprite(string name)
        {
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        static ArtCatalog CatalogWith(string key, params Sprite[] frames)
        {
            var catalog = ScriptableObject.CreateInstance<ArtCatalog>();
            catalog.SetEntries(new[] { new ArtCatalog.Entry { Key = key, Frames = frames, Fps = 10f } });
            return catalog;
        }

        [Test]
        public void MissingArtFallsBackToPlaceholder()
        {
            Art.Override(ScriptableObject.CreateInstance<ArtCatalog>());
            Icons.Spikes(_root.transform);

            Assert.That(_root.transform.childCount, Is.GreaterThan(1));
            Assert.That(_root.GetComponentsInChildren<Image>().Any(i => i.name.StartsWith("Art ")), Is.False);
        }

        [Test]
        public void CatalogArtReplacesPlaceholder()
        {
            var sprite = MakeSprite(ArtKeys.Spikes);
            Art.Override(CatalogWith(ArtKeys.Spikes, sprite));
            Icons.Spikes(_root.transform);

            Assert.That(_root.transform.childCount, Is.EqualTo(1));
            var image = _root.GetComponentInChildren<Image>();
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(image.preserveAspect, Is.True);
            Assert.That(image.GetComponent<SpriteFrameAnimator>(), Is.Null);
        }

        [Test]
        public void MultiFrameArtIsAnimated()
        {
            var frames = new[] { MakeSprite("a"), MakeSprite("b"), MakeSprite("c") };
            Art.Override(CatalogWith(ArtKeys.Actor("goblin"), frames));
            Icons.Enemy(_root.transform, ContentCatalog.CreateDefault().Enemy("goblin"), EnemyMode.Normal);

            var image = _root.GetComponentInChildren<Image>();
            Assert.That(image.sprite, Is.SameAs(frames[0]));
            Assert.That(image.GetComponent<SpriteFrameAnimator>(), Is.Not.Null);
        }

        [Test]
        public void BossFallsBackFromStateArtToIdleArt()
        {
            var idle = MakeSprite("idle");
            Art.Override(CatalogWith(ArtKeys.Actor("lord_blobert"), idle));
            Icons.Enemy(_root.transform, ContentCatalog.CreateDefault().Enemy("lord_blobert"), EnemyMode.Puffed);

            Assert.That(_root.GetComponentsInChildren<Image>().Any(i => i.sprite == idle), Is.True);
        }

        [Test]
        public void ProductionArtWinsOverReferenceSlices()
        {
            const string slice = "Assets/ClickDungeon/Art/Runtime/Placeholders/Tiles/tile_key.png";
            const string production = "Assets/ClickDungeon/Art/Runtime/Tiles/tile_key.png";

            Assert.That(ArtCatalogBuilder.PreferCandidate(slice, production), Is.True);
            Assert.That(ArtCatalogBuilder.PreferCandidate(production, slice), Is.False);
            Assert.That(ArtCatalogBuilder.PreferCandidate(production, production), Is.False);
            Assert.That(ArtCatalogBuilder.IsPlaceholder(slice), Is.True);
            Assert.That(ArtCatalogBuilder.IsPlaceholder(production), Is.False);
        }

        [Test]
        public void FramesGroupByNumericSuffix()
        {
            var grouped = ArtCatalogBuilder.GroupFrames(new[]
            {
                "tile_key", "actor_goblin_idle_010", "actor_goblin_idle_000", "actor_goblin_idle_001",
            });

            Assert.That(grouped.Keys, Is.EquivalentTo(new[] { "tile_key", "actor_goblin_idle" }));
            Assert.That(grouped["actor_goblin_idle"], Is.EqualTo(new[] { "actor_goblin_idle_000", "actor_goblin_idle_001", "actor_goblin_idle_010" }));
            Assert.That(grouped["tile_key"], Is.EqualTo(new[] { "tile_key" }));
        }

        [Test]
        public void WiredKeysFollowNamingConvention()
        {
            var keys = ArtKeys.Wired(ContentCatalog.CreateDefault());
            Assert.That(keys, Is.Unique);
            foreach (var key in keys)
                Assert.That(key, Does.Match("^(tile|actor|portrait|icon|ui|fx|bg|logo)_[a-z0-9_]+$"), key);
            Assert.That(keys, Does.Contain("actor_lord_blobert_idle"));
            // The default hero's expressions (Ironheart since D-057; the retired mascot's are no longer looked up).
            Assert.That(keys, Does.Contain(ArtKeys.Portrait(ArtKeys.HeroId, "happy")));
        }
    }
}
