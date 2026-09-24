using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
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
