using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>Frame, panel and highlight art with procedural fallback (Unity EditMode only).</summary>
    public class HudAndHighlightArtTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HudTestRoot", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Art.Reset();
        }

        static Sprite MakeSprite(string name, Vector4 border = default)
        {
            var sprite = Sprite.Create(new Texture2D(16, 16), new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
            sprite.name = name;
            return sprite;
        }

        static void UseArt(params (string key, Sprite sprite)[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<ArtCatalog>();
            catalog.SetEntries(entries.Select(e => new ArtCatalog.Entry { Key = e.key, Frames = new[] { e.sprite } }));
            Art.Override(catalog);
        }

        Image NewImage(string name, Color color)
        {
            var image = UiFactory.Image(_root.transform, name, color, Shapes.Rounded, true);
            return image;
        }

        [Test]
        public void MissingArtLeavesImageUntouched()
        {
            UseArt();
            var image = NewImage("Back", Color.red);
            Assert.That(UiArt.Apply(image, ArtKeys.Panel), Is.False);
            Assert.That(image.sprite, Is.SameAs(Shapes.Rounded));
            Assert.That(image.color, Is.EqualTo(Color.red));
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
        }

        [Test]
        public void NineSliceArtDrawsSlicedAndPlainArtStretches()
        {
            var sliced = MakeSprite("sliced", new Vector4(4, 4, 4, 4));
            var plain = MakeSprite("plain");
            UseArt((ArtKeys.Panel, sliced), (ArtKeys.Chip, plain));

            var panel = NewImage("Panel", Color.red);
            Assert.That(UiArt.Apply(panel, ArtKeys.Panel), Is.True);
            Assert.That(panel.sprite, Is.SameAs(sliced));
            Assert.That(panel.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(panel.color, Is.EqualTo(Color.white));

            var chip = NewImage("Chip", Color.red);
            Assert.That(UiArt.Apply(chip, ArtKeys.Chip), Is.True);
            Assert.That(chip.type, Is.EqualTo(Image.Type.Simple));
        }

        [Test]
        public void PanelArtHidesTheProceduralBorderOnlyWhenArtExists()
        {
            UseArt((ArtKeys.SpeechStrip, MakeSprite("strip")));

            var back = NewImage("Back", Palette.Navy);
            var border = NewImage("Border", Palette.GoldDark);
            Assert.That(UiArt.ApplyPanel(back, border, ArtKeys.SpeechStrip), Is.True);
            Assert.That(border.enabled, Is.False);

            var otherBack = NewImage("OtherBack", Palette.Navy);
            var otherBorder = NewImage("OtherBorder", Palette.GoldDark);
            Assert.That(UiArt.ApplyPanel(otherBack, otherBorder, ArtKeys.Chip), Is.False);
            Assert.That(otherBorder.enabled, Is.True);
        }

        [Test]
        public void ButtonArtFallsBackToTheSharedAbilityKey()
        {
            var shared = MakeSprite("shared");
            UseArt((ArtKeys.AbilityButtonDefault, shared));
            var image = NewImage("Button", Palette.SlashButton);
            Assert.That(UiArt.Apply(image, ArtKeys.AbilityButton(CommandKind.Slash), ArtKeys.AbilityButtonDefault), Is.True);
            Assert.That(image.sprite, Is.SameAs(shared));
        }

        [Test]
        public void BoardHighlightsUseArtForEachState()
        {
            var legal = MakeSprite("legal");
            var hover = MakeSprite("hover");
            var target = MakeSprite("target");
            UseArt((ArtKeys.HighlightLegal, legal), (ArtKeys.HighlightHover, hover), (ArtKeys.HighlightTarget, target));

            RenderBoard(_root, strong: false, legalCell: new GridPos(2, 3), hover: new GridPos(1, 2));
            Assert.That(Highlight(_root, "Labels 2,3").sprite, Is.SameAs(legal));
            Assert.That(Highlight(_root, "Labels 1,2").sprite, Is.SameAs(hover));
            Assert.That(Highlight(_root, "Labels 3,2").enabled, Is.False);

            var targetRoot = new GameObject("TargetRoot", typeof(RectTransform));
            try
            {
                RenderBoard(targetRoot, strong: true, legalCell: new GridPos(2, 3), hover: null);
                Assert.That(Highlight(targetRoot, "Labels 2,3").sprite, Is.SameAs(target));
            }
            finally
            {
                Object.DestroyImmediate(targetRoot);
            }
        }

        [Test]
        public void BoardHighlightsKeepProceduralFramesWithoutArt()
        {
            UseArt();
            RenderBoard(_root, strong: true, legalCell: new GridPos(2, 3), hover: null);
            var highlight = Highlight(_root, "Labels 2,3");
            Assert.That(highlight.enabled, Is.True);
            Assert.That(highlight.sprite, Is.SameAs(Shapes.Frame));
            Assert.That(highlight.color, Is.EqualTo(Palette.Legal));
        }

        [Test]
        public void HudKeysAreWired()
        {
            var wired = ArtKeys.Wired(Catalog);
            foreach (var kind in ArtKeys.AbilityKinds) Assert.That(wired, Does.Contain(ArtKeys.AbilityButton(kind)));
            foreach (var key in new[] { ArtKeys.HighlightHover, ArtKeys.Panel, ArtKeys.HpFill, ArtKeys.SettingsButton, ArtKeys.FloorPlaque })
                Assert.That(wired, Does.Contain(key));
            Assert.That(wired, Does.Not.Contain("ui_floor_plaque"), "The reference plaque slice has baked text and must stay unwired.");
        }

        static void RenderBoard(GameObject root, bool strong, GridPos legalCell, GridPos? hover)
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = new GridPos(2, 2);
            var run = new RunState
            {
                RunSeed = 1,
                FloorCount = Catalog.RunFloorCount,
                Hero = RunFactory.CreateHero(Catalog, ContentCatalog.DefaultHeroId),
                Floor = floor,
            };
            RunFactory.SetupFloor(run, Catalog, new List<GameEvent>());

            var host = root.AddComponent<SpriteFrameAnimator>();
            var board = new BoardView((RectTransform)root.transform, host, Vector2.zero);
            board.Render(run, Catalog, new List<Threat>(), new HashSet<GridPos> { legalCell }, strong, hover, false);
        }

        static Image Highlight(GameObject root, string labelsName) =>
            root.GetComponentsInChildren<Transform>(true).First(t => t.name == labelsName).Find("Highlight").GetComponent<Image>();
    }
}
