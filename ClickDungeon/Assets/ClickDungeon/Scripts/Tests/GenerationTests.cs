using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClickDungeon.Content;
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
                Assert.That(floor.IsBossFloor, Is.EqualTo(floorIndex % ContentCatalog.BossEvery == 0), "A boss every fifth floor (D-062).");
            }
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            for (int floorIndex = 1; floorIndex <= Catalog.RunFloorCount; floorIndex++)
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
            Assert.That(templates.Count, Is.GreaterThanOrEqualTo(10));
            Assert.That(signatures.Count, Is.GreaterThanOrEqualTo(95));
        }

        [Test]
        public void TemplateLibraryHasEnoughDistinctLayouts()
        {
            var layouts = new HashSet<string>();
            foreach (var template in Catalog.Templates.Where(t => !t.BossArena))
            for (int t = 0; t < TemplateTransform.Count; t++)
            {
                var sb = new StringBuilder();
                foreach (var p in Board.AllCells)
                {
                    var tp = TemplateTransform.ToTemplate(p, t);
                    sb.Append((int)template.TerrainAt(tp.X, tp.Y));
                }
                layouts.Add(sb.ToString());
            }
            TestContext.WriteLine($"Distinct room layouts: {layouts.Count}");
            Assert.That(layouts.Count, Is.GreaterThanOrEqualTo(40), $"Only {layouts.Count} distinct room layouts.");
        }

        [Test]
        public void EveryTemplateCanGenerateValidFloors()
        {
            var used = new HashSet<string>();
            for (ulong seed = 1; seed <= 400; seed++)
            {
                used.Add(FloorGenerator.Generate(seed, 2, Catalog).TemplateId);
                used.Add(FloorGenerator.Generate(seed, Catalog.RunFloorCount, Catalog).TemplateId);
            }
            foreach (var template in Catalog.Templates)
                Assert.That(used, Does.Contain(template.Id), $"Template '{template.Id}' never produced a valid floor.");
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
        public void ValidatorRejectsKeyOnlyReachableAcrossATeleportPad()
        {
            // REL-27: a pad is not a tile the hero walks across. Stepping onto one ends the step on its partner
            // (Hazards.HeroEnter), so this corridor leads into a sealed pocket and the key can never be picked up.
            var floor = Floor(
                "#####",
                "#####",
                "Ht.KX",
                "#####",
                "t####");
            var errors = new List<string>();
            Assert.That(FloorValidator.Validate(floor, Catalog, errors), Is.False, "The only route to the key runs onto a pad.");
            Assert.That(string.Join("; ", errors), Does.Contain("Key is unreachable"));

            // The same board with plain floor where the pads stood is a straight walk.
            floor[P(1, 2)].Content = ContentKind.None;
            floor[P(0, 0)].Content = ContentKind.None;
            errors.Clear();
            Assert.That(FloorValidator.Validate(floor, Catalog, errors), Is.True, string.Join("; ", errors));
        }

        [Test]
        public void ValidatorAcceptsAKeyOnlyTheTeleportPadReaches()
        {
            // The other half of REL-27: a pad is an edge to its partner, so a hop the hero really can make counts as a route.
            var floor = Floor(
                "#####",
                "#####",
                "Ht###",
                "#####",
                "tK.X#");
            var errors = new List<string>();
            Assert.That(FloorValidator.Validate(floor, Catalog, errors), Is.True, string.Join("; ", errors));

            floor[P(1, 2)].Content = ContentKind.None;
            floor[P(0, 0)].Content = ContentKind.None;
            Assert.That(FloorValidator.Validate(floor, Catalog), Is.False, "Without the hop nothing reaches the pocket.");
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
            // Every monster that summons names a minion that exists (D-062): Blobert, the Bat Swarm Leader, the Curtain Demon
            // and the Spellbook do; the Goblin Brute King does not summon at all, so "every boss summons" no longer holds.
            var summoners = new[] { EnemyBehavior.Boss, EnemyBehavior.SwarmLeader, EnemyBehavior.Showman, EnemyBehavior.Caster };
            foreach (var enemy in Catalog.Enemies.Values)
            {
                if (System.Array.IndexOf(summoners, enemy.Behavior) >= 0)
                    Assert.That(enemy.SummonId, Is.Not.Null.And.Not.Empty, $"{enemy.Id} summons, but names no minion.");
                if (!string.IsNullOrEmpty(enemy.SummonId))
                    Assert.That(Catalog.HasEnemy(enemy.SummonId), Is.True, $"{enemy.Id} summons '{enemy.SummonId}', which does not exist.");
            }
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
