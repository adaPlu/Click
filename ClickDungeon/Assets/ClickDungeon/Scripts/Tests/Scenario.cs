using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// Builds hand-drawn boards for rule tests. Rows are top row first.
    /// Legend: '#' wall, 'o' pit, '.' floor, 'H' hero/start, 'X' locked exit, 'x' unlocked exit,
    /// 'K' key, 'C' chest, 'W' great chest, 'P' potion, '^' spikes, 'b' bomb, 'L' lava,
    /// 'D' locked door, 'd' open door, 'p' pressure plate, 't' teleport pad, 'f' healing fountain,
    /// 'g' 's' 'i' dormant goblin/slime/imp, 'G' 'S' 'I' awake, 'B' Lord Blobert (awake, boss floor).
    /// </summary>
    public static class Scenario
    {
        public static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();

        public static GridPos P(int x, int y) => new GridPos(x, y);

        public static FloorState Floor(params string[] rows)
        {
            Assert.That(rows.Length, Is.EqualTo(BoardRules.Size), "Scenario needs 5 rows.");
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.TemplateId = "scenario";
            for (int r = 0; r < BoardRules.Size; r++)
            {
                Assert.That(rows[r].Length, Is.EqualTo(BoardRules.Size), $"Row {r} needs 5 tiles.");
                for (int x = 0; x < BoardRules.Size; x++)
                {
                    var p = new GridPos(x, BoardRules.Size - 1 - r);
                    var cell = floor[p];
                    switch (rows[r][x])
                    {
                        case '.': break;
                        case '#': cell.Terrain = Terrain.Wall; break;
                        case 'o': cell.Terrain = Terrain.Pit; break;
                        case 'H': floor.Start = p; break;
                        case 'X': cell.IsExit = true; floor.Exit = p; break;
                        case 'x': cell.IsExit = true; floor.Exit = p; floor.ExitUnlocked = true; break;
                        case 'K': cell.Content = ContentKind.Key; break;
                        case 'C': cell.Content = ContentKind.Chest; break;
                        case 'W':
                            cell.Content = ContentKind.Chest;
                            cell.GreatChest = true;
                            break;
                        case 'L': cell.Hazard = HazardKind.Lava; break;
                        case 'D': cell.Terrain = Terrain.Door; break;
                        case 'd':
                            cell.Terrain = Terrain.Door;
                            cell.Used = true;
                            break;
                        case 'p': cell.Content = ContentKind.PressurePlate; break;
                        case 't': cell.Content = ContentKind.Teleport; break;
                        case 'f': cell.Content = ContentKind.Fountain; break;
                        case 'P': cell.Content = ContentKind.Potion; break;
                        case '^': cell.Hazard = HazardKind.Spikes; break;
                        case 'b': cell.Hazard = HazardKind.Bomb; break;
                        case 'g': Spawn(floor, "goblin", p, false); break;
                        case 's': Spawn(floor, "crowned_slime", p, false); break;
                        case 'i': Spawn(floor, "fire_imp", p, false); break;
                        case 'G': Spawn(floor, "goblin", p, true); break;
                        case 'S': Spawn(floor, "crowned_slime", p, true); break;
                        case 'I': Spawn(floor, "fire_imp", p, true); break;
                        // First expansion monsters (D-058), awake.
                        case 'Z': Spawn(floor, "skeleton", p, true); break;
                        case 'R': Spawn(floor, "armored_boar", p, true); break;
                        case 'M': Spawn(floor, "goblin_bomber", p, true); break;
                        // Second expansion monsters (D-061). The mimic starts asleep, as it would on a floor.
                        case 'A': Spawn(floor, "cave_spider", p, true); break;
                        case 'Y': Spawn(floor, "spooky_spellbook", p, true); break;
                        case 'Q': Spawn(floor, "mimic_chest", p, false); break;
                        case 'J': Spawn(floor, "goblin_key_warden", p, true).CarriesKey = true; break;
                        // Act bosses (D-062), each making its floor a boss floor as Blobert's 'B' does.
                        case 'N': Spawn(floor, "goblin_brute_king", p, true); floor.IsBossFloor = true; break;
                        case 'V': Spawn(floor, "bat_swarm_leader", p, true); floor.IsBossFloor = true; break;
                        case 'T': Spawn(floor, "theater_curtain_demon", p, true); floor.IsBossFloor = true; break;
                        case 'B':
                            Spawn(floor, "lord_blobert", p, true);
                            floor.IsBossFloor = true;
                            break;
                        default: Assert.Fail($"Unknown scenario tile '{rows[r][x]}'."); break;
                    }
                }
            }
            return floor;
        }

        public static RunState Run(params string[] rows) => Build(1, 1234UL, MovementMode.Free, rows);

        public static RunState Run(int floorIndex, ulong seed, params string[] rows) => Build(floorIndex, seed, MovementMode.Free, rows);

        /// <summary>
        /// A Step by Step board (D-021): the hero moves one tile at a time and nearby tiles are hinted. Enemies behave the
        /// same in both modes.
        /// </summary>
        public static RunState StepRun(params string[] rows) => Build(1, 1234UL, MovementMode.Step, rows);

        public static RunState StepRun(int floorIndex, ulong seed, params string[] rows) => Build(floorIndex, seed, MovementMode.Step, rows);

        /// <summary>
        /// Switches an already-built board to Step by Step. Enemies keep the intent they declared during setup, so this is
        /// only for checking what a command validates to; use <see cref="StepRun"/> to test how enemies behave.
        /// </summary>
        public static RunState Step(RunState run)
        {
            run.Movement = MovementMode.Step;
            return run;
        }

        static RunState Build(int floorIndex, ulong seed, MovementMode movement, string[] rows)
        {
            var floor = Floor(rows);
            floor.FloorIndex = floorIndex;
            var run = new RunState
            {
                RunSeed = seed,
                FloorCount = Catalog.RunFloorCount,
                ContentCatalogVersion = Catalog.Version,
                Hero = RunFactory.CreateHero(Catalog, ContentCatalog.DefaultHeroId),
                Floor = floor,
                // Set before the floor is set up: enemies declare their first intent there, and the two modes declare differently.
                Movement = movement,
            };
            RunFactory.SetupFloor(run, Catalog, new List<GameEvent>());
            return run;
        }

        public static CommandResult Do(RunState run, PlayerCommand command) => TurnResolver.Apply(run, command, Catalog);

        public static CommandResult DoOk(RunState run, PlayerCommand command)
        {
            var result = Do(run, command);
            Assert.That(result.Accepted, Is.True, $"{command} was rejected: {result.RejectReason}");
            return result;
        }

        /// <summary>
        /// Taps a chest until it opens (D-022): Common 2, Rare 3, Epic 4, each tap a full turn. Returns the result of the
        /// tap that opened it.
        /// </summary>
        public static CommandResult OpenChest(RunState run, GridPos cell)
        {
            CommandResult result = null;
            // A covered chest is only a cover (D-023 amendment); these tests start from a chest the hero has already uncovered.
            run.Floor[cell].Knowledge = Knowledge.Revealed;
            for (int i = 0, taps = Chests.TapsToOpen(run.Floor[cell].Quality); i < taps; i++)
                result = DoOk(run, PlayerCommand.Interact(cell));
            return result;
        }

        public static EnemyState Enemy(RunState run, string defId) => run.Floor.Enemies.Find(e => e.DefId == defId);

        public static bool Has(CommandResult result, GameEventKind kind) => result.Events.Exists(e => e.Kind == kind);

        static EnemyState Spawn(FloorState floor, string id, GridPos p, bool awake) => EnemyAi.Spawn(floor, Catalog.Enemy(id), p, awake);
    }
}
