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
    /// <summary>Chest reward sequence art: reactions, reward icons, rays, particles, board shimmer (Unity EditMode only).</summary>
    public class ChestRewardSequenceArtTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;
        bool _reducedMotion;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ChestTestRoot", typeof(RectTransform));
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

        ChestOverlay NewOverlay() =>
            new ChestOverlay((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>());

        Transform Named(string name) => _root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        Image Pose() => Named("Reaction").Find("Pose").GetComponent<Image>();

        [Test]
        public void ClosingTheRevealWithoutATapStillRunsWhatWasWaitingOnIt()
        {
            // D-044: the run-end check rides on this callback, and a turn of the device destroys the overlay rather than
            // tapping it closed. CloseNow is what the screen calls before it is torn down.
            int ran = 0;
            var overlay = NewOverlay();
            overlay.Open(new RewardRecord { Kind = RewardKind.Potion, Amount = 1 }, () => ran++);
            Assert.That(overlay.IsOpen, Is.True);

            overlay.CloseNow();

            Assert.That(ran, Is.EqualTo(1), "What was waiting on the reveal still runs.");
            Assert.That(overlay.IsOpen, Is.False);

            overlay.CloseNow();
            Assert.That(ran, Is.EqualTo(1), "And it runs once, however often the screen settles up.");
        }

        [Test]
        public void TappingTheRevealClosedStillRunsItOnceOnly()
        {
            int ran = 0;
            var overlay = NewOverlay();
            overlay.Open(new RewardRecord { Kind = RewardKind.MaxHp, Amount = 2 }, () => ran++);
            overlay.Tap();   // skips the anticipation beat to the burst
            overlay.Tap();   // closes
            Assert.That(ran, Is.EqualTo(1));
            overlay.CloseNow();
            Assert.That(ran, Is.EqualTo(1), "A reveal already tapped closed does not run it again.");
        }

        [Test]
        public void OpeningShowsTheAnticipationPose()
        {
            var anticipation = MakeSprite("anticipation");
            UseArt((ArtKeys.ChestReaction("anticipation"), anticipation));
            NewOverlay().Open(new RewardRecord { Kind = RewardKind.Potion, Amount = 1 }, null);

            Assert.That(Named("Reaction").gameObject.activeSelf, Is.True);
            Assert.That(Pose().sprite, Is.SameAs(anticipation));
            Assert.That(Named("Reaction").Find("Frame").GetComponent<Image>().enabled, Is.False, "Poses have no portrait frame.");
        }

        [Test]
        public void MissingPoseFallsBackToAFramedPortraitOrHides()
        {
            var confident = MakeSprite("confident");
            UseArt((ArtKeys.Portrait(ArtKeys.HeroId, "confident"), confident));
            NewOverlay().Open(new RewardRecord { Kind = RewardKind.Potion, Amount = 1 }, null);
            Assert.That(Pose().sprite, Is.SameAs(confident));
            Assert.That(Named("Reaction").Find("Frame").GetComponent<Image>().enabled, Is.True);

            Object.DestroyImmediate(_root);
            _root = new GameObject("ChestTestRoot2", typeof(RectTransform));
            UseArt();
            NewOverlay().Open(new RewardRecord { Kind = RewardKind.Potion, Amount = 1 }, null);
            Assert.That(Named("Reaction").gameObject.activeSelf, Is.False);
        }

        [Test]
        public void BurstShowsRevealIconAndParticleArtButNoStarburst()
        {
            var reveal = MakeSprite("reveal");
            var icon = MakeSprite("maxhp");
            var rays = MakeSprite("rays");
            var coin = MakeSprite("coin");
            UseArt((ArtKeys.ChestReaction("reveal"), reveal), (ArtKeys.RewardIcon(RewardKind.MaxHp), icon),
                (ArtKeys.ChestRays, rays), (ArtKeys.ChestCoin, coin));

            var overlay = NewOverlay();
            overlay.Open(new RewardRecord { Kind = RewardKind.MaxHp, Amount = 2 }, null);
            overlay.Tap(); // Skips the anticipation beat straight to the reveal.

            Assert.That(Pose().sprite, Is.SameAs(reveal));
            var rewardIcon = Named("RewardCard").Find("Icon").GetComponent<Image>();
            Assert.That(rewardIcon.gameObject.activeSelf, Is.True);
            Assert.That(rewardIcon.sprite, Is.SameAs(icon));
            Assert.That(Named("RewardCard").Find("Text").GetComponent<Text>().text, Is.EqualTo("+2 MAX HP"), "Reward text stays live.");
            Assert.That(Named("Rays").gameObject.activeSelf, Is.False, "The starburst art is not used.");
            Assert.That(Named("Sparkles").GetComponentsInChildren<Image>(true).Any(i => i.sprite == coin), Is.True);
        }

        [Test]
        public void RewardIconFallsBackToExistingArtOrHides()
        {
            var heart = MakeSprite("heart");
            UseArt((ArtKeys.Heart, heart));
            var overlay = NewOverlay();
            overlay.Open(new RewardRecord { Kind = RewardKind.MaxHp, Amount = 2 }, null);
            overlay.Tap(); // Skips the anticipation beat straight to the reveal.
            Assert.That(Named("RewardCard").Find("Icon").GetComponent<Image>().sprite, Is.SameAs(heart));

            Object.DestroyImmediate(_root);
            _root = new GameObject("ChestTestRoot2", typeof(RectTransform));
            UseArt();
            var bare = NewOverlay();
            bare.Open(new RewardRecord { Kind = RewardKind.SlashDamage, Amount = 1 }, null);
            bare.Tap();
            Assert.That(Named("RewardCard").Find("Icon").gameObject.activeSelf, Is.False);
            Assert.That(Named("Rays").gameObject.activeSelf, Is.False);
        }

        [Test]
        public void TappedBoardChestsShowTapProgress()
        {
            // D-022: after the first tap a board chest carries a backing strip and one pip per tap it needs.
            // Regression: the meter was drawn under the hero token when the hero stood on the chest.
            UseArt();
            int untouched = ChestIconImages(0);
            int tapped = ChestIconImages(2);
            Assert.That(tapped - untouched, Is.EqualTo(1 + Chests.TapsToOpen(ChestQuality.Epic)));
        }

        int ChestIconImages(int taps)
        {
            var root = new GameObject("PipRoot" + taps, typeof(RectTransform));
            try
            {
                var floor = FloorState.CreateEmpty();
                floor.FloorIndex = 1;
                floor.Start = new GridPos(2, 2);
                floor[new GridPos(2, 3)].Content = ContentKind.Chest;
                var run = new RunState
                {
                    RunSeed = 1,
                    FloorCount = Catalog.RunFloorCount,
                    Hero = RunFactory.CreateHero(Catalog, ContentCatalog.DefaultHeroId),
                    Floor = floor,
                };
                RunFactory.SetupFloor(run, Catalog, new List<GameEvent>());
                var chest = floor[new GridPos(2, 3)];
                chest.Knowledge = Knowledge.Revealed;
                chest.Quality = ChestQuality.Epic;
                chest.ChestTaps = taps;

                var board = new BoardView((RectTransform)root.transform, root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);
                board.Render(run, Catalog, new List<Threat>(), new HashSet<GridPos>(), false, null, false);
                // The meter draws in the label layer, above tokens, so a hero standing on the chest cannot hide it.
                return root.GetComponentsInChildren<Transform>(true).First(t => t.name == "Labels 2,3")
                    .GetComponentsInChildren<Image>(true).Length;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ChestSequenceKeysAreWired()
        {
            var wired = ArtKeys.Wired(Catalog);
            foreach (var step in ArtKeys.ChestReactionSteps) Assert.That(wired, Does.Contain(ArtKeys.ChestReaction(step)));
            foreach (var kind in ArtKeys.RewardKinds) Assert.That(wired, Does.Contain(ArtKeys.RewardIcon(kind)));
            foreach (var key in new[] { ArtKeys.ChestCoin, ArtKeys.ChestGem })
                Assert.That(wired, Does.Contain(key));
            foreach (var key in new[] { ArtKeys.ChestRays, ArtKeys.ChestShimmer, ArtKeys.ChestGlow })
                Assert.That(wired, Does.Not.Contain(key), "Star art is not used.");
            Assert.That(ArtKeys.RewardIcon(RewardKind.SlashDamage), Is.EqualTo("icon_reward_slashdamage"));
            Assert.That(ArtKeys.ChestReactionFallback("reveal"), Is.EqualTo("shocked"));
        }
    }
}
