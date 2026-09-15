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
    /// <summary>Underfoot badge art with procedural fallback (Unity EditMode only).</summary>
    public class UnderfootBadgeArtTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("UnderfootTestRoot", typeof(RectTransform));
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

        static CellState Spikes() => new CellState { Hazard = HazardKind.Spikes, Knowledge = Knowledge.Revealed };

        static CellState ArmedBomb() => new CellState { Hazard = HazardKind.Bomb, BombFuse = 0, Knowledge = Knowledge.Revealed };

        Image[] Images() => _root.GetComponentsInChildren<Image>(true);

        [Test]
        public void StateArtReplacesTheWholeBadge()
        {
            var spikes = MakeSprite("spikes");
            UseArt((ArtKeys.UnderfootSpikes, spikes));
            Icons.Underfoot(_root.transform, Spikes(), false);

            Assert.That(_root.transform.childCount, Is.EqualTo(1));
            Assert.That(Images().Single().sprite, Is.SameAs(spikes));
        }

        [Test]
        public void ArmedBombWithoutArmedArtKeepsTheFuseRing()
        {
            var bomb = MakeSprite("bomb");
            UseArt((ArtKeys.UnderfootBomb, bomb));
            Icons.Underfoot(_root.transform, ArmedBomb(), false);

            var images = Images();
            Assert.That(images.Any(i => i.sprite == bomb), Is.True);
            Assert.That(images.Any(i => i.sprite == Shapes.Ring && i.color == Palette.Fuse), Is.True,
                "Armed must stay readable without its own art.");
        }

        [Test]
        public void ArmedBombArtWinsOverPlainBombArt()
        {
            var bomb = MakeSprite("bomb");
            var armed = MakeSprite("armed");
            UseArt((ArtKeys.UnderfootBomb, bomb), (ArtKeys.UnderfootBombArmed, armed));
            Icons.Underfoot(_root.transform, ArmedBomb(), false);

            Assert.That(Images().Select(i => i.sprite), Is.EqualTo(new[] { armed }));
        }

        [Test]
        public void MissingArtKeepsTheProceduralBadge()
        {
            UseArt();
            Icons.Underfoot(_root.transform, Spikes(), false);

            Assert.That(_root.transform.childCount, Is.GreaterThan(2));
            Assert.That(Images().Any(i => i.name.StartsWith("Art ")), Is.False);
        }

        [Test]
        public void KeysFollowTheTileState()
        {
            Assert.That(ArtKeys.Underfoot(new CellState { IsExit = true }, false), Is.EqualTo(ArtKeys.UnderfootExitLocked));
            Assert.That(ArtKeys.Underfoot(new CellState { IsExit = true }, true), Is.EqualTo(ArtKeys.UnderfootExitOpen));
            Assert.That(ArtKeys.Underfoot(new CellState { Hazard = HazardKind.Bomb }, false), Is.EqualTo(ArtKeys.UnderfootBomb));
            Assert.That(ArtKeys.Underfoot(ArmedBomb(), false), Is.EqualTo(ArtKeys.UnderfootBombArmed));
            Assert.That(ArtKeys.Underfoot(new CellState(), false), Is.Null);

            var wired = ArtKeys.Wired(Catalog);
            foreach (var key in new[] { ArtKeys.UnderfootSpikes, ArtKeys.UnderfootBomb, ArtKeys.UnderfootBombArmed, ArtKeys.UnderfootExitLocked, ArtKeys.UnderfootExitOpen })
                Assert.That(wired, Does.Contain(key));
        }

        [Test]
        public void HeroStandingOnSpikesShowsTheBadgeArt()
        {
            var spikes = MakeSprite("spikes");
            UseArt((ArtKeys.UnderfootSpikes, spikes));

            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = new GridPos(2, 2);
            floor[new GridPos(2, 2)].Hazard = HazardKind.Spikes;
            var run = new RunState
            {
                RunSeed = 1,
                FloorCount = Catalog.RunFloorCount,
                Hero = RunFactory.CreateHero(Catalog, ContentCatalog.DefaultHeroId),
                Floor = floor,
            };
            RunFactory.SetupFloor(run, Catalog, new List<GameEvent>());

            var board = new BoardView((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);
            board.Render(run, Catalog, new List<Threat>(), new HashSet<GridPos>(), false, null, false);

            var labels = _root.GetComponentsInChildren<Transform>(true).First(t => t.name == "Labels 2,2");
            Assert.That(labels.GetComponentsInChildren<Image>(true).Any(i => i.sprite == spikes), Is.True);
        }
    }
}
