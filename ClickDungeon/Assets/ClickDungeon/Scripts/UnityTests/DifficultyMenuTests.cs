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
    /// <summary>Difficulty picker built from content (Unity EditMode only).</summary>
    public class DifficultyMenuTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("DifficultyTestRoot", typeof(RectTransform));
            // No button art, so the procedural colours show which tier is highlighted.
            var empty = ScriptableObject.CreateInstance<ArtCatalog>();
            empty.SetEntries(Enumerable.Empty<ArtCatalog.Entry>());
            Art.Override(empty);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Art.Reset();
        }

        Button[] Buttons() => _root.GetComponentsInChildren<Button>(true);

        [Test]
        public void PickerListsEveryTierFromContentAndReportsTheChoice()
        {
            var modal = new ModalOverlay((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>());
            Difficulty? chosen = null;
            bool cancelled = false;

            Menus.OpenDifficulty(modal, Catalog, Difficulty.Medium, tier => chosen = tier, () => cancelled = true);

            var labels = Buttons().Select(b => b.name).ToArray();
            var expected = Menus.DifficultyOrder.Select(t => Catalog.DifficultyInfo(t).DisplayName.ToUpperInvariant()).Concat(new[] { "CANCEL" });
            Assert.That(labels, Is.EqualTo(expected));

            var body = _root.GetComponentsInChildren<Text>(true).First(t => t.name == "Body").text;
            foreach (var tier in Menus.DifficultyOrder)
                Assert.That(body, Does.Contain(Catalog.DifficultyInfo(tier).Tagline));

            Assert.That(Buttons().First(b => b.name == labels[1]).GetComponent<Image>().color, Is.EqualTo(Palette.PlayGreen), "The last tier played is highlighted.");

            Buttons().First(b => b.name == labels[0]).onClick.Invoke();
            Assert.That(chosen, Is.EqualTo(Difficulty.Easy));
            Buttons().First(b => b.name == "CANCEL").onClick.Invoke();
            Assert.That(cancelled, Is.True);
        }
    }
}
