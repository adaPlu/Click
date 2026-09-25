using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;

namespace ClickDungeon.UnityTests
{
    /// <summary>
    /// D-065: the face on the panel, in the speech bubble and over an opened chest is the face of whoever is playing,
    /// pulling the expression the line calls for (Unity EditMode only).
    /// </summary>
    public class SpeechPortraitTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();

        [TearDown]
        public void TearDown() => Art.Reset();

        static Sprite MakeSprite(string name)
        {
            var sprite = Sprite.Create(new Texture2D(8, 8), new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        static void UseArt(params string[] keys)
        {
            var catalog = ScriptableObject.CreateInstance<ArtCatalog>();
            catalog.SetEntries(keys.Select(k => new ArtCatalog.Entry { Key = k, Frames = new[] { MakeSprite(k) } }));
            Art.Override(catalog);
        }

        [Test]
        public void EveryHeroHasAFaceForEveryExpressionADialogueLineCanAskFor()
        {
            // The shipped catalog, not a stub: a face the dialogue asks for and the build does not carry is a hero
            // silently wearing someone else's, which is the bug the fallback in SpeechPortraitKey hides.
            Art.Reset();
            Assert.That(Art.Catalog, Is.Not.Null, "The build's art catalog is missing - rebuild it (ClickDungeon -> Rebuild Art Catalog).");
            var missing = new List<string>();
            foreach (var identity in Catalog.HeroIdentities.Values)
                foreach (var expression in ArtKeys.Expressions)
                    if (!Art.Has(ArtKeys.Portrait(identity.Id, expression)))
                        missing.Add(ArtKeys.Portrait(identity.Id, expression));
            Assert.That(missing, Is.Empty, "No face for: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryExpressionTheDialogueCanPullIsOneTheArtKnowsAbout()
        {
            // Lines.React picks from the Expression enum; ArtKeys.Expressions is what the slicer cuts and the catalog
            // carries. A new expression added to one and not the other draws nothing.
            foreach (Expression face in System.Enum.GetValues(typeof(Expression)))
                Assert.That(ArtKeys.Expressions, Does.Contain(face.ToString().ToLowerInvariant()), face.ToString());
        }

        [Test]
        public void TheBubbleWearsThePlayingHerosOwnFace()
        {
            UseArt(ArtKeys.Portrait("rageclaw", "angry"), ArtKeys.Portrait(ArtKeys.HeroId, "angry"));

            Assert.That(GameScreen.SpeechPortraitKey("rageclaw", Expression.Angry),
                Is.EqualTo(ArtKeys.Portrait("rageclaw", "angry")));
        }

        [Test]
        public void AHeroShortOfAFaceKeepsTheirOwnRatherThanBorrowingAnothers()
        {
            // Their neutral face still says who is speaking; the default hero's angry one says someone else is.
            UseArt(ArtKeys.Portrait("rageclaw", "neutral"), ArtKeys.Portrait(ArtKeys.HeroId, "angry"));

            Assert.That(GameScreen.SpeechPortraitKey("rageclaw", Expression.Angry),
                Is.EqualTo(ArtKeys.Portrait("rageclaw", "neutral")));
        }

        [Test]
        public void WithNoFaceOfTheirOwnTheDefaultHeroStandsIn()
        {
            UseArt(ArtKeys.Portrait(ArtKeys.HeroId, "angry"));

            Assert.That(GameScreen.SpeechPortraitKey("rageclaw", Expression.Angry),
                Is.EqualTo(ArtKeys.Portrait(ArtKeys.HeroId, "angry")));
            Assert.That(GameScreen.SpeechPortraitKey("rageclaw", Expression.Shocked), Is.Null,
                "Nothing is drawn rather than the wrong face for the wrong feeling.");
        }

        /// <summary>The event a turn speaks for, and the face it wears. Nothing called Lines.React until now (TEST-69).</summary>
        static (string line, Expression face, int priority) React(GameEventKind kind, RunState run, int amount = 0, string source = null)
        {
            var e = GameEvent.Of(kind, amount: amount, source: source);
            int priority = Lines.React(e, run, Catalog, out var line, out var face);
            return (line, face, priority);
        }

        static RunState AtFullHealth()
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 5;
            floor.Start = new GridPos(2, 2);
            return new RunState
            {
                RunSeed = 3, FloorCount = Catalog.RunFloorCount,
                Hero = new HeroState { Pos = floor.Start, Hp = 20, MaxHp = 20, SlashDamage = 3 },
                Floor = floor,
            };
        }

        [Test]
        public void AHeavyBlowAnswersAngryAndAnOrdinaryOneWorried()
        {
            var run = AtFullHealth();

            Assert.That(React(GameEventKind.HeroDamaged, run, amount: 3).face, Is.EqualTo(Expression.Worried));
            Assert.That(React(GameEventKind.HeroDamaged, run, amount: 4).face, Is.EqualTo(Expression.Angry),
                "Four hearts is not a scratch (D-065).");
            Assert.That(React(GameEventKind.HeroDamaged, run, amount: 4).priority,
                Is.GreaterThan(React(GameEventKind.HeroDamaged, run, amount: 3).priority),
                "And it outranks the ordinary line when a turn carries both.");
        }

        [Test]
        public void ABossFallingIsSpokenUnlessTheHeroIsAboutToDie()
        {
            var run = AtFullHealth();
            var boss = React(GameEventKind.EnemyDied, run, source: "goblin_brute_king");
            var goblin = React(GameEventKind.EnemyDied, run, source: "goblin");

            Assert.That(boss.face, Is.EqualTo(Expression.Victorious));
            Assert.That(boss.priority, Is.GreaterThan(goblin.priority), "A boss is not one more kill.");

            // REL-80: at 90 this outranked the hero's own peril, so the turn that most earns "I'm fine. This is fine."
            // was the one turn it could not be said.
            run.Hero.Hp = 3;
            var nearlyDead = React(GameEventKind.HeroDamaged, run, amount: 5);
            Assert.That(nearlyDead.face, Is.EqualTo(Expression.Shocked));
            Assert.That(boss.priority, Is.LessThan(nearlyDead.priority),
                "A boss falling must not talk over the hero being down to their last hearts.");
            Assert.That(React(GameEventKind.RunLost, run).priority, Is.GreaterThan(boss.priority), "And dying outranks both.");
            // "Below the hero's last hearts and above every ordinary event" is what the rule says; only the first half
            // was pinned, so dropping the boss line to 51 would have passed while a routine block talked over it.
            // On a HEALTHY hero: the run above is down to three hearts, and at three hearts every blow is the peril
            // line, so asking it about an ordinary one would be asking the wrong question of the wrong hero.
            var healthy = AtFullHealth();
            Assert.That(boss.priority, Is.GreaterThan(React(GameEventKind.HeroBlocked, healthy).priority),
                "A boss falling is not talked over by a block.");
            Assert.That(boss.priority, Is.GreaterThan(React(GameEventKind.HeroDamaged, healthy, amount: 4).priority),
                "Nor by a heavy blow a hero with all their hearts shrugged off.");
        }

        [Test]
        public void AnOpenedChestCelebratesInThePlayingHerosFace()
        {
            UseArt(ArtKeys.Portrait("rageclaw", "victorious"), ArtKeys.Portrait(ArtKeys.HeroId, "victorious"));

            Assert.That(ChestOverlay.ReactionPortraitKey("rageclaw", "triumph"),
                Is.EqualTo(ArtKeys.Portrait("rageclaw", "victorious")));
            Assert.That(ChestOverlay.ReactionPortraitKey("rageclaw", "reveal"), Is.EqualTo(ArtKeys.Portrait(ArtKeys.HeroId, "shocked")),
                "A step this hero has no face for falls back to the default hero's, not to nothing.");
        }
    }
}
