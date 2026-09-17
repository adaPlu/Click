using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>Hero sensing/revealing (rules §2.3) and enemy waking (rules §3.1).</summary>
    public static class Visibility
    {
        public static void Update(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var floor = run.Floor;
            var hero = run.Hero;
            var heroClass = catalog.HeroClass(hero.ClassId);

            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                // Revealing follows melee reach (the eight neighbours); sensing stays a diamond around the hero.
                if (p.Chebyshev(hero.Pos) <= heroClass.RevealRadius)
                {
                    if (cell.Knowledge == Knowledge.Revealed) continue;
                    cell.Knowledge = Knowledge.Revealed;
                    events.Add(GameEvent.Of(GameEventKind.CellRevealed, to: p));
                }
                // Free Roam gives no hints at all: every tile stays a blank cover until the hero reveals it (D-021).
                else if (run.Movement == MovementMode.Step
                         && p.Manhattan(hero.Pos) <= heroClass.SenseRadius && cell.Knowledge == Knowledge.Unseen)
                {
                    cell.Knowledge = Knowledge.Sensed;
                    var sensed = GameEvent.Of(GameEventKind.CellSensed, to: p);
                    sensed.Clue = Board.ClueAt(floor, p);
                    events.Add(sensed);
                }
            }

            foreach (var enemy in floor.Enemies)
            {
                if (enemy.Awake || floor[enemy.Pos].Knowledge != Knowledge.Revealed) continue;
                enemy.Awake = true;
                enemy.JustWoken = true;
                events.Add(GameEvent.Of(GameEventKind.EnemyWoke, enemy.Id, to: enemy.Pos, source: enemy.DefId));
            }
        }
    }
}
