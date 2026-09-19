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
    /// <summary>REL-04 and PERF-01: token tweens yield to newer placements; hover redraws only highlights (Unity EditMode only).</summary>
    public class BoardRemediationTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;
        bool _reducedMotion;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BoardRemediationRoot", typeof(RectTransform));
            _reducedMotion = UserPrefs.ReducedMotion;
            UserPrefs.ReducedMotion = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            UserPrefs.ReducedMotion = _reducedMotion;
        }

        [Test]
        public void OlderMoveStopsOnceANewerOneTakesOver()
        {
            var rt = UiFactory.Rect(_root.transform, "Token");
            rt.anchoredPosition = Vector2.zero;
            int version = 1;
            var older = Tween.MoveTo(rt, new Vector2(100f, 0f), 0.14f, () => version == 1);
            Assert.That(older.MoveNext(), Is.True);

            // A second move on the same token (the next turn within 0.14 s), as BoardView.UpsertToken does it.
            version = 2;
            var newer = Tween.MoveTo(rt, new Vector2(-300f, 50f), 0.14f, () => version == 2);
            Assert.That(newer.MoveNext(), Is.True);
            var afterNewerStep = rt.anchoredPosition;

            Assert.That(older.MoveNext(), Is.False, "The older move stops.");
            Assert.That(rt.anchoredPosition, Is.EqualTo(afterNewerStep), "It does not write its own position again.");
        }

        [Test]
        public void CancelledMoveNeverSnapsToItsTarget()
        {
            var rt = UiFactory.Rect(_root.transform, "Token");
            rt.anchoredPosition = Vector2.zero;
            bool current = true;
            var move = Tween.MoveTo(rt, new Vector2(100f, 0f), 0.14f, () => current);
            Assert.That(move.MoveNext(), Is.True);

            current = false;   // an instant placement on the next floor's start
            var placed = new Vector2(-300f, 50f);
            rt.anchoredPosition = placed;
            Assert.That(move.MoveNext(), Is.False);
            Assert.That(rt.anchoredPosition, Is.EqualTo(placed));
        }

        [Test]
        public void HoverRedrawsHighlightsWithoutRebuildingTiles()
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = new GridPos(2, 2);
            floor.Exit = new GridPos(4, 4);
            floor[floor.Exit].IsExit = true;
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
            var before = _root.GetComponentsInChildren<Transform>(true).ToList();

            board.RenderHighlights(new HashSet<GridPos> { new GridPos(2, 3) }, true, new GridPos(1, 1));

            // Same objects, in the same order: nothing was destroyed and recreated.
            var after = _root.GetComponentsInChildren<Transform>(true).ToList();
            Assert.That(after.Count, Is.EqualTo(before.Count));
            for (int i = 0; i < before.Count; i++)
                Assert.That(ReferenceEquals(after[i], before[i]), Is.True, "No tile objects were destroyed or created.");
            Transform Labels(string name) => _root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
            Assert.That(Labels("Labels 1,1").GetComponentsInChildren<Image>(true).Any(i => i.enabled), Is.True, "Hover frame shown.");
            Assert.That(Labels("Labels 2,3").GetComponentsInChildren<Image>(true).Any(i => i.enabled), Is.True, "Legal frame shown.");
            Assert.That(Labels("Labels 3,3").GetComponentsInChildren<Image>(true).Any(i => i.enabled), Is.False);
        }
    }
}
