using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class GenerationTests
    {
        [Test]
        public void TemplatesAreFullyConnected()
        {
            foreach (var template in Catalog.Templates)
            {
                var floor = FloorState.CreateEmpty();
                int floorCells = 0;
                foreach (var p in Board.AllCells)
                {
                    floor[p].Terrain = template.TerrainAt(p.X, p.Y);
                    if (floor[p].Terrain == Terrain.Floor) floorCells++;
                }
                Assert.That(floorCells, Is.GreaterThanOrEqualTo(10), template.Id);
                Assert.That(FloorGenerator.LargestFloorRegion(floor).Count, Is.EqualTo(floorCells), template.Id);
            }
        }

        [Test]
        public void TransformsAreBijective()
        {
            for (int t = 0; t < TemplateTransform.Count; t++)
            {
                var mapped = new HashSet<GridPos>(Board.AllCells.Select(p => TemplateTransform.ToTemplate(p, t)));
                Assert.That(mapped.Count, Is.EqualTo(BoardRules.CellCount));
                Assert.That(mapped.All(p => p.InBounds), Is.True);
            }
        }

        [Test]
        public void FuzzedFloorsAreAlwaysValid()
        {
            var errors = new List<string>();
            for (ulong seed = 1; seed <= 400; seed++)
            for (int floorIndex = 1; floorIndex <= Catalog.RunFloorCount; floorIndex++)
            {
                var floor = FloorGenerator.Generate(seed, floorIndex, Catalog);
                errors.Clear();
                Assert.That(FloorValidator.Validate(floor, Catalog, errors), Is.True,
                    $"seed {seed} floor {floorIndex}: {string.Join("; ", errors)}");
                Assert.That(floor.IsBossFloor, Is.EqualTo(floorIndex == 5));
            }
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            for (int floorIndex = 1; floorIndex <= 5; floorIndex++)
                Assert.That(Signature(FloorGenerator.Generate(seed, floorIndex, Catalog)),
                    Is.EqualTo(Signature(FloorGenerator.Generate(seed, floorIndex, Catalog))));
        }

        [Test]
        public void SeedsProduceVariety()
        {
            var templates = new HashSet<string>();
            var signatures = new HashSet<string>();
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var floor = FloorGenerator.Generate(seed, 2, Catalog);
                templates.Add(floor.TemplateId);
                signatures.Add(Signature(floor));
            }
            Assert.That(templates.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(signatures.Count, Is.GreaterThanOrEqualTo(95));
        }

        [Test]
        public void ValidatorRejectsExitOnlyReachableThroughHazard()
        {
            var floor = Floor(
                "...#X",
                "...#.",
                "...^.",
                "...#.",
                "H.K#.");
            Assert.That(FloorValidator.Validate(floor, Catalog), Is.False);

            floor[P(3, 2)].Hazard = HazardKind.None;
            var errors = new List<string>();
            Assert.That(FloorValidator.Validate(floor, Catalog, errors), Is.True, string.Join("; ", errors));
        }

        [Test]
        public void ValidatorRejectsKeyBehindClosedChest()
        {
            var floor = Floor(
                "..#.X",
                "..#..",
                "..#..",
                "..#K#",
                "H.C..");
            Assert.That(FloorValidator.Validate(floor, Catalog), Is.False);
        }

        [Test]
        public void ValidatorRejectsEnemyNearStart()
        {
            var floor = Floor(
                "....X",
                ".....",
                ".....",
                ".g...",
                "H.K..");
            Assert.That(FloorValidator.Validate(floor, Catalog), Is.False);
        }

        [Test]
        public void ValidatorRejectsStackedObjectsMissingKeysAndUnknownIds()
        {
            var stacked = Floor(
                "....X",
                ".....",
                ".....",
                ".....",
                "H.K..");
            stacked[P(2, 0)].Hazard = HazardKind.Spikes;
            Assert.That(FloorValidator.Validate(stacked, Catalog), Is.False);

            var keyless = Floor(
                "....X",
                ".....",
                ".....",
                ".....",
                "H....");
            Assert.That(FloorValidator.Validate(keyless, Catalog), Is.False);

            var unknown = Floor(
                "....X",
                ".....",
                "....g",
                ".....",
                "H.K..");
            unknown.Enemies[0].DefId = "dragon";
            Assert.That(FloorValidator.Validate(unknown, Catalog), Is.False);
        }

        [Test]
        public void CatalogReferencesResolve()
        {
            foreach (var profile in Catalog.FloorProfiles)
            {
                foreach (var id in profile.EnemyPool) Assert.That(Catalog.HasEnemy(id), Is.True, id);
                if (profile.IsBoss) Assert.That(Catalog.Enemy(profile.BossId).IsBoss, Is.True);
                Assert.That(profile.Name, Is.Not.Empty);
            }
            foreach (var enemy in Catalog.Enemies.Values)
                if (enemy.IsBoss) Assert.That(Catalog.HasEnemy(enemy.SummonId), Is.True);
            Assert.That(Catalog.ChestRewards, Is.Not.Empty);
        }

        static string Signature(FloorState floor)
        {
            var sb = new StringBuilder();
            sb.Append(floor.TemplateId).Append('/').Append(floor.Transform).Append('/').Append(floor.Start).Append(floor.Exit);
            foreach (var cell in floor.Cells)
                sb.Append((int)cell.Terrain).Append((int)cell.Hazard).Append((int)cell.Content).Append(cell.IsExit ? 'E' : '-');
            foreach (var enemy in floor.Enemies) sb.Append(enemy.DefId).Append(enemy.Pos);
            return sb.ToString();
        }
    }
}
