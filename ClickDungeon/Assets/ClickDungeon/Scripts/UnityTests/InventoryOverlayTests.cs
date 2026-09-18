using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>The INVENTORY screen (D-028): found items wear on a tap, worn slots come off on a tap (Unity EditMode only).</summary>
    public class InventoryOverlayTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("InventoryTestRoot", typeof(RectTransform));
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

        Button Tile(string name) => _root.GetComponentsInChildren<Button>(true).First(b => b.name == name);

        [Test]
        public void TappingAFoundItemWearsItAndTappingItsSlotTakesItOff()
        {
            var catalog = ContentCatalog.CreateDefault();
            var profile = new ProfileState { Items = { "steel_sword", "lucky_wand" } };
            int saved = 0;
            var overlay = new InventoryOverlay((RectTransform)_root.transform);
            overlay.Open(catalog, profile, () => saved++);

            int unknown = _root.GetComponentsInChildren<Button>(true).Count(b => b.name == "Unknown");
            Assert.That(unknown, Is.EqualTo(catalog.Items.Count - 2 + 5), "Every item not found is a question mark; every empty slot too.");

            Tile("lucky_wand").onClick.Invoke();
            Assert.That(Inventory.Worn(profile, ItemSlot.Weapon), Is.EqualTo("lucky_wand"));
            Assert.That(saved, Is.EqualTo(1), "A change is saved at once.");

            // The worn weapon now shows in its slot as well as in the grid; the first button with its name is the slot.
            Tile("lucky_wand").onClick.Invoke();
            Assert.That(Inventory.Worn(profile, ItemSlot.Weapon), Is.Null);
        }
    }
}
