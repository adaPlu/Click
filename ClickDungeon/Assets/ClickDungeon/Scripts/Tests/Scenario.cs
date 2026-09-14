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
    /// 'K' key, 'C' chest, 'P' potion, '^' spikes, 'b' bomb,
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
                        case 'P': cell.Content = ContentKind.Potion; break;
                        case '^': cell.Hazard = HazardKind.Spikes; break;
                        case 'b': cell.Hazard = HazardKind.Bomb; break;
                        case 'g': Spawn(floor, "goblin", p, false); break;
                        case 's': Spawn(floor, "crowned_slime", p, false); break;
                        case 'i': Spawn(floor, "fire_imp", p, false); break;
                        case 'G': Spawn(floor, "goblin", p, true); break;
                        case 'S': Spawn(floor, "crowned_slime", p, true); break;
                        case 'I': Spawn(floor, "fire_imp", p, true); break;
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

        public static RunState Run(params string[] rows) => Run(1, 1234UL, rows);

        public static RunState Run(int floorIndex, ulong seed, params string[] rows)
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

        public static EnemyState Enemy(RunState run, string defId) => run.Floor.Enemies.Find(e => e.DefId == defId);

        public static bool Has(CommandResult result, GameEventKind kind) => result.Events.Exists(e => e.Kind == kind);

        static void Spawn(FloorState floor, string id, GridPos p, bool awake) => EnemyAi.Spawn(floor, Catalog.Enemy(id), p, awake);
    }
}
