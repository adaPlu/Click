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
            // D-025, D-036: the shop's cards dim what the purse cannot pay for, and a purchase is saved at once.
            var catalog = ClickDungeon.Content.ContentCatalog.CreateDefault();
            var profile = new ClickDungeon.Domain.ProfileState { Coins = ClickDungeon.Application.Shop.PotionRationCoins, Gems = 1 };
            int saved = 0;
            var shop = new ShopOverlay((RectTransform)_root.transform);
            shop.Open(catalog, profile, () => new System.DateTime(2026, 9, 18), () => saved++);

            Button Buy(string id) => _root.GetComponentsInChildren<Button>(true).First(b => b.name == "Buy " + id);
            Assert.That(Buy("PotionRation").interactable, Is.True, "The ration is affordable...");
            Assert.That(Buy("HeartToken").interactable, Is.False, "...the token is not.");

            Buy("PotionRation").onClick.Invoke();
            Assert.That(profile.PotionRations, Is.EqualTo(1));
            Assert.That(profile.Coins, Is.Zero);
            Assert.That(saved, Is.EqualTo(1));

            shop.Show(ClickDungeon.Application.ShopTab.Gear);
            var stock = ClickDungeon.Application.Shop.GearStock(catalog, new System.DateTime(2026, 9, 18));
            foreach (var item in stock) Assert.That(Buy(item.Id).interactable, Is.False, "Nothing left to pay with.");
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
