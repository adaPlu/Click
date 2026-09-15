using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>Intent badge icons and danger overlay art, with procedural fallback (Unity EditMode only).</summary>
    public class IntentAndDangerArtTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BoardTestRoot", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Art.Reset();
        }

        static Sprite MakeSprite(string name)
        {
            var sprite = Sprite.Create(new Texture2D(8, 8), new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        /// <summary>Hero at (2,2) with an awake goblin beside it, so the goblin telegraphs HIT 2 on the hero's tile.</summary>
        static RunState GoblinNextToHero()
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = new GridPos(2, 2);
            EnemyAi.Spawn(floor, Catalog.Enemy("goblin"), new GridPos(3, 2), awake: true);
            var run = new RunState
            {
                RunSeed = 1,
                FloorCount = Catalog.RunFloorCount,
                Hero = RunFactory.CreateHero(Catalog, ContentCatalog.DefaultHeroId),
                Floor = floor,
            };
            RunFactory.SetupFloor(run, Catalog, new List<GameEvent>());
            return run;
        }

        void RenderBoard(RunState run)
        {
            var host = _root.AddComponent<SpriteFrameAnimator>();
            var board = new BoardView((RectTransform)_root.transform, host, Vector2.zero);
            board.Render(run, Catalog, Threats.Compute(run, Catalog), new HashSet<GridPos>(), false, null, false);
        }

        Transform Named(string name) => _root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        Image EnemyBadgeIcon() => _root.GetComponentsInChildren<Image>(true)
            .First(i => i.name == "Icon" && i.transform.parent.name == "Badge" && i.transform.parent.parent.name.StartsWith("Enemy"));

        [Test]
        public void ArtReplacesIntentIconAndDangerOverlay()
        {
            var run = GoblinNextToHero();
            Assert.That(run.Floor.Enemies[0].Intent.Kind, Is.EqualTo(IntentKind.Attack));

            var intent = MakeSprite("intent");
            var overlay = MakeSprite("overlay");
            var warning = MakeSprite("warning");
            var catalog = ScriptableObject.CreateInstance<ArtCatalog>();
            catalog.SetEntries(new[]
            {
                new ArtCatalog.Entry { Key = ArtKeys.IntentIcon(IntentKind.Attack), Frames = new[] { intent } },
                new ArtCatalog.Entry { Key = ArtKeys.DangerOverlay(ThreatKind.Attack), Frames = new[] { overlay } },
                new ArtCatalog.Entry { Key = ArtKeys.DangerWarning, Frames = new[] { warning } },
            });
            Art.Override(catalog);

            RenderBoard(run);

            var badgeIcon = EnemyBadgeIcon();
            Assert.That(badgeIcon.gameObject.activeSelf, Is.True);
            Assert.That(badgeIcon.sprite, Is.SameAs(intent));
            Assert.That(badgeIcon.transform.parent.GetComponentInChildren<Text>().text, Is.EqualTo("HIT 2"), "Badge text stays live.");

            var cellOverlay = Named("Cell 2,2").Find("Overlay");
            Assert.That(cellOverlay.childCount, Is.EqualTo(1), "Overlay art replaces the procedural fill and frame.");
            Assert.That(cellOverlay.GetComponentInChildren<Image>().sprite, Is.SameAs(overlay));

            var labels = Named("Labels 2,2");
            Assert.That(labels.GetComponentsInChildren<Image>(true).Any(i => i.sprite == warning), Is.True);
            var texts = labels.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(texts, Has.Some.Contains("-2"));
            Assert.That(texts, Has.Some.Contains("HIT"));
            Assert.That(texts, Does.Not.Contain("!"), "Warning art replaces the procedural triangle glyph.");
        }

        [Test]
        public void MissingArtKeepsProceduralTelegraphs()
        {
            Art.Override(ScriptableObject.CreateInstance<ArtCatalog>());
            RenderBoard(GoblinNextToHero());

            Assert.That(EnemyBadgeIcon().gameObject.activeSelf, Is.False);
            Assert.That(Named("Cell 2,2").Find("Overlay").childCount, Is.EqualTo(2));
            var labels = Named("Labels 2,2").GetComponentsInChildren<Text>(true).ToList();
            var texts = labels.Select(t => t.text).ToList();
            Assert.That(texts, Does.Contain("!"));
            Assert.That(texts, Has.Some.Contains("-2"));
            Assert.That(labels.All(t => t.rectTransform.anchoredPosition.y > 0f), Is.True,
                "Telegraph labels stay out of the tile's lower half, where the badge of an enemy below sits.");
        }

        [Test]
        public void EveryIntentAndThreatHasAWiredKey()
        {
            var wired = ArtKeys.Wired(Catalog);
            foreach (var kind in ArtKeys.IntentKinds) Assert.That(wired, Does.Contain(ArtKeys.IntentIcon(kind)));
            foreach (var kind in ArtKeys.ThreatKinds) Assert.That(wired, Does.Contain(ArtKeys.DangerOverlay(kind)));
            Assert.That(ArtKeys.IntentIcon(IntentKind.None), Is.Null);
            Assert.That(ArtKeys.DangerOverlay(ThreatKind.BombBlast), Is.EqualTo("ui_danger_blast"));
            Assert.That(ArtKeys.IntentIcon(IntentKind.PuffUp), Is.EqualTo("icon_intent_puffup"));
        }
    }
}
