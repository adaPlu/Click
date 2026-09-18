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
    /// <summary>The crown and mail screens (D-030): every goal listed, a letter read on opening, its gift collected once.</summary>
    public class CrownAndMailOverlayTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("CrownMailTestRoot", typeof(RectTransform));
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

        [Test]
        public void TheCrownListsEveryAchievementWithItsProgress()
        {
            var catalog = ContentCatalog.CreateDefault();
            var profile = new ProfileState { MonstersSlain = 12 };
            var overlay = new AchievementsOverlay((RectTransform)_root.transform);
            overlay.Open(catalog, profile);
            foreach (var a in catalog.Achievements)
                Assert.That(_root.transform.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == a.Id), Is.True, a.Id);
            var texts = _root.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(texts, Has.Member("12 / 25"), "Monster Hunter shows how far along it is.");
        }

        [Test]
        public void OpeningTheMailReadsTheNewestLetterAndCollectPaysOnce()
        {
            var catalog = ContentCatalog.CreateDefault();
            var profile = new ProfileState();
            Mailbox.Post(profile, "The Guild", "Level 2!", "A note.");
            var gift = Mailbox.Post(profile, "The Crown", "Achievement: First Steps", "Well done.", catalog.Achievements[0].Reward);
            int saved = 0;
            var overlay = new MailOverlay((RectTransform)_root.transform);
            overlay.Open(profile, () => saved++);

            Assert.That(gift.Read, Is.True, "The newest letter opens first, and opening reads it.");
            Assert.That(saved, Is.EqualTo(1));
            var collect = _root.GetComponentsInChildren<Button>(true).First(b => b.name == "Collect");
            collect.onClick.Invoke();
            Assert.That(profile.Coins, Is.EqualTo(catalog.Achievements[0].Reward.Coins));
            Assert.That(collect.interactable, Is.False);
            collect.onClick.Invoke();
            Assert.That(profile.Coins, Is.EqualTo(catalog.Achievements[0].Reward.Coins), "Collected once.");
            Assert.That(Mailbox.Unread(profile), Is.EqualTo(1), "The level letter is still unread.");
        }
    }
}
