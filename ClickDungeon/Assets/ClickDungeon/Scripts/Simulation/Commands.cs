using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// The single command model for every input device. Validation lives only here so UI
    /// highlighting and the resolver can never disagree.
    /// </summary>
    public static class Commands
    {
        public static bool Validate(RunState run, PlayerCommand command, ContentCatalog catalog, out string reason)
        {
            reason = null;
            if (run.Status != RunStatus.InProgress) return Fail(out reason, "The run is over.");

            var hero = run.Hero;
            var floor = run.Floor;
            var target = command.Target;

            switch (command.Kind)
            {
                case CommandKind.Wait:
                    return true;

                case CommandKind.Move:
                    if (!target.InBounds || !hero.Pos.IsOrthogonallyAdjacent(target))
                        return Fail(out reason, "Sir Clickington can only step to a neighbouring tile.");
                    if (!Board.HeroCanEnter(run, target)) return Fail(out reason, "That way is blocked.");
                    return true;

                case CommandKind.Slash:
                {
                    if (!target.InBounds || !hero.Pos.IsOrthogonallyAdjacent(target))
                        return Fail(out reason, "Slash only reaches neighbouring tiles.");
                    var enemy = floor.EnemyAt(target);
                    if (enemy != null && enemy.Awake) return true;
                    var cell = floor[target];
                    if (cell.Hazard == HazardKind.Bomb && !cell.BombArmed && cell.Knowledge == Knowledge.Revealed) return true;
                    return Fail(out reason, "Nothing to slash there.");
                }

                case CommandKind.Shield:
                    if (hero.ShieldCooldown > 0) return Fail(out reason, $"Shield is recharging ({hero.ShieldCooldown}).");
                    return true;

                case CommandKind.Dash:
                    return ValidateDash(run, target, catalog.HeroClass(hero.ClassId), out reason);

                case CommandKind.Potion:
                    if (hero.Potions <= 0) return Fail(out reason, "No potions left.");
                    if (hero.Hp >= hero.MaxHp) return Fail(out reason, "Already at full health.");
                    return true;

                case CommandKind.Interact:
                    if (!target.InBounds || !hero.Pos.IsOrthogonallyAdjacent(target))
                        return Fail(out reason, "Stand next to it first.");
                    if (!floor[target].IsClosedChest) return Fail(out reason, "Nothing to open there.");
                    return true;
            }

            return Fail(out reason, "Unknown command.");
        }

        static bool ValidateDash(RunState run, GridPos target, HeroClassDefinition heroClass, out string reason)
        {
            var hero = run.Hero;
            if (hero.DashCooldown > 0) return Fail(out reason, $"Dash is recharging ({hero.DashCooldown}).");
            if (!target.InBounds
                || !Directions.TryFromDelta(target.X - hero.Pos.X, target.Y - hero.Pos.Y, out var dir)
                || hero.Pos.Manhattan(target) != heroClass.DashDistance)
                return Fail(out reason, $"Dash moves exactly {heroClass.DashDistance} tiles in a straight line.");

            for (int i = 1; i < heroClass.DashDistance; i++)
            {
                var middle = hero.Pos.Step(dir, i);
                var cell = run.Floor[middle];
                if (cell.Terrain != Terrain.Floor || cell.IsClosedChest || run.Floor.EnemyAt(middle) != null)
                    return Fail(out reason, "Something blocks the dash.");
            }

            var landing = run.Floor[target];
            if (landing.Knowledge == Knowledge.Unseen) return Fail(out reason, "Can't dash into the unknown.");
            var occupant = run.Floor.EnemyAt(target);
            if (occupant != null) return Fail(out reason, occupant.Awake ? "An enemy stands there." : "Something lurks there.");
            if (!Board.HeroCanEnter(run, target)) return Fail(out reason, "Can't land there.");

            reason = null;
            return true;
        }

        /// <summary>Cells where a targeted command of this kind is currently legal.</summary>
        public static List<GridPos> LegalTargets(RunState run, CommandKind kind, ContentCatalog catalog)
        {
            var cells = new List<GridPos>();
            foreach (var p in Board.AllCells)
                if (Validate(run, new PlayerCommand(kind, p), catalog, out _)) cells.Add(p);
            return cells;
        }

        /// <summary>MOVE-mode tap (decision D-011): step, slash an adjacent enemy, open a chest, or wait on self.</summary>
        public static bool TryContextual(RunState run, GridPos cell, out PlayerCommand command)
        {
            var hero = run.Hero;
            command = default;
            if (cell == hero.Pos)
            {
                command = PlayerCommand.Wait();
                return true;
            }
            if (!cell.InBounds || !hero.Pos.IsOrthogonallyAdjacent(cell)) return false;

            var enemy = run.Floor.EnemyAt(cell);
            if (enemy != null && enemy.Awake) command = PlayerCommand.Slash(cell);
            else if (run.Floor[cell].IsClosedChest) command = PlayerCommand.Interact(cell);
            else command = PlayerCommand.Move(cell);
            return true;
        }

        static bool Fail(out string reason, string message)
        {
            reason = message;
            return false;
        }
    }
}
