using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>Floor features from the production tile set (D-019, rules §11).</summary>
    public class TileFeatureTests
    {
        [Test]
        public void LavaBurnsEveryTimeAndShieldDoesNotHelp()
        {
            var run = Run(
                ".....",
                ".....",
                "HL...",
                ".....",
                ".....");
            int hp = run.Hero.Hp;

            var result = DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(Has(result, GameEventKind.LavaBurned), Is.True);
            Assert.That(run.Hero.Hp, Is.EqualTo(hp - Catalog.Hazards.LavaDamage));
            Assert.That(run.Floor[P(1, 2)].Hazard, Is.EqualTo(HazardKind.Lava), "Lava is permanent.");

            DoOk(run, PlayerCommand.Move(P(0, 2)));
            DoOk(run, PlayerCommand.Shield());
            DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(run.Hero.Hp, Is.EqualTo(hp - 2 * Catalog.Hazards.LavaDamage), "Guard does not block lava.");
        }

        [Test]
        public void EnemiesRefuseToPathThroughLava()
        {
            var run = Run(
                ".....",
                ".....",
                "HLG..",
                ".....",
                ".....");
            Assert.That(Board.EnemyPathable(run.Floor, P(1, 2)), Is.False);
        }

        [Test]
        public void FallingThroughAPitCostsHpAndLandsOnTheNextFloor()
        {
            var run = Run(2, 77UL,
                ".....",
                "...o.",
                "...H.",
                ".....",
                ".....");
            int hp = run.Hero.Hp;

            var result = DoOk(run, PlayerCommand.Move(P(3, 3)));

            Assert.That(Has(result, GameEventKind.FellThroughPit), Is.True);
            Assert.That(run.Hero.Hp, Is.EqualTo(hp - Catalog.Hazards.FallDamage));
            Assert.That(run.Floor.FloorIndex, Is.EqualTo(3), "You land on the next floor.");
            Assert.That(Has(result, GameEventKind.FloorCompleted), Is.True);
            Assert.That(run.Hero.HasKey, Is.False, "The skipped floor's key stays behind.");
            Assert.That(run.Floor[run.Hero.Pos].Terrain, Is.EqualTo(Terrain.Floor));
        }

        [Test]
        public void AFatalFallLeavesNoHeroInsideThePit()
        {
            var run = Run(2, 77UL,
                ".....",
                "...o.",
                "...H.",
                ".....",
                ".....");
            var from = run.Hero.Pos;
            run.Hero.Hp = 1;

            var result = DoOk(run, PlayerCommand.Move(P(3, 3)));

            Assert.That(run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(Has(result, GameEventKind.FellThroughPit), Is.True);
            Assert.That(run.Hero.Pos, Is.EqualTo(from));
            Assert.That(run.Floor[run.Hero.Pos].Terrain, Is.EqualTo(Terrain.Floor));
            Assert.That(run.Floor.FloorIndex, Is.EqualTo(2), "The fall never reached the next floor.");
            Assert.DoesNotThrow(() => SaveSerializer.FromJson(SaveSerializer.ToJson(run)), "The finished run still saves.");
        }

        [Test]
        public void TeleportWithAnOccupiedFarPadDoesNothing()
        {
            var run = Run(
                ".....",
                ".....",
                "Ht..t",
                ".....",
                ".....");
            EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), P(4, 2), awake: true);

            DoOk(run, PlayerCommand.Move(P(1, 2)));

            Assert.That(run.Hero.Pos, Is.EqualTo(P(1, 2)), "Two actors may never share a tile.");
        }

        [Test]
        public void FountainHealsOnceThenIsSpent()
        {
            var run = Run(
                ".....",
                ".....",
                "Hf...",
                ".....",
                ".....");
            run.Hero.Hp = 2;

            var result = DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(run.Hero.Hp, Is.EqualTo(2 + Catalog.Hazards.FountainHeal));
            Assert.That(result.Events.Exists(e => e.Kind == GameEventKind.HeroHealed && e.Source == "fountain"), Is.True);
            Assert.That(run.Floor[P(1, 2)].Used, Is.True);

            run.Hero.Hp = 2;
            DoOk(run, PlayerCommand.Move(P(0, 2)));
            DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(run.Hero.Hp, Is.EqualTo(2), "A spent fountain heals nothing.");
        }

        [Test]
        public void TeleportPadsMoveTheHeroOnceWithoutBouncingBack()
        {
            var run = Run(
                ".....",
                ".....",
                "Ht..t",
                ".....",
                ".....");
            var result = DoOk(run, PlayerCommand.Move(P(1, 2)));

            Assert.That(run.Hero.Pos, Is.EqualTo(P(4, 2)), "The hop ends on the far pad.");
            Assert.That(result.Events.Count(e => e.Kind == GameEventKind.Teleported), Is.EqualTo(1));
            Assert.That(run.Floor[P(1, 2)].Content, Is.EqualTo(ContentKind.Teleport), "Pads stay put.");
        }

        [Test]
        public void PressurePlateOpensTheDoorAndTheDoorBlocksUntilThen()
        {
            var run = Run(
                ".....",
                ".....",
                "HpD..",
                ".....",
                ".....");
            var door = P(2, 2);
            Assert.That(Board.BlocksMovement(run.Floor[door]), Is.True, "A locked door blocks.");
            Assert.That(Board.ClueAt(run.Floor, door).HasFlag(Clue.Objective), Is.True, "Sensing a door says 'objective'.");

            var result = DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(Has(result, GameEventKind.DoorsOpened), Is.True);
            Assert.That(run.Floor[door].IsOpenDoor, Is.True);
            Assert.That(Board.BlocksMovement(run.Floor[door]), Is.False, "An open door can be walked into.");
        }

        [Test]
        public void GreatChestGrantsSeveralRewardsAtOnce()
        {
            var run = Run(
                ".....",
                ".....",
                "HW...",
                ".....",
                ".....");
            run.Floor.IsVault = true;

            var result = DoOk(run, PlayerCommand.Interact(P(1, 2)));
            Assert.That(run.Rewards.Count, Is.EqualTo(Catalog.Vault.GreatChestRewards));
            Assert.That(result.Events.Count(e => e.Kind == GameEventKind.ChestOpened), Is.EqualTo(Catalog.Vault.GreatChestRewards));
            Assert.That(run.Rewards.Select(r => r.TransactionId).Distinct().Count(), Is.EqualTo(run.Rewards.Count));

            Assert.That(Do(run, PlayerCommand.Interact(P(1, 2))).Accepted, Is.False, "An opened chest stays opened.");
        }

        [Test]
        public void VaultChestsDoNotShareIdsWithTheFloorTheyHangOff()
        {
            Assert.That(Chests.TransactionId(2, P(3, 3), true, 0), Is.Not.EqualTo(Chests.TransactionId(2, P(3, 3), false, 0)));
            Assert.That(Chests.TransactionId(2, P(3, 3), false, 0), Is.EqualTo(Chests.TransactionId(2, P(3, 3))), "Ordinary chests keep their old id.");
        }

        [Test]
        public void SteppingIntoAnOpenDoorEntersTheVaultAndComesBackToTheDoor()
        {
            var run = Run(
                ".....",
                ".....",
                "HdK.X",
                ".....",
                ".....");
            run.Hero.HasKey = true;
            var door = P(1, 2);
            int turnsBefore = run.Turn;

            var result = DoOk(run, PlayerCommand.Move(door));
            Assert.That(Has(result, GameEventKind.VaultEntered), Is.True);
            Assert.That(run.Floor.IsVault, Is.True);
            Assert.That(run.OuterFloor, Is.Not.Null);
            Assert.That(run.ReturnPos, Is.EqualTo(P(0, 2)), "You come back out onto the tile you stepped in from.");
            Assert.That(run.Hero.HasKey, Is.True, "The key comes along.");
            Assert.That(run.Hero.Pos, Is.EqualTo(run.Floor.Start));
            Assert.That(run.Floor.Enemies.Count, Is.GreaterThanOrEqualTo(Catalog.Vault.MinEnemies));
            Assert.That(run.Floor.Enemies.TrueForAll(e => e.Awake), Is.True, "Vault guards are awake.");
            Assert.That(Board.AllCells.Count(p => run.Floor[p].IsClosedChest), Is.GreaterThanOrEqualTo(1));
            Assert.That(run.Turn, Is.GreaterThan(turnsBefore));

            // Walk out: put the hero next to the vault's stair, then step onto it.
            var exit = run.Floor.Exit;
            var approach = Directions.All.Select(d => exit.Step(d)).First(p => Board.HeroCanEnter(run, p));
            run.Floor.Enemies.Clear();
            run.Hero.Pos = approach;
            var back = DoOk(run, PlayerCommand.Move(exit));

            Assert.That(Has(back, GameEventKind.VaultLeft), Is.True);
            Assert.That(run.Floor.IsVault, Is.False);
            Assert.That(run.OuterFloor, Is.Null);
            Assert.That(run.Hero.Pos, Is.EqualTo(P(0, 2)), "You come back out beside the door you went in.");
            Assert.That(run.Floor[run.Hero.Pos].Terrain, Is.EqualTo(Terrain.Floor), "Nobody ever stands in a doorway.");
            Assert.That(run.Hero.HasKey, Is.True);
        }

        [Test]
        public void GeneratedFloorsNeverHaveADoorWithoutItsPlate()
        {
            var catalog = ContentCatalog.CreateDefault();
            bool sawDoor = false, sawLava = false, sawTeleport = false, sawFountain = false;
            for (ulong seed = 1; seed <= 60; seed++)
            for (int index = 1; index <= catalog.RunFloorCount; index++)
            {
                var floor = FloorGenerator.Generate(seed, index, catalog);
                int doors = Board.AllCells.Count(p => floor[p].Terrain == Terrain.Door);
                int plates = Board.AllCells.Count(p => floor[p].Content == ContentKind.PressurePlate);
                int pads = Board.AllCells.Count(p => floor[p].Content == ContentKind.Teleport);
                Assert.That(doors <= plates, Is.True, $"seed {seed} floor {index}: {doors} doors but {plates} plates");
                Assert.That(pads == 0 || pads == 2, Is.True, $"seed {seed} floor {index}: teleports come in pairs, found {pads}");
                sawDoor |= doors > 0;
                sawLava |= Board.AllCells.Any(p => floor[p].Hazard == HazardKind.Lava);
                sawTeleport |= pads == 2;
                sawFountain |= Board.AllCells.Any(p => floor[p].Content == ContentKind.Fountain);
            }
            Assert.That(sawDoor && sawLava && sawTeleport && sawFountain, Is.True, "Every feature should appear across 60 seeds.");
        }

        [Test, Explicit("Tuning aid: prints what a generated floor holds")]
        public void PrintFloorTiles()
        {
            var catalog = ContentCatalog.CreateDefault();
            foreach (var (seed, index) in new[] { (42UL, 3), (7UL, 3), (99UL, 4), (2026UL, 2) })
            {
                var floor = FloorGenerator.Generate(seed, index, catalog);
                TestContext.Out.WriteLine($"seed {seed} floor {index}: template {floor.TemplateId} transform {floor.Transform}");
                for (int y = BoardRules.Size - 1; y >= 0; y--)
                {
                    var row = new System.Text.StringBuilder("  ");
                    for (int x = 0; x < BoardRules.Size; x++)
                    {
                        var p = new GridPos(x, y);
                        var cell = floor[p];
                        char ch = cell.Terrain == Terrain.Wall ? '#'
                            : cell.Terrain == Terrain.Pit ? 'o'
                            : cell.Terrain == Terrain.Door ? 'D'
                            : cell.Hazard == HazardKind.Spikes ? '^'
                            : cell.Hazard == HazardKind.Bomb ? 'b'
                            : cell.Hazard == HazardKind.Lava ? 'L'
                            : cell.Content == ContentKind.Key ? 'K'
                            : cell.Content == ContentKind.Chest ? 'C'
                            : cell.Content == ContentKind.Potion ? 'P'
                            : cell.Content == ContentKind.Fountain ? 'f'
                            : cell.Content == ContentKind.Teleport ? 't'
                            : cell.Content == ContentKind.PressurePlate ? 'p'
                            : cell.IsExit ? 'X'
                            : p == floor.Start ? 'H'
                            : '.';
                        row.Append(ch);
                    }
                    TestContext.Out.WriteLine(row.ToString());
                }
                TestContext.Out.WriteLine($"  pads={Board.AllCells.Count(p => floor[p].Content == ContentKind.Teleport)} " +
                    $"doors={Board.AllCells.Count(p => floor[p].Terrain == Terrain.Door)} " +
                    $"plates={Board.AllCells.Count(p => floor[p].Content == ContentKind.PressurePlate)} " +
                    $"lava={Board.AllCells.Count(p => floor[p].Hazard == HazardKind.Lava)} " +
                    $"pits={Board.AllCells.Count(p => floor[p].Terrain == Terrain.Pit)}");
            }
        }

        [Test]
        public void VaultsAreValidAndReachable()
        {
            var catalog = ContentCatalog.CreateDefault();
            for (ulong seed = 1; seed <= 25; seed++)
            {
                var vault = FloorGenerator.GenerateVault(seed, 2, P(1, 1), catalog);
                Assert.That(FloorValidator.Validate(vault, catalog), Is.True, $"seed {seed}");
                Assert.That(vault.IsVault, Is.True);
                Assert.That(vault.ExitUnlocked, Is.True, "The way back is always open.");
                Assert.That(vault.Exit, Is.Not.EqualTo(vault.Start));
            }
        }
    }
}
