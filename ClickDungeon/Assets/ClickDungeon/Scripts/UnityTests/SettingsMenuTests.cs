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
        public void HeroSelectShowsEveryHeroLocksTheComingOnesAndChoosesAPlayableOne()
        {
            // D-024, D-037: the roster holds every hero; a locked one cannot be chosen; choosing hands the hero back.
            var catalog = ClickDungeon.Content.ContentCatalog.CreateDefault();
            var overlay = new HeroSelectOverlay((RectTransform)_root.transform);
            string chosen = null, talentsFor = null;
            overlay.Open(catalog, new ClickDungeon.Domain.ProfileState(), ClickDungeon.Content.ContentCatalog.DefaultHeroId, id => chosen = id, c => talentsFor = c);

            var names = Buttons().Select(b => b.name).ToList();
            foreach (var identity in catalog.HeroIdentities.Values) Assert.That(names, Does.Contain("Roster " + identity.Id));
            foreach (var preview in catalog.ComingSoon) Assert.That(names, Does.Contain("Roster " + preview.Id));
            Button Choose() => Buttons().First(b => b.name == "Choose");
            Assert.That(Choose().interactable, Is.False, "Already chosen.");

            // Every hero on the roster is playable since D-063; a preview, if the list ever holds one again, is not.
            overlay.Show("rageclaw");
            Assert.That(Choose().interactable, Is.True, "Rageclaw is playable.");
            foreach (var preview in catalog.ComingSoon)
            {
                overlay.Show(preview.Id);
                Assert.That(Choose().interactable, Is.False, "Coming soon: locked.");
            }
            overlay.Show("dawnward");
            Assert.That(Choose().interactable, Is.True);
            Choose().onClick.Invoke();
            Assert.That(chosen, Is.EqualTo("dawnward"));
            Buttons().First(b => b.name == "ViewTalents").onClick.Invoke();
            Assert.That(talentsFor, Is.EqualTo("paladin"));
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
        public void TalentsShowTheTreeLockWhatIsNotOpenAndLearnOnConfirm()
        {
            // D-037: nodes are icons; tapping selects, LEARN confirms; a tier-2 node stays locked at level 2.
            var catalog = ClickDungeon.Content.ContentCatalog.CreateDefault();
            var profile = new ClickDungeon.Domain.ProfileState { Xp = ClickDungeon.Application.Progression.XpForLevel(2) };
            int saved = 0;
            var overlay = new TalentOverlay((RectTransform)_root.transform);
            overlay.Open(catalog, profile, "knight", () => saved++);

            foreach (var talent in catalog.TalentsOf("knight"))
                Assert.That(Buttons().Any(b => b.name == "Node " + talent.Id), Is.True, talent.Id);
            Button Learn() => Buttons().First(b => b.name == "Learn");

            Buttons().First(b => b.name == "Node k_cleave").onClick.Invoke();
            Assert.That(Learn().interactable, Is.False, "Tier 2 is not open yet.");
            Buttons().First(b => b.name == "Node k_opening_strike").onClick.Invoke();
            Assert.That(Learn().interactable, Is.True);
            Learn().onClick.Invoke();
            Assert.That(ClickDungeon.Application.Progression.Rank(profile, "k_opening_strike"), Is.EqualTo(1));
            Assert.That(saved, Is.EqualTo(1));
            Assert.That(Learn().interactable, Is.False, "Out of points.");

            Buttons().First(b => b.name == "Class paladin").onClick.Invoke();
            Assert.That(Buttons().Any(b => b.name == "Node p_judgement"), Is.True, "The Paladin's own tree, with its own point.");
            Assert.That(Learn().interactable, Is.True);
        }
    }
}
