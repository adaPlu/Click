using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>Hero sensing/revealing (rules §2.3) and enemy waking (rules §3.1).</summary>
    public static class Visibility
    {
        /// <summary>
        /// Tiles are revealed by clicking them, never by how far the hero can see (D-023): the hero's own tile is revealed,
        /// and a bumped tile is revealed through <see cref="Reveal"/>. Step by Step still senses clues nearby.
        /// </summary>
        public static void Update(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var floor = run.Floor;
            var hero = run.Hero;
            var heroClass = catalog.HeroClass(hero.ClassId);

            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                if (p == hero.Pos)
                {
                    Reveal(floor, p, events);
                }
                // Free Roam gives no hints at all: every tile stays a blank cover until it is clicked (D-021).
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

        /// <summary>Uncovers one tile. An enemy hiding there wakes on the next <see cref="Update"/>.</summary>
        public static void Reveal(FloorState floor, GridPos p, List<GameEvent> events)
        {
            var cell = floor[p];
            if (cell.Knowledge == Knowledge.Revealed) return;
            cell.Knowledge = Knowledge.Revealed;
            events.Add(GameEvent.Of(GameEventKind.CellRevealed, to: p));
        }
    }
}
