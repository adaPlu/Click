using System.Linq;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>Popups that land on one tile in the same turn stay readable (Unity EditMode only).</summary>
    public class PopupStackTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PopupTestRoot", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        float[] PopupHeights() => _root.GetComponentsInChildren<Text>(true)
            .Where(t => t.name == "Popup")
            .Select(t => t.rectTransform.anchoredPosition.y)
            .ToArray();

        [Test]
        public void SameTilePopupsStackInsteadOfOverlapping()
        {
            var board = new BoardView((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);
            board.Popup(new GridPos(1, 2), "DAZED", Color.yellow);
            board.Popup(new GridPos(1, 2), "-2", Color.white);
            board.Popup(new GridPos(3, 2), "BLOCK!", Color.yellow);

            var heights = PopupHeights();
            Assert.That(heights.Length, Is.EqualTo(3));
            Assert.That(heights[0] - heights[1], Is.GreaterThanOrEqualTo(BoardView.PopupSpacing - 0.5f), "The second popup on a tile sits below the first.");
            Assert.That(heights[2], Is.EqualTo(heights[0]).Within(0.5f), "A popup on another tile in the same row is not pushed down.");
        }

        [Test]
        public void BottomRowPopupsStayApartWhenThereIsNoRoomBelow()
        {
            var board = new BoardView((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);
            for (int i = 0; i < 3; i++) board.Popup(new GridPos(1, 0), "#" + i, Color.white);

            var heights = PopupHeights();
            for (int a = 0; a < heights.Length; a++)
            for (int b = a + 1; b < heights.Length; b++)
                Assert.That(Mathf.Abs(heights[a] - heights[b]), Is.GreaterThanOrEqualTo(BoardView.PopupSpacing - 0.5f), $"popups {a} and {b}");
        }

        [Test]
        public void EachTurnStartsFreshStacks()
        {
            var board = new BoardView((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);
            board.Popup(new GridPos(2, 2), "turn 1", Color.white);
            board.BeginPopupBatch();
            board.Popup(new GridPos(2, 2), "turn 2", Color.white);

            var heights = PopupHeights();
            Assert.That(heights[1], Is.EqualTo(heights[0]).Within(0.5f));
        }
    }
}
