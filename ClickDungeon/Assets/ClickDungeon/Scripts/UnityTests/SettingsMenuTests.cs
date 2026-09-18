using System.Linq;
using ClickDungeon.Domain;
using ClickDungeon.Unity;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>The settings menu's movement picker (Unity EditMode only).</summary>
    public class SettingsMenuTests
    {
        GameObject _root;
        MovementMode _savedMovement;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SettingsTestRoot", typeof(RectTransform));
            var empty = ScriptableObject.CreateInstance<ArtCatalog>();
            empty.SetEntries(Enumerable.Empty<ArtCatalog.Entry>());
            Art.Override(empty);
            // The picker writes a real preference; keep the developer's own setting intact.
            _savedMovement = UserPrefs.Movement;
        }

        [TearDown]
        public void TearDown()
        {
            UserPrefs.Movement = _savedMovement;
            Object.DestroyImmediate(_root);
            Art.Reset();
        }

        Button[] Buttons() => _root.GetComponentsInChildren<Button>(true);

        [Test]
        public void MovementPickerShowsAndTogglesTheModeForTheNextRun()
        {
            UserPrefs.Movement = MovementMode.Free;
            var modal = new ModalOverlay((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>());
            Menus.OpenSettings(modal, () => { });

            Assert.That(Buttons().Select(b => b.name), Does.Contain("MOVEMENT: FREE ROAM"));
            var body = _root.GetComponentsInChildren<Text>(true).First(t => t.name == "Body").text;
            Assert.That(body, Does.Contain("next new run"), "The menu says a run in progress keeps its own mode.");

            Buttons().First(b => b.name == "MOVEMENT: FREE ROAM").onClick.Invoke();
            Assert.That(UserPrefs.Movement, Is.EqualTo(MovementMode.Step));
            Assert.That(Buttons().Select(b => b.name), Does.Contain("MOVEMENT: STEP BY STEP"), "The menu redraws with the new mode.");

            Buttons().First(b => b.name == "MOVEMENT: STEP BY STEP").onClick.Invoke();
            Assert.That(UserPrefs.Movement, Is.EqualTo(MovementMode.Free));
        }

        [Test]
        public void HeroSelectListsEveryHeroWithItsNumbersAndMarksTheChosenOne()
        {
            // D-024: the hero is picked before a run; a run in progress keeps its own.
            var catalog = ClickDungeon.Content.ContentCatalog.CreateDefault();
            var modal = new ModalOverlay((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>());
            string picked = null;
            Menus.OpenHeroSelect(modal, catalog, "dawnward", id => picked = id, () => { });

            var names = Buttons().Select(b => b.name).ToList();
            foreach (var identity in catalog.HeroIdentities.Values)
                Assert.That(names.Any(n => n.Contains(identity.DisplayName.ToUpperInvariant())), Is.True, identity.Id);
            Assert.That(names.Any(n => n.StartsWith("> DAWNWARD")), Is.True, "The chosen hero is marked.");

            var body = _root.GetComponentsInChildren<Text>(true).First(t => t.name == "Body").text;
            Assert.That(body, Does.Contain("keeps its own hero"));
            Assert.That(body, Does.Contain($"{catalog.HeroClass("paladin").MaxHp} hearts"), "The numbers that differ are shown.");

            Buttons().First(b => b.name.Contains("SIR CLICKINGTON")).onClick.Invoke();
            Assert.That(picked, Is.EqualTo("sir_clickington"));
        }

        [Test]
        public void TheShopShowsThePurseAndOnlyOffersWhatTheCoinsCover()
        {
            // D-025: coins come out of runs; the shop is the only place they go.
            var catalog = ClickDungeon.Content.ContentCatalog.CreateDefault();
            var profile = new ClickDungeon.Domain.ProfileState { Coins = ClickDungeon.Application.Shop.PotionRationCoins, Gems = 1 };
            var modal = new ModalOverlay((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>());
            ClickDungeon.Application.ShopItem? bought = null;
            Menus.OpenShop(modal, catalog, profile, item => bought = item, () => { });

            var body = _root.GetComponentsInChildren<Text>(true).First(t => t.name == "Body").text;
            Assert.That(body, Does.Contain($"{profile.Coins} coins"));
            Assert.That(body, Does.Contain("1 gems").Or.Contain("1 gem"));

            var names = Buttons().Select(b => b.name).ToList();
            Assert.That(names.Any(n => n.StartsWith("POTION RATION") && !n.Contains("NOT ENOUGH")), Is.True,
                "The ration is affordable...");
            Assert.That(names.Any(n => n.StartsWith("HEART TOKEN") && n.Contains("NOT ENOUGH")), Is.True,
                "...the token is not, and says so.");

            Buttons().First(b => b.name.StartsWith("POTION RATION")).onClick.Invoke();
            Assert.That(bought, Is.EqualTo(ClickDungeon.Application.ShopItem.PotionRation));
        }

        [Test]
        public void TalentsShowTheLevelThePointsAndWhatCanBeLearned()
        {
            // D-027: one point per level; a talent at its top rank, or with no point free, is not offered as learnable.
            var profile = new ClickDungeon.Domain.ProfileState { Xp = ClickDungeon.Application.Progression.XpForLevel(2) };
            var modal = new ModalOverlay((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>());
            string learned = null;
            bool reset = false;
            Menus.OpenTalents(modal, profile, id => learned = id, () => reset = true, () => { });

            var body = _root.GetComponentsInChildren<Text>(true).First(t => t.name == "Body").text;
            Assert.That(body, Does.Contain("Level 2"));
            Assert.That(body, Does.Contain("1 talent point"));
            var names = Buttons().Select(b => b.name).ToList();
            Assert.That(names.Any(n => n.StartsWith("TOUGH 0/3")), Is.True);
            Assert.That(names.Any(n => n.StartsWith("RESET")), Is.False, "Nothing learned, nothing to reset.");

            Buttons().First(b => b.name.StartsWith("TOUGH")).onClick.Invoke();
            Assert.That(learned, Is.EqualTo(ClickDungeon.Application.Progression.Tough));
            Assert.That(reset, Is.False);
        }
    }
}
