using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.PlayModeTests
{
    /// <summary>
    /// What a tile actually draws, on a board built and rendered in a running player (TEST-15). The EditMode suite can
    /// check the strings and keys a screen would ask for; only here does a board exist with labels on it, which is why
    /// REL-38 and REL-42 were fixed at COMPILED level and never seen.
    /// </summary>
    public class BoardDrawTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;
        BoardView _board;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BoardTestRoot", typeof(RectTransform));
            _board = new BoardView((RectTransform)_root.transform, _root.AddComponent<Host>(), Vector2.zero);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        sealed class Host : MonoBehaviour { }

        /// <summary>A board with the hero in the middle and everything uncovered, unless a test covers a tile itself.</summary>
        static RunState Board5x5(params GridPos[] covered)
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 3;
            floor.Start = new GridPos(2, 2);
            floor.Exit = new GridPos(4, 4);
            floor[floor.Exit].IsExit = true;
            foreach (var p in Board.AllCells) floor[p].Knowledge = Knowledge.Revealed;
            foreach (var p in covered) floor[p].Knowledge = Knowledge.Unseen;
            return new RunState
            {
                RunSeed = 7,
                FloorCount = Catalog.RunFloorCount,
                Hero = new HeroState { Pos = floor.Start, Hp = 10, MaxHp = 10, SlashDamage = 2 },
                Floor = floor,
                Movement = MovementMode.Free,
            };
        }

        static Threat At(ThreatKind kind, GridPos cell, int damage = 0) =>
            new Threat { Kind = kind, Cell = cell, Damage = damage, SourceId = 1 };

        void Render(RunState run, params Threat[] threats) =>
            _board.Render(run, Catalog, threats.ToList(), new HashSet<GridPos>(), false, null, false);

        /// <summary>Every warning label drawn on a tile, with the row it sits on.</summary>
        List<(string text, float y)> Labels(GridPos p) =>
            _root.GetComponentsInChildren<Text>(true)
                .Where(t => t.transform.parent != null && InCell(t.transform, p))
                .Select(t => (t.text, t.rectTransform.anchoredPosition.y))
                .ToList();

        // Warning labels are parented to the board's label layer rather than the tile, so they draw over the tiles around
        // them; the group is named for the cell it belongs to.
        static bool InCell(Transform t, GridPos p)
        {
            for (var cursor = t; cursor != null; cursor = cursor.parent)
                if (cursor.name == $"Cell {p.X},{p.Y}" || cursor.name == $"Labels {p.X},{p.Y}") return true;
            return false;
        }

        static bool Says(List<(string text, float y)> labels, string word) => labels.Any(l => l.text.Contains(word));

        [Test]
        public void AWebAndALandingBombAreBothReadableOnATileThatIsAlsoBeingHit()
        {
            // REL-38: neither carries damage, and they used to be drawn only when nothing else was on the tile - one
            // adjacent chaser erased the only warning a spider or a bomber gives.
            var run = Board5x5();
            var tile = new GridPos(1, 1);

            Render(run, At(ThreatKind.Attack, tile, 3), At(ThreatKind.Throw, tile), At(ThreatKind.Web, tile));

            var labels = Labels(tile);
            Assert.That(Says(labels, "HIT"), Is.True, "The blow is marked.");
            Assert.That(Says(labels, "BOMB"), Is.True, "So is the bomb about to land.");
            Assert.That(Says(labels, "WEB"), Is.True, "So is the web.");
            var rows = labels.Where(l => l.text.Contains("HIT") || l.text.Contains("BOMB") || l.text.Contains("WEB"))
                .Select(l => Mathf.Round(l.y)).ToList();
            Assert.That(rows.Distinct().Count(), Is.EqualTo(3), "Each warning gets its own row: " + string.Join(", ", rows));
        }

        [Test]
        public void ACoverKeepsAWarningWhoseShapeWouldReportWhatIsUnderIt()
        {
            // REL-42: a lane or a landing bomb stops at what a cover hides, so drawing it on covered ground would tell the
            // player what is under that cover for free (rules 2.1). A blow is geometric and is owed to them.
            var run = Board5x5(new GridPos(3, 1));
            var covered = new GridPos(3, 1);

            Render(run, At(ThreatKind.Fire, covered, 2), At(ThreatKind.Throw, covered), At(ThreatKind.Web, covered));
            Assert.That(Labels(covered), Is.Empty, "Nothing board-dependent is drawn over a cover.");

            Render(run, At(ThreatKind.Attack, covered, 3));
            Assert.That(Says(Labels(covered), "HIT"), Is.True, "A blow is marked wherever it lands.");
        }

        [Test]
        public void TheStoneAroundAVaultDoesNotLookLikeACover()
        {
            // D-064: the ring is real wall, which no dungeon floor generates, so this is the only board where the wall
            // branch of the draw path runs at all. It matters that it does not draw as a cover: a cover is a tile the
            // player is invited to click, and clicking stone costs a turn and gives nothing back (rules 2.1).
            var vault = FloorGenerator.GenerateVault(11UL, 3, new GridPos(1, 2), Catalog);
            var run = new RunState
            {
                RunSeed = 11,
                FloorCount = Catalog.RunFloorCount,
                Hero = new HeroState { Pos = vault.Start, Hp = 10, MaxHp = 10, SlashDamage = 2 },
                Floor = vault,
                Movement = MovementMode.Free,
            };
            var centre = new GridPos(BoardRules.Size / 2, BoardRules.Size / 2);
            var covered = Board.AllCells.First(p => p.Chebyshev(centre) <= 1 && vault[p].Knowledge != Knowledge.Revealed);

            Render(run);

            var coverLook = BaseImage(covered).color;
            var stone = Board.AllCells.Where(p => p.Chebyshev(centre) > 1).ToList();
            foreach (var p in stone)
            {
                Assert.That(BaseImage(p).color, Is.Not.EqualTo(coverLook),
                    $"The stone at {p} is drawn as a cover - the player would click it.");
                Assert.That(BaseImage(p).color, Is.EqualTo(BaseImage(stone[0]).color), $"The ring reads as one wall at {p}.");
            }
            Assert.That(BaseImage(centre).color, Is.Not.EqualTo(coverLook), "The door they came in by is drawn, not covered.");
        }

        Image BaseImage(GridPos p) => CellRoot(p).GetComponent<Image>();

        Transform CellRoot(GridPos p) =>
            _root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == $"Cell {p.X},{p.Y}");
    }
}
