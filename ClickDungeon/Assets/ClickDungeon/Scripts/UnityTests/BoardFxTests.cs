using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>Board FX: event mapping, playback, Reduced Motion and the bomb fuse loop (Unity EditMode only).</summary>
    public class BoardFxTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;
        bool _reducedMotion;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("FxTestRoot", typeof(RectTransform));
            _reducedMotion = UserPrefs.ReducedMotion;
            UserPrefs.ReducedMotion = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Art.Reset();
            UserPrefs.ReducedMotion = _reducedMotion;
        }

        static Sprite[] Frames(string name, int count) => Enumerable.Range(0, count).Select(i =>
        {
            var sprite = Sprite.Create(new Texture2D(16, 16), new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f));
            sprite.name = $"{name}_{i}";
            return sprite;
        }).ToArray();

        static void UseArt(params (string key, Sprite[] frames)[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<ArtCatalog>();
            catalog.SetEntries(entries.Select(e => new ArtCatalog.Entry { Key = e.key, Frames = e.frames, Fps = 10f }));
            Art.Override(catalog);
        }

        static RunState EmptyRun()
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = new GridPos(0, 0);
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

        BoardView RenderWith(IReadOnlyList<GameEvent> events)
        {
            var run = EmptyRun();
            var board = new BoardView((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);
            board.QueueActorAnimations(run, events);
            board.Render(run, Catalog, new List<Threat>(), new HashSet<GridPos>(), false, null, false);
            return board;
        }

        Transform Effects() => _root.GetComponentsInChildren<Transform>(true).First(t => t.name == "Effects");

        [Test]
        public void EventsMapToBoardEffects()
        {
            var effects = BoardFx.Pick(new List<GameEvent>
            {
                GameEvent.Of(GameEventKind.SpikesTriggered, to: new GridPos(1, 1)),
                GameEvent.Of(GameEventKind.BombExploded, to: new GridPos(2, 2)),
                GameEvent.Of(GameEventKind.KeyCollected, to: new GridPos(3, 3)),
                GameEvent.Of(GameEventKind.PotionCollected, to: new GridPos(4, 3)),
                GameEvent.Of(GameEventKind.EnemyWoke, to: new GridPos(0, 4)),
                GameEvent.Of(GameEventKind.HeroMoved, to: new GridPos(0, 1)),
                GameEvent.Of(GameEventKind.ExitUnlocked),
            });

            // D-045: keys, potions and spikes no longer sparkle — they happen every few turns and already have a popup.
            Assert.That(effects.Select(e => e.Key), Is.EqualTo(new[] { ArtKeys.FxExplosion, ArtKeys.FxEnemyWake }),
                "Only the once-in-a-while moments draw an effect; events without a cell add nothing.");
            Assert.That(effects[0].Tiles, Is.EqualTo(3));
            Assert.That(effects[0].Cell, Is.EqualTo(new GridPos(2, 2)));
        }

        [Test]
        public void ExplosionCoversTheBlastAndRemovesItself()
        {
            var frames = Frames("boom", 3);
            UseArt((ArtKeys.FxExplosion, frames));
            RenderWith(new List<GameEvent> { GameEvent.Of(GameEventKind.BombExploded, to: new GridPos(2, 2)) });

            var fx = Effects().Find("FX " + ArtKeys.FxExplosion);
            Assert.That(fx, Is.Not.Null);
            var rect = (RectTransform)fx;
            Assert.That(rect.sizeDelta.x, Is.EqualTo(3 * BoardView.CellSize + 2 * BoardView.Gap).Within(0.01f));
            Assert.That(rect.anchoredPosition, Is.EqualTo(BoardView.CellPosition(new GridPos(2, 2))));
            Assert.That(fx.GetComponent<Image>().sprite, Is.SameAs(frames[0]));

            fx.GetComponent<SpriteFrameAnimator>().Advance(1f);
            Assert.That(Effects().Find("FX " + ArtKeys.FxExplosion), Is.Null);
        }

        [Test]
        public void ReducedMotionSkipsBoardEffects()
        {
            UserPrefs.ReducedMotion = true;
            UseArt((ArtKeys.FxEnemyWake, Frames("wake", 2)));
            RenderWith(new List<GameEvent> { GameEvent.Of(GameEventKind.EnemyWoke, to: new GridPos(1, 0)) });
            Assert.That(Effects().Find("FX " + ArtKeys.FxEnemyWake), Is.Null);
        }

        [Test]
        public void ArmedBombsLoopTheFuseArt()
        {
            var fuse = Frames("fuse", 2);
            UseArt((ArtKeys.BombFuse, fuse));

            var armed = UiFactory.Rect(_root.transform, "Armed");
            Icons.Bomb(armed, true, 1);
            var fuseImage = armed.GetComponentsInChildren<Image>().FirstOrDefault(i => i.sprite == fuse[0]);
            Assert.That(fuseImage, Is.Not.Null);
            Assert.That(fuseImage.GetComponent<SpriteFrameAnimator>(), Is.Not.Null, "Multi-frame fuse art loops.");
            Assert.That(armed.GetComponentsInChildren<Text>().Any(t => t.text == "ARMED"), Is.True, "The armed state stays live text.");

            var idle = UiFactory.Rect(_root.transform, "Idle");
            Icons.Bomb(idle, false, -1);
            Assert.That(idle.GetComponentsInChildren<Image>().Any(i => i.sprite == fuse[0]), Is.False);
        }

        [Test]
        public void FxKeysAreWired()
        {
            var wired = ArtKeys.Wired(Catalog);
            foreach (var key in new[] { ArtKeys.FxExplosion, ArtKeys.FxExitUnlock, ArtKeys.FxEnemyWake, ArtKeys.BombFuse })
                Assert.That(wired, Does.Contain(key));
            Assert.That(wired, Is.Unique);
        }
    }
}
