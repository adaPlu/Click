using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Terrain = ClickDungeon.Domain.Terrain;

namespace ClickDungeon.UnityTests
{
    /// <summary>
    /// D-023 amendment regression: in Free Roam every covered tile is drawn exactly alike, whatever is under it. Above all,
    /// the exit stairs are indistinguishable from any other cover until their tile is clicked (Unity EditMode only).
    /// </summary>
    public class HiddenTileArtTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();

        static readonly GridPos Start = new GridPos(2, 2);
        static readonly GridPos Exit = new GridPos(4, 4);

        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HiddenTileRoot", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Art.Reset();
        }

        /// <summary>A floor with one of everything hidden under the covers, the exit included.</summary>
        static RunState EverythingHidden()
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = Start;
            floor.Exit = Exit;
            floor[Exit].IsExit = true;
            floor[new GridPos(0, 4)].Content = ContentKind.Key;
            floor[new GridPos(1, 4)].Content = ContentKind.Chest;
            floor[new GridPos(2, 4)].Content = ContentKind.Potion;
            floor[new GridPos(3, 4)].Hazard = HazardKind.Spikes;
            floor[new GridPos(0, 3)].Hazard = HazardKind.Bomb;
            floor[new GridPos(1, 3)].Hazard = HazardKind.Lava;
            floor[new GridPos(3, 3)].Terrain = Terrain.Pit;
            floor[new GridPos(4, 3)].Terrain = Terrain.Door;
            floor[new GridPos(0, 1)].Content = ContentKind.PressurePlate;
            floor[new GridPos(1, 1)].Content = ContentKind.Teleport;
            floor[new GridPos(3, 1)].Content = ContentKind.Fountain;
            floor[new GridPos(0, 0)].Content = ContentKind.Chest;
            floor[new GridPos(0, 0)].GreatChest = true;
            EnemyAi.Spawn(floor, Catalog.Enemy("goblin"), new GridPos(4, 1), awake: false);
            var run = new RunState
            {
                RunSeed = 1,
                FloorCount = Catalog.RunFloorCount,
                Hero = RunFactory.CreateHero(Catalog, ContentCatalog.DefaultHeroId),
                Floor = floor,
                Movement = MovementMode.Free,
            };
            RunFactory.SetupFloor(run, Catalog, new List<GameEvent>());
            return run;
        }

        static void UsePlaceholders()
        {
            var catalog = ScriptableObject.CreateInstance<ArtCatalog>();
            catalog.SetEntries(new ArtCatalog.Entry[0]);
            Art.Override(catalog);
        }

        BoardView Render(RunState run, GridPos? hover = null)
        {
            Object.DestroyImmediate(_root);
            _root = new GameObject("HiddenTileRoot", typeof(RectTransform));
            var board = new BoardView((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);
            var legal = new HashSet<GridPos>(Commands.LegalTargets(run, CommandKind.Move, Catalog));
            board.Render(run, Catalog, Threats.Compute(run, Catalog), legal, false, hover, false);
            return board;
        }

        /// <summary>
        /// Everything drawn for one tile: its cell (base, edge, icons, overlay) and its label layer (highlight, meters, badges),
        /// with every image, text and transform. Names carrying the tile's coordinates are made position-free.
        /// </summary>
        string Signature(GridPos p)
        {
            var sb = new StringBuilder();
            var all = _root.GetComponentsInChildren<Transform>(true);
            foreach (var name in new[] { $"Cell {p.X},{p.Y}", $"Labels {p.X},{p.Y}" })
            {
                var top = all.Single(t => t.name == name);
                foreach (var t in top.GetComponentsInChildren<Transform>(true))
                {
                    sb.Append(t == top ? name.Split(' ')[0] : t.name).Append(" active=").Append(t.gameObject.activeSelf);
                    if (t != top && t is RectTransform rt)
                        sb.Append(" pos=").Append(rt.anchoredPosition).Append(" size=").Append(rt.sizeDelta)
                            .Append(" anchors=").Append(rt.anchorMin).Append(rt.anchorMax);
                    sb.Append(" scale=").Append(t.localScale).Append(" rot=").Append(t.localEulerAngles);
                    foreach (var image in t.GetComponents<Image>())
                        sb.Append(" [image ").Append(image.enabled).Append(' ').Append(image.sprite ? image.sprite.name : "none")
                            .Append(' ').Append(image.color).Append(' ').Append(image.type).Append(' ').Append(image.preserveAspect)
                            .Append(' ').Append(image.fillAmount).Append(' ').Append(image.pixelsPerUnitMultiplier).Append(']');
                    foreach (var text in t.GetComponents<Text>())
                        sb.Append(" [text ").Append(text.enabled).Append(" '").Append(text.text).Append("' ").Append(text.color)
                            .Append(' ').Append(text.fontSize).Append(']');
                    foreach (var group in t.GetComponents<CanvasGroup>()) sb.Append(" [group ").Append(group.alpha).Append(']');
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        static IEnumerable<GridPos> Covered(RunState run) =>
            Board.AllCells.Where(p => run.Floor[p].Knowledge != Knowledge.Revealed);

        void AssertEveryCoverLooksLikeTheExit(RunState run, string state)
        {
            var covered = Covered(run).ToList();
            Assert.That(covered, Does.Contain(Exit), $"{state}: the exit starts covered.");
            Assert.That(covered.Count, Is.EqualTo(BoardRules.CellCount - 1), $"{state}: only the hero's own tile is uncovered.");
            string exit = Signature(Exit);
            foreach (var p in covered)
                Assert.That(Signature(p), Is.EqualTo(exit), $"{state}: the cover at {p} must look exactly like the exit's cover.");
        }

        void CheckAllStates(string art)
        {
            var run = EverythingHidden();
            Render(run);
            AssertEveryCoverLooksLikeTheExit(run, $"{art}, new floor");

            run.Hero.HasKey = true;
            Render(run);
            AssertEveryCoverLooksLikeTheExit(run, $"{art}, key held (the exit reads open)");

            run.Floor.ExitUnlocked = true;
            Render(run);
            AssertEveryCoverLooksLikeTheExit(run, $"{art}, exit unlocked");

            // Hovering the exit looks like hovering anything else.
            Render(run, Exit);
            string hoveredExit = Signature(Exit);
            Render(run, new GridPos(0, 4));
            Assert.That(Signature(new GridPos(0, 4)), Is.EqualTo(hoveredExit), $"{art}: hovering the exit.");
        }

        [Test]
        public void ExitStairsLookLikeEveryOtherCoverWithProductionArt()
        {
            Art.Reset();
            CheckAllStates("catalog art");
        }

        [Test]
        public void ExitStairsLookLikeEveryOtherCoverWithPlaceholders()
        {
            UsePlaceholders();
            CheckAllStates("placeholders");
        }

        [Test]
        public void ClickingTheExitIsWhatUncoversIt()
        {
            UsePlaceholders();
            var run = EverythingHidden();
            Render(run);
            string cover = Signature(Exit);

            // Walk next to it first: being close shows nothing.
            TurnResolver.Apply(run, PlayerCommand.Move(new GridPos(3, 2)), Catalog);
            Render(run);
            Assert.That(Signature(Exit), Is.EqualTo(cover), "Standing near the exit does not uncover it.");

            var result = TurnResolver.Apply(run, PlayerCommand.Move(Exit), Catalog);
            Assert.That(result.Accepted, Is.True, result.RejectReason);
            Assert.That(run.Floor[Exit].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Render(run);
            Assert.That(Signature(Exit), Is.Not.EqualTo(cover), "Once clicked, the stairs show.");
            Assert.That(Signature(new GridPos(4, 1)), Is.EqualTo(Signature(new GridPos(0, 4))), "Every other cover is still alike.");
        }
    }
}
